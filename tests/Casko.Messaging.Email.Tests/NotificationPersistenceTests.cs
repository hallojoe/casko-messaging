using Casko.Messaging.Email.BulkDelivery;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Casko.Messaging.Email.Tests;

public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CASKO_TEST_SQL")))
            Skip = "Set CASKO_TEST_SQL to a SQL Server connection with permission to create isolated test databases.";
    }
}

// Providers can reuse these assertions by supplying their own scope/initializer fixture.
public abstract class NotificationPersistenceContract
{
    protected abstract IServiceScope OpenScope();
    protected static NotificationInput Input(string key, params RecipientInput[] recipients) =>
        new(Guid.Empty, "Test", "inline", new("Subject", "Text", null), key, recipients);
    protected async Task<NotificationBatchResult> Write(params NotificationInput[] inputs)
    {
        using var scope = OpenScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationWriter>().CreateBatchAsync(new(inputs), default);
    }

    [SqlFact]
    public async Task Supports_ten_thousand_recipients_and_replay()
    {
        var input = Input("shared", Enumerable.Range(0, 10_000).Select(i => new RecipientInput($"user{i}@example.test")).ToArray());
        var first = Assert.Single((await Write(input)).Notifications);
        Assert.True(first.Created);
        Assert.Equal(10_000, first.AddedRecipients);
        var replay = Assert.Single((await Write(input)).Notifications);
        Assert.Equal(first.Id, replay.Id);
        Assert.False(replay.Created);
        Assert.Equal(0, replay.AddedRecipients);
        Assert.Equal(10_000, replay.ExistingRecipients);
    }

    [SqlFact]
    public async Task Supports_ten_thousand_events_with_correct_payload_mapping()
    {
        var inputs = Enumerable.Range(0, 10_000).Select(i =>
            Input($"event-{i}", new RecipientInput($"user{i}@example.test")) with { Message = new($"Subject-{i}", "Text", null) }).ToArray();
        var result = await Write(inputs);
        Assert.Equal(10_000, result.Notifications.Count);
        Assert.Equal(10_000, result.Notifications.Select(x => x.Id).Distinct().Count());
        using var scope = OpenScope();
        var claimed = await scope.ServiceProvider.GetRequiredService<INotificationQueueStore>().ClaimAsync(10_000, "worker", TimeSpan.FromMinutes(5), default);
        Assert.Equal(10_000, claimed.Count);
        var map = result.Notifications.ToDictionary(x => x.Id, x => int.Parse(x.IdempotencyKey[6..]));
        foreach (var delivery in claimed)
        {
            var i = map[delivery.NotificationEventId];
            Assert.Equal($"user{i}@example.test", delivery.EmailAddress);
            Assert.Contains($"Subject-{i}\"", delivery.Payload);
        }
    }

    [SqlFact]
    public async Task Deduplicates_input_and_conflicts_roll_back_entire_request()
    {
        var input = Input("key", new RecipientInput(" one@example.test "), new RecipientInput("ONE@example.test"));
        var result = Assert.Single((await Write(input, input)).Notifications);
        Assert.Equal(1, result.AddedRecipients);
        Assert.Equal(3, result.DuplicateRecipients);
        Assert.Equal(1, result.DuplicateEvents);
        await Assert.ThrowsAsync<NotificationConflictException>(() => Write(
            Input("must-rollback", new RecipientInput("two@example.test")), input with { Message = new("Different", "Text", null) }));
        Assert.True(Assert.Single((await Write(Input("must-rollback"))).Notifications).Created);
    }

