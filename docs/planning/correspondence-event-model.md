# Correspondence BC — Event Model

**Date:** 2026-03-13
**Status:** ✅ Approved for implementation
**Cycle:** 28 (Correspondence BC Phase 1)
**Participants:** Principal Solutions Architect (solo planning session)

---

## Executive Summary

The **Correspondence BC** (formerly "Notifications BC" — see ADR 0030 for rename rationale) owns all customer-facing transactional communication triggered by business events across CritterSupply's bounded contexts. This Event Model follows Adam Dymitruk's Event Modeling methodology to design the BC's internal domain, command/query surface, integration points, and workflows.

**Key Findings:**
- **Pure choreography pattern** — no orchestration sagas required
- **Message aggregate** as the core domain entity (event-sourced)
- **Channel abstraction** — email, SMS, push notification (future)
- **10 integration event subscriptions** from Orders, Fulfillment, Returns, Payments BCs
- **Idempotency as first-class concern** — MessageId-based deduplication
- **No PCI compliance risks** — never stores or transmits raw payment details

---

## 1. Brain Dump — What Happens in This Domain?

### Triggers (What Causes Correspondence to Act?)

**From Orders BC:**
- Order is placed → send order confirmation
- Order is cancelled → send cancellation notification

**From Fulfillment BC:**
- Shipment is dispatched → send tracking number
- Shipment is delivered → send delivery confirmation, invite product review
- Shipment delivery fails → alert customer, initiate re-delivery

**From Payments BC:**
- Refund is completed → confirm refund amount and timeline

**From Returns BC:**
- Return is approved → send return label and instructions
- Return is denied → explain rejection reason
- Return is completed → confirm return received and refund triggered
- Return expires → remind customer the return window has closed
- Return is received → acknowledge warehouse receipt (optional)
- Return is rejected (after inspection) → explain rejection reason (optional)

### Actions (What Does Correspondence Do?)

1. **Receive integration event** from another BC via RabbitMQ
2. **Check customer preferences** — query Customer Identity BC for email, SMS opt-in status
3. **Check idempotency** — have we already sent this message? (MessageId deduplication)
4. **Compose message content** — render template with event data (order number, tracking number, etc.)
5. **Select channel** — email, SMS, or push notification (based on preferences + event type)
6. **Send via provider** — call SendGrid, Twilio, FCM, etc.
7. **Handle delivery result** — success, failure, retry logic
8. **Publish outcome** — `CorrespondenceDelivered` or `CorrespondenceFailed` (for observability)

### Concepts (What Are the "Things" in This Domain?)

- **Message** — the central aggregate. Represents a correspondence sent to a customer.
- **MessageTemplate** — reusable template for rendering email/SMS content (HTML, plain text, subject line)
- **Channel** — delivery mechanism (Email, SMS, PushNotification)
- **DeliveryAttempt** — record of an attempt to send a message (timestamp, provider response, error code)
- **CustomerPreferences** — owned by Customer Identity BC, not Correspondence (queried via HTTP)

### Open Questions (Flagged for Owner Review)

1. **Template storage:** Should templates live in code (Razor files), database (Marten documents), or external service (SendGrid Dynamic Templates)?
   - **Recommendation:** Start with Razor templates in code (simplicity, type safety, version control). Migrate to external service in Phase 2 if marketing team needs self-service editing.

2. **Retry policy:** How many retry attempts? What backoff schedule?
   - **Recommendation:** 3 attempts with exponential backoff (5 min, 30 min, 2 hours). After 3 failures, mark as permanently failed and publish `CorrespondenceFailed`.

3. **SMS pricing:** SendGrid email is ~$0.0001/email. Twilio SMS is ~$0.0075/SMS. Should SMS be opt-in or automatic for high-priority events?
   - **Recommendation:** Phase 1 = email only. Phase 2 = opt-in SMS for "shipment dispatched" and "return approved" (customers can enable in preferences).

