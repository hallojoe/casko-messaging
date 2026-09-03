using System.Text;
using Casko.Messaging.Email;
using Casko.Messaging.Email.Attachments;
using Casko.Messaging.Email.Delivery;
using Casko.Messaging.Email.MailKit.DependencyInjection;
using Casko.Messaging.Email.Reading;
using Casko.Messaging.Email.Recipients;
using Casko.OpenTelemetry.Extensions.AspNetCore;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Utils;

var builder = WebApplication.CreateBuilder(args);

builder.AddOpinionatedOpenTelemetry();

var mailPitConnection = builder.Configuration.GetConnectionString("mailpit");
if (!string.IsNullOrWhiteSpace(mailPitConnection))
{
    var smtpEndpoint = mailPitConnection.Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .FirstOrDefault(parts => parts.Length == 2 && string.Equals(parts[0], "endpoint", StringComparison.OrdinalIgnoreCase))?[1];
    if (smtpEndpoint is not null && Uri.TryCreate(smtpEndpoint, UriKind.Absolute, out var uri))
    {
        builder.Configuration["Email:MailKit:Host"] = uri.Host;
        builder.Configuration["Email:MailKit:Port"] = uri.Port.ToString();
        builder.Configuration["Email:MailKit:UseSsl"] = "false";
    }
}

builder.Services.AddMailKitEmail(builder.Configuration.GetSection("Email:MailKit"));
var app = builder.Build();

var senderAddress = new EmailAddress { Address = "support@casko.dev.local", DisplayName = "Casko Support" };
var alice = new EmailAddress { Address = "alice@example.local", DisplayName = "Alice" };

app.MapPost("/email/single", async (IEmailSender sender, CancellationToken cancellationToken) =>
{
    var result = await sender.SendAsync(alice, new EmailMessage { Subject = "Single email", Text = "This is a plain-text email." }, cancellationToken);
    return Results.Accepted(value: result);
});

app.MapPost("/email/multiple-recipients", async (IEmailSender sender, CancellationToken cancellationToken) =>
{
    var result = await sender.SendAsync(new EmailDelivery
    {
        Message = new EmailMessage { Subject = "Team update", Text = "Plain-text fallback.", Html = "<h1>Team update</h1><p>This version includes HTML.</p>" },
        Recipients = [new EmailRecipient { Address = alice }, new EmailRecipient { Address = new EmailAddress { Address = "bob@example.local" }, Type = EmailRecipientType.Cc }, new EmailRecipient { Address = new EmailAddress { Address = "private@example.test" }, Type = EmailRecipientType.Bcc }],
        ReplyTo = senderAddress
    }, cancellationToken);
    return Results.Accepted(value: result);
});

app.MapPost("/email/personalized", async (IEmailSender sender, CancellationToken cancellationToken) =>
{
    var recipients = new[] { "Alice", "Bob", "Chris" };
    var results = await sender.SendAsync(recipients.Select(name => new EmailDelivery
    {
        Message = new EmailMessage { Subject = $"Hello {name}", Text = $"Hello {name}, this is your personal message." },
        Recipients = [new EmailRecipient { Address = new EmailAddress { Address = $"{name.ToLowerInvariant()}@example.local", DisplayName = name } }]
    }), cancellationToken);
    return Results.Accepted(value: results);
});

app.MapPost("/email/attachment", async (IEmailSender sender, CancellationToken cancellationToken) =>
{
    var result = await sender.SendAsync(alice, new EmailMessage
    {
        Subject = "Attachment example", Text = "The attached text file demonstrates attachments.",
        Attachments = [new EmailAttachment { FileName = "example.txt", ContentType = "text/plain", Content = Encoding.UTF8.GetBytes("Casko Messaging attachment.") }]
    }, cancellationToken);
    return Results.Accepted(value: result);
});

app.MapPost("/email/inline-image", async (IEmailSender sender, CancellationToken cancellationToken) =>
{
    var pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9WlWZc8AAAAASUVORK5CYII=");
    var result = await sender.SendAsync(alice, new EmailMessage
    {
        Subject = "Inline image example", Text = "This message contains an inline image.", Html = "<p>This message contains an inline image:</p><img alt=\"pixel\" src=\"cid:logo\">",
        Attachments = [new EmailAttachment { FileName = "logo.png", ContentType = "image/png", Content = pixel, Disposition = EmailAttachmentDisposition.Inline, ContentId = "logo" }]
    }, cancellationToken);
    return Results.Accepted(value: result);
});

app.MapGet("/email/mailboxes/{mailbox}/messages", async (string mailbox, IEmailReader reader, CancellationToken cancellationToken) =>
    Results.Ok(await reader.ReadAsync(new EmailMailboxId(mailbox), new EmailReadRequest(), cancellationToken)));

