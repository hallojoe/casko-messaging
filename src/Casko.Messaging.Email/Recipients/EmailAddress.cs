namespace Casko.Messaging.Email.Recipients;

/// <summary>Represents an email address and its optional display name.</summary>
public sealed record EmailAddress
{
    /// <summary>Gets the email address.</summary>
    public required string Address { get; init; }

    /// <summary>Gets the optional display name.</summary>
    public string? DisplayName { get; init; }
}
