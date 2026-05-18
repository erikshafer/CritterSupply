# Cross-product exchange

> **Status:** Active
> **Type:** Orchestration (Returns as orchestrator + Returns ↔ Payments / Returns ↔ Inventory choreography)
> **Initiating actor:** Customer (requesting a different SKU as a replacement) or Operator
> **BCs involved:** Returns (orchestrator), Inventory, Payments, Orders (saga acknowledger), Customer Experience, Backoffice
> **Most recent material milestone:** M47.0 — Slice 4 compensation paths complete

## Purpose

The cross-product exchange workflow handles the case where a customer returns a SKU and wants a different SKU as a replacement (the original and replacement SKUs differ). Returns orchestrates the end-to-end flow: reserving the replacement in Inventory; capturing any positive price-difference delta via Payments (or partially refunding the customer if the replacement is cheaper); shipping the replacement; and handling compensating paths if delta capture fails or inspection rejects the exchange after the delta was captured. ADR 0061 (replacement reservation) and ADR 0062 (Returns ↔ Payments choreography) govern the design.

The Order saga is the **acknowledger**, not the orchestrator (see `order-saga.md`). The standard refund return workflow is in `standard-return-refund.md`.

## Actors and triggers

- **Initiating actor:** Customer (via Customer Experience / Storefront) or Operator (via Backoffice)
- **Trigger:** `ApproveExchange` command on Returns BC, fired when the customer has requested an exchange with a replacement SKU that differs from the original SKU
- **Prerequisite state:** A `Return` aggregate exists in `Requested` state with a replacement SKU recorded; the originating `Order` saga is in `Delivered` (or post-delivery) state

## Trace (happy path — replacement more expensive than original)

