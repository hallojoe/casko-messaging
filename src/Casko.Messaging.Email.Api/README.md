# Casko.Messaging.Email.Api

Development-only ASP.NET Core API for exercising the email abstractions and local mail infrastructure.

## Run

Run through `Casko.Messaging.AppHost` for automatic MailPit and GreenMail configuration. When started independently, the launch profiles use HTTP `5000` and HTTPS `7001`.

## Endpoints and requests

`Email.Single.http` and `Email.Scenarios.http` contain ready-to-run sample requests. The API covers basic delivery, recipient variants, attachments, inline images, personalized messages, IMAP reads, reply discovery, and seeded support conversations.

## Configuration

- `Email:MailKit` configures the production-style sender and reader.
- Aspire overrides outbound SMTP with MailPit.
- Aspire configures the local logical `Support` IMAP mailbox using GreenMail's Alice account.
- `Email:GreenMail:Smtp` is development-only configuration used to inject sample incoming messages.

Keep this project a thin test host. Production endpoints, templates, persistence, and domain workflows do not belong here.