4. **Push notifications:** Apple Push Notification Service (APNS) and Firebase Cloud Messaging (FCM) require mobile app. Phase 3?
   - **Recommendation:** Defer to Phase 3. Email covers 95% of transactional communication needs.

5. **Preference caching:** Should we cache customer preferences to avoid querying Customer Identity BC on every message?
   - **Recommendation:** No caching in Phase 1. Always query Customer Identity BC (HTTP GET /api/customers/{customerId}). Prevents stale preferences. Latency is acceptable (<50ms).

---

## 2. The Timeline — Event Ordering

### Happy Path: Order Confirmation Email

```
Time →

1. OrderPlaced (Orders BC publishes to RabbitMQ)
   ↓
2. OrderPlacedHandler receives event (Wolverine handler in Correspondence BC)
   ↓
3. Query Customer Identity: GET /api/customers/{customerId} → {email, emailOptIn: true}
   ↓
4. Check idempotency: Does MessageId already exist in Marten event store? → No
   ↓
5. Create Message aggregate: Message.Create(customerId, OrderPlacedTemplate, OrderPlaced data)
   ↓ (domain event: MessageQueued)
6. Compose email content: Render Razor template with order summary, items, shipping address
   ↓
7. Send via SendGrid: HTTP POST to SendGrid API
   ↓
8. SendGrid responds: 202 Accepted (queued for delivery)
   ↓ (domain event: MessageDelivered)
9. Publish integration event: CorrespondenceDelivered (for observability, logging, analytics)
```

### Sad Path: Email Delivery Failure

```
Time →

1. ShipmentDispatched (Fulfillment BC publishes)
   ↓
2. ShipmentDispatchedHandler receives event
   ↓
3. Query Customer Identity: GET /api/customers/{customerId} → {email, emailOptIn: false}
   ↓
4. Customer has opted out of email → Do not send
   ↓ (domain event: MessageSkipped)
5. Publish integration event: CorrespondenceSkipped (reason: opted out)
```

**Alternative Sad Path: Provider Failure**

```
Time →

1. ReturnApproved (Returns BC publishes)
   ↓
2. ReturnApprovedHandler receives event
   ↓
3. Query Customer Identity: succeeds
   ↓
4. Check idempotency: Message already sent? → No
   ↓
5. Create Message aggregate
   ↓
6. Send via SendGrid: HTTP POST → 500 Internal Server Error
   ↓ (domain event: DeliveryFailed, attempt: 1)
7. Schedule retry: Wolverine.Delay(5 minutes)
   ↓
8. Retry 1: Send via SendGrid → 500 Internal Server Error
   ↓ (domain event: DeliveryFailed, attempt: 2)
9. Schedule retry: Wolverine.Delay(30 minutes)
   ↓
10. Retry 2: Send via SendGrid → 200 OK
   ↓ (domain event: MessageDelivered, deliveredAt: now, attempt: 3)
11. Publish integration event: CorrespondenceDelivered
```

---

## 3. Commands and Queries

### Commands (Write Operations)

Correspondence BC does **not expose HTTP commands** to external callers. All actions are triggered by integration events from other BCs.

**Internal Commands (not exposed as HTTP endpoints):**

1. **SendMessage** — triggered by integration event handlers
   - Input: `CustomerId`, `TemplateId`, `TemplateData` (order ID, tracking number, etc.)
   - Output: `MessageId` (Guid)
   - Side effect: Publishes `CorrespondenceQueued` or `CorrespondenceSkipped`

2. **RetryDelivery** — triggered by scheduled message (Wolverine durable scheduling)
   - Input: `MessageId`, `AttemptNumber`
   - Output: success or failure
   - Side effect: Publishes `CorrespondenceDelivered` or `CorrespondenceFailed`

### Queries (Read Operations)

**Phase 1 (Minimum Viable):**

