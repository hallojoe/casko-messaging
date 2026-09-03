namespace Casko.Messaging.Email.Recipients;

/// <summary>Specifies how a recipient receives an email.</summary>
public enum EmailRecipientType
{
    /// <summary>A primary recipient.</summary>
    To,
    /// <summary>A carbon-copy recipient.</summary>
    Cc,
    /// <summary>A blind-carbon-copy recipient.</summary>
    Bcc
}
