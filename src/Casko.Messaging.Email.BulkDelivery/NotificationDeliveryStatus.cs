namespace Casko.Messaging.Email.BulkDelivery;

/// <summary>Describes the durable state of an individual recipient delivery.</summary>
public enum NotificationDeliveryStatus
{
    /// <summary>The delivery is eligible for claiming.</summary>
    Pending,
    /// <summary>The delivery is leased by a worker.</summary>
    Processing,
    /// <summary>The delivery is waiting until its next retry time.</summary>
    Retry,
    /// <summary>The SMTP server accepted the delivery.</summary>
    Sent,
    /// <summary>The delivery has exhausted retries or failed permanently.</summary>
    Failed
}
