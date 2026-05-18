# Transactional communication

> **Status:** Active (3 of 4 events emitted in production code; SMS channel infrastructure stubbed)
> **Type:** Choreography (Correspondence subscribes to upstream lifecycle events; outbound provider calls are stubbed)
> **Initiating actor:** System (upstream BC events)
> **BCs involved:** Correspondence (subscriber + sender); Orders, Fulfillment, Returns, Payments (upstream publishers); Backoffice + Customer Experience (downstream observers of `CorrespondenceQueued` / `CorrespondenceDelivered` / `CorrespondenceFailed`)
> **Most recent material milestone:** M31.0 — Phase 2 (7 additional integration events + SMS channel infrastructure)

## Purpose

Whenever a business event occurs in Orders, Fulfillment, Returns, or Payments that should produce a customer-facing email (or, in future, SMS), Correspondence opens a new `Message` event stream, queues a `SendMessage` command on the internal Wolverine bus, calls out to an `IEmailProvider` abstraction (stubbed at this milestone), and records `MessageDelivered` or `DeliveryFailed` on the same stream. Outbound `Correspondence.CorrespondenceQueued` / `CorrespondenceDelivered` / `CorrespondenceFailed` integration events are republished to Backoffice (and any other monitoring subscribers). The send schedule is `5 min` / `30 min` / `2 hr` retries; after three retriable failures the message is terminal `Failed`.

## Actors and triggers

- **Initiating actor:** System (any of 12 upstream integration events)
- **Trigger:** One of 12 inbound integration events (per the dossier — `OrderPlaced`, `OrderShipped`, `OrderDelivered`, `OrderCancelled`, `PaymentCaptured`, `PaymentFailed`, `ReturnApproved`, `ReturnDenied`, `ReturnRefunded`, `ShipmentDelivered`, `BackorderCreated`, and others enumerated in `bcs/correspondence.md`)
- **Prerequisite state:** Correspondence BC's Marten schema `correspondence` registered; `IEmailProvider` registered in DI (stub at this milestone)

## Trace (happy path — `OrderPlaced` → email queued + delivered)

