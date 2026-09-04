using Casko.Messaging.Email.MailKit.Configuration;
using Casko.Messaging.Email.Reading;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Options;

namespace Casko.Messaging.Email.MailKit.Reading;

internal sealed class MailKitEmailReader(IOptions<MailKitEmailOptions> options) : IEmailReader
{
    private readonly MailKitEmailOptions _options = options.Value;

    /// <inheritdoc />
    public Task<IReadOnlyCollection<ReceivedEmailMessage>> ReadAsync(EmailMailboxId mailbox, EmailReadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxResults is <= 0) return Task.FromResult<IReadOnlyCollection<ReceivedEmailMessage>>([]);

        var query = SearchQuery.All;
        if (request.UnreadOnly) query = query.And(SearchQuery.NotSeen);
        if (request.ReceivedAfter is { } receivedAfter) query = query.And(SearchQuery.DeliveredAfter(receivedAfter.UtcDateTime));
        return ReadAsync(mailbox, query, request.MaxResults, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<ReceivedEmailMessage>> FindRepliesAsync(EmailMailboxId mailbox, EmailMessageReference parent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (string.IsNullOrWhiteSpace(parent.MessageId)) throw new ArgumentException("A parent message ID is required.", nameof(parent));
        var query = SearchQuery.HeaderContains("In-Reply-To", parent.MessageId)
            .Or(SearchQuery.HeaderContains("References", parent.MessageId));
        return ReadAsync(mailbox, query, null, cancellationToken);
    }

    private async Task<IReadOnlyCollection<ReceivedEmailMessage>> ReadAsync(EmailMailboxId mailbox, SearchQuery query, int? maxResults, CancellationToken cancellationToken)
    {
        var options = GetMailbox(mailbox);
        using var client = new ImapClient();
        await client.ConnectAsync(options.Host, options.Port, options.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None, cancellationToken);
        if (!string.IsNullOrWhiteSpace(options.Username) && !string.IsNullOrWhiteSpace(options.Password))
            await client.AuthenticateAsync(options.Username, options.Password, cancellationToken);

        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        var identifiers = await inbox.SearchAsync(query, cancellationToken);
        IEnumerable<UniqueId> selected = identifiers.OrderBy(identifier => identifier.Id);
        if (maxResults is { } max) selected = selected.Take(max);

        var messages = new List<ReceivedEmailMessage>();
        foreach (var identifier in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var message = await inbox.GetMessageAsync(identifier, cancellationToken);
            messages.Add(ReceivedEmailMessageMapper.Map(mailbox, inbox.UidValidity, identifier, message));
        }

        await client.DisconnectAsync(true, cancellationToken);
        return messages;
    }

    private MailKitMailboxOptions GetMailbox(EmailMailboxId mailbox)
    {
        if (string.IsNullOrWhiteSpace(mailbox.Value)) throw new ArgumentException("A mailbox identifier is required.", nameof(mailbox));
        if (!_options.Mailboxes.TryGetValue(mailbox.Value, out var options)) throw new InvalidOperationException($"No MailKit mailbox is configured for '{mailbox.Value}'.");
        if (string.IsNullOrWhiteSpace(options.Host)) throw new InvalidOperationException($"The MailKit mailbox '{mailbox.Value}' has no IMAP host.");
        return options;
    }
}
