using Casko.Messaging.Email.BulkDelivery;

namespace Casko.Messaging.Email.Api;

public static class DeliveryStatusEndpoints
{
    public static void MapDeliveryStatusEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/email-delivery/status").WithTags("Delivery Status");
        group.MapGet("/{deliveryBatchId:guid}", async (Guid deliveryBatchId, INotificationDeliveryStatus status, CancellationToken ct) =>
        {
            var result = await status.GetAsync(deliveryBatchId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetDeliveryBatchStatus")
        .WithSummary("Get delivery-batch progress")
        .WithDescription("Returns current database-aggregated delivery state for a committed batch.")
        .Produces<DeliveryBatchStatus>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
