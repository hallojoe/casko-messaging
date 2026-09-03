namespace Casko.Messaging.Email.Delivery;

/// <summary>Describes an email message successfully accepted by the transport.</summary>
public sealed record EmailDeliveryResult
{
    /// <summary>Gets the RFC email <c>Message-Id</c> assigned to the outgoing message.</summary>
    public required string MessageId { get; init; }
}
