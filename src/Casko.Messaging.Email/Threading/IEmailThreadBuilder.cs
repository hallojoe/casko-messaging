using Casko.Messaging.Email.Reading;

namespace Casko.Messaging.Email.Threading;

/// <summary>Builds mailbox-local conversation threads from received email metadata.</summary>
public interface IEmailThreadBuilder
{
    /// <summary>Builds ordered conversation trees from the supplied mailbox messages.</summary>
    IReadOnlyCollection<EmailThread> Build(IReadOnlyCollection<ReceivedEmailMessage> messages);
}