1. **GET /api/correspondence/messages/{customerId}** — list all messages sent to a customer
   - Returns: `MessageId`, `SentAt`, `Channel`, `Subject`, `Status` (Queued/Delivered/Failed)
   - Used by: Customer Experience BC (future "View My Messages" page)

2. **GET /api/correspondence/messages/{messageId}** — get details of a specific message
   - Returns: full message content, delivery attempts, error logs
   - Used by: Backoffice (customer service tooling for investigating delivery issues)

**Phase 2 (Future):**

3. **GET /api/correspondence/messages/{customerId}/unread** — count of unread messages
   - Used by: Customer Experience BC (notification badge)

---

## 4. Views and Projections

### Primary View: MessageListView (Marten Inline Projection)

**Purpose:** Support customer message history queries

**Schema:**
```csharp
public sealed record MessageListView
{
    public Guid Id { get; init; } // MessageId
    public Guid CustomerId { get; init; }
    public string Channel { get; init; } // Email, SMS, Push
    public string Subject { get; init; }
    public string Status { get; init; } // Queued, Delivered, Failed, Skipped
    public DateTimeOffset QueuedAt { get; init; }
    public DateTimeOffset? DeliveredAt { get; init; }
    public int AttemptCount { get; init; }
    public string? FailureReason { get; init; }
}
```

**Projection Logic:**
- `MessageQueued` → insert row (Status = Queued)
- `MessageDelivered` → update row (Status = Delivered, DeliveredAt = now)
- `DeliveryFailed` → update row (AttemptCount += 1, FailureReason = error message)
- `MessageSkipped` → update row (Status = Skipped, FailureReason = "Customer opted out")

### Secondary View: DeliveryMetricsView (Future — Phase 2)

**Purpose:** Observability dashboard (delivery success rate, latency, error counts)

**Schema:**
```csharp
public sealed record DeliveryMetricsView
{
    public string Date { get; init; } // yyyy-MM-dd
    public string Channel { get; init; }
    public int TotalQueued { get; init; }
    public int TotalDelivered { get; init; }
    public int TotalFailed { get; init; }
    public int TotalSkipped { get; init; }
    public double SuccessRate { get; init; } // TotalDelivered / (TotalQueued - TotalSkipped)
    public double AvgDeliveryTimeSeconds { get; init; }
}
```

**Projection Logic:**
- `MessageQueued` → increment TotalQueued for (Date, Channel)
- `MessageDelivered` → increment TotalDelivered, update AvgDeliveryTimeSeconds
- `DeliveryFailed` (after max retries) → increment TotalFailed
- `MessageSkipped` → increment TotalSkipped

---

## 5. Aggregates — Consistency Boundaries

### Primary Aggregate: Message (Event-Sourced)

**Stream ID:** `message-{Guid}` (natural key: Guid.NewGuid() on creation)

**Why event-sourced?**
- Full audit trail of delivery attempts (compliance, debugging)
- Immutable history (never lose data on retry failures)
- Wolverine saga-like retry coordination via durable scheduling

**Aggregate State:**
```csharp
public sealed record Message
{
    public Guid Id { get; init; }
    public Guid CustomerId { get; init; }
    public string Channel { get; init; } // Email, SMS, Push
    public string TemplateId { get; init; }
    public string Subject { get; init; }
    public string Body { get; init; }
    public MessageStatus Status { get; init; } // Queued, Delivered, Failed, Skipped
    public int AttemptCount { get; init; }
    public DateTimeOffset QueuedAt { get; init; }
    public DateTimeOffset? DeliveredAt { get; init; }
    public IReadOnlyList<DeliveryAttempt> Attempts { get; init; } = [];
}

public enum MessageStatus
{
    Queued,      // Created, not yet sent
    Delivered,   // Successfully delivered by provider
    Failed,      // Permanently failed after max retries
    Skipped      // Not sent (customer opted out, or channel disabled)
}

public sealed record DeliveryAttempt
{
    public int AttemptNumber { get; init; }
    public DateTimeOffset AttemptedAt { get; init; }
    public string ProviderResponse { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}
```

