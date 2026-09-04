# Casko.Messaging.AppHost

Aspire AppHost for the solution’s development-only mail infrastructure.

## Resources

- **email-api**: the development API.
- **mailpit**: outbound SMTP catcher and inspection UI.
- **greenmail**: IMAP/SMTP test server and HTTP API.
- **roundcube**: browser mail client; reads GreenMail over IMAP and sends through MailPit SMTP.

GreenMail development accounts are `alice@example.test` / `password` and `bob@example.test` / `password`. The API’s logical `Support` mailbox is wired to Alice.

## Run

```bash
dotnet run --project src/Casko.Messaging.AppHost
```

Open the Aspire dashboard and use the MailPit and Roundcube HTTP endpoints. Container configuration belongs here, not in the core abstraction or MailKit adapter. The Roundcube config override deliberately disables SMTP authentication because local MailPit accepts unauthenticated SMTP.