| Step | BC | Command / Event / Edge | Resulting state / output | Dossier reference |
|------|----|------------------------|--------------------------|-------------------|
| 1 | Orders | Publishes `Orders.OrderPlaced` (broadcast event) | RabbitMQ delivery to Correspondence's listener queue | `bcs/orders.md#publishes`, `workflows/order-saga.md` |
| 2 | Correspondence | `OrderPlacedHandler` consumes the event; calls `MessageFactory.Create(...)` which mints a UUID v7 `MessageId` (`Guid.CreateVersion7()`) and produces a `MessageQueued` event; uses `MartenOps.StartStream<Message>(message.Id, messageQueued)` to open a new Marten stream | `Message` aggregate at `Status = Queued`; `Channel = "Email"` (only emitted value today); `AttemptCount = 0` | `bcs/correspondence.md#aggregates`, `bcs/correspondence.md#domain-events` |
| 3 | Correspondence | Same handler emits `Correspondence.CorrespondenceQueued(MessageId, CustomerId, Channel, QueuedAt)` to the broadcast queue | Outbound integration event published (consumed by Backoffice etc.) | `bcs/correspondence.md#outbound-published` |
| 4 | Correspondence | Same handler queues an internal `SendMessage(MessageId)` command on the local Wolverine bus | Command queued | `bcs/correspondence.md#commands` |
| 5 | Correspondence | `SendMessageHandler.Before(SendMessage, Message?)` returns `HandlerContinuation.Stop` if aggregate missing or terminal `Delivered` / `Skipped`. Otherwise `Handle` runs (compound handler shape) | Pre-send guards enforced | `bcs/correspondence.md#commands` |
| 6 | Correspondence | `SendMessageHandler.Handle` calls `IEmailProvider.SendAsync(...)`; on `ProviderResult.Success == true` emits `MessageDelivered(MessageId, DeliveredAt, AttemptNumber, ProviderResponse)` | `Message` at terminal `Status = Delivered`; `DeliveryAttempt` appended to `Attempts` list | `bcs/correspondence.md#domain-events` |
| 7 | Correspondence | Handler emits `Correspondence.CorrespondenceDelivered` integration event | Backoffice + monitoring observers consume | `bcs/correspondence.md#outbound-published` |
| 8 | Backoffice | `CorrespondenceDeliveredHandler` updates `CorrespondenceMetricsView` (singleton `Id = "current"`); also drives the `CorrespondenceHistoryView` BFF composition for the customer-service workflow | Operator dashboard reflects delivery | `bcs/backoffice.md#composition-map`, `bcs/backoffice.md#read-models--projections` (#5) |

## Trace (retry path — provider failure then success)

| Step | BC | Action |
|------|----|--------|
| A | Correspondence | `SendMessageHandler` receives `Success == false` (or non-`OperationCanceledException` throw); emits `DeliveryFailed(MessageId, AttemptNumber, FailedAt, ErrorMessage, ProviderResponse)` |
| B | Correspondence | `Apply(DeliveryFailed)` re-enters `Status = Queued` if `AttemptNumber < 3`, or sets terminal `Status = Failed` if `AttemptNumber >= 3` |
| C | Correspondence | Handler reschedules `SendMessage` with delay per the hard-coded `switch`: attempt 1 → 5 min, attempt 2 → 30 min, attempt 3 → 2 hr — via `outgoing.Add(new SendMessage(message.Id).DelayedFor(delay))` |
| D | Correspondence | After 3 retriable failures: emits `Correspondence.CorrespondenceFailed`; no further reschedule |
| E | Backoffice | `CorrespondenceFailedHandler` consumes; updates `CorrespondenceMetricsView` and pushes to `AlertFeedView` (Correspondence is one of the 6 alert-source BCs) |

## Projections and views

- **`Message` snapshot** (Correspondence) — inline; keyed by stream ID; loaded by `SendMessageHandler` via `[WriteAggregate]` and by `GetMessageDetails` via `AggregateStreamAsync<Message>`. Dossier: `bcs/correspondence.md#projections`.
- **`MessageListView`** (Correspondence) — inline `SingleStreamProjection<MessageListView, Guid>`; keyed by `MessageId`; covers all 4 event types; read by `GetMessagesForCustomer`. Dossier: `bcs/correspondence.md#projections`.
- **`CorrespondenceMetricsView`** (Backoffice) — singleton, queue-depth / deliveries / failures counters. See `backoffice-fan-in-dashboards.md`.
- **`CorrespondenceHistoryView`** (Backoffice composition) — per-customer message history surfaced via `GET /api/backoffice/customers/{customerId}/correspondence`. See `backoffice-customer-service.md`.

## Compensation paths

### Failure: Three retriable provider failures
- **Compensating action:** Stream stamped `MessageStatus = Failed` after the third `DeliveryFailed`; `Correspondence.CorrespondenceFailed` emitted; no further send attempts.
- **Resulting state:** Operator becomes responsible via `AlertFeedView`.

### Failure: Aggregate missing on `SendMessage` retry (transient consistency)
- **Compensating action:** `SendMessageHandler.Before` returns `HandlerContinuation.Stop`. No double-send.
- **Resulting state:** No-op.

### Failure: Already-terminal `Delivered` or `Skipped` on retry
- **Compensating action:** `Before` guard stops processing.
- **Resulting state:** Idempotent.

## Variants and edge cases

### `MessageSkipped` is declared-not-emitted
`MessageSkipped` (`Reason`) is defined as an event with both an aggregate `Apply` and a `MessageListView` projection branch, but **no production handler instantiates it**. Only `MessageFactory.Skip` constructs it, and that factory is invoked only by the unit test at `tests/Correspondence/Correspondence.UnitTests/MessageAggregateTests.cs#L66-L82`. The customer-opt-out / channel-disabled path it represents is not yet exercised. Dossier: `bcs/correspondence.md#domain-events`.

### SMS channel infrastructure stubbed
M31.0 / Phase 2 added the SMS channel infrastructure (provider abstraction, queue surface) but no production handler emits `Channel = "Sms"`; the only emitted value today is `"Email"`. Dossier: `bcs/correspondence.md#aggregates`.

### No HTTP write surface
Correspondence has no public HTTP command surface; all writes are message-bus driven by the inbound integration handlers. Read endpoints (`GetMessagesForCustomer`, `GetMessageDetails`) are read-only.

### Stream-per-trigger; no idempotency key on the inbound boundary
Each inbound integration event opens a fresh stream — there is no upstream-key-derived stream id and no idempotency key on the inbound boundary other than the message-bus delivery semantics. Wolverine's durable inbox handles message-level deduplication.

## BCs and roles

- **Correspondence** — Subscriber + sender. Owns the `Message` aggregate and the `SendMessage` retry loop. Dossier: `bcs/correspondence.md`.
- **Orders / Fulfillment / Returns / Payments** — Upstream publishers. Their workflows trigger the 12 inbound events.
- **Backoffice** — Downstream observer of `CorrespondenceQueued` / `CorrespondenceDelivered` / `CorrespondenceFailed`; aggregates into `CorrespondenceMetricsView` and `AlertFeedView`. Dossier: `bcs/backoffice.md`.
- **Customer Experience** — No direct subscription to Correspondence events at this milestone; storefront views show order / return state directly from the originating BC's events via SignalR (see `storefront-real-time-updates.md`), not from Correspondence.

## Tests as behavioral evidence

- **Integration tests:** `tests/Correspondence/Correspondence.Api.IntegrationTests/` covers each inbound handler → `MessageQueued` → `SendMessage` → `MessageDelivered` happy path plus the retry / failure paths against a stubbed `IEmailProvider`.
- **Unit tests:** `tests/Correspondence/Correspondence.UnitTests/MessageAggregateTests.cs` covers the aggregate state machine including the `Skip` factory.

## ADRs

No dedicated correspondence ADR cited in `bcs/correspondence.md`. The retry schedule and Phase 2 SMS infrastructure are documented in the M31.0 milestone notes.

## Declared vs. implemented

- **Declared shape:** `MessageSkipped` event + `MessageStatus.Skipped` terminal state + projection branch all exist.
- **Implemented shape:** No production emitter; only test fixture invokes `MessageFactory.Skip`.
- **Gap:** Customer-opt-out / channel-disabled path is declared but not wired. Dossier source: `bcs/correspondence.md#domain-events`.

- **Declared shape:** SMS channel infrastructure added in M31.0 Phase 2.
- **Implemented shape:** Only `Channel = "Email"` emitted by current handlers; provider abstraction present, no SMS provider implementation.
- **Gap:** SMS channel declared, not used. Dossier source: `bcs/correspondence.md#aggregates`.

- **Declared shape:** SendGrid / Twilio production provider implementations.
- **Implemented shape:** Real provider integrations stubbed at this milestone; production wiring deferred.
- **Gap:** Known deferral. Dossier source: `bcs/correspondence.md#purpose`.

## Source citations

- Dossier sections referenced: `bcs/correspondence.md#aggregates`, `bcs/correspondence.md#commands`, `bcs/correspondence.md#domain-events`, `bcs/correspondence.md#projections`, `bcs/correspondence.md#outbound-published`, `bcs/backoffice.md#composition-map`, `bcs/backoffice.md#read-models--projections`.
- Tests: `tests/Correspondence/Correspondence.Api.IntegrationTests/`, `tests/Correspondence/Correspondence.UnitTests/MessageAggregateTests.cs`.