app.MapGet("/email/mailboxes/{mailbox}/unread", async (string mailbox, IEmailReader reader, CancellationToken cancellationToken) =>
    Results.Ok(await reader.ReadAsync(new EmailMailboxId(mailbox), new EmailReadRequest { UnreadOnly = true }, cancellationToken)));

app.MapGet("/email/mailboxes/{mailbox}/replies/{messageId}", async (string mailbox, string messageId, IEmailReader reader, CancellationToken cancellationToken) =>
    Results.Ok(await reader.FindRepliesAsync(new EmailMailboxId(mailbox), new EmailMessageReference { MessageId = messageId }, cancellationToken)));

app.MapPost("/email/reply", async (EmailReplyRequest request, IEmailSender sender, CancellationToken cancellationToken) =>
{
    var result = await sender.SendAsync(new EmailDelivery
    {
        Message = new EmailMessage { Subject = request.Subject, Text = request.Text, Html = request.Html },
        Recipients = [new EmailRecipient { Address = request.Recipient }],
        ReplyToMessage = new EmailMessageReference { MessageId = request.MessageId, References = request.References ?? [] }
    }, cancellationToken);
    return Results.Accepted(value: result);
});

app.MapPost("/email/support/seed", async (IConfiguration configuration, IEmailSender sender, CancellationToken cancellationToken) =>
{
    const string supportAddress = "support@example.test";
    var customer = new EmailAddress { Address = "customer@example.test", DisplayName = "Jamie Customer" };
    var supportRequest = await SeedIncomingMessageAsync(configuration, customer, "Help needed with my order", "Hello, I need help with order #12345.", null, [], cancellationToken);

    var applicationReply = await sender.SendAsync(new EmailDelivery
    {
        Message = new EmailMessage { Subject = "Re: Help needed with my order", Text = "Thanks Jamie. We are looking into order #12345." },
        Recipients = [new EmailRecipient { Address = customer }],
        ReplyTo = new EmailAddress { Address = supportAddress, DisplayName = "Casko Support" },
        ReplyToMessage = supportRequest
    }, cancellationToken);

    var firstReply = await SeedIncomingMessageAsync(
        configuration, customer, "Re: Help needed with my order", "Thank you. Could you also confirm when it will ship?", applicationReply.MessageId,
        [supportRequest.MessageId, applicationReply.MessageId], cancellationToken);
    var secondReply = await SeedIncomingMessageAsync(
        configuration, customer, "Re: Help needed with my order", "Following up on my shipping question.", firstReply.MessageId,
        [supportRequest.MessageId, applicationReply.MessageId, firstReply.MessageId], cancellationToken);

    return Results.Created($"/email/mailboxes/Support/replies/{Uri.EscapeDataString(applicationReply.MessageId)}", new
    {
        supportRequest,
        applicationReply,
        firstReply,
        secondReply
    });
});

static async Task<EmailMessageReference> SeedIncomingMessageAsync(
    IConfiguration configuration,
    EmailAddress from,
    string subject,
    string text,
    string? inReplyTo,
    IReadOnlyCollection<string> references,
    CancellationToken cancellationToken)
{
    var host = configuration["Email:GreenMail:Smtp:Host"] ?? throw new InvalidOperationException("Email:GreenMail:Smtp:Host must be configured.");
    var port = configuration.GetValue<int?>("Email:GreenMail:Smtp:Port") ?? 3025;
    var message = new MimeMessage
    {
        MessageId = MimeUtils.GenerateMessageId(),
        Subject = subject,
        Body = new TextPart("plain") { Text = text }
    };
    message.From.Add(new MailboxAddress(from.DisplayName, from.Address));
    message.To.Add(new MailboxAddress("Casko Support", "support@example.test"));
    if (!string.IsNullOrWhiteSpace(inReplyTo)) message.Headers[HeaderId.InReplyTo] = inReplyTo;
    if (references.Count > 0) message.Headers[HeaderId.References] = string.Join(" ", references.Distinct(StringComparer.Ordinal));

    using var client = new SmtpClient();
    await client.ConnectAsync(host, port, SecureSocketOptions.None, cancellationToken);
    var username = configuration["Email:GreenMail:Smtp:Username"];
    var password = configuration["Email:GreenMail:Smtp:Password"];
    if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
        await client.AuthenticateAsync(username, password, cancellationToken);
    await client.SendAsync(message, cancellationToken);
    await client.DisconnectAsync(true, cancellationToken);

    return new EmailMessageReference { MessageId = message.MessageId!, References = references };
}

app.Run();

public partial class Program;

public sealed record EmailReplyRequest
{
    public required string MessageId { get; init; }
    public IReadOnlyCollection<string>? References { get; init; }
    public required EmailAddress Recipient { get; init; }
    public required string Subject { get; init; }
    public required string Text { get; init; }
    public string? Html { get; init; }
}
