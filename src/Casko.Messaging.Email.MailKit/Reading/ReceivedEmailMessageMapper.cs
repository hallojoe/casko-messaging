using System.Text.RegularExpressions;
using Casko.Messaging.Email.Attachments;
using Casko.Messaging.Email.Recipients;
using Casko.Messaging.Email.Reading;
using MailKit;
using MimeKit;

namespace Casko.Messaging.Email.MailKit.Reading;

internal static partial class ReceivedEmailMessageMapper
{
    public static ReceivedEmailMessage Map(EmailMailboxId mailbox, uint uidValidity, UniqueId uniqueId, MimeMessage message)
    {
        var from = message.From.Mailboxes.FirstOrDefault();
        return new ReceivedEmailMessage
        {
            Id = $"{mailbox.Value}:{uidValidity}:{uniqueId.Id}",
            MessageId = NullIfWhiteSpace(message.MessageId),
            InReplyTo = NullIfWhiteSpace(message.InReplyTo),
            References = ParseReferences(message.Headers[HeaderId.References]),
            From = new EmailAddress { Address = from?.Address ?? string.Empty, DisplayName = from?.Name },
            Recipients = GetRecipients(message),
            Subject = NullIfWhiteSpace(message.Subject),
            Text = NullIfWhiteSpace(message.TextBody),
            Html = NullIfWhiteSpace(message.HtmlBody),
            Attachments = GetAttachments(message),
            ReceivedAt = message.Date
        };
    }

    private static IReadOnlyCollection<EmailRecipient> GetRecipients(MimeMessage message) =>
        Map(message.To, EmailRecipientType.To)
            .Concat(Map(message.Cc, EmailRecipientType.Cc))
            .Concat(Map(message.Bcc, EmailRecipientType.Bcc))
            .ToArray();

    private static IEnumerable<EmailRecipient> Map(InternetAddressList addresses, EmailRecipientType type) =>
        addresses.Mailboxes.Select(address => new EmailRecipient
        {
            Address = new EmailAddress { Address = address.Address, DisplayName = address.Name },
            Type = type
        });

    private static IReadOnlyCollection<EmailAttachment> GetAttachments(MimeMessage message)
    {
        var attachments = new List<EmailAttachment>();
        foreach (var part in message.BodyParts.OfType<MimePart>().Where(part => part.IsAttachment || !string.IsNullOrWhiteSpace(part.ContentId)))
        {
            if (part.Content is null) continue;
            using var content = new MemoryStream();
            part.Content.DecodeTo(content);
            attachments.Add(new EmailAttachment
            {
                FileName = part.FileName ?? "attachment",
                ContentType = part.ContentType.MimeType,
                Content = content.ToArray(),
                Disposition = part.IsAttachment ? EmailAttachmentDisposition.Attachment : EmailAttachmentDisposition.Inline,
                ContentId = NullIfWhiteSpace(part.ContentId)
            });
        }
        return attachments;
    }

    private static IReadOnlyCollection<string> ParseReferences(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : MessageIdPattern().Matches(value).Select(match => match.Value).Distinct(StringComparer.Ordinal).ToArray();

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex MessageIdPattern();
}
