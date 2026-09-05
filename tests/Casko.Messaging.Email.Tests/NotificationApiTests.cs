using System.Net;
using System.Net.Http.Json;
using Casko.Messaging.Email.BulkDelivery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Casko.Messaging.Email.Tests;

public sealed class NotificationApiTests
{
    private static WebApplicationFactory<global::Program> Factory() => new WebApplicationFactory<global::Program>()
        .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:ApplyMigrations"] = "false",
            ["Notifications:Ingestion:MaximumRequestBytes"] = "4096",
            ["Email:MailKit:Host"] = "localhost", ["Email:MailKit:FromAddress"] = "noreply@example.test"
        })).ConfigureServices(s =>
        {
            s.RemoveAll<INotificationWriter>();
            s.AddSingleton<INotificationWriter, FakeWriter>();
            s.RemoveAll<INotificationDeliveryStatus>();
            s.AddSingleton<INotificationDeliveryStatus, FakeDeliveryStatus>();
        }));

    [Fact]
    public async Task Batch_and_existing_endpoints_preserve_response_shapes()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        var message = new InlineEmailTemplate("Subject", "Text", null);
        var single = await client.PostAsJsonAsync("/api/notifications", new CreateNotificationEventRequest(Guid.Empty, "Test", "inline", message, "key"));
        Assert.Equal(HttpStatusCode.Accepted, single.StatusCode);
        Assert.Equal("/api/notifications/42", single.Headers.Location?.OriginalString);
        Assert.Contains("\"createdUtc\"", await single.Content.ReadAsStringAsync());
        Assert.Contains("\"deliveryBatchId\"", await single.Content.ReadAsStringAsync());
        var recipients = await client.PostAsJsonAsync("/api/notifications/42/recipients", new[] { new RecipientInput("a@example.test") });
        Assert.Equal("{\"added\":1}", await recipients.Content.ReadAsStringAsync());
        var batch = await client.PostAsJsonAsync("/api/notifications/batch",
            new NotificationBatchRequest([new(Guid.Empty, "Test", "inline", message, "key", [new("a@example.test")])]));
        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        Assert.Contains("\"addedRecipients\":1", await batch.Content.ReadAsStringAsync());
        var status = await client.GetAsync("/api/email-delivery/status/11111111-1111-1111-1111-111111111111");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Contains("\"completed\":3", await status.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/email-delivery/status/22222222-2222-2222-2222-222222222222")).StatusCode);
    }

    [Fact]
    public async Task Maps_conflicts_validation_and_body_limits()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        var message = new InlineEmailTemplate("Subject", "Text", null);
        var conflict = await client.PostAsJsonAsync("/api/notifications", new CreateNotificationEventRequest(Guid.Empty, "Test", "inline", message, "conflict"));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var invalid = await client.PostAsJsonAsync("/api/notifications/batch", new NotificationBatchRequest([]));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var oversized = await client.PostAsync("/api/notifications/batch", new StringContent(new string('x', 4097), System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
    }

    [Fact]
    public async Task Development_openapi_document_includes_documented_notification_endpoints()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal("3.1.1", document.RootElement.GetProperty("openapi").GetString());
        Assert.Equal("Create an atomic notification batch", document.RootElement
            .GetProperty("paths").GetProperty("/api/notifications/batch").GetProperty("post").GetProperty("summary").GetString());
    }

    private sealed class FakeWriter : INotificationWriter
    {
        public Task<NotificationEventResult> CreateEventAsync(CreateNotificationEventRequest request, CancellationToken ct)
        {
            if (request.IdempotencyKey == "conflict") throw new NotificationConflictException();
            return Task.FromResult(new NotificationEventResult(42, DateTimeOffset.UnixEpoch, Guid.Parse("11111111-1111-1111-1111-111111111111")));
        }
        public Task<int> AddRecipientsAsync(long eventId, IReadOnlyCollection<RecipientInput> recipients, CancellationToken ct) => Task.FromResult(recipients.Count);
        public Task<NotificationBatchResult> CreateBatchAsync(NotificationBatchRequest request, CancellationToken ct)
        {
            NotificationValidation.Validate(request, new());
            var deliveryBatchId = request.DeliveryBatchId ?? Guid.Parse("11111111-1111-1111-1111-111111111111");
            return Task.FromResult(new NotificationBatchResult(request.Notifications.Select(n =>
                new NotificationWriteResult(n.IdempotencyKey, 42, DateTimeOffset.UnixEpoch, true, n.Recipients.Count, 0, 0, 0)).ToArray(), deliveryBatchId));
        }
    }

    private sealed class FakeDeliveryStatus : INotificationDeliveryStatus
    {
        public Task<DeliveryBatchStatus?> GetAsync(Guid deliveryBatchId, CancellationToken ct = default) =>
            Task.FromResult<DeliveryBatchStatus?>(deliveryBatchId == Guid.Parse("11111111-1111-1111-1111-111111111111")
                ? new(deliveryBatchId, 5, 1, 0, 1, 2, 1) : null);
    }
}
