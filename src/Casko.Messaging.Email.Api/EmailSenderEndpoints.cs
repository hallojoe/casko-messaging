using System.Text;
using Casko.Messaging.Email;
using Casko.Messaging.Email.Api.Contracts;
using Casko.Messaging.Email.Api.Services;
using Casko.Messaging.Email.Attachments;
using Casko.Messaging.Email.Delivery;
using Casko.Messaging.Email.Extensions;
using Casko.Messaging.Email.Reading;
using Casko.Messaging.Email.Recipients;
using Casko.Messaging.Email.Threading;

namespace Casko.Messaging.Email.Api;

public static class EmailSenderEndpoints
{
    private static readonly EmailAddress Sender = new() { Address = "support@casko.dev.local", DisplayName = "Casko Support" };
    private static readonly EmailAddress Alice = new() { Address = "alice@example.local", DisplayName = "Alice" };

    public static void MapEmailSenderEndpoints(this WebApplication app)
    {
        app.MapPost("/email/single", async (IEmailSender sender, CancellationToken ct) =>
            Results.Accepted(value: await sender.SendAsync(Alice, new EmailMessage { Subject = "Single email", Text = "This is a plain-text email." }, ct)));

        app.MapPost("/email/multiple-recipients", async (IEmailSender sender, CancellationToken ct) =>
            Results.Accepted(value: await sender.SendAsync(new EmailDelivery
            {
                Message = new EmailMessage { Subject = "Team update", Text = "Plain-text fallback.", Html = "<h1>Team update</h1><p>This version includes HTML.</p>" },
                Recipients = [new EmailRecipient { Address = Alice }, new EmailRecipient { Address = new EmailAddress { Address = "bob@example.local" }, Type = EmailRecipientType.Cc }, new EmailRecipient { Address = new EmailAddress { Address = "private@example.test" }, Type = EmailRecipientType.Bcc }],
                ReplyTo = Sender
            }, ct)));

        app.MapPost("/email/personalized", async (IEmailSender sender, CancellationToken ct) =>
        {
            var recipients = new[] { "Alice", "Bob", "Chris" };
            var results = await sender.SendAsync(recipients.Select(name => new EmailDelivery
            {
                Message = new EmailMessage { Subject = $"Hello {name}", Text = $"Hello {name}, this is your personal message." },
                Recipients = [new EmailRecipient { Address = new EmailAddress { Address = $"{name.ToLowerInvariant()}@example.local", DisplayName = name } }]
            }), ct);
            return Results.Accepted(value: results);
        });

        app.MapPost("/email/attachment", async (IEmailSender sender, CancellationToken ct) =>
            Results.Accepted(value: await sender.SendAsync(Alice, new EmailMessage
            {
                Subject = "Attachment example", Text = "The attached text file demonstrates attachments.",
                Attachments = [new EmailAttachment { FileName = "example.txt", ContentType = "text/plain", Content = Encoding.UTF8.GetBytes("Casko Messaging attachment.") }]
            }, ct)));

        app.MapPost("/email/inline-image", async (IEmailSender sender, CancellationToken ct) =>
        {
            var pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9WlWZc8AAAAASUVORK5CYII=");
            var result = await sender.SendAsync(Alice, new EmailMessage
            {
                Subject = "Inline image example", Text = "This message contains an inline image.", Html = "<p>This message contains an inline image:</p><img alt=\"pixel\" src=\"cid:logo\">",
                Attachments = [new EmailAttachment { FileName = "logo.png", ContentType = "image/png", Content = pixel, Disposition = EmailAttachmentDisposition.Inline, ContentId = "logo" }]
            }, ct);
            return Results.Accepted(value: result);
        });

        app.MapGet("/email/mailboxes/{mailbox}/messages", async (string mailbox, IEmailReader reader, CancellationToken ct) =>
            Results.Ok(await reader.ReadAsync(new EmailMailboxId(mailbox), new EmailReadRequest(), ct)));
        app.MapGet("/email/mailboxes/{mailbox}/unread", async (string mailbox, IEmailReader reader, CancellationToken ct) =>
            Results.Ok(await reader.ReadAsync(new EmailMailboxId(mailbox), new EmailReadRequest { UnreadOnly = true }, ct)));
        app.MapGet("/email/mailboxes/{mailbox}/replies/{messageId}", async (string mailbox, string messageId, IEmailReader reader, CancellationToken ct) =>
            Results.Ok(await reader.FindRepliesAsync(new EmailMailboxId(mailbox), new EmailMessageReference { MessageId = messageId }, ct)));

        MapInboxEndpoints(app);
        app.MapPost("/email/reply", async (EmailReplyRequest request, IEmailSender sender, CancellationToken ct) =>
        {
            var result = await sender.SendAsync(new EmailDelivery
            {
                Message = new EmailMessage { Subject = request.Subject, Text = request.Text, Html = request.Html },
                Recipients = [new EmailRecipient { Address = request.Recipient }],
                ReplyToMessage = new EmailMessageReference { MessageId = request.MessageId, References = request.References ?? [] }
            }, ct);
            return Results.Accepted(value: result);
        });
    }

    private static void MapInboxEndpoints(WebApplication app)
    {
        var inbox = app.MapGroup("/api").WithTags("Inbox viewer");
        inbox.MapGet("/mailboxes", (InboxQueryService service) => Results.Ok(service.GetMailboxes())).WithName("GetMailboxes").Produces<IReadOnlyCollection<MailboxResponse>>();
        inbox.MapGet("/mailboxes/{mailbox}/threads", async (string mailbox, InboxQueryService service, CancellationToken ct) =>
            !service.GetMailboxes().Any(item => string.Equals(item.Id, mailbox, StringComparison.OrdinalIgnoreCase))
                ? Results.NotFound() : Results.Ok(await service.GetThreadsAsync(mailbox, ct)))
            .WithName("GetMailboxThreads").Produces<IReadOnlyCollection<EmailThreadSummaryResponse>>().Produces(StatusCodes.Status404NotFound);
        inbox.MapGet("/mailboxes/{mailbox}/threads/{threadId}", async (string mailbox, string threadId, InboxQueryService service, CancellationToken ct) =>
        {
            if (!service.GetMailboxes().Any(item => string.Equals(item.Id, mailbox, StringComparison.OrdinalIgnoreCase))) return Results.NotFound();
            var thread = await service.GetThreadAsync(mailbox, threadId, ct);
            return thread is null ? Results.NotFound() : Results.Ok(thread);
        }).WithName("GetMailboxThread").Produces<EmailThreadResponse>().Produces(StatusCodes.Status404NotFound);
    }
}

public sealed record EmailReplyRequest
{
    public required string MessageId { get; init; }
    public IReadOnlyCollection<string>? References { get; init; }
    public required EmailAddress Recipient { get; init; }
    public required string Subject { get; init; }
    public required string Text { get; init; }
    public string? Html { get; init; }
}
