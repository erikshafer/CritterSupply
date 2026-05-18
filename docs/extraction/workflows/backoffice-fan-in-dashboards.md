# Backoffice fan-in dashboards

> **Status:** Active
> **Type:** Read-only fan-in (RabbitMQ subscribers → Marten projections → SignalR push to operator WASM)
> **Initiating actor:** System (driven by upstream BCs); consumed by operators (Executive, Operations Manager, Warehouse Manager, etc.)
> **BCs involved:** Backoffice (fan-in target) ← Orders, Payments, Inventory, Returns, Fulfillment, Product Catalog, Correspondence
> **Most recent material milestone:** M46.0 — Operations health dashboard

## Purpose

The Backoffice fan-in dashboards collect events from seven upstream BCs into five operator-facing Marten projections (`AdminDailyMetrics`, `AlertFeedView`, `ReturnMetricsView`, `CorrespondenceMetricsView`, `FulfillmentPipelineView`). Two handlers also push a real-time `IBackofficeWebSocketMessage` (`LiveMetricUpdated`, `AlertCreated`) via SignalR to the appropriate role-keyed group. Backoffice is a read-only BFF in this workflow — it publishes no cross-BC integration events; its outbound surface is per-connection SignalR plus synchronous HTTP proxy calls to upstream BCs for write actions.

## Actors and triggers

- **Initiating actor:** System (upstream BCs publish events as a side effect of their own workflows)
- **Trigger:** Any of 21+ inbound RabbitMQ integration events on Backoffice's 17 dedicated queues (`Backoffice.Api/Program.cs#L221-L250`)
- **Prerequisite state:** Backoffice projections registered inline in `Backoffice.Api/Program.cs#L88-L107` against the `backoffice` Marten schema

## Trace (per-event template)

| Step | BC | Command / Event / Edge | Resulting state / output | Dossier reference |
|------|----|------------------------|--------------------------|-------------------|
| 1 | Upstream BC (e.g. Orders, Payments, Inventory) | Publishes an integration event as part of its own workflow (e.g. `OrderPlaced`, `PaymentFailed`, `LowStockAlertRaised`) | RabbitMQ delivery to Backoffice's dedicated queue | `bcs/orders.md#publishes`, `bcs/payments.md`, `bcs/inventory.md#published-by-inventory-outbound` |
| 2 | Backoffice | Notification handler under `src/Backoffice/Backoffice/Notifications/` (one per inbound event) consumes from queue; appends a Marten event so the multi-stream projection advances | Projection upserts its target document(s) under the `backoffice` schema | `bcs/backoffice.md#subscribed-inbound` |
| 3 | Backoffice | Inline projection (`AdminDailyMetricsProjection`, `AlertFeedViewProjection`, etc.) materialises the per-document update synchronously inside the handler's session | Document state visible to subsequent reads | `bcs/backoffice.md#read-models--projections` |
| 4 | Backoffice (for the 2 wired SignalR cases) | `OrderPlacedHandler` returns a `LiveMetricUpdated` alongside the event-store append; `PaymentFailedHandler` returns an `AlertCreated` alongside the `AlertFeedView` upsert | SignalR fan-out to role-keyed group via `IHubContext<BackofficeHub>.Clients.Group("role:executive")` etc. | `bcs/backoffice.md#signalr-channels` |
| 5 | Operator WASM (`/hub/backoffice`) | Operator's connection (enrolled into exactly one `role:{role}` group via `BackofficeHub.OnConnectedAsync`) receives the real-time push; WASM shell stores and re-applies the JWT via `AccessTokenProvider` lambda; reconnects with staircase `[0s, 2s, 10s]` via `WithAutomaticReconnect` | Operator dashboard updates in real time | `bcs/backoffice.md#signalr-channels` |

## Projections and views

All inline, all under the `backoffice` schema:

