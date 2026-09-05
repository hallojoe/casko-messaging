using Casko.Messaging.Email;
using Casko.Messaging.Email.BulkDelivery;
using Casko.Messaging.Email.Delivery;
using Casko.Messaging.Email.MailKit.Configuration;
using Casko.Messaging.Email.Recipients;
using Casko.Messaging.Email.Reading;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Utils;

namespace Casko.Messaging.Email.Api;

public static class DemoFlowsEndpoints
{
    public static void MapDemoFlowsEndpoints(this WebApplication app)
    {
        app.MapPost("/api/notifications/demo/test-inboxes", QueueBulkInboxDemoAsync)
            .WithTags("Demo data").WithName("QueueBulkInboxDemo").WithSummary("Queue bulk inbox demo messages").Produces(StatusCodes.Status202Accepted);
        app.MapPost("/email/support/seed", SeedSupportConversationAsync)
            .WithTags("Demo data").WithName("SeedSupportConversation").WithSummary("Seed a support conversation").Produces(StatusCodes.Status201Created);
        app.MapPost("/email/demo/seed", SeedInboxDemoAsync)
            .WithTags("Demo data").WithName("SeedInboxDemo").WithSummary("Seed inbox demo conversations").Produces(StatusCodes.Status201Created);
    }

    private static async Task<IResult> QueueBulkInboxDemoAsync(INotificationWriter writer, CancellationToken ct)
    {
        const int count = 100;
        var recipients = new[] { "alice@example.test", "bob@example.test" };
        var notifications = Enumerable.Range(0, count).Select(index => new NotificationInput(
            Guid.NewGuid(), "DevelopmentInboxTest", "development-inbox-test",
            new InlineEmailTemplate($"Bulk delivery test {index + 1} of {count}",
                "This message was queued by the bulk-email development scenario.",
                $"<p>This message was queued by the bulk-email development scenario.</p><p><strong>{index + 1} of {count}</strong></p>"),
            $"development-inbox-test-{Guid.NewGuid():N}", [new RecipientInput(recipients[index % recipients.Length])], NotificationPriority.Bulk)).ToArray();
        await writer.CreateBatchAsync(new(notifications), ct);
        return Results.Accepted(value: new { queued = count, recipients });
    }

    private static async Task<IResult> SeedSupportConversationAsync(IConfiguration configuration, IEmailSender sender, CancellationToken ct)
    {
        const string supportAddress = "alice@example.test";
        var customer = new EmailAddress { Address = "customer@example.test", DisplayName = "Jamie Customer" };
        var supportRequest = await SeedIncomingMessageAsync(configuration, customer, "Help needed with my order", "Hello, I need help with order #12345.", null, [], ct);
        var applicationReply = await sender.SendAsync(new EmailDelivery
        {
            Message = new EmailMessage { Subject = "Re: Help needed with my order", Text = "Thanks Jamie. We are looking into order #12345." },
            Recipients = [new EmailRecipient { Address = customer }],
            ReplyTo = new EmailAddress { Address = supportAddress, DisplayName = "Casko Support" },
            ReplyToMessage = supportRequest
        }, ct);
        var firstReply = await SeedIncomingMessageAsync(configuration, customer, "Re: Help needed with my order", "Thank you. Could you also confirm when it will ship?", applicationReply.MessageId, [supportRequest.MessageId, applicationReply.MessageId], ct);
        var secondReply = await SeedIncomingMessageAsync(configuration, customer, "Re: Help needed with my order", "Following up on my shipping question.", firstReply.MessageId, [supportRequest.MessageId, applicationReply.MessageId, firstReply.MessageId], ct);
        return Results.Created($"/email/mailboxes/Support/replies/{Uri.EscapeDataString(applicationReply.MessageId)}", new { supportRequest, applicationReply, firstReply, secondReply });
    }

    private static async Task<IResult> SeedInboxDemoAsync(IConfiguration configuration, CancellationToken ct)
    {
        var support = await SeedConversationAsync(configuration, "alice@example.test", "customer@example.test", "Casko Support", "Help needed with my order", ct);
        var sales = await SeedConversationAsync(configuration, "bob@example.test", "prospect@example.test", "Casko Sales", "Question about enterprise pricing", ct);
        return Results.Created("/", new { support, sales });
    }

    private static Task<EmailMessageReference> SeedIncomingMessageAsync(IConfiguration configuration, EmailAddress from, string subject,
        string text, string? inReplyTo, IReadOnlyCollection<string> references, CancellationToken ct) =>
        SeedMessageAsync(configuration, from, "alice@example.test", subject, text, inReplyTo, references, ct);

    private static async Task<object> SeedConversationAsync(IConfiguration configuration, string recipient, string customerAddress,
        string teamName, string subject, CancellationToken ct)
    {
        var customer = new EmailAddress { Address = customerAddress, DisplayName = "Jamie Customer" };
        var team = new EmailAddress { Address = $"{teamName.ToLowerInvariant().Replace(" ", ".")}@example.test", DisplayName = teamName };
        var root = await SeedMessageAsync(configuration, customer, recipient, subject, "Hello, I need some help.", null, [], ct);
        var reply = await SeedMessageAsync(configuration, team, recipient, $"Re: {subject}", "Thanks for contacting us. We are looking into it.", root.MessageId, [root.MessageId], ct);
        var followUp = await SeedMessageAsync(configuration, customer, recipient, $"Re: {subject}", "Thank you. Could you share the next steps?", reply.MessageId, [root.MessageId, reply.MessageId], ct);
        var finalReply = await SeedMessageAsync(configuration, team, recipient, $"Re: {subject}", "Certainly. We will follow up shortly.", followUp.MessageId, [root.MessageId, reply.MessageId, followUp.MessageId], ct);
        return new { root, reply, followUp, finalReply };
    }

    private static async Task<EmailMessageReference> SeedMessageAsync(IConfiguration configuration, EmailAddress from, string recipient,
        string subject, string text, string? inReplyTo, IReadOnlyCollection<string> references, CancellationToken ct)
    {
        var host = configuration["Email:GreenMail:Smtp:Host"] ?? throw new InvalidOperationException("Email:GreenMail:Smtp:Host must be configured.");
        var port = configuration.GetValue<int?>("Email:GreenMail:Smtp:Port") ?? 3025;
        var message = new MimeMessage { MessageId = MimeUtils.GenerateMessageId(), Subject = subject, Body = new TextPart("plain") { Text = text } };
        message.From.Add(new MailboxAddress(from.DisplayName, from.Address));
        message.To.Add(new MailboxAddress(recipient, recipient));
        if (!string.IsNullOrWhiteSpace(inReplyTo)) message.Headers[HeaderId.InReplyTo] = inReplyTo;
        if (references.Count > 0) message.Headers[HeaderId.References] = string.Join(" ", references.Distinct(StringComparer.Ordinal));
        using var client = new SmtpClient();
        await client.ConnectAsync(host, port, SecureSocketOptions.None, ct);
        var username = configuration["Email:GreenMail:Smtp:Username"];
        var password = configuration["Email:GreenMail:Smtp:Password"];
        if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password)) await client.AuthenticateAsync(username, password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
        return new EmailMessageReference { MessageId = message.MessageId!, References = references };
    }
}
