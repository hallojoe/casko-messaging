using Casko.Messaging.Email.Attachments;
using Casko.Messaging.Email.Delivery;
using Casko.Messaging.Email.Recipients;
using MimeKit;
using MimeKit.Utils;

namespace Casko.Messaging.Email.MailKit.Mapping;

internal sealed class MimeMessageFactory : IMimeMessageFactory
{
    private readonly Configuration.MailKitEmailOptions _options;

    public MimeMessageFactory(Microsoft.Extensions.Options.IOptions<Configuration.MailKitEmailOptions> options) => _options = options.Value;

    public MimeMessage Create(EmailDelivery delivery)
    {
        Validate(delivery);
        var message = new MimeMessage { Subject = delivery.Message.Subject, MessageId = MimeUtils.GenerateMessageId() };
        message.From.Add(CreateAddress(new EmailAddress { Address = _options.FromAddress, DisplayName = _options.FromDisplayName }));
        foreach (var recipient in delivery.Recipients)
        {
            var collection = recipient.Type switch { EmailRecipientType.To => message.To, EmailRecipientType.Cc => message.Cc, EmailRecipientType.Bcc => message.Bcc, _ => throw new ArgumentOutOfRangeException() };
            collection.Add(CreateAddress(recipient.Address));
        }
        if (delivery.ReplyTo is not null) message.ReplyTo.Add(CreateAddress(delivery.ReplyTo));
        if (delivery.ReplyToMessage is not null)
        {
            message.Headers[HeaderId.InReplyTo] = delivery.ReplyToMessage.MessageId;
            var references = delivery.ReplyToMessage.References
                .Append(delivery.ReplyToMessage.MessageId)
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (references.Length > 0) message.Headers[HeaderId.References] = string.Join(" ", references);
        }

        var body = new BodyBuilder { TextBody = delivery.Message.Text, HtmlBody = delivery.Message.Html };
        foreach (var attachment in delivery.Message.Attachments)
        {
            var contentType = ContentType.Parse(attachment.ContentType);
            if (attachment.Disposition == EmailAttachmentDisposition.Inline)
            {
                var resource = body.LinkedResources.Add(attachment.FileName, attachment.Content.ToArray(), contentType);
                resource.ContentId = attachment.ContentId;
            }
            else body.Attachments.Add(attachment.FileName, attachment.Content.ToArray(), contentType);
        }
        message.Body = body.ToMessageBody();
        return message;
    }

    private static MailboxAddress CreateAddress(EmailAddress address) => new(address.DisplayName, address.Address);

    private void Validate(EmailDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(delivery.Message);
        if (string.IsNullOrWhiteSpace(_options.Host)) throw new InvalidOperationException("Email:MailKit:Host must be configured.");
        if (string.IsNullOrWhiteSpace(_options.FromAddress)) throw new InvalidOperationException("Email:MailKit:FromAddress must be configured.");
        if (string.IsNullOrWhiteSpace(delivery.Message.Subject)) throw new ArgumentException("An email subject is required.", nameof(delivery));
        if (string.IsNullOrWhiteSpace(delivery.Message.Text)) throw new ArgumentException("An email text body is required.", nameof(delivery));
        if (delivery.Recipients.Count == 0) throw new ArgumentException("At least one recipient is required.", nameof(delivery));
        if (delivery.Recipients.Any(r => r.Address is null || string.IsNullOrWhiteSpace(r.Address.Address))) throw new ArgumentException("Every recipient requires an email address.", nameof(delivery));
        if (delivery.Message.Attachments.Any(a => a.Disposition == EmailAttachmentDisposition.Inline && string.IsNullOrWhiteSpace(a.ContentId))) throw new ArgumentException("Inline attachments require a content ID.", nameof(delivery));
        if (delivery.ReplyToMessage is not null && string.IsNullOrWhiteSpace(delivery.ReplyToMessage.MessageId)) throw new ArgumentException("A reply-to message requires a message ID.", nameof(delivery));
    }
}
