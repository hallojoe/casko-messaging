using Casko.Messaging.Email.Recipients;

namespace Casko.Messaging.Email.Delivery;

/// <summary>Represents one physical delivery of email content to one or more recipients.</summary>
public sealed record EmailDelivery
{
    /// <summary>Gets the content being delivered.</summary>
    public required EmailMessage Message { get; init; }
    /// <summary>Gets the recipients of this delivery.</summary>
    public required IReadOnlyCollection<EmailRecipient> Recipients { get; init; }
    /// <summary>Gets the optional reply-to address.</summary>
    public EmailAddress? ReplyTo { get; init; }
}
