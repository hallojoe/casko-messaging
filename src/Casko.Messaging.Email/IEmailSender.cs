using Casko.Messaging.Email.Delivery;

namespace Casko.Messaging.Email;

/// <summary>Sends email deliveries.</summary>
public interface IEmailSender
{
    /// <summary>Sends one physical email message.</summary>
    /// <param name="delivery">The content and recipients to deliver.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    Task<EmailDeliveryResult> SendAsync(EmailDelivery delivery, CancellationToken cancellationToken = default);

    /// <summary>Sends multiple independent email messages.</summary>
    /// <param name="deliveries">The deliveries to send.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    Task<IReadOnlyCollection<EmailDeliveryResult>> SendAsync(IEnumerable<EmailDelivery> deliveries, CancellationToken cancellationToken = default);
}