- **`AdminDailyMetrics`** (date-keyed `Id = "yyyy-MM-dd"`) — `TotalOrdersToday`, `TotalProducts`, `TotalRevenueToday`, `OutOfStockSkus`, `FailedPaymentsToday`. Multi-stream over `OrderPlaced`, `ProductPublished`, `LowStockAlertRaised`, `OutOfStockOccurred`, `PaymentFailed`. Dossier: `bcs/backoffice.md#read-models--projections` (#2).
- **`AlertFeedView`** (per-alert `Id = Guid.NewGuid()` per inbound event) — 6 alert types × 3 severities (Critical / Warning / Info); acknowledgment fields. Multi-stream over `LowStockAlertRaised`, `OutOfStockOccurred`, `PaymentFailed`, `ShipmentLostInTransit`, `GhostShipmentDetected`, `ReturnAutoApprovalFailed`, `CorrespondenceFailed`. Dossier: `bcs/backoffice.md#read-models--projections` (#3).
- **`ReturnMetricsView`** (singleton `Id = "current"`) — counters keyed by `ReturnStatus` and `RmaReason`. Multi-stream over 5 Returns events. Dossier: `bcs/backoffice.md#read-models--projections` (#4).
- **`CorrespondenceMetricsView`** (singleton `Id = "current"`) — queue depth, deliveries, failures. Multi-stream over 3 Correspondence events. Dossier: `bcs/backoffice.md#read-models--projections` (#5).
- **`FulfillmentPipelineView`** (singleton `Id = "current"`) — counters per pipeline stage. Multi-stream over 5 Fulfillment events. Dossier: `bcs/backoffice.md#read-models--projections` (#6).

## Compensation paths

### Failure: Upstream event consumption fails
- **Compensating action:** Wolverine handler retry policies + the dead-letter aggregator at `GET /api/backoffice/operations/dead-letters` (M46.0). See `backoffice-operations-health.md`.
- **Resulting state:** Event held in DLQ until retried or manually addressed.

### Failure: Operator acknowledges an alert
- **Compensating action:** `AcknowledgeAlert` command (`AlertManagement/AcknowledgeAlertHandler.Handle`) sets `IsAcknowledged = true`, `AcknowledgedBy*`, `AcknowledgedAt` on the `AlertFeedView` document. Wolverine-dispatched in-process. Authorized as `WarehouseClerk`.
- **Resulting state:** Alert hidden from the active feed.

## Variants and edge cases

### Operator role determines visibility
`BackofficeHub.OnConnectedAsync` enrolls each connection into exactly one `role:{role}` group based on the first `ClaimTypes.Role` claim. Connections without `UserId` **or** `Role` are aborted. Different roles see different real-time updates because handlers target group-specific fan-out (`role:executive`, `role:operations-manager`, etc.). Dossier: `bcs/backoffice.md#signalr-channels`.

### Wired vs declared SignalR types
Two of five `IBackofficeWebSocketMessage` types (`LiveMetricUpdated`, `AlertCreated`) are wired and emitted. Three (`ActiveOrderIncremented`, `ActiveOrderDecremented`, `PendingReturnIncremented`) are declared but have no instantiator in code (routes-without-instantiator). Dossier: `bcs/backoffice.md#message-types`.

### Anti-forgery disabled on hub
The SignalR hub disables antiforgery on the WS endpoint; browser same-origin enforcement of WS upgrade requests is the documented defence. Dossier: `bcs/backoffice.md#signalr-channels`.

### Dead-letter aggregation as a parallel read surface
`GET /api/backoffice/operations/dead-letters` opens a fresh `NpgsqlConnection` (not `IDocumentSession.Connection`) per M46.0 to avoid pinning Backoffice's own schema for the read lifetime. See `backoffice-operations-health.md`.

## BCs and roles

- **Backoffice** — Fan-in target. Subscribes via 17 RabbitMQ queues; runs 21 notification handlers; materialises 5 projections; fans out via 1 SignalR hub. Dossier: `bcs/backoffice.md`.
- **Orders / Payments / Inventory / Returns / Fulfillment / Product Catalog / Correspondence** — Upstream publishers. Their workflows trigger the inbound traffic.
- **Payments** — In code, Backoffice subscribes to `PaymentCaptured` and `PaymentFailed`, but **Payments is not listed in the Backoffice subscription table at CONTEXTS.md lines 279-289** (a drift item — see Declared vs. implemented).
- **Fulfillment** — In CONTEXTS.md line 287 listed as "queries"; the BC is in fact subscribed-to and the typed `IFulfillmentClient` is registered with no consumer anywhere (a drift item).
- **Pricing** — `IPricingClient` is registered but has no consumer in `Backoffice.Api/`; the WASM `PriceEdit.razor` page calls `/api/pricing/...` against the `BackofficeApi` named client base — those paths are not served by `Backoffice.Api` (a drift item — see `bcs/backoffice.md#composition-map`).

## Tests as behavioral evidence

- **Integration tests:** `tests/Backoffice/Backoffice.Api.IntegrationTests/` covers each notification handler, each projection, and the SignalR hub end-to-end via Alba + Testcontainers.
- **Specific test of note:** `OperationsHealth/GetDeadLetterSummaryTests` (M46.0/D) introspects `information_schema.columns` to seed DLQ rows portably across Wolverine version bumps — see `backoffice-operations-health.md`.

## ADRs

No dedicated dashboard ADR; the Backoffice composition pattern (typed `HttpClient` for synchronous reads, RabbitMQ for event subscription, inline projections for read models) is the implementation model documented across the M37–M46 series.

## Declared vs. implemented

- **Declared shape (CONTEXTS.md lines 279-289):** Backoffice subscription table omits Payments.
- **Implemented shape:** Backoffice subscribes to `PaymentCaptured` and `PaymentFailed` via dedicated handlers.
- **Gap:** Documentation undercount; functional gap absent. Dossier source: `bcs/backoffice.md#composition-map` (Payments row).

- **Declared shape:** `ActiveOrderIncremented`, `ActiveOrderDecremented`, `PendingReturnIncremented` SignalR types defined.
- **Implemented shape:** No instantiator in code; never sent to clients.
- **Gap:** Routes-without-instantiator on the SignalR side. Dossier source: `bcs/backoffice.md#message-types`.

- **Declared shape (CONTEXTS.md line 287):** Fulfillment relationship is "queries."
- **Implemented shape:** Backoffice subscribes to 5 Fulfillment events; the typed `IFulfillmentClient` is registered with no consumer.
- **Gap:** Direction misclassified; client unused. Dossier source: `bcs/backoffice.md#composition-map` (Fulfillment row).

## Source citations

- Dossier sections referenced: `bcs/backoffice.md#subscribed-inbound`, `bcs/backoffice.md#read-models--projections`, `bcs/backoffice.md#signalr-channels`, `bcs/backoffice.md#composition-map`, `bcs/backoffice.md#message-types`, `bcs/backoffice.md#commands`.
- Tests: `tests/Backoffice/Backoffice.Api.IntegrationTests/`.
