namespace Casko.Messaging.Email.Reading;

/// <summary>Identifies a configured mailbox without exposing its transport details.</summary>
/// <param name="Value">The logical mailbox identifier.</param>
public readonly record struct EmailMailboxId(string Value);
