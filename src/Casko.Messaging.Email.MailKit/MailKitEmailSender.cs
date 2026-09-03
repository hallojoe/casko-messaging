using Casko.Messaging.Email.Delivery;
using Casko.Messaging.Email.MailKit.Configuration;
using Casko.Messaging.Email.MailKit.Mapping;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;

namespace Casko.Messaging.Email.MailKit;

internal sealed class MailKitEmailSender : IEmailSender
{
    private readonly IMimeMessageFactory _messageFactory;
    private readonly MailKitEmailOptions _options;

    public MailKitEmailSender(IMimeMessageFactory messageFactory, IOptions<MailKitEmailOptions> options)
    {
        _messageFactory = messageFactory;
        _options = options.Value;
    }

    public async Task SendAsync(EmailDelivery delivery, CancellationToken cancellationToken = default)
    {
        var message = _messageFactory.Create(delivery);
        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, _options.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None, cancellationToken);
        if (!string.IsNullOrWhiteSpace(_options.Username) && !string.IsNullOrWhiteSpace(_options.Password))
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    public async Task SendAsync(IEnumerable<EmailDelivery> deliveries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deliveries);
        foreach (var delivery in deliveries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SendAsync(delivery, cancellationToken);
        }
    }
}
