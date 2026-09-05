namespace Casko.Messaging.Email.BulkDelivery;

public sealed class NotificationDelivery
{
    public long Id { get; set; }
    public long NotificationEventId { get; set; }
    public NotificationEvent? NotificationEvent { get; set; }
    public Guid? RecipientId { get; set; }
    public required string EmailAddress { get; set; }
    public required string NormalizedEmailAddress { get; set; }
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;
    public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? LastAttemptUtc { get; set; }
    public DateTimeOffset? NextAttemptUtc { get; set; }
    public DateTimeOffset? SentUtc { get; set; }
    public DateTimeOffset? ProcessingStartedUtc { get; set; }
    public DateTimeOffset? ProcessingLeaseUntilUtc { get; set; }
    public string? ProcessingWorkerId { get; set; }
    public string? LastError { get; set; }
    public string? SmtpMessageId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
