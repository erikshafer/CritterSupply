# Standard return and refund

> **Status:** Active
> **Type:** Hybrid (Return aggregate state machine + cross-BC choreography)
> **Initiating actor:** Customer (via Customer Experience or directly via Returns endpoint) or Operator (via Backoffice)
> **BCs involved:** Returns (state owner), Orders (saga acknowledger), Payments, Inventory, Fulfillment, Customer Experience, Correspondence, Backoffice
> **Most recent material milestone:** M47.0 — Storefront real-time

## Purpose

After a customer takes delivery of an order, they may request a refund return within the return-eligibility window. The Return aggregate runs through `Requested → Approved → Received → Inspected → Completed` (or `Denied`, `Rejected`, `Expired`), with `ReturnApproved` and `ReturnCompleted` driving the Order saga to keep itself open and to issue the customer refund via Payments. Inventory restocks restockable items on `ReturnCompleted`.

The cross-product **exchange** variant is a distinct workflow with its own orchestration — see `cross-product-exchange.md`. This workflow covers refund returns and same-SKU exchanges only.

## Actors and triggers

- **Initiating actor:** Customer or Operator
- **Trigger:** `POST /api/returns` (`RequestReturn` command) on Returns BC
- **Prerequisite state:** The originating `Order` saga is in `Delivered` state (or in a state where `ActiveReturnIds` membership is permitted by the saga); the SKU is within the return-eligibility window opened by `Fulfillment.ShipmentDelivered` (`ReturnEligibilityWindow` document)

## Trace (refund happy path)

| Step | BC | Command / Event / Edge | Resulting state / output | Dossier reference |
|------|----|------------------------|--------------------------|-------------------|
| 1 | Fulfillment | Publishes `Fulfillment.ShipmentDelivered` upstream | Saga `Status → Delivered`; Returns `ShipmentDeliveredHandler` creates `ReturnEligibilityWindow` document | `bcs/returns.md#inbound-subscribes` |
| 2 | Returns | `RequestReturn` command → `ReturnRequested` stream event + `Messages.Contracts.Returns.ReturnRequested` published | Return aggregate at `Requested` state; saga adds `ReturnId` to `ActiveReturnIds` | `bcs/returns.md#outbound-return-lifecycle-publishes`, `bcs/orders.md#subscribes` |
| 3 | Returns | Operator-driven `ApproveReturn` → `ReturnApproved` + public `Returns.ReturnApproved` (carries `EstimatedRefundAmount`, `RestockingFeeAmount`, `ShipByDeadline`) | Return aggregate at `Approved` | `bcs/returns.md#outbound-return-lifecycle-publishes` |
| 4 | Returns | `MarkReturnReceived` after physical receipt → `ReturnReceived` + `Returns.ReturnReceived` | Return aggregate at `Received` | `bcs/returns.md#outbound-return-lifecycle-publishes` |
| 5 | Returns | `SubmitInspection` evaluates each line item → either `ReturnCompleted` (refund happy path, mixed-inspection variant) or `ReturnRejected` (full failure); publishes corresponding integration event | Per-line `ReturnedItem` payload carries `Sku, Quantity, IsRestockable, WarehouseId, RestockCondition, RefundAmount, RejectionReason` | `bcs/returns.md#outbound-return-lifecycle-publishes` |
| 6 | Orders | Consumes `Returns.ReturnCompleted`; saga emits `Payments.RefundRequested` when `FinalRefundAmount > 0` | Saga removes `ReturnId` from `ActiveReturnIds`; refund routed via Payments | `bcs/orders.md#subscribes` |
| 7 | Payments | Consumes `RefundRequested`; processes refund via card-network adapter; publishes `Payments.RefundCompleted` (or `RefundFailed`) | Refund applied to original payment instrument | `bcs/payments.md#saga-replies-drive-the-order-saga-forward` |
| 8 | Orders | Consumes `RefundCompleted`; closes saga only when prior `Status` was `Cancelled` or `OutOfStock`. For post-delivery returns the saga closes when `ActiveReturnIds` is empty AND `ReturnWindowFired == true` | Saga `Status → Closed` and `MarkCompleted()` deletes saga document | `bcs/orders.md#compensation-and-divergent-paths` |
| 9 | Inventory | Consumes `Returns.ReturnCompleted`; restocks each `IsRestockable` `ReturnedItem` at `WarehouseId` | Per-SKU stock counts incremented | `bcs/inventory.md#subscribed-by-inventory-inbound`, `bcs/returns.md#outbound-return-lifecycle-publishes` |
| 10 | Customer Experience | Consumes `Returns.ReturnApproved` / `Returns.ReturnCompleted` / `Returns.ReturnDenied` / `Returns.ReturnRejected`; pushes `ReturnStatusChanged` SignalR message to `customer:{customerId}` group | Storefront return-history view updated in real time | `bcs/customer-experience.md#channel-return-status-updates` |
| 11 | Correspondence | Consumes the same return-lifecycle events; queues transactional email / SMS via stub providers | Customer notified through Correspondence's `Message` aggregate | `bcs/correspondence.md#inbound-subscribed` |
| 12 | Backoffice | Consumes the same events; updates `ReturnMetricsView` BFF projection; pushes real-time updates to `/hub/backoffice` | Operator dashboard reflects return state | `bcs/backoffice.md#subscribed-inbound` |

