namespace Casko.Messaging.Email.Recipients;

/// <summary>Associates an email address with a recipient type.</summary>
public sealed record EmailRecipient
{
    /// <summary>Gets the recipient's address.</summary>
    public required EmailAddress Address { get; init; }

    /// <summary>Gets the recipient type.</summary>
    public EmailRecipientType Type { get; init; } = EmailRecipientType.To;
}
