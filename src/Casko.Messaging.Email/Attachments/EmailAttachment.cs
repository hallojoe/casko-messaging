namespace Casko.Messaging.Email.Attachments;

/// <summary>Represents a binary attachment or inline resource.</summary>
public sealed record EmailAttachment
{
    /// <summary>Gets the attachment file name.</summary>
    public required string FileName { get; init; }
    /// <summary>Gets the MIME content type.</summary>
    public required string ContentType { get; init; }
    /// <summary>Gets the binary content.</summary>
    public required ReadOnlyMemory<byte> Content { get; init; }
    /// <summary>Gets the attachment disposition.</summary>
    public EmailAttachmentDisposition Disposition { get; init; } = EmailAttachmentDisposition.Attachment;
    /// <summary>Gets the content ID for an inline resource.</summary>
    public string? ContentId { get; init; }
}
