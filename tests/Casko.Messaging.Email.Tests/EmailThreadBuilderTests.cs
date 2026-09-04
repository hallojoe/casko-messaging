using Casko.Messaging.Email.Reading;
using Casko.Messaging.Email.Recipients;
using Casko.Messaging.Email.Threading;
using Xunit;

namespace Casko.Messaging.Email.Tests;

public sealed class EmailThreadBuilderTests
{
    private readonly EmailThreadBuilder _builder = new();

    [Fact]
    public void Uses_in_reply_to_before_references_and_orders_children_chronologically()
    {
        var root = Message("root", "<root>", 1);
        var referenced = Message("referenced", "<referenced>", 2);
        var first = Message("first", "<first>", 4, "<root>", ["<referenced>"]);
        var second = Message("second", "<second>", 3, "<root>");

        var threads = _builder.Build([root, referenced, first, second]);
        var rootThread = Assert.Single(threads, thread => thread.Id == "root");
        Assert.Equal(["second", "first"], rootThread.Root.Children.Select(child => child.Message.Id));
    }

    [Fact]
    public void Uses_last_known_reference_when_in_reply_to_is_unavailable()
    {
        var root = Message("root", "<root>", 1);
        var parent = Message("parent", "<parent>", 2, "<root>");
        var child = Message("child", "<child>", 3, "<missing>", ["<root>", "<parent>"]);

        var thread = Assert.Single(_builder.Build([root, parent, child]));
        Assert.Equal("parent", Assert.Single(thread.Root.Children).Message.Id);
        Assert.Equal("child", Assert.Single(thread.Root.Children.Single().Children).Message.Id);
    }

    [Fact]
    public void Keeps_unknown_and_missing_message_id_messages_as_roots()
    {
        var unknownParent = Message("unknown", "<unknown>", 1, "<not-present>");
        var noRfcId = Message("no-rfc", null, 2);

        Assert.Equal(["unknown", "no-rfc"], _builder.Build([unknownParent, noRfcId]).Select(thread => thread.Id));
    }

    [Fact]
    public void Breaks_cycles_into_independent_roots()
    {
        var first = Message("first", "<first>", 1, "<second>");
        var second = Message("second", "<second>", 2, "<first>");

        var threads = _builder.Build([first, second]);
        Assert.Equal(["first", "second"], threads.Select(thread => thread.Id));
        Assert.All(threads, thread => Assert.Empty(thread.Root.Children));
    }

    private static ReceivedEmailMessage Message(string id, string? messageId, int minute, string? inReplyTo = null, IReadOnlyCollection<string>? references = null) => new()
    {
        Id = id,
        MessageId = messageId,
        InReplyTo = inReplyTo,
        References = references ?? [],
        From = new EmailAddress { Address = "sender@example.test" },
        ReceivedAt = new DateTimeOffset(2026, 9, 4, 9, minute, 0, TimeSpan.Zero)
    };
}
