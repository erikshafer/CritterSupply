# Correspondence

> **Source folder:** `src/Correspondence/`
> **Status:** Implemented
> **Most recent material milestone:** M31.0 — Phase 2 (7 additional integration events + SMS channel infrastructure)
> **Stub depth:** S1 — to be deepened in S3

## Purpose

Correspondence owns transactional customer messaging — the email and SMS messages triggered by business events from across the rest of the system. It subscribes to lifecycle events from Orders, Fulfillment, Returns, and Payments, decides whether a message is warranted, queues it, calls out to a provider, records the delivery outcome, and surfaces history through a list view. Real provider integrations (SendGrid, Twilio) are stubbed; the production wiring is deferred.

## Top-level structure

### Aggregates

- `Message` — stream ID: UUID v7.

### Commands

- `SendMessage`

### Domain events

- `MessageQueued`
- `MessageDelivered`
- `MessageSkipped`
- `DeliveryFailed`

### Projections

- `Message` snapshot — inline, keyed by `MessageId` (`Correspondence.Api/Program.cs`).
- `MessageListView` — inline.

### Integration events

- `Correspondence.CorrespondenceQueued` — publishes
- `Correspondence.CorrespondenceDelivered` — publishes
- `Correspondence.CorrespondenceFailed` — publishes
- Subscribes to `Orders.OrderPlaced` / `OrderCancelled`
- Subscribes to `Fulfillment.ShipmentHandedToCarrier` / `ShipmentDelivered` / `DeliveryAttemptFailed` / `ReturnToSenderInitiated` / `ShipmentLostInTransit` / `BackorderCreated`
- Subscribes to `Returns.ReturnApproved` / `ReturnDenied` / `ReturnCompleted` / `ReturnExpired`
- Subscribes to `Payments.RefundCompleted`

### HTTP / API surface (one line)

`Correspondence.Api` exposes the message read endpoints (history + status) consumed by Backoffice for customer-service workflows.

### Frontend surface (if applicable)

Not applicable — no frontend in this BC.

### Identity / auth posture (if applicable)

JWT Bearer (Backoffice scheme).

## Prior event modeling

- `docs/planning/correspondence-event-model.md`
- `docs/planning/correspondence-risk-analysis-roadmap.md`

## ADRs

- ADR 0030 — Notifications BC Renamed to Correspondence BC

## Source citations (S1 stub)

- `src/Correspondence/`
- `src/Shared/Messages.Contracts/Correspondence/`
- `CONTEXTS.md` (section: `Correspondence`)
- `docs/decisions/0030-notifications-to-correspondence-rename.md`
- `docs/planning/correspondence-event-model.md`
