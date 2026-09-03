namespace Casko.Messaging.Email.Attachments;

/// <summary>Specifies whether an email attachment is downloaded or embedded in the message body.</summary>
public enum EmailAttachmentDisposition
{
    /// <summary>A regular downloadable attachment.</summary>
    Attachment,
    /// <summary>An inline resource, normally referenced by a content ID in HTML.</summary>
    Inline
}
