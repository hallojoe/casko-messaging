namespace Casko.Messaging.Email.Api.Contracts;

public sealed record MailboxResponse(string Id, string DisplayName, string Address);

public sealed record EmailAddressResponse(string Address, string? DisplayName);

public sealed record EmailRecipientResponse(EmailAddressResponse Address, string Type);

public sealed record EmailAttachmentResponse(string FileName, string ContentType, string Disposition, string? ContentId);

public sealed record EmailThreadSummaryResponse(string Id, string? Subject, string Participant, string? Preview, int MessageCount, DateTimeOffset LatestReceivedAt);

public sealed record EmailThreadResponse(string Id, string? Subject, int MessageCount, DateTimeOffset LatestReceivedAt, EmailThreadMessageResponse Root);

public sealed record EmailThreadMessageResponse(
    string Id,
    string? MessageId,
    string? ParentId,
    string? InReplyTo,
    IReadOnlyCollection<string> References,
    EmailAddressResponse From,
    IReadOnlyCollection<EmailRecipientResponse> Recipients,
    string? Subject,
    string? Text,
    string? Html,
    IReadOnlyCollection<EmailAttachmentResponse> Attachments,
    DateTimeOffset ReceivedAt,
    IReadOnlyCollection<EmailThreadMessageResponse> Children);
