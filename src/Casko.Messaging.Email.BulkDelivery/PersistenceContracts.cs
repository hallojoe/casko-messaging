using System.Net.Mail;

namespace Casko.Messaging.Email.BulkDelivery;

/// <summary>Describes one notification event and all recipients to create atomically.</summary>
/// <param name="EntityId">The application entity that caused the notification.</param>
/// <param name="EventType">The application-defined event type.</param>
/// <param name="Template">The application-defined template identifier.</param>
/// <param name="Message">The immutable message content to send.</param>
/// <param name="IdempotencyKey">The stable key used to deduplicate the event.</param>
/// <param name="Recipients">The recipients to create for the event.</param>
/// <param name="Priority">The priority assigned to every created delivery.</param>
public sealed record NotificationInput(Guid EntityId, string EventType, string Template, InlineEmailTemplate Message,
    string IdempotencyKey, IReadOnlyList<RecipientInput> Recipients, NotificationPriority Priority = NotificationPriority.Normal);

/// <summary>Groups notification events that must be created atomically.</summary>
/// <param name="Notifications">The notification events to create.</param>
/// <param name="DeliveryBatchId">The optional stable correlation identifier; one is generated when omitted.</param>
public sealed record NotificationBatchRequest(IReadOnlyList<NotificationInput> Notifications, Guid? DeliveryBatchId = null);

/// <summary>Identifies a durable notification event created or found by an idempotent request.</summary>
/// <param name="Id">The durable event identifier.</param>
/// <param name="CreatedUtc">The timestamp at which the event was first created.</param>
/// <param name="DeliveryBatchId">The batch that owns deliveries created for this event.</param>
public sealed record NotificationEventResult(long Id, DateTimeOffset CreatedUtc, Guid DeliveryBatchId);

/// <summary>Reports the committed outcome for one distinct notification event key.</summary>
/// <param name="IdempotencyKey">The event key supplied by the caller.</param>
/// <param name="Id">The durable event identifier.</param>
/// <param name="CreatedUtc">The timestamp at which the event was first created.</param>
/// <param name="Created">Whether this request created the event.</param>
/// <param name="AddedRecipients">The number of new deliveries created.</param>
/// <param name="ExistingRecipients">The number of supplied recipients already persisted for the event.</param>
/// <param name="DuplicateRecipients">The number of duplicate recipient entries in the request.</param>
/// <param name="DuplicateEvents">The number of repeated equivalent event entries in the request.</param>
public sealed record NotificationWriteResult(string IdempotencyKey, long Id, DateTimeOffset CreatedUtc,
    bool Created, int AddedRecipients, int ExistingRecipients, int DuplicateRecipients, int DuplicateEvents);

/// <summary>Reports the committed outcome of an atomic notification batch.</summary>
/// <param name="Notifications">One result for each distinct notification event key.</param>
/// <param name="DeliveryBatchId">The stable identifier for the logical delivery batch.</param>
public sealed record NotificationBatchResult(IReadOnlyList<NotificationWriteResult> Notifications, Guid DeliveryBatchId);

/// <summary>Reports the current aggregate state of one persisted delivery batch.</summary>
/// <param name="DeliveryBatchId">The identifier of the batch queried.</param>
/// <param name="Total">The total persisted deliveries in the batch.</param>
/// <param name="Pending">Deliveries eligible to be claimed.</param>
/// <param name="Processing">Deliveries currently leased by workers.</param>
/// <param name="Retrying">Deliveries waiting for a scheduled retry.</param>
/// <param name="Delivered">Deliveries accepted by SMTP.</param>
/// <param name="Failed">Deliveries that failed permanently.</param>
public sealed record DeliveryBatchStatus(Guid DeliveryBatchId, long Total, long Pending, long Processing, long Retrying, long Delivered, long Failed)
{
    /// <summary>Gets the total number of terminal deliveries.</summary>
    public long Completed => Delivered + Failed;
    /// <summary>Gets the completion percentage in the range 0 through 100.</summary>
    public double Progress => Total == 0 ? 100 : (double)Completed / Total * 100;
    /// <summary>Gets whether no delivery remains pending, leased, or scheduled for retry.</summary>
    public bool IsComplete => Pending == 0 && Processing == 0 && Retrying == 0;
}