| Step | BC | Command / Event / Edge | Resulting state / output | Dossier reference |
|------|----|------------------------|--------------------------|-------------------|
| 1 | Returns | `ApproveExchange` command — handler detects original SKU ≠ replacement SKU. Appends `ExchangeApproved`, `CrossProductExchangeRequested`, `ExchangePriceDifferenceCalculated`. Publishes `Returns.CrossProductExchangeRequested` and `Inventory.ReserveReplacementForExchange(WarehouseId = "WH-01")` (per `ReturnsExchangeDefaults.ReplacementWarehouseId`) | Return aggregate at `Approved`; reservation requested keyed on `ReturnId` (ADR 0061) | `bcs/returns.md#forward-path--replacement-reservation-slice-1-adr-0061` |
| 2 | Orders | Consumes `Returns.CrossProductExchangeRequested`; adds the exchange `ReturnId` to `ActiveReturnIds` (acknowledger only) | Saga stays open; no orchestration | `bcs/orders.md#cross-product-exchange-acknowledgement-branch` |
| 3 | Inventory | Consumes `ReserveReplacementForExchange` from queue `inventory-returns-events`; reuses `ProductInventory` lifecycle keyed by `ReturnId`; publishes `Inventory.ReplacementReserved(InventoryId, ReturnId, Sku, WarehouseId, Quantity, ReservedAt)` on success | Inventory hold placed | `bcs/inventory.md#subscribed-by-inventory-inbound`, `bcs/returns.md#inbound-subscribes` |
| 4 | Returns | `ReplacementReservationOutcomeHandler` consumes `ReplacementReserved` from `returns-inventory-events`; appends `ReplacementReservationConfirmed` (new event capturing `ReplacementInventoryId` per M47.0/Slice 4); idempotent on repeat | Return aggregate retains `ReplacementInventoryId` | `bcs/returns.md#forward-path--replacement-reservation-slice-1-adr-0061` |
| 5 | Returns | When `priceDifference < 0` (replacement is more expensive): `ApproveExchangeHandler` appends `ExchangeAdditionalPaymentRequired` and publishes `Returns.ExchangeAdditionalPaymentRequired` (routed to both `payments-returns-events` for Payments and `storefront-returns-events` for CX SignalR) | Customer notified delta is owed; Payments asked to capture | `bcs/returns.md#forward-path--additional-payment-delta-capture-slice-2-adr-0062` |
| 6 | Payments | `CaptureExchangeDeltaHandler` consumes `ExchangeAdditionalPaymentRequired`; opens a new `Payment` stream at the deterministic UUID id `ExchangePaymentIds.ComputeDeltaPaymentId(returnId)` (per ADR 0062 — piggybacks on the existing Payment aggregate rather than introducing a new aggregate); captures the delta against the customer's saved payment instrument; publishes `Payments.ExchangeDeltaCaptured(ReturnId, PaymentId, AmountCaptured, TransactionId, CapturedAt)` | Delta captured against the dedicated delta-payment stream | `bcs/payments.md#cross-product-exchange-payments↔returns-choreography-adr-0062` |
| 7 | Returns | `ExchangeDeltaCapturedHandler` consumes `Payments.ExchangeDeltaCaptured` from `returns-payments-events`; appends `ExchangeAdditionalPaymentCaptured`; republishes `Returns.ExchangeAdditionalPaymentCaptured` | Return aggregate marks `AdditionalPaymentCaptured = true` | `bcs/returns.md#forward-path--additional-payment-delta-capture-slice-2-adr-0062` |
| 8 | Returns | Operator runs `SubmitInspection` after receiving the original item back — inspection passes | Return aggregate at `Inspected` | `bcs/returns.md` |
| 9 | Returns | `ShipReplacementItem` command emits `ExchangeReplacementShipped` and `Returns.ExchangeReplacementShipped(ShipmentId, TrackingNumber)` | Replacement on its way to customer | `bcs/returns.md#outbound-exchange-lifecycle-publishes` |
| 10 | Returns | `ExchangeCompleted` event appended on confirmation; `Returns.ExchangeCompleted(PriceDifferenceRefund?)` published | Return aggregate terminal `Completed` | `bcs/returns.md#outbound-exchange-lifecycle-publishes` |
| 11 | Customer Experience | Subscribes to `Returns.CrossProductExchangeRequested` / `ExchangeAdditionalPaymentRequired` / `ExchangeAdditionalPaymentCaptured` / `ExchangePartialRefundIssued` / `ExchangeCancelled`; pushes `ReturnExchangePaymentChanged` SignalR message | Storefront exchange-payment chip updated in real time | `bcs/customer-experience.md#channel-exchange-payment-updates` |
| 12 | Orders | Consumes `Returns.ExchangeAdditionalPaymentRequired` / `ExchangeAdditionalPaymentCaptured` / `ExchangePartialRefundIssued` as **no-op acknowledgers**. Deliberately publishes nothing on `ExchangePartialRefundIssued` per ADR 0062 (forwarding would double-refund) | Saga acknowledges only | `bcs/orders.md#cross-product-exchange-acknowledgement-branch` |

## Trace (happy path — replacement cheaper than original)

When `aggregate.PriceDifference > 0` (replacement is cheaper), steps 5–7 are replaced by the partial-refund path: `ShipReplacementItemHandler` emits `Payments.ExchangePartialRefundRequested` against the original Order payment after exchange inspection passes; Payments replies with `Payments.ExchangePartialRefundIssued` on `returns-payments-events`; `ExchangePartialRefundIssuedHandler` appends `ExchangePartialRefundIssued` and republishes `Returns.ExchangePartialRefundIssued`. The handler idempotency check scans the stream for an existing `ExchangePartialRefundIssued` event (cannot use `FinalRefundAmount` as the dedupe key because `ExchangeCompleted` sets it first). Dossier: `bcs/returns.md#forward-path--partial-refund-at-exchange-completion-slice-2-adr-0062`.

## Projections and views

- `Return` snapshot (Returns) — inline snapshot keyed by `ReturnId`. Holds `IsCrossProductExchange`, `AdditionalPaymentCaptured`, `PriceDifference`, `ReplacementInventoryId`, `OriginalPaymentId`. Dossier: `bcs/returns.md#aggregates`.
- `Payment` snapshot (Payments) — both the original Order payment and the delta-payment stream (at deterministic UUID v5 id derived from `ReturnId`) share the same aggregate type. Refunds carry `PaymentRefunded.ReturnId` for delta-refund correlation. Dossier: `bcs/payments.md#cross-product-exchange-payments↔returns-choreography-adr-0062`.
- Customer Experience inline exchange-payment chip in `OrderConfirmation.razor` driven by SignalR `ReturnExchangePaymentChanged`.

