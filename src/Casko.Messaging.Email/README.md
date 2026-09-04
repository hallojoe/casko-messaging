# Casko.Messaging.Email

Provider-neutral email contracts for .NET applications.

## Responsibility

Defines outgoing email content and delivery models, incoming email reading models, and the `IEmailSender` / `IEmailReader` interfaces. `EmailMessage` is reusable content; `EmailDelivery` defines its recipients and optional thread parent.

## Boundaries

Do not add dependencies on MailKit, MimeKit, SMTP, IMAP, Aspire, ASP.NET Core, or a specific email provider. Keep public models immutable where practical and document public APIs with XML comments.

## Key contracts

- `IEmailSender` returns `EmailDeliveryResult`, including the RFC `Message-Id`.
- `IEmailReader` retrieves `ReceivedEmailMessage` values without changing read state.
- `EmailMessageReference` carries `Message-Id` plus conversation references for reply correlation.
- `EmailAttachment` supports both standard and CID inline attachments.

Provider implementations belong in `Casko.Messaging.Email.MailKit`.
