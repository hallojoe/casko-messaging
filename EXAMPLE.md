# Email notification examples

These two scenarios use C#-style pseudocode. Business services such as `orders` and `billing` are illustrative; the notification contracts and method names match this solution.

Both scenarios queue durable notifications through `INotificationWriter`. After the queue transaction commits, the email worker claims the deliveries and sends them through SMTP. A successful writer call means **queued**, not delivered.

## Setup

Register the SQL Server implementation in your application's startup:

```csharp
using Casko.Messaging.Email.BulkDelivery;

builder.Services.AddSqlServerNotifications(
    builder.Configuration.GetConnectionString("notifications"));

builder.Services.Configure<NotificationIngestionOptions>(
    builder.Configuration.GetSection("Notifications:Ingestion"));
```

Inject `INotificationWriter` into the application service. Business logic depends on this shared contract; a future PostgreSQL provider would replace the startup registration.

## Scenario 1: Send one email after completing some work

An employee approves an order. After saving the approval, queue one confirmation for the customer.

```csharp
async Task ApproveOrderAsync(Guid orderId, CancellationToken ct)
{
    // 1. Do the business work and save the result.
    // ApprovalId is a persisted identifier for this particular approval.
    var approval = await orders.ApproveAndSaveAsync(orderId, ct);

    // 2. Describe the email resulting from that work.
    var notification = new NotificationInput(
        EntityId: approval.OrderId,
        EventType: "OrderApproved",
        Template: "order-approved",
        Message: new InlineEmailTemplate(
            Subject: $"Order {approval.OrderNumber} approved",
            Text: $"Your order {approval.OrderNumber} has been approved.",
            Html: null),
        IdempotencyKey: $"order-approved:{approval.ApprovalId}",
        Recipients:
        [
            new RecipientInput(
                EmailAddress: approval.CustomerEmail,
                RecipientId: approval.CustomerId)
        ]);

    // 3. Queue the event AND its recipient in one atomic transaction.
    // A one-item batch is also the convenient single-email creation path.
    var batch = await notifications.CreateBatchAsync(
        new NotificationBatchRequest([notification]), ct);

    var result = batch.Notifications.Single();
    logger.LogInformation(
        "Order notification {Id}: {Added} new delivery queued",
        result.Id, result.AddedRecipients);

    // 4. The separate email worker sends the email and records its outcome.
}
```

Here `notifications` is the injected `INotificationWriter`. The business operation does not open an SMTP connection or wait for delivery.

The stable approval ID makes the notification safe to retry: the same event content and recipient will not create another delivery. A genuinely new approval must have a different approval ID.

For a password-reset email, set `Priority: NotificationPriority.Critical` on the `NotificationInput` (or `CreateNotificationEventRequest`). The worker's critical lane claims it ahead of normal and bulk backlogs. Keep the reset request's stable ID as the idempotency key; changing its priority later is treated as conflicting event content.

## Scenario 2: Send many emails after work produces many notifications

A billing run generates 6,000 invoices. Each invoice needs its own email, with its own subject, message, customer, and idempotency key.

```csharp
async Task RunMonthlyBillingAsync(Guid billingRunId, CancellationToken ct)
{
    // 1. Do the business work and persist the invoices.
    // These are immutable results of this run, including recipient details.
    var invoices = await billing.GenerateAndSaveInvoicesAsync(billingRunId, ct);

    // 2. Build notification inputs, without making a database call per invoice.
    var pending = invoices.Select(invoice => new NotificationInput(
        EntityId: invoice.Id,
        EventType: "InvoiceIssued",
        Template: "invoice-issued",
        Message: new InlineEmailTemplate(
            Subject: $"Invoice {invoice.Number}",
            Text: $"Your invoice {invoice.Number} is ready to view.",
            Html: null),
        IdempotencyKey: $"invoice-issued:{invoice.Id}",
        Recipients:
        [
            new RecipientInput(
                EmailAddress: invoice.CustomerEmail,
                RecipientId: invoice.CustomerId)
        ]));

    // 3. Submit bounded batches. This example has exactly one recipient per
    // event, so 1,000 events also means 1,000 recipient entries per request.
    foreach (var chunk in pending.Chunk(1_000))
    {
        var result = await notifications.CreateBatchAsync(
            new NotificationBatchRequest(chunk), ct);

        logger.LogInformation(
            "Billing notifications queued: {Added} new, {Existing} already present",
            result.Notifications.Sum(x => x.AddedRecipients),
            result.Notifications.Sum(x => x.ExistingRecipients));
    }

    // 4. The email worker drains the queue at its configured concurrency/rate.
}
```

For 6,000 invoices, this makes six bulk writer calls. Each SQL Server call streams the batch into temporary tables and inserts missing events and deliveries using set-based SQL. Each call commits completely or rolls back completely; all six calls are not one shared transaction.

The default limits are 10,000 events and 10,000 total recipient entries per call. HTTP callers use `POST /api/notifications/batch` and must also fit within the default 10 MiB request-body limit. If events have multiple recipients, size batches by the total recipient count as well as the event count; the simple `Chunk(1_000)` example assumes one recipient each.

For one shared message sent to many people, use a single `NotificationInput` with many `Recipients` instead. This stores the message once and creates one delivery per distinct recipient.

## Business transaction and retry boundary

The examples show the order of operations clearly, but saving business data and then calling the writer leaves a failure window: the process can stop after saving an order/invoice and before queuing its notification.

For production reliability, persist an immutable notification intent in your application's outbox **in the same transaction as the business change**. An outbox dispatcher then makes the writer calls shown above and marks each intent processed after success. The outbox is an application responsibility and is not implemented by this solution.

Retry using the saved event ID/key, message, and recipient details. Do not generate a fresh random idempotency key or rebuild changed message content on each retry. Reusing a key with different event content is a conflict; the entire batch is rejected.

If a response is lost after commit, retrying the same batch reuses the events and deliveries. Counts may report existing rows on that retry. Already committed chunks remain queued if a later chunk fails.

Queue deduplication does not make SMTP delivery exactly-once: the worker may retry after SMTP accepts a message but before the sent status is saved.
