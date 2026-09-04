using Casko.Messaging.Email.Api.Contracts;
using Casko.Messaging.Email.Attachments;
using Casko.Messaging.Email.MailKit.Configuration;
using Casko.Messaging.Email.Reading;
using Casko.Messaging.Email.Recipients;
using Casko.Messaging.Email.Threading;
using Ganss.Xss;
using Microsoft.Extensions.Options;

namespace Casko.Messaging.Email.Api.Services;

public sealed class InboxQueryService(
    IEmailReader reader,
    IEmailThreadBuilder threadBuilder,
    IOptions<MailKitEmailOptions> options,
    HtmlSanitizer sanitizer)
{
    private readonly MailKitEmailOptions _options = options.Value;

    public IReadOnlyCollection<MailboxResponse> GetMailboxes() => _options.Mailboxes
        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
        .Select(pair => new MailboxResponse(pair.Key, ToDisplayName(pair.Key), pair.Value.Address))
        .ToArray();

    public async Task<IReadOnlyCollection<EmailThreadSummaryResponse>> GetThreadsAsync(string mailbox, CancellationToken cancellationToken)
    {
        var threads = await GetBuiltThreadsAsync(mailbox, cancellationToken);
        return threads.Select(MapSummary).OrderByDescending(thread => thread.LatestReceivedAt).ToArray();
    }

    public async Task<EmailThreadResponse?> GetThreadAsync(string mailbox, string threadId, CancellationToken cancellationToken)
    {
        var thread = (await GetBuiltThreadsAsync(mailbox, cancellationToken)).SingleOrDefault(thread => string.Equals(thread.Id, threadId, StringComparison.Ordinal));
        return thread is null ? null : MapThread(thread);
    }

    private async Task<IReadOnlyCollection<EmailThread>> GetBuiltThreadsAsync(string mailbox, CancellationToken cancellationToken)
    {
        EnsureMailboxExists(mailbox);
        var messages = await reader.ReadAsync(new EmailMailboxId(mailbox), new EmailReadRequest(), cancellationToken);
        return threadBuilder.Build(messages);
    }

    private void EnsureMailboxExists(string mailbox)
    {
        if (!_options.Mailboxes.ContainsKey(mailbox)) throw new KeyNotFoundException($"Mailbox '{mailbox}' is not configured.");
    }

    private EmailThreadSummaryResponse MapSummary(EmailThread thread)
    {
        var messages = Flatten(thread.Root).ToArray();
        var latest = messages.MaxBy(message => message.ReceivedAt)!;
        return new EmailThreadSummaryResponse(thread.Id, thread.Root.Message.Subject, DisplayAddress(thread.Root.Message.From), latest.Text ?? latest.Subject, messages.Length, latest.ReceivedAt);
    }

    private EmailThreadResponse MapThread(EmailThread thread)
    {
        var messages = Flatten(thread.Root).ToArray();
        return new EmailThreadResponse(thread.Id, thread.Root.Message.Subject, messages.Length, messages.Max(message => message.ReceivedAt), MapNode(thread.Root));
    }

    private EmailThreadMessageResponse MapNode(EmailThreadNode node)
    {
        var message = node.Message;
        return new EmailThreadMessageResponse(
            message.Id,
            message.MessageId,
            node.ParentId,
            message.InReplyTo,
            message.References,
            MapAddress(message.From),
            message.Recipients.Select(recipient => new EmailRecipientResponse(MapAddress(recipient.Address), recipient.Type.ToString())).ToArray(),
            message.Subject,
            message.Text,
            string.IsNullOrWhiteSpace(message.Html) ? null : sanitizer.Sanitize(message.Html),
            message.Attachments.Select(MapAttachment).ToArray(),
            message.ReceivedAt,
            node.Children.Select(MapNode).ToArray());
    }

    private static IEnumerable<ReceivedEmailMessage> Flatten(EmailThreadNode node) => [node.Message, .. node.Children.SelectMany(Flatten)];
    private static EmailAddressResponse MapAddress(EmailAddress address) => new(address.Address, address.DisplayName);
    private static EmailAttachmentResponse MapAttachment(EmailAttachment attachment) => new(attachment.FileName, attachment.ContentType, attachment.Disposition.ToString(), attachment.ContentId);
    private static string DisplayAddress(EmailAddress address) => address.DisplayName ?? address.Address;
    private static string ToDisplayName(string id) => string.Concat(id.Select((character, index) => index > 0 && char.IsUpper(character) ? $" {character}" : character.ToString()));
}
