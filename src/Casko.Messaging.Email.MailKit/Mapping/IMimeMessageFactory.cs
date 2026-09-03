using Casko.Messaging.Email.Delivery;
using MimeKit;

namespace Casko.Messaging.Email.MailKit.Mapping;

internal interface IMimeMessageFactory
{
    MimeMessage Create(EmailDelivery delivery);
}
