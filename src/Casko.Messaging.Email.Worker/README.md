# Casko.Messaging.Email.Worker

Dedicated .NET worker that drains the durable SQL delivery backlog through SMTP.

## Responsibility

- Claims a bounded batch through `INotificationQueueStore` from the configured persistence provider.
- Sends with configured bounded concurrency and optional global message-rate limiting.
- Renders the immutable inline event template immediately before SMTP delivery.
- Reuses one MailKit SMTP client per sender loop, renews active leases, and persists the result of every attempt.

## Processing flow

```mermaid
flowchart LR
    SQL[(SQL delivery backlog)] -->|atomic leased batch| W[Email worker]
    W --> Q{{Bounded channel}}
    Q --> S1[Sender 1]
    Q --> S2[Sender 2]
    Q --> SN[Sender N]
    S1 & S2 & SN --> R[Shared rate limiter]
    R --> SMTP[(SMTP server)]
    SMTP -->|result| SQL

    classDef database fill:#dbeafe,stroke:#2563eb,color:#172554
    classDef worker fill:#fef3c7,stroke:#d97706,color:#78350f
    classDef sender fill:#dcfce7,stroke:#16a34a,color:#14532d
    classDef transport fill:#f3e8ff,stroke:#9333ea,color:#581c87
    class SQL database
    class W,Q worker
    class S1,S2,SN sender
    class R,SMTP transport
```

## Configuration

Configure `EmailDelivery` with `BatchSize`, `Concurrency`, `MaximumAttempts`, `MaximumMessagesPerSecond`, `PollInterval`, and `ProcessingLeaseDuration`. The defaults are deliberately gentle for local testing: 10 deliveries per batch, one SMTP sender loop, three attempts, one message per second, two-second polling, and a five-minute lease.

Critical mail has a separate lane configured by `CriticalBatchSize`, `CriticalConcurrency`, and `CriticalMaximumMessagesPerSecond`. It claims only `Critical` deliveries; the standard lane claims `Normal` and `Bulk`. `BulkPromotionAfter` promotes old bulk rows into normal ordering to prevent starvation. The default rate limits allow one standard and one critical message per second, so configure both below your SMTP provider's aggregate limit.

SMTP settings use `Email:MailKit`. Run through `Casko.Messaging.AppHost` to receive the SQL and MailPit connection configuration automatically.

Startup registers the SQL Server provider with `AddSqlServerNotifications`; migration initialization uses `INotificationStoreInitializer`. Delivery logic references only shared contracts, allowing a future provider to replace persistence registration without changing SMTP handling.

## Boundaries

The worker is an at-least-once transport. A process can fail after SMTP accepts a message but before SQL records `Sent`; deterministic per-delivery message IDs and guarded lease ownership updates reduce duplicate delivery risk. Do not move notification creation, recipient fan-out, or domain status-change handling into this project.
