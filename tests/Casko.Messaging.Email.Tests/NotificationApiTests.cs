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
        var recipients = await client.PostAsJsonAsync("/api/notifications/42/recipients", new[] { new RecipientInput("a@example.test") });
        Assert.Equal("{\"added\":1}", await recipients.Content.ReadAsStringAsync());
        var batch = await client.PostAsJsonAsync("/api/notifications/batch",
            new NotificationBatchRequest([new(Guid.Empty, "Test", "inline", message, "key", [new("a@example.test")])]));
        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        Assert.Contains("\"addedRecipients\":1", await batch.Content.ReadAsStringAsync());
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

    private sealed class FakeWriter : INotificationWriter
    {
        public Task<NotificationEventResult> CreateEventAsync(CreateNotificationEventRequest request, CancellationToken ct)
        {
            if (request.IdempotencyKey == "conflict") throw new NotificationConflictException();
            return Task.FromResult(new NotificationEventResult(42, DateTimeOffset.UnixEpoch));
        }
        public Task<int> AddRecipientsAsync(long eventId, IReadOnlyCollection<RecipientInput> recipients, CancellationToken ct) => Task.FromResult(recipients.Count);
        public Task<NotificationBatchResult> CreateBatchAsync(NotificationBatchRequest request, CancellationToken ct)
        {
            NotificationValidation.Validate(request, new());
            return Task.FromResult(new NotificationBatchResult(request.Notifications.Select(n =>
                new NotificationWriteResult(n.IdempotencyKey, 42, DateTimeOffset.UnixEpoch, true, n.Recipients.Count, 0, 0, 0)).ToArray()));
        }
    }
}
