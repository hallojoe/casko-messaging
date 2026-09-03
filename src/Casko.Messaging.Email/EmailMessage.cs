using Casko.Messaging.Email.Attachments;

namespace Casko.Messaging.Email;

/// <summary>Represents reusable email content independent of its recipients.</summary>
public sealed record EmailMessage
{
    /// <summary>Gets the subject line.</summary>
    public required string Subject { get; init; }
    /// <summary>Gets the plain-text body, which is always the fallback representation.</summary>
    public required string Text { get; init; }
    /// <summary>Gets the optional HTML body.</summary>
    public string? Html { get; init; }
    /// <summary>Gets binary attachments and inline resources.</summary>
    public IReadOnlyCollection<EmailAttachment> Attachments { get; init; } = [];
}
