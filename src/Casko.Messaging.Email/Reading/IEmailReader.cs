namespace Casko.Messaging.Email.Reading;

/// <summary>Reads incoming email from configured mailboxes.</summary>
public interface IEmailReader
{
    /// <summary>Reads messages from one logical mailbox without changing their read state.</summary>
    /// <param name="mailbox">The configured logical mailbox identifier.</param>
    /// <param name="request">The query to apply.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The matching messages.</returns>
    Task<IReadOnlyCollection<ReceivedEmailMessage>> ReadAsync(EmailMailboxId mailbox, EmailReadRequest request, CancellationToken cancellationToken = default);

    /// <summary>Finds messages that reply to or belong to the conversation of a parent message.</summary>
    /// <param name="mailbox">The configured logical mailbox identifier.</param>
    /// <param name="parent">The conversation message to match.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The matching conversation messages.</returns>
    Task<IReadOnlyCollection<ReceivedEmailMessage>> FindRepliesAsync(EmailMailboxId mailbox, EmailMessageReference parent, CancellationToken cancellationToken = default);
}
