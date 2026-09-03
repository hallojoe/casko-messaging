namespace Casko.Messaging.Email.Reading;

/// <summary>Identifies an RFC email message and its known conversation ancestry.</summary>
public sealed record EmailMessageReference
{
    /// <summary>Gets the RFC email <c>Message-Id</c>.</summary>
    public required string MessageId { get; init; }
    /// <summary>Gets preceding message IDs in the conversation.</summary>
    public IReadOnlyCollection<string> References { get; init; } = [];
}
