namespace Casko.Messaging.Email.MailKit.Configuration;

/// <summary>Configures the SMTP transport used by the MailKit sender.</summary>
public sealed class MailKitEmailOptions
{
    /// <summary>Gets or sets the SMTP host.</summary>
    public required string Host { get; set; }
    /// <summary>Gets or sets the SMTP port.</summary>
    public int Port { get; set; } = 25;
    /// <summary>Gets or sets the optional SMTP username.</summary>
    public string? Username { get; set; }
    /// <summary>Gets or sets the optional SMTP password.</summary>
    public string? Password { get; set; }
    /// <summary>Gets or sets whether to connect with SSL.</summary>
    public bool UseSsl { get; set; }
    /// <summary>Gets or sets the sender address.</summary>
    public required string FromAddress { get; set; }
    /// <summary>Gets or sets the optional sender display name.</summary>
    public string? FromDisplayName { get; set; }

    /// <summary>Gets or sets receiving mailboxes by their logical identifier.</summary>
    public Dictionary<string, MailKitMailboxOptions> Mailboxes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
