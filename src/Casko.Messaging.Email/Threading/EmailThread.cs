using Casko.Messaging.Email.Reading;

namespace Casko.Messaging.Email.Threading;

/// <summary>Represents one mailbox-local email conversation.</summary>
public sealed record EmailThread
{
    /// <summary>Gets the stable mailbox message identifier of the conversation root.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the root message and all of its replies.</summary>
    public required EmailThreadNode Root { get; init; }
}

/// <summary>Represents one received message within an email conversation.</summary>
public sealed record EmailThreadNode
{
    /// <summary>Gets the provider-neutral message data.</summary>
    public required ReceivedEmailMessage Message { get; init; }

    /// <summary>Gets the resolved parent provider-neutral message identifier, if any.</summary>
    public string? ParentId { get; init; }

    /// <summary>Gets replies in chronological order.</summary>
    public IReadOnlyCollection<EmailThreadNode> Children { get; init; } = [];
}
