using Casko.Messaging.Email.Attachments;
using Casko.Messaging.Email.Recipients;

namespace Casko.Messaging.Email.Reading;

/// <summary>Represents an incoming mailbox message and its transport metadata.</summary>
public sealed record ReceivedEmailMessage
{
    /// <summary>Gets the provider-neutral identifier within the configured mailbox.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the RFC email <c>Message-Id</c>, when supplied by the sender.</summary>
    public string? MessageId { get; init; }
    /// <summary>Gets the immediate parent message ID, when supplied.</summary>
    public string? InReplyTo { get; init; }
    /// <summary>Gets message IDs that precede this message in its conversation.</summary>
    public IReadOnlyCollection<string> References { get; init; } = [];
    /// <summary>Gets the sender.</summary>
    public required EmailAddress From { get; init; }
    /// <summary>Gets To, Cc, and Bcc recipients available in the message.</summary>
    public IReadOnlyCollection<EmailRecipient> Recipients { get; init; } = [];
    /// <summary>Gets the optional subject.</summary>
    public string? Subject { get; init; }
    /// <summary>Gets the optional plain-text body.</summary>
    public string? Text { get; init; }
    /// <summary>Gets the optional HTML body.</summary>
    public string? Html { get; init; }
    /// <summary>Gets message attachments and inline resources.</summary>
    public IReadOnlyCollection<EmailAttachment> Attachments { get; init; } = [];
    /// <summary>Gets when the message was received according to the mailbox message date.</summary>
    public required DateTimeOffset ReceivedAt { get; init; }
}