/// <summary>Writes notification events and recipient deliveries durably and idempotently.</summary>
public interface INotificationWriter
{
    /// <summary>Creates or finds one notification event.</summary>
    /// <param name="request">The event to create or find.</param>
    /// <param name="cancellationToken">Cancels the operation before it commits.</param>
    /// <returns>The durable event identifier and original creation time.</returns>
    Task<NotificationEventResult> CreateEventAsync(CreateNotificationEventRequest request, CancellationToken cancellationToken);

    /// <summary>Adds recipients to an existing notification event without duplicating deliveries.</summary>
    /// <param name="eventId">The durable event identifier.</param>
    /// <param name="recipients">The recipients to add.</param>
    /// <param name="cancellationToken">Cancels the operation before it commits.</param>
    /// <returns>The number of deliveries created.</returns>
    Task<int> AddRecipientsAsync(long eventId, IReadOnlyCollection<RecipientInput> recipients, CancellationToken cancellationToken);

    /// <summary>Creates all events and recipients in the request within one atomic commit.</summary>
    /// <param name="request">The batch to create.</param>
    /// <param name="cancellationToken">Cancels the operation before it commits.</param>
    /// <returns>The committed result for each distinct event key.</returns>
    Task<NotificationBatchResult> CreateBatchAsync(NotificationBatchRequest request, CancellationToken cancellationToken);
}

/// <summary>Claims and updates durable recipient deliveries for a transport worker.</summary>
public interface INotificationQueueStore
{
    /// <summary>Claims the highest-priority eligible deliveries.</summary>
    /// <param name="batchSize">The maximum number of deliveries to lease.</param>
    /// <param name="workerId">The identifier recorded as the lease owner.</param>
    /// <param name="lease">How long the lease remains valid without renewal.</param>
    /// <param name="cancellationToken">Cancels the claim attempt.</param>
    /// <returns>The deliveries leased to the worker.</returns>
    Task<IReadOnlyList<ClaimedDelivery>> ClaimAsync(int batchSize, string workerId, TimeSpan lease, CancellationToken cancellationToken);

    /// <summary>Claims eligible deliveries restricted to a priority range.</summary>
    /// <param name="batchSize">The maximum number of deliveries to lease.</param>
    /// <param name="workerId">The identifier recorded as the lease owner.</param>
    /// <param name="lease">How long the lease remains valid without renewal.</param>
    /// <param name="priorityRange">The priorities that the caller is permitted to claim.</param>
    /// <param name="cancellationToken">Cancels the claim attempt.</param>
    /// <returns>The deliveries leased to the worker.</returns>
    Task<IReadOnlyList<ClaimedDelivery>> ClaimAsync(int batchSize, string workerId, TimeSpan lease, NotificationPriorityRange priorityRange, CancellationToken cancellationToken);

    /// <summary>Extends the live lease held by a worker.</summary>
    /// <param name="id">The delivery identifier.</param>
    /// <param name="workerId">The current lease owner.</param>
    /// <param name="lease">The new lease duration.</param>
    /// <param name="cancellationToken">Cancels the update attempt.</param>
    /// <returns><see langword="true"/> when the worker still owns a live lease; otherwise, <see langword="false"/>.</returns>
    Task<bool> RenewLeaseAsync(long id, string workerId, TimeSpan lease, CancellationToken cancellationToken);

    /// <summary>Records a successful SMTP submission for a live worker lease.</summary>
    /// <param name="id">The delivery identifier.</param>
    /// <param name="workerId">The current lease owner.</param>
    /// <param name="messageId">The SMTP message identifier.</param>
    /// <param name="cancellationToken">Cancels the update attempt.</param>
    /// <returns><see langword="true"/> when the sent status was recorded; otherwise, <see langword="false"/>.</returns>
    Task<bool> MarkSentAsync(long id, string workerId, string messageId, CancellationToken cancellationToken);

