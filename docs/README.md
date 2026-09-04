# Casko.Messaging

A provider-agnostic email abstraction for .NET 10, with a MailKit SMTP/IMAP adapter and an Aspire local email environment.

## Projects

- `Casko.Messaging.Email` contains the public email models and `IEmailSender` only.
- `Casko.Messaging.Email.MailKit` maps those models to MIME and sends them over SMTP.
- `Casko.Messaging.Email.Api` provides development-only sample endpoints.
- `Casko.Messaging.AppHost` starts the API, MailPit, GreenMail, and Roundcube through Aspire.

`EmailMessage` describes reusable content. `EmailDelivery` describes one actual delivery, including recipients, reply-to address, and optional conversation parent. This supports a message with To/Cc/Bcc recipients, as well as private or personalized fan-out using multiple deliveries. Every successful send returns an `EmailDeliveryResult` containing the RFC `Message-Id` for correlation.

`IEmailReader` reads provider-neutral `ReceivedEmailMessage` values from configured logical mailboxes. It uses `Message-Id`, `In-Reply-To`, and `References` to find direct and later conversation replies; opening a mailbox is read-only and does not mark messages as read.

## Run locally

```bash
dotnet run --project src/Casko.Messaging.AppHost
```

Open the Aspire dashboard URL printed in the console. It exposes these development resources:

- **MailPit** captures API-generated outbound SMTP messages and provides their inspection UI.
- **GreenMail** provides the IMAP mailbox server used by the API reader.
- **Roundcube** is the mailbox UI: it reads GreenMail over IMAP and sends messages through MailPit SMTP.

Roundcube and the API can log into GreenMail with either `alice@example.test` / `password` or `bob@example.test` / `password`. The API exposes Alice as the local `Support` inbox and Bob as the local `Sales` inbox. Aspire injects dynamic endpoint information; production settings remain under `Email:MailKit`.

Roundcube intentionally sends to MailPit without SMTP authentication, because the local MailPit resource accepts unauthenticated SMTP.

The API exposes these development sample routes:

- `POST /email/single`
- `POST /email/multiple-recipients`
- `POST /email/personalized`
- `POST /email/attachment`
- `POST /email/inline-image`
- `GET /email/mailboxes/{mailbox}/messages`
- `GET /email/mailboxes/{mailbox}/unread`
- `GET /email/mailboxes/{mailbox}/replies/{messageId}`
- `POST /email/reply`
- `POST /email/support/seed`
- `POST /email/demo/seed`

## Inbox thread viewer

The API host also serves a development inbox viewer at its root URL. It lists the configured GreenMail inboxes, displays each inbox's mailbox-local conversation threads, and renders a selected thread recursively. HTML mail is sanitized before it is displayed and attachment payload bytes are not exposed by the viewer API.

Seed both local inboxes with:

```bash
curl -X POST http://localhost:5000/email/demo/seed
```

UI-oriented API routes are `GET /api/mailboxes`, `GET /api/mailboxes/{mailbox}/threads`, and `GET /api/mailboxes/{mailbox}/threads/{threadId}`. In Development, the generated .NET OpenAPI document is available at `/openapi/v1.json`.

For example:

```bash
curl -X POST http://localhost:5000/email/inline-image
```

## Consumer configuration

Register the MailKit sender in an application:

```csharp
builder.Services.AddMailKitEmail(
    builder.Configuration.GetSection("Email:MailKit"));
```

```json
{
  "Email": {
    "MailKit": {
      "Host": "smtp.example.com",
      "Port": 587,
      "UseSsl": false,
      "FromAddress": "noreply@example.com",
      "FromDisplayName": "Example",
      "Mailboxes": {
        "Support": {
          "Address": "support@example.com",
          "Host": "imap.example.com",
          "Port": 993,
          "UseSsl": true
        }
      }
    }
  }
}
```

Supply credentials from configuration providers appropriate to the environment, rather than source-controlled configuration.