    [SqlFact]
    public async Task Concurrent_overlapping_writes_insert_each_delivery_once()
    {
        var input = Input("concurrent", Enumerable.Range(0, 100).Select(i => new RecipientInput($"u{i}@example.test")).ToArray());
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Write(input)));
        Assert.Equal(1, results.Sum(x => x.Notifications.Count(n => n.Created)));
        Assert.Equal(100, results.Sum(x => x.Notifications.Sum(n => n.AddedRecipients)));
    }

    [SqlFact]
    public async Task Claims_are_exclusive_and_updates_require_owner_and_live_lease()
    {
        await Write(Input("queue", new RecipientInput("a@example.test"), new RecipientInput("b@example.test")));
        async Task<IReadOnlyList<ClaimedDelivery>> Claim(string owner)
        {
            using var scope = OpenScope();
            return await scope.ServiceProvider.GetRequiredService<INotificationQueueStore>()
                .ClaimAsync(2, owner, TimeSpan.FromSeconds(1), default);
        }
        var batches = await Task.WhenAll(Claim("one"), Claim("two"));
        var claims = batches.SelectMany(x => x).ToArray();
        Assert.Equal(2, claims.Length);
        Assert.Equal(2, claims.Select(x => x.Id).Distinct().Count());
        using var scope = OpenScope();
        var queue = scope.ServiceProvider.GetRequiredService<INotificationQueueStore>();
        Assert.False(await queue.MarkSentAsync(claims[0].Id, "wrong", "message", default));
        await Task.Delay(1200);
        Assert.False(await queue.MarkSentAsync(claims[0].Id, claims[0].WorkerId, "message", default));
        var reclaimed = await queue.ClaimAsync(2, "three", TimeSpan.FromMinutes(5), default);
        Assert.Equal(2, reclaimed.Count);
        Assert.True(await queue.RenewLeaseAsync(reclaimed[0].Id, "three", TimeSpan.FromMinutes(5), default));
        Assert.True(await queue.MarkSentAsync(reclaimed[0].Id, "three", "message", default));
        Assert.True(await queue.MarkFailureAsync(reclaimed[1].Id, "three", "test failure", 3, null, default));
        Assert.True(await queue.RetryAsync(reclaimed[1].Id, default));
        Assert.Single(await queue.ClaimAsync(2, "four", TimeSpan.FromMinutes(5), default));
    }

    [SqlFact]
    public async Task Claims_critical_before_normal_and_keeps_bulk_in_the_standard_lane()
    {
        await Write(
            Input("bulk", new RecipientInput("bulk@example.test")) with { Priority = NotificationPriority.Bulk },
            Input("normal", new RecipientInput("normal@example.test")) with { Priority = NotificationPriority.Normal },
            Input("critical", new RecipientInput("critical@example.test")) with { Priority = NotificationPriority.Critical });

        using var scope = OpenScope();
        var queue = scope.ServiceProvider.GetRequiredService<INotificationQueueStore>();
        var critical = Assert.Single(await queue.ClaimAsync(1, "critical", TimeSpan.FromMinutes(5),
            new(NotificationPriority.Critical, NotificationPriority.Critical), default));
        Assert.Equal(NotificationPriority.Critical, critical.Priority);

        var standard = await queue.ClaimAsync(2, "standard", TimeSpan.FromMinutes(5),
            new(NotificationPriority.Bulk, NotificationPriority.Normal), default);
        Assert.Equal([NotificationPriority.Normal, NotificationPriority.Bulk], standard.Select(x => x.Priority));
    }

    [SqlFact]
    public async Task Treats_priority_as_immutable_event_content()
    {
        await Write(Input("same", new RecipientInput("a@example.test")) with { Priority = NotificationPriority.Normal });
        await Assert.ThrowsAsync<NotificationConflictException>(() => Write(
            Input("same", new RecipientInput("a@example.test")) with { Priority = NotificationPriority.Critical }));
    }

    [SqlFact]
    public async Task Rejects_invalid_input_and_honors_cancellation()
    {
        using var scope = OpenScope();
        var writer = scope.ServiceProvider.GetRequiredService<INotificationWriter>();
        await Assert.ThrowsAsync<ArgumentException>(() => writer.CreateBatchAsync(new([]), default));
        await Assert.ThrowsAsync<ArgumentException>(() => Write(Input("bad", new RecipientInput("not an address"))));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.CreateBatchAsync(new([Input("cancelled")]), cancelled.Token));
        Assert.True(Assert.Single((await Write(Input("cancelled"))).Notifications).Created);
    }

    [SqlFact]
    public async Task Existing_recipient_endpoint_supports_large_batches_and_missing_events()
    {
        var existing = Assert.Single((await Write(Input("append"))).Notifications);
        using var scope = OpenScope();
        var writer = scope.ServiceProvider.GetRequiredService<INotificationWriter>();
        var recipients = Enumerable.Range(0, 2000).Select(i => new RecipientInput($"u{i}@example.test")).ToArray();
        Assert.Equal(2000, await writer.AddRecipientsAsync(existing.Id, recipients, default));
        Assert.Equal(0, await writer.AddRecipientsAsync(existing.Id, recipients, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => writer.AddRecipientsAsync(long.MaxValue, [], default));
        await Assert.ThrowsAsync<ArgumentException>(() => Write(Input("too-many",
            Enumerable.Range(0, 10001).Select(i => new RecipientInput($"u{i}@example.test")).ToArray())));
        await Assert.ThrowsAsync<ArgumentException>(() => Write(Input(new string('x', 201))));
        await Assert.ThrowsAsync<ArgumentException>(() => Write(Input("null-message") with { Message = null! }));
    }
}

