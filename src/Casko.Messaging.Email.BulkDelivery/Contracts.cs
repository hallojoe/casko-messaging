namespace Casko.Messaging.Email.BulkDelivery;

/// <summary>Represents the rendered content shared by all recipients of a notification event.</summary>
/// <param name="Subject">The email subject.</param>
/// <param name="Text">The required plain-text email body.</param>
/// <param name="Html">The optional HTML email body.</param>
public sealed record InlineEmailTemplate(string Subject, string Text, string? Html);

/// <summary>Identifies one recipient to add to a notification event.</summary>
/// <param name="EmailAddress">The recipient email address.</param>
/// <param name="RecipientId">The optional application-level identifier for the recipient.</param>
public sealed record RecipientInput(string EmailAddress, Guid? RecipientId = null);

/// <summary>Controls the relative order in which a notification delivery is claimed.</summary>
public enum NotificationPriority
{
    /// <summary>Campaign or other background mail that may yield to transactional work.</summary>
    Bulk,
    /// <summary>The default priority for transactional mail.</summary>
    Normal,
    /// <summary>Security-sensitive mail, such as password-reset messages.</summary>
    Critical
}

/// <summary>Creates one durable notification event without requiring its recipients to be supplied immediately.</summary>
/// <param name="EntityId">The application entity that caused the notification.</param>
/// <param name="EventType">The application-defined event type.</param>
/// <param name="Template">The application-defined template identifier.</param>
/// <param name="Message">The immutable content to send.</param>
/// <param name="IdempotencyKey">The stable key used to prevent duplicate event creation.</param>
/// <param name="Priority">The priority applied to deliveries subsequently created for the event.</param>
public sealed record CreateNotificationEventRequest(Guid EntityId, string EventType, string Template, InlineEmailTemplate Message, string IdempotencyKey,
    NotificationPriority Priority = NotificationPriority.Normal);

/// <summary>Represents a delivery atomically leased by a queue worker.</summary>
/// <param name="Id">The durable delivery identifier.</param>
/// <param name="NotificationEventId">The durable parent notification event identifier.</param>
/// <param name="EmailAddress">The recipient email address.</param>
/// <param name="Payload">The serialized event message payload.</param>
/// <param name="Template">The application-defined template identifier.</param>
/// <param name="Attempts">The number of completed send attempts before this lease.</param>
/// <param name="WorkerId">The worker that owns the lease.</param>
/// <param name="Priority">The delivery priority retained while it is processed or retried.</param>
public sealed record ClaimedDelivery(long Id, long NotificationEventId, string EmailAddress, string Payload, string Template, int Attempts, string WorkerId,
    NotificationPriority Priority);
