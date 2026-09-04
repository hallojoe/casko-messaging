namespace Casko.Messaging.Email.Reading;

/// <summary>Specifies filtering and limits for reading a mailbox.</summary>
public sealed record EmailReadRequest
{
    /// <summary>Gets whether only unread messages should be returned.</summary>
    public bool UnreadOnly { get; init; }
    /// <summary>Gets the inclusive lower bound for received messages.</summary>
    public DateTimeOffset? ReceivedAfter { get; init; }
    /// <summary>Gets the maximum number of results, or <see langword="null"/> for no limit.</summary>
    public int? MaxResults { get; init; }
}
