using System.Text;
using Casko.Messaging.Email;
using Casko.Messaging.Email.Attachments;
using Casko.Messaging.Email.Delivery;
using Casko.Messaging.Email.MailKit.DependencyInjection;
using Casko.Messaging.Email.Recipients;

var builder = WebApplication.CreateBuilder(args);

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
    await sender.SendAsync(alice, new EmailMessage { Subject = "Single email", Text = "This is a plain-text email." }, cancellationToken);
    return Results.Accepted();
});

app.MapPost("/email/multiple-recipients", async (IEmailSender sender, CancellationToken cancellationToken) =>
{
    await sender.SendAsync(new EmailDelivery
    {
        Message = new EmailMessage { Subject = "Team update", Text = "Plain-text fallback.", Html = "<h1>Team update</h1><p>This version includes HTML.</p>" },
        Recipients = [new EmailRecipient { Address = alice }, new EmailRecipient { Address = new EmailAddress { Address = "bob@example.local" }, Type = EmailRecipientType.Cc }, new EmailRecipient { Address = new EmailAddress { Address = "private@example.test" }, Type = EmailRecipientType.Bcc }],
        ReplyTo = senderAddress
    }, cancellationToken);
    return Results.Accepted();
});

app.MapPost("/email/personalized", async (IEmailSender sender, CancellationToken cancellationToken) =>
{
    var recipients = new[] { "Alice", "Bob", "Chris" };
    await sender.SendAsync(recipients.Select(name => new EmailDelivery
    {
        Message = new EmailMessage { Subject = $"Hello {name}", Text = $"Hello {name}, this is your personal message." },
        Recipients = [new EmailRecipient { Address = new EmailAddress { Address = $"{name.ToLowerInvariant()}@example.local", DisplayName = name } }]
    }), cancellationToken);
    return Results.Accepted();
});

app.MapPost("/email/attachment", async (IEmailSender sender, CancellationToken cancellationToken) =>
{
    await sender.SendAsync(alice, new EmailMessage
    {
        Subject = "Attachment example", Text = "The attached text file demonstrates attachments.",
        Attachments = [new EmailAttachment { FileName = "example.txt", ContentType = "text/plain", Content = Encoding.UTF8.GetBytes("Casko Messaging attachment.") }]
    }, cancellationToken);
    return Results.Accepted();
});

app.MapPost("/email/inline-image", async (IEmailSender sender, CancellationToken cancellationToken) =>
{
    var pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9WlWZc8AAAAASUVORK5CYII=");
    await sender.SendAsync(alice, new EmailMessage
    {
        Subject = "Inline image example", Text = "This message contains an inline image.", Html = "<p>This message contains an inline image:</p><img alt=\"pixel\" src=\"cid:logo\">",
        Attachments = [new EmailAttachment { FileName = "logo.png", ContentType = "image/png", Content = pixel, Disposition = EmailAttachmentDisposition.Inline, ContentId = "logo" }]
    }, cancellationToken);
    return Results.Accepted();
});

app.Run();

public partial class Program;
