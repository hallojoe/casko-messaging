# Casko.Messaging

A provider-agnostic email abstraction for .NET 10, with a MailKit SMTP adapter and an Aspire/MailPit development host.

## Projects

- `Casko.Messaging.Email` contains the public email models and `IEmailSender` only.
- `Casko.Messaging.Email.MailKit` maps those models to MIME and sends them over SMTP.
- `Casko.Messaging.Email.Api` provides development-only sample endpoints.
- `Casko.Messaging.AppHost` starts the API and MailPit through Aspire.

`EmailMessage` describes reusable content. `EmailDelivery` describes one actual delivery, including recipients and reply-to address. This supports a message with To/Cc/Bcc recipients, as well as private or personalized fan-out using multiple deliveries.

## Run locally

```bash
dotnet run --project src/Casko.Messaging.AppHost
```

Open the Aspire dashboard URL printed in the console, then open the MailPit resource. Aspire injects MailPit's SMTP connection string into the API; production-style settings remain in `Email:MailKit`.

The API exposes these development sample routes:

- `POST /email/single`
- `POST /email/multiple-recipients`
- `POST /email/personalized`
- `POST /email/attachment`
- `POST /email/inline-image`

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
      "FromDisplayName": "Example"
    }
  }
}
```

Supply credentials from configuration providers appropriate to the environment, rather than source-controlled configuration.
