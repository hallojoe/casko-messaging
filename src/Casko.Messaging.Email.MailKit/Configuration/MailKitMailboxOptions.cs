namespace Casko.Messaging.Email.MailKit.Configuration;

/// <summary>Configures IMAP access to one receiving mailbox.</summary>
public sealed class MailKitMailboxOptions
{
    /// <summary>Gets or sets the mailbox's email address.</summary>
    public required string Address { get; set; }
    /// <summary>Gets or sets the IMAP host.</summary>
    public required string Host { get; set; }
    /// <summary>Gets or sets the IMAP port.</summary>
    public int Port { get; set; } = 993;
    /// <summary>Gets or sets whether SSL is used for the IMAP connection.</summary>
    public bool UseSsl { get; set; } = true;
    /// <summary>Gets or sets the optional IMAP username.</summary>
    public string? Username { get; set; }
    /// <summary>Gets or sets the optional IMAP password.</summary>
    public string? Password { get; set; }
}
