using System.Text.Json;
using System.Threading.RateLimiting;
using System.Threading.Channels;
using Casko.Messaging.Email.BulkDelivery;
using Casko.Messaging.Email.MailKit.Configuration;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Casko.Messaging.Email.Worker;

public sealed class DeliveryWorker(IServiceScopeFactory scopes, IOptions<EmailDeliveryWorkerOptions> options, IOptions<MailKitEmailOptions> smtp, ILogger<DeliveryWorker> logger) : BackgroundService
{
    private readonly EmailDeliveryWorkerOptions _options = options.Value;
    private readonly MailKitEmailOptions _smtp = smtp.Value;
    private readonly string _workerId = $"{Environment.MachineName}/{Environment.ProcessId}/{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var migrationScope = scopes.CreateScope())
            await migrationScope.ServiceProvider.GetRequiredService<INotificationStoreInitializer>().InitializeAsync(stoppingToken);
        using var standardLimiter = CreateLimiter(_options.MaximumMessagesPerSecond, _options.BatchSize * _options.Concurrency);
        using var criticalLimiter = CreateLimiter(_options.CriticalMaximumMessagesPerSecond, _options.CriticalBatchSize * _options.CriticalConcurrency);
        await Task.WhenAll(
            RunLaneAsync("critical", _options.CriticalBatchSize, _options.CriticalConcurrency,
                new(NotificationPriority.Critical, NotificationPriority.Critical), criticalLimiter, stoppingToken),
            RunLaneAsync("standard", _options.BatchSize, _options.Concurrency,
                new(NotificationPriority.Bulk, NotificationPriority.Normal, _options.BulkPromotionAfter), standardLimiter, stoppingToken));
    }

    private static RateLimiter? CreateLimiter(int messagesPerSecond, int queueLimit) => messagesPerSecond > 0
        ? new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions { TokenLimit = messagesPerSecond, TokensPerPeriod = messagesPerSecond, ReplenishmentPeriod = TimeSpan.FromSeconds(1), QueueProcessingOrder = QueueProcessingOrder.OldestFirst, QueueLimit = queueLimit * 2, AutoReplenishment = true })
        : null;

    private async Task RunLaneAsync(string lane, int batchSize, int concurrency, NotificationPriorityRange priorityRange, RateLimiter? limiter, CancellationToken stoppingToken)
    {
        var workerId = $"{_workerId}/{lane}";
        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<ClaimedDelivery> batch;
            using (var scope = scopes.CreateScope()) batch = await scope.ServiceProvider.GetRequiredService<INotificationQueueStore>().ClaimAsync(batchSize, workerId, _options.ProcessingLeaseDuration, priorityRange, stoppingToken);
            if (batch.Count == 0) { await Task.Delay(_options.PollInterval, stoppingToken); continue; }
            logger.LogInformation("Claimed {Count} {Lane} notification deliveries.", batch.Count, lane);
            var channel = Channel.CreateBounded<ClaimedDelivery>(new BoundedChannelOptions(batch.Count) { SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
            var senders = Enumerable.Range(0, concurrency).Select(_ => SenderLoopAsync(channel.Reader, workerId, limiter, stoppingToken)).ToArray();
            foreach (var delivery in batch) await channel.Writer.WriteAsync(delivery, stoppingToken);
            channel.Writer.Complete();
            await Task.WhenAll(senders);
        }
    }

    private async Task SenderLoopAsync(ChannelReader<ClaimedDelivery> reader, string workerId, RateLimiter? limiter, CancellationToken ct)
    {
        using var client = new SmtpClient();
        await foreach (var delivery in reader.ReadAllAsync(ct))
        {
            if (limiter is not null)
            {
                using var lease = await limiter.AcquireAsync(1, ct);
                if (!lease.IsAcquired) continue;
            }
            await SendAsync(client, delivery, workerId, ct);
        }
        if (client.IsConnected) await client.DisconnectAsync(true, CancellationToken.None);
    }

    private async Task SendAsync(SmtpClient client, ClaimedDelivery delivery, string workerId, CancellationToken ct)
    {
        using var renewalCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var renewal = RenewLeaseAsync(delivery.Id, workerId, renewalCancellation.Token);
        try
        {
            var template = JsonSerializer.Deserialize<InlineEmailTemplate>(delivery.Payload) ?? throw new InvalidOperationException("Invalid notification template payload.");
            var message = new MimeMessage { Subject = template.Subject, MessageId = $"<notification-delivery-{delivery.Id}@casko.local>" };
            message.From.Add(new MailboxAddress(_smtp.FromDisplayName, _smtp.FromAddress));
            message.To.Add(MailboxAddress.Parse(delivery.EmailAddress));
            message.Body = new BodyBuilder { TextBody = template.Text, HtmlBody = template.Html }.ToMessageBody();
            if (!client.IsConnected)
            {
                await client.ConnectAsync(_smtp.Host, _smtp.Port, _smtp.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None, ct);
                if (!string.IsNullOrWhiteSpace(_smtp.Username) && !string.IsNullOrWhiteSpace(_smtp.Password)) await client.AuthenticateAsync(_smtp.Username, _smtp.Password, ct);
            }
            await client.SendAsync(message, ct);
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<INotificationQueueStore>().MarkSentAsync(delivery.Id, workerId, message.MessageId!, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            if (client.IsConnected) await client.DisconnectAsync(false, CancellationToken.None);
            var permanent = ex is SmtpCommandException { StatusCode: >= SmtpStatusCode.MailboxUnavailable } || ex is ArgumentException || ex is JsonException;
            TimeSpan? retry = permanent || delivery.Attempts + 1 >= _options.MaximumAttempts ? null : RetryDelay(delivery.Attempts + 1);
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<INotificationQueueStore>().MarkFailureAsync(delivery.Id, workerId, Truncate(ex.Message), _options.MaximumAttempts, retry, ct);
            logger.LogWarning(ex, "Notification delivery {DeliveryId} failed; retry={Retry}.", delivery.Id, retry is not null);
        }
        finally
        {
            renewalCancellation.Cancel();
            try { await renewal; } catch (OperationCanceledException) when (renewalCancellation.IsCancellationRequested) { }
        }
    }

    private async Task RenewLeaseAsync(long deliveryId, string workerId, CancellationToken ct)
    {
        var interval = TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(1).Ticks, _options.ProcessingLeaseDuration.Ticks / 3));
        try
        {
            while (true)
            {
                await Task.Delay(interval, ct);
                using var scope = scopes.CreateScope();
                if (!await scope.ServiceProvider.GetRequiredService<INotificationQueueStore>().RenewLeaseAsync(deliveryId, workerId, _options.ProcessingLeaseDuration, ct)) return;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private static TimeSpan RetryDelay(int attempt) => attempt switch { 1 => TimeSpan.FromMinutes(1), 2 => TimeSpan.FromMinutes(5), 3 => TimeSpan.FromMinutes(15), _ => TimeSpan.FromHours(1) };
    private static string Truncate(string value) => value.Length <= 4000 ? value : value[..4000];
}