## Projections and views

- `Return` snapshot (Returns) — inline snapshot keyed by `ReturnId`. Dossier: `bcs/returns.md#aggregates`.
- `ReturnEligibilityWindow` (Returns) — document opened by `Fulfillment.ShipmentDelivered`; tracks per-SKU window expiry. Dossier: `bcs/returns.md#projections`.
- `OrderHistoryView` (Orders) — adds the return / refund event to the order timeline.
- `ReturnMetricsView` (Backoffice) — BFF projection over return events; powers operator returns-management dashboard. Dossier: `bcs/backoffice.md#read-models--projections`.
- Customer Experience inline return-status view in `OrderConfirmation.razor` and `OrderHistory.razor`.

## Compensation paths

### Failure: `DenyReturn`
- **Compensating action:** `ReturnDenied` + `Returns.ReturnDenied` published with `Reason` and optional customer-facing `Message`. Order saga removes the `ReturnId` from `ActiveReturnIds`; closes if applicable.
- **Resulting state:** Return aggregate terminal `Denied`; no refund, no restock.
- **BCs involved in compensation:** Returns, Orders, CX, Correspondence, Backoffice.

### Failure: Inspection rejects all items (`SubmitInspection` → `ReturnRejected`)
- **Compensating action:** `ReturnRejected` + `Returns.ReturnRejected` published with per-line `ReturnedItem` payload carrying `RejectionReason`. Saga removes from `ActiveReturnIds`; closes if applicable.
- **Resulting state:** Return aggregate terminal `Rejected`; no refund.

### Failure: Window expires before customer ships
- **Compensating action:** `ReturnExpired` + `Returns.ReturnExpired` published. Saga removes from `ActiveReturnIds`.
- **Resulting state:** Return aggregate terminal `Expired`. Source `ReturnStatus` declared-but-unused values `LabelGenerated` and `InTransit` suggest a richer state machine planned but not yet implemented (forward-note carried into S5).

### Failure: `Payments.RefundFailed`
- **Compensating action:** Logged-only at Order saga (per `OrderDecider.cs#L301-L307`); no status change on the saga. Operator must intervene through Backoffice tooling.
- **Resulting state:** Refund unresolved; Return aggregate at `Completed` but customer not yet refunded.
- **BCs involved in compensation:** None at the system layer — operator intervention.

### Failure: Mixed inspection — partial refund
- **Compensating action:** `ReturnCompleted` with `FinalRefundAmount = sum of per-line RefundAmount` for restockable + accepted lines only. Rejected lines carry `RejectionReason` and `IsRestockable = false`. Inventory restocks only the accepted-and-restockable lines.
- **Resulting state:** Partial refund issued via Payments; partial restock via Inventory.

## Variants and edge cases

### Same-SKU exchange (refund-style)
The Returns BC handles same-SKU exchanges through the standard exchange lifecycle events (`ExchangeRequested`, `ExchangeApproved`, `ExchangeReplacementShipped`, `ExchangeCompleted`). When the original SKU == replacement SKU, no `CrossProductExchangeRequested` is published and no replacement-reservation choreography fires. The flow is realised entirely through the Return aggregate state machine. Dossier: `bcs/returns.md#sagas--orchestration` (paragraph 1).