## Compensation paths

### Failure: Inventory replacement out of stock (`Inventory.ReplacementReservationFailed`)
- **Compensating action:** `ReplacementReservationOutcomeHandler` appends `ExchangeDenied` with reason code `ReplacementOutOfStock` and customer-facing message "Replacement item currently unavailable. Please request a refund or try again later." Republishes `Returns.ExchangeDenied`. Idempotent — only acts when aggregate is still in `Approved`.
- **Resulting state:** Return aggregate terminal `Denied`; no inventory hold; no payment captured.
- **BCs involved in compensation:** Returns, Orders (acknowledger), CX, Backoffice.

### Failure: Payments delta capture fails (`Payments.ExchangeDeltaCaptureFailed`) — M47.0 / Slice 4
- **Compensating action:** `ExchangeDeltaCaptureFailedHandler` appends `ExchangeCancelled` with reason `PaymentCaptureFailed` and customer-facing message "Payment for price difference could not be processed. Exchange cancelled." Publishes `Returns.ExchangeCancelled` to CX / Orders / Backoffice. If `ReplacementInventoryId` was recorded, publishes `Inventory.ReleaseExchangeReservation` to release the held stock; the Inventory side is idempotent against missing reservations and an `ExpireReservation` timer would otherwise eventually clean up. Idempotent — no-op when the aggregate is missing, not a cross-product exchange, has already captured the delta, or has already moved past `Approved`.
- **Resulting state:** Return aggregate terminal `Cancelled`; replacement reservation released; no delta charged to customer.
- **BCs involved in compensation:** Returns, Inventory, CX, Orders, Backoffice.

### Failure: Inspection rejects the exchange after delta was captured — M47.0 / Slice 4
- **Compensating action:** `SubmitInspection` runs against an exchange that has `IsCrossProductExchange == true`, `AdditionalPaymentCaptured == true`, and at least one failed inspection line. `SubmitInspectionHandler` appends `ExchangeRejected`, publishes `Returns.ExchangeRejected`, then emits `Payments.RefundExchangeDeltaRequested` for the captured delta amount and `Inventory.ReleaseExchangeReservation` if a `ReplacementInventoryId` is present. Payments' `RefundExchangeDeltaHandler` (M47.0 / Slice 4) refunds against the deterministic delta-payment stream and replies with `Payments.ExchangePartialRefundIssued`, which Returns reuses via the existing `ExchangePartialRefundIssuedHandler`.
- **Resulting state:** Return aggregate terminal `Rejected`; delta refunded; inventory hold released.
- **BCs involved in compensation:** Returns, Payments, Inventory.

## Variants and edge cases

### Equal-price replacement
When `PriceDifference == 0`, neither the additional-payment-required nor the partial-refund paths fire. Steps 5–7 (and the parallel partial-refund variant) are skipped; the exchange completes after `ShipReplacementItem`.

### Idempotency
- `ReplacementReservationOutcomeHandler` skips if the aggregate already has a `ReplacementInventoryId` (success path) or already moved past `Approved` (failure path).
- `ExchangeDeltaCapturedHandler` skips if `aggregate.AdditionalPaymentCaptured` is already true.
- `ExchangePartialRefundIssuedHandler` scans the stream for an existing `ExchangePartialRefundIssued` event because `aggregate.FinalRefundAmount` is set by `ExchangeCompleted` first and cannot serve as the dedupe key.
- `ExchangeDeltaCaptureFailedHandler` no-ops on missing aggregate, non-cross-product, already-captured, or post-`Approved`.

### Order saga acknowledger discipline
The four exchange contracts (`CrossProductExchangeRequested`, `ExchangeAdditionalPaymentRequired`, `ExchangeAdditionalPaymentCaptured`, `ExchangePartialRefundIssued`) reach the Order saga but are handled as no-op acknowledgers that only update `ActiveReturnIds`. The saga deliberately publishes nothing on `ExchangePartialRefundIssued` per ADR 0062 — the Returns ↔ Payments choreography drives the refund directly. Forwarding `RefundRequested` would double-refund. Dossier source: `bcs/orders.md#cross-product-exchange-acknowledgement-branch`.

