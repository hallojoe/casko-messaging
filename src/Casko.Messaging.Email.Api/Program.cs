using System.Text;
using Casko.Messaging.Email;
using Casko.Messaging.Email.Api.Components;
using Casko.Messaging.Email.Api.Contracts;
using Casko.Messaging.Email.Api.Services;
using Casko.Messaging.Email.Attachments;
using Casko.Messaging.Email.Delivery;
using Casko.Messaging.Email.Extensions;
using Casko.Messaging.Email.MailKit.Configuration;
using Casko.Messaging.Email.MailKit.DependencyInjection;
using Casko.Messaging.Email.Reading;
using Casko.Messaging.Email.Recipients;
using Casko.Messaging.Email.Threading;
using Casko.Messaging.Email.BulkDelivery;
using Casko.OpenTelemetry.Extensions.AspNetCore;
using Ganss.Xss;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Utils;
using MudBlazor.Services;
using Casko.Messaging.Email.Api;

var builder = WebApplication.CreateBuilder(args);

builder.AddOpinionatedOpenTelemetry();
builder.Services.AddOpenApi();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();

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
builder.Services.AddSingleton<IEmailThreadBuilder, EmailThreadBuilder>();
builder.Services.AddSingleton<HtmlSanitizer>();
builder.Services.AddScoped<InboxQueryService>();
builder.Services.AddSqlServerNotifications(builder.Configuration.GetConnectionString("notifications"));
builder.Services.Configure<NotificationIngestionOptions>(builder.Configuration.GetSection("Notifications:Ingestion"));
var app = builder.Build();

if (builder.Configuration.GetValue("Notifications:ApplyMigrations", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<INotificationStoreInitializer>().InitializeAsync();
}

if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseAntiforgery();
app.MapStaticAssets();

var senderAddress = new EmailAddress { Address = "support@casko.dev.local", DisplayName = "Casko Support" };
var alice = new EmailAddress { Address = "alice@example.local", DisplayName = "Alice" };

app.MapPost("/email/single", async (IEmailSender sender, CancellationToken cancellationToken) =>
{
    var result = await sender.SendAsync(alice, new EmailMessage { Subject = "Single email", Text = "This is a plain-text email." }, cancellationToken);
    return Results.Accepted(value: result);
});

app.MapNotificationEndpoints();

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

var inboxApi = app.MapGroup("/api").WithTags("Inbox viewer");
inboxApi.MapGet("/mailboxes", (InboxQueryService inboxes) => Results.Ok(inboxes.GetMailboxes()))
    .WithName("GetMailboxes")
    .Produces<IReadOnlyCollection<MailboxResponse>>();
inboxApi.MapGet("/mailboxes/{mailbox}/threads", async (string mailbox, InboxQueryService inboxes, CancellationToken cancellationToken) =>
    !inboxes.GetMailboxes().Any(configured => string.Equals(configured.Id, mailbox, StringComparison.OrdinalIgnoreCase))
        ? Results.NotFound()
        : Results.Ok(await inboxes.GetThreadsAsync(mailbox, cancellationToken)))
    .WithName("GetMailboxThreads")
    .Produces<IReadOnlyCollection<EmailThreadSummaryResponse>>()
    .Produces(StatusCodes.Status404NotFound);
inboxApi.MapGet("/mailboxes/{mailbox}/threads/{threadId}", async (string mailbox, string threadId, InboxQueryService inboxes, CancellationToken cancellationToken) =>
{
    if (!inboxes.GetMailboxes().Any(configured => string.Equals(configured.Id, mailbox, StringComparison.OrdinalIgnoreCase))) return Results.NotFound();
    var thread = await inboxes.GetThreadAsync(mailbox, threadId, cancellationToken);
    return thread is null ? Results.NotFound() : Results.Ok(thread);
})
    .WithName("GetMailboxThread")
    .Produces<EmailThreadResponse>()
    .Produces(StatusCodes.Status404NotFound);

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
    const string supportAddress = "alice@example.test";
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

app.MapPost("/email/demo/seed", async (IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var support = await SeedConversationAsync(configuration, "alice@example.test", "customer@example.test", "Casko Support", "Help needed with my order", cancellationToken);
    var sales = await SeedConversationAsync(configuration, "bob@example.test", "prospect@example.test", "Casko Sales", "Question about enterprise pricing", cancellationToken);
    return Results.Created("/", new { support, sales });
}).WithTags("Demo data");

static async Task<EmailMessageReference> SeedIncomingMessageAsync(
    IConfiguration configuration,
    EmailAddress from,
    string subject,
    string text,
    string? inReplyTo,
    IReadOnlyCollection<string> references,
    CancellationToken cancellationToken)
    => await SeedMessageAsync(configuration, from, "alice@example.test", subject, text, inReplyTo, references, cancellationToken);

static async Task<object> SeedConversationAsync(IConfiguration configuration, string recipient, string customerAddress, string teamName, string subject, CancellationToken cancellationToken)
{
    var customer = new EmailAddress { Address = customerAddress, DisplayName = "Jamie Customer" };
    var team = new EmailAddress { Address = $"{teamName.ToLowerInvariant().Replace(" ", ".")}@example.test", DisplayName = teamName };
    var root = await SeedMessageAsync(configuration, customer, recipient, subject, "Hello, I need some help.", null, [], cancellationToken);
    var reply = await SeedMessageAsync(configuration, team, recipient, $"Re: {subject}", "Thanks for contacting us. We are looking into it.", root.MessageId, [root.MessageId], cancellationToken);
    var followUp = await SeedMessageAsync(configuration, customer, recipient, $"Re: {subject}", "Thank you. Could you share the next steps?", reply.MessageId, [root.MessageId, reply.MessageId], cancellationToken);
    var finalReply = await SeedMessageAsync(configuration, team, recipient, $"Re: {subject}", "Certainly. We will follow up shortly.", followUp.MessageId, [root.MessageId, reply.MessageId, followUp.MessageId], cancellationToken);
    return new { root, reply, followUp, finalReply };
}

static async Task<EmailMessageReference> SeedMessageAsync(
    IConfiguration configuration,
    EmailAddress from,
    string recipient,
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
    message.To.Add(new MailboxAddress(recipient, recipient));
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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

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