**Domain Events:**
```csharp
// Lifecycle events
public sealed record MessageQueued(
    Guid MessageId,
    Guid CustomerId,
    string Channel,
    string TemplateId,
    string Subject,
    string Body,
    DateTimeOffset QueuedAt
);

public sealed record MessageDelivered(
    Guid MessageId,
    DateTimeOffset DeliveredAt,
    int AttemptNumber,
    string ProviderResponse
);

public sealed record DeliveryFailed(
    Guid MessageId,
    int AttemptNumber,
    DateTimeOffset FailedAt,
    string ErrorMessage,
    string ProviderResponse
);

public sealed record MessageSkipped(
    Guid MessageId,
    string Reason // "Customer opted out of email" or "Channel disabled"
);
```

**Aggregate Methods:**
```csharp
public static class MessageFactory
{
    // Factory method: Create a new message
    public static (Message, MessageQueued) Create(
        Guid customerId,
        string channel,
        string templateId,
        string subject,
        string body)
    {
        var messageId = Guid.NewGuid();
        var @event = new MessageQueued(
            messageId,
            customerId,
            channel,
            templateId,
            subject,
            body,
            DateTimeOffset.UtcNow
        );

        var message = new Message
        {
            Id = messageId,
            CustomerId = customerId,
            Channel = channel,
            TemplateId = templateId,
            Subject = subject,
            Body = body,
            Status = MessageStatus.Queued,
            AttemptCount = 0,
            QueuedAt = @event.QueuedAt
        };

        return (message, @event);
    }
}

public partial record Message
{
    // Apply methods (event sourcing pattern)
    public Message Apply(MessageQueued @event) => this with
    {
        Id = @event.MessageId,
        CustomerId = @event.CustomerId,
        Channel = @event.Channel,
        TemplateId = @event.TemplateId,
        Subject = @event.Subject,
        Body = @event.Body,
        Status = MessageStatus.Queued,
        QueuedAt = @event.QueuedAt
    };

    public Message Apply(MessageDelivered @event) => this with
    {
        Status = MessageStatus.Delivered,
        DeliveredAt = @event.DeliveredAt,
        Attempts = Attempts.Add(new DeliveryAttempt
        {
            AttemptNumber = @event.AttemptNumber,
            AttemptedAt = @event.DeliveredAt,
            Success = true,
            ProviderResponse = @event.ProviderResponse
        })
    };

    public Message Apply(DeliveryFailed @event) => this with
    {
        Status = @event.AttemptNumber >= 3 ? MessageStatus.Failed : MessageStatus.Queued,
        AttemptCount = @event.AttemptNumber,
        Attempts = Attempts.Add(new DeliveryAttempt
        {
            AttemptNumber = @event.AttemptNumber,
            AttemptedAt = @event.FailedAt,
            Success = false,
            ErrorMessage = @event.ErrorMessage,
            ProviderResponse = @event.ProviderResponse
        })
    };

    public Message Apply(MessageSkipped @event) => this with
    {
        Status = MessageStatus.Skipped
    };
}
```

---

## 6. Domain Events vs. Integration Messages

### Domain Events (Internal to Correspondence BC)

**Purpose:** Event sourcing the Message aggregate. These events are stored in the Marten event stream `message-{Guid}`.

1. `MessageQueued` — message created and queued for delivery
2. `MessageDelivered` — provider confirmed delivery
3. `DeliveryFailed` — provider returned error (transient or permanent)
4. `MessageSkipped` — message not sent (customer opted out)

**Naming Convention:** Past tense verbs describing state changes within the Message aggregate.

### Integration Messages (Published to Other BCs)

**Purpose:** Notify other BCs of correspondence outcomes (observability, analytics, customer experience).