### Restocking fee
`ReturnApproved` carries a `RestockingFeeAmount` deducted from `EstimatedRefundAmount`. The final amount is set on `ReturnCompleted` based on per-line inspection outcomes.

### Idempotency
`ReplacementReservationOutcomeHandler` and `ExchangeDeltaCapturedHandler` are documented as idempotent for the exchange branch (see `cross-product-exchange.md`); the refund branch handlers rely on Wolverine's durable inbox and the Return aggregate's state-machine guards (idempotent transitions).

## BCs and roles

- **Returns** — State owner of the `Return` aggregate; publishes the return lifecycle events; subscribes to `Fulfillment.ShipmentDelivered` to open the eligibility window. Dossier: `bcs/returns.md`.
- **Orders** — Saga acknowledger; consumes return lifecycle events to manage `ActiveReturnIds`; emits `Payments.RefundRequested` from the saga on `ReturnCompleted`. Dossier: `bcs/orders.md`.
- **Payments** — Processes refunds via card-network adapter; replies with `RefundCompleted` / `RefundFailed`. Dossier: `bcs/payments.md`.
- **Inventory** — Restocks accepted-and-restockable items on `ReturnCompleted`. Dossier: `bcs/inventory.md`.
- **Fulfillment** — Upstream initiator (opens the eligibility window via `ShipmentDelivered`). Dossier: `bcs/fulfillment.md`.
- **Customer Experience** — SignalR push of `ReturnStatusChanged`; storefront return-history view. Dossier: `bcs/customer-experience.md`.
- **Correspondence** — Transactional notification fan-out. Dossier: `bcs/correspondence.md`.
- **Backoffice** — Operator returns-management dashboard. Dossier: `bcs/backoffice.md`.

## Tests as behavioral evidence

- **Gherkin features:** `docs/features/returns/` and `docs/features/customer-experience/`. The M47.0 closeout retro confirmed all M47.0 `@pending` scenarios in `cross-product-exchange.feature` were closed; the standard return surface has no `@pending` markers.
- **Integration tests (Alba):** `tests/Returns/Returns.Api.IntegrationTests/` covers the full Return state machine and outbound integration events.
- **Cross-BC E2E:** Reqnroll Returns → Inventory → Payments → Storefront end-to-end is deferred from M47.0 to M48 per the M47.0 closeout retro.

## ADRs

- **ADR 0061** — Cross-product exchange replacement reservation (Returns ↔ Inventory). Relevant here only as a contrast — the standard refund path does not invoke `ReserveReplacementForExchange`. File: `docs/decisions/0061-cross-product-exchange-replacement-reservation.md`.
- **ADR 0062** — Cross-product exchange Payments choreography. Relevant only as contrast. File: `docs/decisions/0062-cross-product-exchange-payments-choreography.md`.

## Declared vs. implemented

- **Declared shape (`ReturnStatus` enum):** `LabelGenerated` and `InTransit` values are declared.
- **Implemented shape:** No production code path appends these values; the active state machine goes `Requested → Approved → Received → Inspected → Completed/Rejected`.
- **Gap:** Declared-but-unused enum values suggest a richer shipping-label and in-transit state machine planned but not implemented. Forward-note for S5; dossier source: `bcs/returns.md` (per S3 retro forward-notes).

- **Declared shape (M47.0 closeout):** Persisted return-history (Storefront BFF projection + history endpoint) so the timeline survives a page refresh.
- **Implemented shape:** The OrderConfirmation timeline is in-memory only, driven by SignalR `ReturnStatusChanged` events. Persistence deferred to a future milestone.
- **Gap:** Timeline persistence is a known deferral.

## Source citations

- Dossier sections referenced: `bcs/returns.md#outbound-return-lifecycle-publishes`, `bcs/returns.md#inbound-subscribes`, `bcs/returns.md#aggregates`, `bcs/orders.md#subscribes`, `bcs/orders.md#compensation-and-divergent-paths`, `bcs/payments.md#saga-replies-drive-the-order-saga-forward`, `bcs/inventory.md#subscribed-by-inventory-inbound`, `bcs/customer-experience.md#channel-return-status-updates`, `bcs/correspondence.md#inbound-subscribed`, `bcs/backoffice.md#subscribed-inbound`, `bcs/fulfillment.md#integration-events`.
- ADRs: 0061, 0062.
- Tests: `tests/Returns/Returns.Api.IntegrationTests/`.