    /// <summary>Records a failed attempt and either schedules a retry or marks the delivery permanently failed.</summary>
    /// <param name="id">The delivery identifier.</param>
    /// <param name="workerId">The current lease owner.</param>
    /// <param name="error">The error message to retain for diagnostics.</param>
    /// <param name="maximumAttempts">The configured maximum send-attempt count.</param>
    /// <param name="retryAfter">The retry delay, or <see langword="null"/> to fail permanently.</param>
    /// <param name="cancellationToken">Cancels the update attempt.</param>
    /// <returns><see langword="true"/> when the failure was recorded; otherwise, <see langword="false"/>.</returns>
    Task<bool> MarkFailureAsync(long id, string workerId, string error, int maximumAttempts, TimeSpan? retryAfter, CancellationToken cancellationToken);

    /// <summary>Returns a permanently failed delivery to the pending queue.</summary>
    /// <param name="id">The delivery identifier.</param>
    /// <param name="cancellationToken">Cancels the update attempt.</param>
    /// <returns><see langword="true"/> when the delivery was reset; otherwise, <see langword="false"/>.</returns>
    Task<bool> RetryAsync(long id, CancellationToken cancellationToken);
}

/// <summary>Retrieves database-aggregated progress for durable delivery batches.</summary>
public interface INotificationDeliveryStatus
{
    /// <summary>Retrieves the current status for a batch.</summary>
    /// <param name="deliveryBatchId">The batch identifier returned when it was queued.</param>
    /// <param name="cancellationToken">Cancels the database query.</param>
    /// <returns>The aggregate status, or <see langword="null"/> when no persisted deliveries belong to the batch.</returns>
    Task<DeliveryBatchStatus?> GetAsync(Guid deliveryBatchId, CancellationToken cancellationToken = default);
}

/// <summary>Restricts a queue claim to a range of priorities and controls bulk aging.</summary>
/// <param name="Minimum">The lowest priority eligible for the claim, or <see langword="null"/> for no lower bound.</param>
/// <param name="Maximum">The highest priority eligible for the claim, or <see langword="null"/> for no upper bound.</param>
/// <param name="BulkPromotionAfter">The age at which bulk work is ordered as normal work, or <see langword="null"/> to disable aging.</param>
public sealed record NotificationPriorityRange(NotificationPriority? Minimum = null, NotificationPriority? Maximum = null,
    TimeSpan? BulkPromotionAfter = null);

/// <summary>Initializes provider-owned notification persistence, including pending schema migrations.</summary>
public interface INotificationStoreInitializer
{
    /// <summary>Initializes the configured notification store.</summary>
    /// <param name="cancellationToken">Cancels initialization.</param>
    /// <returns>A task that completes after initialization.</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>Indicates that an idempotency key was reused with different immutable event content.</summary>
public sealed class NotificationConflictException() : Exception("An idempotency key was already used with different event content.");

/// <summary>Configures the maximum size of one notification ingestion request.</summary>
public sealed class NotificationIngestionOptions
{
    /// <summary>Gets or sets the maximum number of notification events in one batch.</summary>
    public int MaximumEvents { get; set; } = 10_000;

    /// <summary>Gets or sets the maximum total recipient entries in one batch.</summary>
    public int MaximumRecipients { get; set; } = 10_000;

    /// <summary>Gets or sets the maximum HTTP request-body size in bytes.</summary>
    public long MaximumRequestBytes { get; set; } = 10 * 1024 * 1024;
}

/// <summary>Validates shared notification contracts before a provider writes them.</summary>
public static class NotificationValidation
{
    /// <summary>Produces the canonical email-address key used for recipient deduplication.</summary>
    /// <param name="value">The email address to normalize.</param>
    /// <returns>The trimmed, uppercase address.</returns>
    public static string Normalize(string value) => value.Trim().ToUpperInvariant();

    /// <summary>Validates a notification batch against configured limits and shared field rules.</summary>
    /// <param name="request">The batch to validate.</param>
    /// <param name="options">The applicable ingestion limits.</param>
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

    /// <summary>Validates recipient input against configured limits and email-address rules.</summary>
    /// <param name="recipients">The recipients to validate.</param>
    /// <param name="options">The applicable ingestion limits.</param>
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