1. `CorrespondenceQueued` — published immediately after `MessageQueued`
   - Consumed by: Operations Dashboard (future) for real-time monitoring
   - Schema: `{ MessageId, CustomerId, Channel, TemplateId, QueuedAt }`

2. `CorrespondenceDelivered` — published after `MessageDelivered`
   - Consumed by: Analytics BC (future) for delivery success rate metrics
   - Schema: `{ MessageId, CustomerId, Channel, DeliveredAt, AttemptCount }`

3. `CorrespondenceFailed` — published after permanent failure (3 retries exhausted)
   - Consumed by: Backoffice (future) for alerting CS agents
   - Schema: `{ MessageId, CustomerId, Channel, FailureReason, FailedAt }`

**Naming Convention:** Noun-based events following CONTEXTS.md integration contract pattern (e.g., `OrderPlaced`, `ShipmentDispatched`, `ReturnApproved`).

---

## 7. Sagas — Stateful Orchestration

**Conclusion:** Correspondence BC does **not require sagas**.

**Why not?**
- Each integration event triggers a single, isolated message send operation
- No multi-step workflow coordination (unlike Orders saga coordinating Inventory + Payments + Fulfillment)
- Retry logic is handled by Wolverine's durable scheduling + the Message aggregate's `AttemptCount` state
- No inter-message dependencies (sending "order confirmation" does not depend on "shipment dispatched")

**Retry Pattern (Without Saga):**
```csharp
public sealed class SendMessageHandler
{
    public async Task<OutgoingMessages> Handle(
        SendMessage command,
        IDocumentSession session,
        ICorrespondenceChannel channel,
        IMessageBus bus)
    {
        var message = await session.Events.AggregateStreamAsync<Message>(command.MessageId);

        if (message.Status == MessageStatus.Delivered)
            return OutgoingMessages.Empty; // Idempotency: already sent

        try
        {
            var result = await channel.SendAsync(message);

            var delivered = new MessageDelivered(
                message.Id,
                DateTimeOffset.UtcNow,
                message.AttemptCount + 1,
                result.ProviderResponse
            );

            session.Events.Append(message.Id, delivered);

            return new OutgoingMessages(new CorrespondenceDelivered(
                message.Id,
                message.CustomerId,
                message.Channel,
                delivered.DeliveredAt,
                delivered.AttemptNumber
            ));
        }
        catch (Exception ex)
        {
            var failed = new DeliveryFailed(
                message.Id,
                message.AttemptCount + 1,
                DateTimeOffset.UtcNow,
                ex.Message,
                ex.ToString()
            );

            session.Events.Append(message.Id, failed);

            // Retry logic: exponential backoff
            if (failed.AttemptNumber < 3)
            {
                var delay = failed.AttemptNumber switch
                {
                    1 => TimeSpan.FromMinutes(5),
                    2 => TimeSpan.FromMinutes(30),
                    _ => TimeSpan.FromHours(2)
                };

                return new OutgoingMessages(
                    new SendMessage(message.Id).DelayedFor(delay)
                );
            }

            // Permanent failure after 3 attempts
            return new OutgoingMessages(new CorrespondenceFailed(
                message.Id,
                message.CustomerId,
                message.Channel,
                failed.ErrorMessage,
                failed.FailedAt
            ));
        }
    }
}
```

---

## 8. Subscribers and Reactors

### Inbound: What Correspondence Subscribes To (RabbitMQ Queues)

Correspondence BC consumes integration events from **4 upstream BCs**:

| Upstream BC | Queue Name | Events Consumed |
|-------------|------------|-----------------|
| **Orders** | `correspondence-orders-events` | `OrderPlaced`, `OrderCancelled` |
| **Fulfillment** | `correspondence-fulfillment-events` | `ShipmentDispatched`, `ShipmentDelivered`, `ShipmentDeliveryFailed` |
| **Payments** | `correspondence-payments-events` | `RefundCompleted` |
| **Returns** | `correspondence-returns-events` | `ReturnApproved`, `ReturnDenied`, `ReturnCompleted`, `ReturnExpired` |

