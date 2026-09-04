# Casko.Messaging.Email.Api

Development-only ASP.NET Core API for exercising the email abstractions and local mail infrastructure.

## Run

Run through `Casko.Messaging.AppHost` for automatic MailPit and GreenMail configuration. When started independently, the launch profiles use HTTP `5000` and HTTPS `7001`.

## Endpoints and requests

`Email.Single.http` and `Email.Scenarios.http` contain ready-to-run sample requests. The API covers basic delivery, recipient variants, attachments, inline images, personalized messages, IMAP reads, reply discovery, and seeded support conversations.

## Inbox viewer and OpenAPI

The root URL serves an interactive MudBlazor inbox viewer. It discovers configured logical mailboxes, lists their mailbox-local threads, and displays a selected thread as a nested conversation. In the Aspire development environment, `Support` uses Alice's GreenMail inbox and `Sales` uses Bob's.

Seed both demo inboxes with `POST /email/demo/seed`. The viewer-ready endpoints are:

- `GET /api/mailboxes`
- `GET /api/mailboxes/{mailbox}/threads`
- `GET /api/mailboxes/{mailbox}/threads/{threadId}`

The .NET 10 OpenAPI document is available in Development at `/openapi/v1.json`.

## Configuration

- `Email:MailKit` configures the production-style sender and reader.
- Aspire overrides outbound SMTP with MailPit.
- Aspire configures the local logical `Support` IMAP mailbox using GreenMail's Alice account.
- `Email:GreenMail:Smtp` is development-only configuration used to inject sample incoming messages.

Keep this project a thin test host. Production endpoints, templates, persistence, and domain workflows do not belong here.
