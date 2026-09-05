namespace Casko.Messaging.Email.BulkDelivery;

public sealed record InlineEmailTemplate(string Subject, string Text, string? Html);
public sealed record RecipientInput(string EmailAddress, Guid? RecipientId = null);
public enum NotificationPriority { Bulk, Normal, Critical }
public sealed record CreateNotificationEventRequest(Guid EntityId, string EventType, string Template, InlineEmailTemplate Message, string IdempotencyKey,
    NotificationPriority Priority = NotificationPriority.Normal);
public sealed record ClaimedDelivery(long Id, long NotificationEventId, string EmailAddress, string Payload, string Template, int Attempts, string WorkerId,
    NotificationPriority Priority);
