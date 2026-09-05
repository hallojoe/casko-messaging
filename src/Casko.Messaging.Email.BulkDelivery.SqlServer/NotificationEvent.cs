namespace Casko.Messaging.Email.BulkDelivery;

public sealed class NotificationEvent
{
    public long Id { get; set; }
    public Guid EntityId { get; set; }
    public required string EventType { get; set; }
    public required string Template { get; set; }
    public required string Payload { get; set; }
    public required string IdempotencyKey { get; set; }
    public Guid? DeliveryBatchId { get; set; }
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;
    public DateTimeOffset CreatedUtc { get; set; }
    public ICollection<NotificationDelivery> Deliveries { get; } = new List<NotificationDelivery>();
}
