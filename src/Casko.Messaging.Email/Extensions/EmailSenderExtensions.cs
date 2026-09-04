using Casko.Messaging.Email.Delivery;
using Casko.Messaging.Email.Recipients;

namespace Casko.Messaging.Email;

/// <summary>Provides provider-independent convenience methods for sending email.</summary>
public static class EmailSenderExtensions
{
    /// <summary>Sends a message to one primary recipient.</summary>
    public static Task<EmailDeliveryResult> SendAsync(this IEmailSender sender, EmailAddress recipient, EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sender);
        return sender.SendAsync(new EmailDelivery { Message = message, Recipients = [new EmailRecipient { Address = recipient }] }, cancellationToken);
    }

    /// <summary>Sends one message to multiple primary recipients.</summary>
    public static Task<EmailDeliveryResult> SendAsync(this IEmailSender sender, IEnumerable<EmailAddress> recipients, EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(recipients);
        return sender.SendAsync(new EmailDelivery { Message = message, Recipients = recipients.Select(address => new EmailRecipient { Address = address }).ToArray() }, cancellationToken);
    }
}
