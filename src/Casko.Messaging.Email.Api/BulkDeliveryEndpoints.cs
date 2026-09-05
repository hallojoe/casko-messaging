using Casko.Messaging.Email.BulkDelivery;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace Casko.Messaging.Email.Api;

public static class BulkDeliveryEndpoints
{
    public static void MapBulkDeliveryEndpoints(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/notifications") && HttpMethods.IsPost(context.Request.Method))
            {
                var limit = context.RequestServices.GetRequiredService<IOptions<NotificationIngestionOptions>>().Value.MaximumRequestBytes;
                if (context.Request.ContentLength > limit)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }
                var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = limit;
            }
            await next(context);
        });

        var group = app.MapGroup("/api/notifications").WithTags("Bulk notifications");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (NotificationConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });
        group.MapPost("", async (CreateNotificationEventRequest request, INotificationWriter writer, CancellationToken ct) =>
        {
            var notification = await writer.CreateEventAsync(request, ct);
            return Results.Accepted($"/api/notifications/{notification.Id}", new { notification.Id, notification.CreatedUtc, notification.DeliveryBatchId });
        })
        .WithName("CreateNotification")
        .WithSummary("Create one notification event")
        .WithDescription("Creates an idempotent notification event and returns its delivery-batch identifier.")
        .Produces(StatusCodes.Status202Accepted);
        group.MapPost("/batch", async (NotificationBatchRequest request, INotificationWriter writer, CancellationToken ct) =>
            Results.Ok(await writer.CreateBatchAsync(request, ct)))
            .WithName("CreateNotificationBatch")
            .WithSummary("Create an atomic notification batch")
            .WithDescription("Creates all notification events and recipient deliveries in one transaction.")
            .Produces<NotificationBatchResult>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict);
        group.MapPost("/{eventId:long}/recipients", async (long eventId, IReadOnlyCollection<RecipientInput> recipients,
            INotificationWriter writer, CancellationToken ct) => Results.Ok(new { added = await writer.AddRecipientsAsync(eventId, recipients, ct) }))
            .WithName("AddNotificationRecipients")
            .WithSummary("Add recipients to a notification")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
        group.MapPost("/deliveries/{deliveryId:long}/retry", async (long deliveryId, INotificationQueueStore store, CancellationToken ct) =>
            await store.RetryAsync(deliveryId, ct) ? Results.Accepted() : Results.NotFound())
            .WithName("RetryNotificationDelivery")
            .WithSummary("Retry a failed notification delivery")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound);
    }
}