public sealed class SqlServerNotificationPersistenceTests(ITestOutputHelper output) : NotificationPersistenceContract, IAsyncLifetime
{
    private readonly string databaseName = $"casko_bulk_test_{Guid.NewGuid():N}";
    private ServiceProvider? services;
    private string? connectionString;
    protected override IServiceScope OpenScope() => services!.CreateScope();

    public async Task InitializeAsync()
    {
        var supplied = Environment.GetEnvironmentVariable("CASKO_TEST_SQL");
        if (string.IsNullOrEmpty(supplied)) return;
        var builder = new SqlConnectionStringBuilder(supplied) { InitialCatalog = "master" };
        await using (var connection = new SqlConnection(builder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand($"CREATE DATABASE [{databaseName}]", connection);
            await command.ExecuteNonQueryAsync();
        }
        builder.InitialCatalog = databaseName;
        connectionString = builder.ConnectionString;
        services = new ServiceCollection().AddLogging().AddSqlServerNotifications(connectionString).BuildServiceProvider();
        using var scope = OpenScope();
        await scope.ServiceProvider.GetRequiredService<INotificationStoreInitializer>().InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (services is null) return;
        await services.DisposeAsync();
        SqlConnection.ClearAllPools();
        var builder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        // Name is generated above, never taken from the supplied connection string.
        await using var command = new SqlCommand($"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]", connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task Execute(string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    [SqlFact]
    public async Task Uncommitted_deliveries_are_not_claimed()
    {
        using var scope = OpenScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var notification = new NotificationEvent
        {
            EntityId = Guid.Empty, EventType = "Test", Template = "inline", Payload = "{}",
            IdempotencyKey = "uncommitted", CreatedUtc = DateTimeOffset.UtcNow
        };
        db.NotificationEvents.Add(notification);
        await db.SaveChangesAsync();
        db.NotificationDeliveries.Add(new NotificationDelivery
        {
            NotificationEventId = notification.Id, EmailAddress = "a@example.test",
            NormalizedEmailAddress = "A@EXAMPLE.TEST", CreatedUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        using var workerScope = OpenScope();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.Empty(await workerScope.ServiceProvider.GetRequiredService<INotificationQueueStore>()
            .ClaimAsync(10, "worker", TimeSpan.FromMinutes(5), timeout.Token));
        await transaction.CommitAsync();
        Assert.Single(await workerScope.ServiceProvider.GetRequiredService<INotificationQueueStore>()
            .ClaimAsync(10, "worker", TimeSpan.FromMinutes(5), default));
    }

    [SqlFact]
    public async Task Cancellation_during_insertion_rolls_back()
    {
        await Execute("CREATE TRIGGER DelayDelivery ON dbo.NotificationDeliveries AFTER INSERT AS WAITFOR DELAY '00:00:05';");
        using var scope = OpenScope();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.ServiceProvider.GetRequiredService<INotificationWriter>()
            .CreateBatchAsync(new([Input("cancel-insert", new RecipientInput("a@example.test"))]), cancellation.Token));
        await Execute("DROP TRIGGER dbo.DelayDelivery;");
        Assert.True(Assert.Single((await Write(Input("cancel-insert", new RecipientInput("a@example.test")))).Notifications).Created);
    }

    [SqlBenchmarkFact]
    public async Task Benchmark_creation_strategies()
    {
        // Same database, fresh context per measurement. Exclude fixture/migrations and report client-wide allocations.
        foreach (var count in new[] { 1_000, 10_000 })
        {
            await Measure($"sequential-events-{count}", count, async () =>
            {
                using var scope = OpenScope();
                var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
                for (var i = 0; i < count; i++)
                {
                    var key = $"sequential-{count}-{i}";
                    await context.NotificationEvents.SingleOrDefaultAsync(e => e.IdempotencyKey == key);
                    var item = new NotificationEvent { EntityId = Guid.Empty, EventType = "Test", Template = "inline",
                        Payload = "{\"Subject\":\"Subject\",\"Text\":\"Text\",\"Html\":null}", IdempotencyKey = key, CreatedUtc = DateTimeOffset.UtcNow };
                    context.NotificationEvents.Add(item);
                    await context.SaveChangesAsync();
                    await context.NotificationEvents.AnyAsync(e => e.Id == item.Id);
                    await context.NotificationDeliveries.Where(d => d.NotificationEventId == item.Id).Select(d => d.NormalizedEmailAddress).ToListAsync();
                    context.NotificationDeliveries.Add(new() { NotificationEventId = item.Id, EmailAddress = $"u{i}@example.test",
                        NormalizedEmailAddress = $"U{i}@EXAMPLE.TEST", CreatedUtc = DateTimeOffset.UtcNow });
                    await context.SaveChangesAsync();
                }
            });
            await Measure($"ef-recipient-batches-{count}", count, async () =>
            {
                using var scope = OpenScope();
                var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
                var item = new NotificationEvent { EntityId = Guid.Empty, EventType = "Test", Template = "inline",
                    Payload = "{}", IdempotencyKey = $"ef-{count}", CreatedUtc = DateTimeOffset.UtcNow };
                context.NotificationEvents.Add(item);
                await context.SaveChangesAsync();
                foreach (var chunk in Enumerable.Range(0, count).Chunk(1000))
                {
                    await context.NotificationEvents.AnyAsync(e => e.Id == item.Id);
                    var keys = chunk.Select(i => $"U{i}@EXAMPLE.TEST").ToArray();
                    await context.NotificationDeliveries.Where(d => d.NotificationEventId == item.Id && keys.Contains(d.NormalizedEmailAddress)).ToListAsync();
                    context.NotificationDeliveries.AddRange(chunk.Select(i => new NotificationDelivery { NotificationEventId = item.Id,
                        EmailAddress = $"u{i}@example.test", NormalizedEmailAddress = $"U{i}@EXAMPLE.TEST", CreatedUtc = DateTimeOffset.UtcNow }));
                    await context.SaveChangesAsync();
                }
            });
            await Measure($"bulk-events-{count}", count, async () => await Write(Enumerable.Range(0, count)
                .Select(i => Input($"bulk-{count}-{i}", new RecipientInput($"u{i}@example.test"))).ToArray()));
            await Measure($"bulk-recipients-{count}", count, async () => await Write(Input($"bulk-shared-{count}",
                Enumerable.Range(0, count).Select(i => new RecipientInput($"u{i}@example.test")).ToArray())));
        }
    }

    private async Task Measure(string name, int rows, Func<Task> action)
    {
        using var commands = new SqlCommandCounter();
        var allocations = GC.GetTotalAllocatedBytes(true);
        var timer = Stopwatch.StartNew();
        await action();
        timer.Stop();
        output.WriteLine($"{name}: {timer.Elapsed.TotalMilliseconds:F0} ms; {rows / timer.Elapsed.TotalSeconds:F0} deliveries/s; " +
            $"{GC.GetTotalAllocatedBytes(true) - allocations} allocated bytes; {commands.Count} SQL commands (bulk-copy streams excluded)");
        if (name.StartsWith("bulk-")) Assert.Equal(2, commands.Count);
    }

    [SqlFact]
    public async Task Failure_after_event_insert_rolls_back_events_and_deliveries()
    {
        await Execute("CREATE TRIGGER FailDelivery ON dbo.NotificationDeliveries AFTER INSERT AS THROW 51003, 'Injected failure', 1;");
        await Assert.ThrowsAsync<SqlException>(() => Write(Input("rollback", new RecipientInput("a@example.test"))));
        await Execute("DROP TRIGGER dbo.FailDelivery;");
        Assert.True(Assert.Single((await Write(Input("rollback", new RecipientInput("a@example.test")))).Notifications).Created);
    }

    [SqlFact]
    public async Task Initialization_preserves_existing_migration_history_and_data()
    {
        var first = Assert.Single((await Write(Input("migration", new RecipientInput("a@example.test")))).Notifications);
        using var scope = OpenScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.Equal(["20260904200400_InitialNotifications", "20260905190000_AddNotificationPriority"], await context.Database.GetAppliedMigrationsAsync());
        await scope.ServiceProvider.GetRequiredService<INotificationStoreInitializer>().InitializeAsync();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(first.Id, Assert.Single((await Write(Input("migration", new RecipientInput("a@example.test")))).Notifications).Id);
    }

    [SqlFact]
    public async Task Staging_honors_database_collation_and_keeps_first_recipient_metadata()
    {
        var recipientId = Guid.NewGuid();
        var result = Assert.Single((await Write(Input("SameKey", new RecipientInput("a@example.test", recipientId)),
            Input("samekey", new RecipientInput("A@example.test", Guid.NewGuid())))).Notifications);
        Assert.Equal(1, result.DuplicateEvents);
        Assert.Equal(1, result.DuplicateRecipients);
        using var scope = OpenScope();
        Assert.Equal(recipientId, (await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().NotificationDeliveries.SingleAsync()).RecipientId);
    }
}

public sealed class SqlBenchmarkFactAttribute : FactAttribute
{
    public SqlBenchmarkFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CASKO_TEST_SQL")) ||
            Environment.GetEnvironmentVariable("CASKO_RUN_BENCHMARKS") != "1")
            Skip = "Set CASKO_TEST_SQL and CASKO_RUN_BENCHMARKS=1 to run database benchmarks.";
    }
}

internal sealed class SqlCommandCounter : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private readonly List<IDisposable> subscriptions = [];
    private readonly IDisposable listener;
    private int count;
    public int Count => count;
    public SqlCommandCounter() => listener = DiagnosticListener.AllListeners.Subscribe(this);
    public void OnNext(DiagnosticListener value)
    {
        if (value.Name == "SqlClientDiagnosticListener")
            subscriptions.Add(value.Subscribe(this, name => name.EndsWith("WriteCommandBefore")));
    }
    public void OnNext(KeyValuePair<string, object?> value) => Interlocked.Increment(ref count);
    public void OnError(Exception error) { }
    public void OnCompleted() { }
    public void Dispose() { listener.Dispose(); foreach (var item in subscriptions) item.Dispose(); }
}
