using Microsoft.EntityFrameworkCore;

namespace Casko.Messaging.Email.BulkDelivery;

/// <summary>Aggregates delivery-batch progress in SQL Server without materializing delivery rows.</summary>
public sealed class SqlServerNotificationDeliveryStatus(NotificationDbContext db) : INotificationDeliveryStatus
{
    /// <inheritdoc />
    public async Task<DeliveryBatchStatus?> GetAsync(Guid deliveryBatchId, CancellationToken cancellationToken = default)
    {
        var status = await db.NotificationDeliveries
            .Where(delivery => delivery.DeliveryBatchId == deliveryBatchId)
            .GroupBy(_ => 1)
            .Select(group => new DeliveryBatchStatus(
                deliveryBatchId,
                group.LongCount(),
                group.LongCount(delivery => delivery.Status == NotificationDeliveryStatus.Pending),
                group.LongCount(delivery => delivery.Status == NotificationDeliveryStatus.Processing),
                group.LongCount(delivery => delivery.Status == NotificationDeliveryStatus.Retry),
                group.LongCount(delivery => delivery.Status == NotificationDeliveryStatus.Sent),
                group.LongCount(delivery => delivery.Status == NotificationDeliveryStatus.Failed)))
            .SingleOrDefaultAsync(cancellationToken);
        return status;
    }
}
