using Casko.Messaging.Email.Reading;

namespace Casko.Messaging.Email.Threading;

/// <summary>Resolves RFC reply headers into mailbox-local conversation trees.</summary>
public sealed class EmailThreadBuilder : IEmailThreadBuilder
{
    /// <inheritdoc />
    public IReadOnlyCollection<EmailThread> Build(IReadOnlyCollection<ReceivedEmailMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var ordered = messages.OrderBy(message => message.ReceivedAt).ThenBy(message => message.Id, StringComparer.Ordinal).ToArray();
        var byMessageId = ordered.Where(message => !string.IsNullOrWhiteSpace(message.MessageId))
            .GroupBy(message => message.MessageId!, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

        var candidates = ordered.ToDictionary(message => message.Id, message => FindParent(message, byMessageId)?.Id, StringComparer.Ordinal);
        var parents = ordered.ToDictionary(
            message => message.Id,
            message => WouldCreateCycle(message.Id, candidates) ? null : candidates[message.Id],
            StringComparer.Ordinal);
        var children = ordered.ToDictionary(message => message.Id, _ => new List<ReceivedEmailMessage>(), StringComparer.Ordinal);

        foreach (var message in ordered)
        {
            if (parents[message.Id] is { } parentId) children[parentId].Add(message);
        }

        EmailThreadNode BuildNode(ReceivedEmailMessage message) => new()
        {
            Message = message,
            ParentId = parents[message.Id],
            Children = children[message.Id].Select(BuildNode).ToArray()
        };

        return ordered.Where(message => parents[message.Id] is null)
            .Select(message => new EmailThread { Id = message.Id, Root = BuildNode(message) })
            .ToArray();
    }

    private static ReceivedEmailMessage? FindParent(ReceivedEmailMessage message, IReadOnlyDictionary<string, ReceivedEmailMessage> byMessageId)
    {
        if (!string.IsNullOrWhiteSpace(message.InReplyTo) && byMessageId.TryGetValue(message.InReplyTo, out var inReplyTo)) return inReplyTo;
        foreach (var reference in message.References.Reverse())
            if (byMessageId.TryGetValue(reference, out var referenced)) return referenced;
        return null;
    }

    private static bool WouldCreateCycle(string messageId, IReadOnlyDictionary<string, string?> candidates)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { messageId };
        var current = candidates[messageId];
        while (current is not null)
        {
            if (!visited.Add(current)) return true;
            current = candidates[current];
        }
        return false;
    }
}