### Delta-payment stream as deterministic id
Payments' `ExchangePaymentIds.ComputeDeltaPaymentId(returnId)` derives a deterministic UUID v5 from the `ReturnId` so that the delta-capture and any delta-refund land on the same `Payment` stream — without introducing a new aggregate type. Refunds carry `PaymentRefunded.ReturnId` to correlate.

## BCs and roles

- **Returns** — Orchestrator. Owns the cross-product exchange state machine on the `Return` aggregate. Publishes the exchange lifecycle events; emits the `Inventory.ReserveReplacementForExchange` / `ReleaseExchangeReservation` and the `Payments.ExchangePartialRefundRequested` / `RefundExchangeDeltaRequested` commands. Dossier: `bcs/returns.md`.
- **Inventory** — Participant. Reserves and releases replacement holds keyed by `ReturnId`. Dossier: `bcs/inventory.md`.
- **Payments** — Participant. Captures delta on a dedicated `Payment` stream; refunds partial or delta amounts. Dossier: `bcs/payments.md`.
- **Orders** — Saga acknowledger only (see Order saga workflow). Dossier: `bcs/orders.md`.
- **Customer Experience** — SignalR push of `ReturnExchangePaymentChanged`; storefront chip in `OrderConfirmation.razor`. Dossier: `bcs/customer-experience.md`.
- **Backoffice** — Operator dashboard updates via inbound subscription. Dossier: `bcs/backoffice.md`.

## Tests as behavioral evidence

- **Gherkin features:** `docs/features/returns/cross-product-exchange.feature` — all 5 M47.0 `@pending` scenarios are closed per the M47.0 closeout retro.
- **Integration tests:** `tests/Returns/Returns.Api.IntegrationTests/` covers all four slices. Payments-side coverage in `tests/Payments/Payments.Api.IntegrationTests/`.
- **Cross-BC E2E:** Reqnroll Returns → Inventory → Payments → Storefront end-to-end is deferred from M47.0 to M48.

## ADRs

- **ADR 0061** — Cross-product exchange replacement reservation. Returns is the orchestrator. Reservation key = `ReturnId`. File: `docs/decisions/0061-cross-product-exchange-replacement-reservation.md`.
- **ADR 0062** — Cross-product exchange Payments choreography. Delta capture / partial refund / capture-failure compensation / inspection-rejection compensation. File: `docs/decisions/0062-cross-product-exchange-payments-choreography.md`.

## Declared vs. implemented

- **Declared shape (S3 retro forward-note):** Persisted return-history and Backoffice timeline for the cross-product exchange end-to-end.
- **Implemented shape:** OrderConfirmation timeline is in-memory only (driven by SignalR events). Persistence and Backoffice timeline are deferred to a future milestone.
- **Gap:** Known deferral.

## Source citations

- Dossier sections referenced: `bcs/returns.md#sagas--orchestration`, `bcs/returns.md#forward-path--replacement-reservation-slice-1-adr-0061`, `bcs/returns.md#forward-path--additional-payment-delta-capture-slice-2-adr-0062`, `bcs/returns.md#forward-path--partial-refund-at-exchange-completion-slice-2-adr-0062`, `bcs/returns.md#compensation-path--capture-failure-slice-4-adr-0062`, `bcs/returns.md#compensation-path--inspection-rejection-with-captured-delta-slice-4`, `bcs/payments.md#cross-product-exchange-payments↔returns-choreography-adr-0062`, `bcs/inventory.md#subscribed-by-inventory-inbound`, `bcs/orders.md#cross-product-exchange-acknowledgement-branch`, `bcs/customer-experience.md#channel-exchange-payment-updates`.
- ADRs: 0061, 0062.
- Tests: `tests/Returns/Returns.Api.IntegrationTests/`, `tests/Payments/Payments.Api.IntegrationTests/`.