**Total:** 10 integration event subscriptions

**Queue Configuration (Wolverine):**
```csharp
// Correspondence.Api/Program.cs
builder.Host.UseWolverine(opts =>
{
    opts.UseRabbitMq(builder.Configuration.GetConnectionString("rabbitmq"));

    opts.ListenToRabbitQueue("correspondence-orders-events");
    opts.ListenToRabbitQueue("correspondence-fulfillment-events");
    opts.ListenToRabbitQueue("correspondence-payments-events");
    opts.ListenToRabbitQueue("correspondence-returns-events");
});
```

**Handler Example:**
```csharp
// Correspondence/Handlers/OrderPlacedHandler.cs
public sealed class OrderPlacedHandler
{
    public async Task<OutgoingMessages> Handle(
        OrderPlaced @event,
        IDocumentSession session,
        ICustomerIdentityClient customerClient,
        IMessageTemplateRepository templates)
    {
        // 1. Query customer preferences
        var customer = await customerClient.GetCustomerAsync(@event.CustomerId);
        if (!customer.EmailOptIn)
        {
            var skipped = Message.Skip(@event.CustomerId, "Customer opted out of email");
            session.Events.Append(skipped.Id, skipped);
            return OutgoingMessages.Empty;
        }

        // 2. Compose message from template
        var template = await templates.GetAsync("OrderConfirmationEmail");
        var body = template.Render(new
        {
            OrderId = @event.OrderId,
            Items = @event.Items,
            Total = @event.Total,
            ShippingAddress = @event.ShippingAddress
        });

        // 3. Create Message aggregate
        var (message, queued) = MessageFactory.Create(
            @event.CustomerId,
            "Email",
            "OrderConfirmationEmail",
            $"Order Confirmation - {@event.OrderId}",
            body
        );

        session.Events.StartStream<Message>(message.Id, queued);

        // 4. Schedule immediate send
        return new OutgoingMessages(
            new CorrespondenceQueued(message.Id, message.CustomerId, "Email", DateTimeOffset.UtcNow),
            new SendMessage(message.Id) // Handled by SendMessageHandler
        );
    }
}
```

### Outbound: What Correspondence Publishes (RabbitMQ Exchanges)

Correspondence BC publishes integration events to **2 downstream consumers**:

| Integration Event | Downstream BC | Purpose |
|-------------------|---------------|---------|
| `CorrespondenceQueued` | Operations Dashboard (future) | Real-time monitoring of message queue depth |
| `CorrespondenceDelivered` | Analytics BC (future) | Delivery success rate metrics, customer engagement tracking |
| `CorrespondenceFailed` | Backoffice (future) | Alert CS agents to investigate delivery failures |

**Note:** In Phase 1, these events are published but have no consumers. They are retained in RabbitMQ for 7 days (default TTL) and will be consumed when Operations Dashboard and Backoffice are implemented.

---

## 9. Naming Critique and Ubiquitous Language

### Aggregate Names

✅ **Message** — Clear, domain-appropriate. Avoids technical jargon. A "message" is what customers receive (email, SMS, push notification). Alternative "Correspondence" would be confusing (aggregate name == BC name).

✅ **MessageStatus** — Enum values (Queued, Delivered, Failed, Skipped) match customer-facing language. "Skipped" is better than "Cancelled" (no action was cancelled; we chose not to send).

✅ **DeliveryAttempt** — Describes the concept clearly. Alternative "MessageAttempt" would be ambiguous.

### Event Names

✅ **MessageQueued** — Past tense, describes state change. Consistent with CritterSupply event naming (OrderPlaced, ShipmentDispatched).

✅ **MessageDelivered** — Clear completion event. Matches Fulfillment BC's `ShipmentDelivered` pattern.

