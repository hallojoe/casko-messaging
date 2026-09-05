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

Queue 100 low-rate bulk-delivery messages, split evenly between the Alice and Bob GreenMail inboxes, with `POST /api/notifications/demo/test-inboxes`.

## Bulk notification creation

`POST /api/notifications/batch` creates many events and recipients atomically. It returns 200 after commit, with a `deliveryBatchId`, per-event IDs, and inserted/existing/duplicate counts. A client that may retry after a lost response should include a stable `deliveryBatchId` in its request:

```json
{
  "deliveryBatchId": "e1a9dfd7-c4a9-4a06-9059-1954d3d0747f",
  "notifications": [
    {
      "entityId": "f177b6da-6968-411a-b4f1-aae4b9e8b948",
      "eventType": "OrderUpdated",
      "template": "order-update",
      "message": { "subject": "Order update", "text": "Your order has shipped.", "html": null },
      "idempotencyKey": "order-123-shipped",
      "priority": "Normal",
      "recipients": [
        { "emailAddress": "alice@example.test", "recipientId": null },
        { "emailAddress": "bob@example.test", "recipientId": null }
      ]
    }
  ]
}
```

Retrieve progress with `GET /api/email-delivery/status/{deliveryBatchId}`. The response contains `total`, `pending`, `processing`, `retrying`, `delivered`, `failed`, `completed`, `progress`, and `isComplete`. It returns 404 when no persisted deliveries belong to that ID; legacy deliveries created before batch correlation remain intentionally unavailable through this endpoint.

Default `Notifications:Ingestion` settings are MaximumEvents=10000, MaximumRecipients=10000 (total input entries), and MaximumRequestBytes=10485760. The body limit applies to notification POST endpoints, including chunked requests on Kestrel. A single event may use the whole recipient allowance. Larger imports must be split into separate atomic requests.

The original create-event and append-recipient routes remain available with their original response shapes; recipient append now uses bulk persistence and the configured recipient limit.

Repeated keys with identical event content reuse events and add only missing recipients. Reusing a key with different content returns 409. Invalid fields/limits return 400, oversized bodies 413, and missing events 404. A failed batch commits nothing. Retries after a lost response may return existing counts. See the shared and SQL Server provider READMEs for identity and transaction guarantees.

Use `priority: "Critical"` for password-reset and security messages, `"Normal"` for normal transactional mail, and `"Bulk"` for campaigns. Priorities are immutable for an idempotency key. The worker reserves a critical lane so bulk work cannot claim that capacity.

## Configuration

- `Email:MailKit` configures the production-style sender and reader.
- Aspire overrides outbound SMTP with MailPit.
- Aspire configures the local logical `Support` IMAP mailbox using GreenMail's Alice account.
- `Email:GreenMail:Smtp` is development-only configuration used to inject sample incoming messages.

Keep this project a thin test host. Production endpoints, templates, persistence, and domain workflows do not belong here.
