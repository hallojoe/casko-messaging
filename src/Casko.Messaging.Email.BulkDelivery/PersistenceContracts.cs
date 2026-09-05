using System.Net.Mail;

namespace Casko.Messaging.Email.BulkDelivery;

public sealed record NotificationInput(Guid EntityId, string EventType, string Template, InlineEmailTemplate Message,
    string IdempotencyKey, IReadOnlyList<RecipientInput> Recipients, NotificationPriority Priority = NotificationPriority.Normal);
public sealed record NotificationBatchRequest(IReadOnlyList<NotificationInput> Notifications);
public sealed record NotificationEventResult(long Id, DateTimeOffset CreatedUtc);
public sealed record NotificationWriteResult(string IdempotencyKey, long Id, DateTimeOffset CreatedUtc,
    bool Created, int AddedRecipients, int ExistingRecipients, int DuplicateRecipients, int DuplicateEvents);
public sealed record NotificationBatchResult(IReadOnlyList<NotificationWriteResult> Notifications);

public interface INotificationWriter
{
    Task<NotificationEventResult> CreateEventAsync(CreateNotificationEventRequest request, CancellationToken cancellationToken);
    Task<int> AddRecipientsAsync(long eventId, IReadOnlyCollection<RecipientInput> recipients, CancellationToken cancellationToken);
    Task<NotificationBatchResult> CreateBatchAsync(NotificationBatchRequest request, CancellationToken cancellationToken);
}

public interface INotificationQueueStore
{
    Task<IReadOnlyList<ClaimedDelivery>> ClaimAsync(int batchSize, string workerId, TimeSpan lease, CancellationToken cancellationToken);
    Task<IReadOnlyList<ClaimedDelivery>> ClaimAsync(int batchSize, string workerId, TimeSpan lease, NotificationPriorityRange priorityRange, CancellationToken cancellationToken);
    Task<bool> RenewLeaseAsync(long id, string workerId, TimeSpan lease, CancellationToken cancellationToken);
    Task<bool> MarkSentAsync(long id, string workerId, string messageId, CancellationToken cancellationToken);
    Task<bool> MarkFailureAsync(long id, string workerId, string error, int maximumAttempts, TimeSpan? retryAfter, CancellationToken cancellationToken);
    Task<bool> RetryAsync(long id, CancellationToken cancellationToken);
}

public sealed record NotificationPriorityRange(NotificationPriority? Minimum = null, NotificationPriority? Maximum = null,
    TimeSpan? BulkPromotionAfter = null);

public interface INotificationStoreInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public sealed class NotificationConflictException() : Exception("An idempotency key was already used with different event content.");

public sealed class NotificationIngestionOptions
{
    public int MaximumEvents { get; set; } = 10_000;
    public int MaximumRecipients { get; set; } = 10_000;
    public long MaximumRequestBytes { get; set; } = 10 * 1024 * 1024;
}

public static class NotificationValidation
{
    public static string Normalize(string value) => value.Trim().ToUpperInvariant();

    public static void Validate(NotificationBatchRequest request, NotificationIngestionOptions options)
    {
        if (request?.Notifications is null || request.Notifications.Count == 0 || request.Notifications.Count > options.MaximumEvents)
            throw new ArgumentException($"Supply between 1 and {options.MaximumEvents} notifications.");
        long count = 0;
        foreach (var item in request.Notifications)
        {
            if (item is null) throw new ArgumentException("Notification must not be null.");
            Required(item.EventType, 200, "EventType");
            Required(item.Template, 200, "Template");
            Required(item.IdempotencyKey, 200, "IdempotencyKey");
            if (item.Message is null) throw new ArgumentException("Message is required.");
            Required(item.Message.Subject, int.MaxValue, "Subject");
            Required(item.Message.Text, int.MaxValue, "Text");
            ValidateRecipients(item.Recipients, options);
            count += item.Recipients.Count;
        }
        if (count > options.MaximumRecipients) throw new ArgumentException($"A batch may contain at most {options.MaximumRecipients} recipient entries.");
    }

    public static void ValidateRecipients(IReadOnlyCollection<RecipientInput> recipients, NotificationIngestionOptions options)
    {
        if (recipients is null || recipients.Count > options.MaximumRecipients)
            throw new ArgumentException($"Supply at most {options.MaximumRecipients} recipients.");
        foreach (var recipient in recipients)
        {
            if (recipient is null) throw new ArgumentException("Recipient must not be null.");
            Required(recipient.EmailAddress, 320, "EmailAddress");
            var address = recipient.EmailAddress.Trim();
            if (!MailAddress.TryCreate(address, out var parsed) || parsed.Address != address || Normalize(address).Length > 320)
                throw new ArgumentException("A recipient email address is invalid.");
        }
    }

    private static void Required(string value, int maximum, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
            throw new ArgumentException($"{name} is required and must not exceed {maximum} characters.");
    }
}
