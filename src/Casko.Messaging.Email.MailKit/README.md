# Casko.Messaging.Email.MailKit

MailKit-based SMTP sender and IMAP reader for the provider-neutral contracts in `Casko.Messaging.Email`.

## Responsibility

- Maps outgoing deliveries to MIME messages and sends them through SMTP.
- Generates and returns outgoing RFC `Message-Id` values.
- Applies `In-Reply-To` and `References` headers for application-generated replies.
- Reads IMAP mailboxes in read-only mode, maps MIME content to `ReceivedEmailMessage`, and finds conversation replies through standard headers.

## Configuration

Register with:

```csharp
services.AddMailKitEmail(configuration.GetSection("Email:MailKit"));
```

SMTP uses the root `Email:MailKit` settings. IMAP mailboxes are configured under `Email:MailKit:Mailboxes:{logicalMailboxName}`. Keep production credentials outside source-controlled configuration.

## Boundaries

MailKit and MimeKit types must remain internal to this project. Do not expose protocol-specific types through the core project’s public API.
