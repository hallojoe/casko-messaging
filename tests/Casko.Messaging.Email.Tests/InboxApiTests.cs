using System.Net;
using Casko.Messaging.Email.Reading;
using Casko.Messaging.Email.Recipients;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Casko.Messaging.Email.Tests;

public sealed class InboxApiTests
{
    [Fact]
    public async Task Exposes_configured_mailboxes_and_nested_threads()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var mailboxes = await client.GetStringAsync("/api/mailboxes");
        var threads = await client.GetStringAsync("/api/mailboxes/Support/threads");
        var thread = await client.GetStringAsync("/api/mailboxes/Support/threads/Support:1:1");

        Assert.Contains("\"id\":\"Support\"", mailboxes);
        Assert.Contains("\"messageCount\":2", threads);
        Assert.Contains("\"parentId\":\"Support:1:1\"", thread);
        Assert.DoesNotContain("Content", thread, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Returns_not_found_for_unknown_mailbox_or_thread()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/mailboxes/Unknown/threads")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/mailboxes/Support/threads/missing")).StatusCode);
    }

    [Fact]
    public async Task Generates_openapi_document_in_development()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var document = await client.GetStringAsync("/openapi/v1.json");
        Assert.Contains("/api/mailboxes/{mailbox}/threads/{threadId}", document);
    }

    private static WebApplicationFactory<global::Program> CreateFactory() => new WebApplicationFactory<global::Program>()
        .WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:MailKit:Host"] = "localhost",
                ["Email:MailKit:FromAddress"] = "noreply@example.test",
                ["Email:MailKit:Mailboxes:Support:Address"] = "alice@example.test",
                ["Email:MailKit:Mailboxes:Support:Host"] = "localhost",
                ["Notifications:ApplyMigrations"] = "false"
            }))
            .ConfigureServices(services =>
            {
                services.RemoveAll<IEmailReader>();
                services.AddSingleton<IEmailReader>(new FakeEmailReader());
            }));

    private sealed class FakeEmailReader : IEmailReader
    {
        private static readonly IReadOnlyCollection<ReceivedEmailMessage> Messages =
        [
            Message("Support:1:1", "<root@example.test>", 1),
            Message("Support:1:2", "<reply@example.test>", 2, "<root@example.test>")
        ];

        public Task<IReadOnlyCollection<ReceivedEmailMessage>> ReadAsync(EmailMailboxId mailbox, EmailReadRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Messages);

        public Task<IReadOnlyCollection<ReceivedEmailMessage>> FindRepliesAsync(EmailMailboxId mailbox, EmailMessageReference parent, CancellationToken cancellationToken = default) =>
            Task.FromResult(Messages);

        private static ReceivedEmailMessage Message(string id, string messageId, int minute, string? inReplyTo = null) => new()
        {
            Id = id,
            MessageId = messageId,
            InReplyTo = inReplyTo,
            From = new EmailAddress { Address = "sender@example.test", DisplayName = "Sender" },
            Subject = "A subject",
            Text = "A message",
            ReceivedAt = new DateTimeOffset(2026, 9, 4, 9, minute, 0, TimeSpan.Zero)
        };
    }
}
