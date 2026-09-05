using Casko.Messaging.Email.BulkDelivery;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace Casko.Messaging.Email.Api;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        // Middleware executes before minimal API JSON binding, including for chunked request bodies.
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
            return Results.Accepted($"/api/notifications/{notification.Id}", new { notification.Id, notification.CreatedUtc });
        });
        group.MapPost("/batch", async (NotificationBatchRequest request, INotificationWriter writer, CancellationToken ct) =>
            Results.Ok(await writer.CreateBatchAsync(request, ct)));
        group.MapPost("/{eventId:long}/recipients", async (long eventId, IReadOnlyCollection<RecipientInput> recipients,
            INotificationWriter writer, CancellationToken ct) =>
            Results.Ok(new { added = await writer.AddRecipientsAsync(eventId, recipients, ct) }));
        group.MapPost("/deliveries/{deliveryId:long}/retry", async (long deliveryId, INotificationQueueStore store, CancellationToken ct) =>
            await store.RetryAsync(deliveryId, ct) ? Results.Accepted() : Results.NotFound());
        group.MapPost("/demo/test-inboxes", async (INotificationWriter writer, CancellationToken ct) =>
        {
            const int count = 100;
            var recipients = new[] { "alice@example.test", "bob@example.test" };
            var notifications = Enumerable.Range(0, count).Select(index => new NotificationInput(
                Guid.NewGuid(), "DevelopmentInboxTest", "development-inbox-test",
                new InlineEmailTemplate($"Bulk delivery test {index + 1} of {count}",
                    "This message was queued by the bulk-email development scenario.",
                    $"<p>This message was queued by the bulk-email development scenario.</p><p><strong>{index + 1} of {count}</strong></p>"),
                $"development-inbox-test-{Guid.NewGuid():N}", [new RecipientInput(recipients[index % recipients.Length])])).ToArray();
            await writer.CreateBatchAsync(new(notifications), ct);
            return Results.Accepted(value: new { queued = count, recipients });
        });
    }
}