✅ **DeliveryFailed** — Past tense, specific. Better than "MessageFailed" (ambiguous — did the message fail to create, or fail to deliver?).

✅ **MessageSkipped** — Communicates "we chose not to send" vs. "we tried and failed."

### Integration Event Names

✅ **CorrespondenceQueued / CorrespondenceDelivered / CorrespondenceFailed** — Noun-based pattern matching CONTEXTS.md conventions. Avoids collision with internal domain events (MessageQueued vs. CorrespondenceQueued).

### Command Names

✅ **SendMessage** — Imperative verb, internal command (not exposed as HTTP). Clear action.

❓ **RetryDelivery** — Consider renaming to **RetrySendMessage** for consistency. Current name is acceptable but slightly inconsistent with SendMessage.

### View Names

✅ **MessageListView** — Follows CritterSupply projection naming pattern (e.g., `CurrentPriceView`, `CartView`).

✅ **DeliveryMetricsView** — Clear purpose (observability metrics, not customer-facing data).

### Channel Names

✅ **Email / SMS / Push** — Industry-standard terms. "Email" is better than "EmailChannel" (redundant when used as `message.Channel == "Email"`).

---

## 10. Integration Surface Map

### HTTP API Endpoints (Correspondence.Api)

**Phase 1:**
- `GET /api/correspondence/messages/{customerId}` — list messages sent to customer
- `GET /api/correspondence/messages/{messageId}` — get message details

**Phase 2 (future):**
- `GET /api/correspondence/messages/{customerId}/unread` — unread message count

**Not Exposed:**
- ❌ No `POST /api/correspondence/messages` — all messages are triggered by integration events, not HTTP commands

### RabbitMQ Subscriptions (Inbound)

| BC | Queue | Events |
|----|-------|--------|
| Orders | `correspondence-orders-events` | OrderPlaced, OrderCancelled |
| Fulfillment | `correspondence-fulfillment-events` | ShipmentDispatched, ShipmentDelivered, ShipmentDeliveryFailed |
| Payments | `correspondence-payments-events` | RefundCompleted |
| Returns | `correspondence-returns-events` | ReturnApproved, ReturnDenied, ReturnCompleted, ReturnExpired |

**Total:** 4 queues, 10 events

### RabbitMQ Publications (Outbound)

| Event | Exchange | Routing Key | Consumers (Phase 2+) |
|-------|----------|-------------|----------------------|
| CorrespondenceQueued | `correspondence-events` | `correspondence.queued` | Operations Dashboard |
| CorrespondenceDelivered | `correspondence-events` | `correspondence.delivered` | Analytics BC |
| CorrespondenceFailed | `correspondence-events` | `correspondence.failed` | Backoffice |

**Total:** 3 events published to 1 exchange

### External Services (Outbound HTTP)

| Service | Purpose | Phase |
|---------|---------|-------|
| **Customer Identity BC** | Query customer email + preferences | 1 |
| **SendGrid API** | Send transactional emails | 1 |
| **Twilio API** | Send SMS (future) | 2 |
| **Firebase Cloud Messaging (FCM)** | Send push notifications (future) | 3 |

---

## Summary

This Event Model provides a complete design for the Correspondence BC:

- **Aggregate:** Message (event-sourced, idempotent)
- **Domain Events:** 4 lifecycle events (MessageQueued, MessageDelivered, DeliveryFailed, MessageSkipped)
- **Integration Events:** 10 inbound (from 4 BCs), 3 outbound
- **Commands:** 2 internal commands (SendMessage, RetryDelivery)
- **Queries:** 2 HTTP endpoints (customer message history, message details)
- **Projections:** 1 primary view (MessageListView), 1 future view (DeliveryMetricsView)
- **Sagas:** None required (pure choreography)
- **Subscribers:** 4 RabbitMQ queues
- **Publishers:** 1 RabbitMQ exchange

**Next Steps:** Risk analysis, implementation roadmap (see sections below).
