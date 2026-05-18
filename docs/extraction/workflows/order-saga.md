# Order saga

> **Status:** Active
> **Type:** Orchestration (stateful Wolverine saga + Decider pattern)
> **Initiating actor:** Customer (via Shopping → Orders handoff) or Operator (via Backoffice fraud-review / cancellation)
> **BCs involved:** Orders (orchestrator), Payments, Inventory, Fulfillment, Returns; downstream observers Customer Experience, Correspondence, Backoffice
> **Most recent material milestone:** M47.0 — Storefront real-time + cross-product exchange acknowledger refresh; M45.1 — fraud-review and shipping-address-change branches

## Purpose

The Order saga is CritterSupply's canonical multi-BC orchestration. After a customer completes checkout, the Order saga drives an order through payment authorization, per-SKU inventory reservation, payment capture, per-SKU inventory commitment, fulfillment, carrier handoff, delivery, the post-delivery return window, and final saga closure — with compensating fan-outs on cancellation, payment failure, stock failure, fulfillment cancellation, and fraud rejection. It is the most-cited orchestration in the codebase and the canonical implementation of the Decider pattern per ADR 0029.

## Actors and triggers

- **Initiating actor:** Customer (via `Shopping.CartCheckoutCompleted` from the checkout funnel)
- **Trigger:** `Shopping.CartCheckoutCompleted` integration event consumed by `PlaceOrderHandler` on the local queue `order-placement` (with durable inbox)
- **Prerequisite state:** A `Checkout` aggregate in Orders has reached terminal `CheckoutCompleted` (which republishes as `CartCheckoutCompleted`). See `cart-to-checkout.md`.

## Trace (happy path)

| Step | BC | Command / Event / Edge | Resulting state / output | Dossier reference |
|------|----|------------------------|--------------------------|-------------------|
| 1 | Orders | `PlaceOrderHandler.Handle(CartCheckoutCompleted)` calls `OrderDecider.Start`; constructs `Order` saga document at `Status = Placed` with `ExpectedReservationCount` = distinct SKU count; publishes `Orders.OrderPlaced` | Saga `Status = Placed`; observers (Payments, Inventory, CX, Correspondence, Backoffice) react | `bcs/orders.md#publishes`, `bcs/orders.md#state-machine` (step 1) |
| 2 | Payments | Consumes `OrderPlaced`; authorizes the payment method; publishes `Payments.PaymentAuthorized` | Saga `Status → PendingPayment` | `bcs/payments.md#saga-replies-drive-the-order-saga-forward`, `bcs/orders.md#state-machine` (step 2) |
| 3 | Payments | Captures the authorized amount; publishes `Payments.PaymentCaptured` | Saga `Status → PaymentConfirmed`, `IsPaymentCaptured = true`, persists `PaymentId` | `bcs/orders.md#state-machine` (step 2) |
| 4 | Inventory | Consumes `OrderPlaced` (per-SKU); reserves stock; publishes `Inventory.ReservationConfirmed` per SKU | Per-SKU: `ConfirmedReservationCount++`, `ReservationIds.Add(...)`; flips to `InventoryReserved` only when `ConfirmedReservationCount == ExpectedReservationCount` | `bcs/inventory.md#published-by-inventory-outbound`, `bcs/orders.md#state-machine` (step 3) |
| 5 | Orders | When `IsPaymentCaptured && IsAllInventoryReserved`: saga fans `Orders.ReservationCommitRequested` for every entry in `ReservationIds` (commit fan-out handles the payment-between-reservations race per `OrderDecider.cs#L352-L376`) | One `ReservationCommitRequested` per SKU | `bcs/orders.md#publishes`, `bcs/orders.md#state-machine` (step 4) |
| 6 | Inventory | Consumes each `ReservationCommitRequested`; commits stock; publishes `Inventory.ReservationCommitted` per SKU | Per-SKU: `CommittedReservationIds.Add(...)`; flips to `Fulfilling` and emits `FulfillmentRequested` once `CommittedReservationIds.Count == ExpectedReservationCount && IsPaymentCaptured` (idempotent — dispatched once and only once) | `bcs/orders.md#state-machine` (step 4) |
| 7 | Orders | Publishes `Fulfillment.FulfillmentRequested` (owned in Fulfillment namespace but emitted from Orders) to Fulfillment | Saga `Status = Fulfilling` | `bcs/orders.md#publishes` |
| 8 | Fulfillment | Picks, packs, and hands off to carrier; publishes `Fulfillment.ShipmentHandedToCarrier` (M41.0/S4 successor to `ShipmentDispatched`) | Saga `Status → Shipped` | `bcs/fulfillment.md#integration-events`, `bcs/orders.md#state-machine` (step 5) |
| 9 | Fulfillment | Publishes `Fulfillment.TrackingNumberAssigned` | Saga stores `TrackingNumber` without status change | `bcs/orders.md#subscribes` |
| 10 | Fulfillment | Publishes `Fulfillment.ShipmentDelivered` | Saga `Status → Delivered`, persists `DeliveredAt`, schedules `ReturnWindowExpired` 30 days out via `OutgoingMessages.Delay(...)` (saga-internal scheduled message; never crosses the bus) | `bcs/orders.md#state-machine` (step 6) |
| 11 | Orders | At `ReturnWindowExpired` (self-delivered): `ReturnWindowFired = true`. If `ActiveReturnIds.Count == 0`: `Status → Closed` and `MarkCompleted()` deletes saga document | Saga terminal | `bcs/orders.md#state-machine` (step 7) |

If returns are in flight at the window-fire time, the saga stays open and is closed by the last `ReturnCompleted` / `ReturnDenied` / `ReturnRejected` / `ReturnExpired` after the window has fired.

## Projections and views

- `Order` Marten saga document (Orders) — numeric-revisioned saga state. Dossier: `bcs/orders.md#saga-shape`.
- `OrderHistoryView` (Orders) — projection over `Order` events; consumed by Customer Experience / Backoffice for order-history display.
- `Checkout` snapshot (Orders) — predecessor aggregate that emits `CartCheckoutCompleted` to start the saga. Dossier: `bcs/orders.md#aggregates`.

## Compensation paths

### Failure: `CancelOrder` (customer or operator cancellation)
- **Compensating action:** Eligibility checked by `OrderDecider.CanBeCancelled` (disallowed in `Delivered`, `Closed`, `Cancelled`, `OutOfStock`, `PaymentFailed`). Emits one `Orders.ReservationReleaseRequested` per known reservation, a `Payments.RefundRequested` if `IsPaymentCaptured`, and `Orders.OrderCancelled`. If no payment was captured, the saga `MarkCompleted()` immediately (no `RefundCompleted` to await).
- **Resulting state:** Inventory releases reservations; Payments refunds (when captured); saga `Cancelled` or terminal `Closed`.
- **BCs involved in compensation:** Orders, Inventory, Payments, Fulfillment (via cascading `OrderCancelled`), Customer Experience, Backoffice.

### Failure: `Payments.PaymentFailed`
- **Compensating action:** Saga `Status → PaymentFailed`; emits one `ReservationReleaseRequested` per known reservation.
- **Resulting state:** Inventory releases reservations; saga terminal in `PaymentFailed`.
- **BCs involved in compensation:** Orders, Inventory.

### Failure: `Inventory.ReservationFailed`
- **Compensating action:** Saga `Status → OutOfStock`; if `IsPaymentCaptured`, emits `Payments.RefundRequested`; emits `ReservationReleaseRequested` for other previously-confirmed reservations; emits `Orders.OrderCancelled`.
- **Resulting state:** Inventory releases other reservations; Payments refunds; saga closes after `RefundCompleted`.
- **BCs involved in compensation:** Orders, Inventory, Payments, Customer Experience.

### Failure: `Fulfillment.FulfillmentCancelled`
- **Compensating action:** Reuses the cancellation compensation shape via `OrderDecider.CanBeCancelled` gate. Emits `ReservationReleaseRequested`, `RefundRequested`, `OrderCancelled`.
- **Resulting state:** Same as cancellation.
- **BCs involved in compensation:** Orders, Inventory, Payments, Fulfillment, CX, Backoffice.

### Failure: `RejectOrderForFraud` (fraud review)
- **Compensating action:** Eligibility checked by `OrderDecider.CanBeRejectedForFraud` (any pre-fulfillment status plus `OnHold`). Co-emits `Orders.OrderRejectedForFraud` (for CX messaging + Backoffice account-flagging) and `Orders.OrderCancelled` (so downstream BCs react via existing cancellation choreography). Reuses cancellation compensation shape; immediate `MarkCompleted` when no payment captured.
- **Resulting state:** Same as cancellation, plus fraud-review side effects.
- **BCs involved in compensation:** Orders, Payments, Inventory, Fulfillment, CX, Backoffice.

### Failure: `RefundCompleted` only closes saga when prior `Status` was `Cancelled` or `OutOfStock`
Per `OrderDecider.cs#L279-L295`. `RefundFailed` is logged-only with no status change.

## Variants and edge cases

### Fraud-review branch (M45.1 / S5)
- **`PutOrderOnHold`** — eligible only in `Placed`, `PendingPayment`, `PaymentConfirmed`, `InventoryReserved` (`OrderDecider.CanBePutOnHold`). Emits `Orders.OrderPutOnHold`. Saga `Status → OnHold`.
- **`ReleaseOrderFromHold`** — eligible only when `Status == OnHold`. Emits `Orders.OrderReleasedFromHold`. Saga `Status → PaymentConfirmed` as a safe-default recovery. Restoring exact pre-hold status is documented in source as deferred to the Orders remaster.
- **`RejectOrderForFraud`** — see compensation paths above.

### Shipping-address change branch (M45.1 / S4)
- **`ChangeShippingAddress`** — eligible in `Placed`, `PendingPayment`, `PaymentConfirmed`, `InventoryReserved`, `OnHold`. HTTP layer returns 409 when ineligible; saga handler re-validates and silently no-ops for at-least-once safety. Mutates saga's `ShippingAddress` in place and emits `Orders.ShippingAddressChanged` to both `fulfillment-requests` and `storefront-notifications` queues.

### Cross-product exchange acknowledgement branch (M45.1 / S3, refreshed M47.0 / S2)
The Order saga is the **acknowledger**, not the orchestrator. Returns orchestrates cross-product exchange (see `cross-product-exchange.md`). Order saga handlers for the four exchange contracts (`CrossProductExchangeRequested`, `ExchangeAdditionalPaymentRequired`, `ExchangeAdditionalPaymentCaptured`, `ExchangePartialRefundIssued`) keep the saga open via `ActiveReturnIds` membership. The `ExchangePartialRefundIssued` handler **deliberately publishes nothing** per ADR 0062 — forwarding `RefundRequested` would double-refund, since Returns ↔ Payments choreography drives the refund directly. Dossier source: `bcs/orders.md#cross-product-exchange-acknowledgement-branch`.

### Return-to-sender, reshipment, backorder
Non-terminal observation states reached on the corresponding Fulfillment events. `ReshipmentCreated` records `ActiveReshipmentShipmentId`; saga returns to `Shipped` on the next `ShipmentHandedToCarrier` for the new shipment. Dossier: `bcs/orders.md#state-machine`.

### Idempotency invariants
- An already-known `ReservationId` on `ReservationConfirmed` is dropped silently (per-SKU counter not double-incremented).
- `FulfillmentRequested` is dispatched once and only once — gated on `CommittedReservationIds.Count == ExpectedReservationCount && IsPaymentCaptured && Status not already in post-fulfillment or terminal`.
- The payment-between-reservations race is handled by fanning commits for *all* known reservations when `PaymentCaptured` arrives after some `ReservationConfirmed`s.

## BCs and roles

- **Orders** — Orchestrator. Owns the `Order` saga document and the `OrderDecider` pure-function state machine (ADR 0029). 24 inbound integration contracts, 10 outbound. Dossier: `bcs/orders.md`.
- **Payments** — Participant. Authorizes / captures / refunds / fails; replies on the saga-driving inbound contracts. Dossier: `bcs/payments.md`.
- **Inventory** — Participant. Per-SKU reservations / commits / releases; replies on per-SKU saga-driving contracts. Dossier: `bcs/inventory.md`.
- **Fulfillment** — Participant. Picks, packs, ships, delivers; replies via `ShipmentHandedToCarrier` / `ShipmentDelivered` and variants. Dossier: `bcs/fulfillment.md`.
- **Returns** — Participant (post-delivery). Keeps the saga open via `ActiveReturnIds` until refund / denial / rejection / expiry. Dossier: `bcs/returns.md`.
- **Customer Experience / Correspondence / Backoffice** — Read-side observers. Subscribe to the broadcast `OrderPlaced` / `OrderCancelled` / status events for SignalR push, transactional email, and operator dashboards. Dossiers: `bcs/customer-experience.md`, `bcs/correspondence.md`, `bcs/backoffice.md`.

## Tests as behavioral evidence

- **Gherkin features:** `docs/features/orders/` covers the saga state-machine transitions and the M45.1 fraud-review + shipping-address-change branches.
- **Integration tests (Alba):** `tests/Orders/Orders.Api.IntegrationTests/Placement/` covers the full saga lifecycle, the M45.1 branches, and the cross-product exchange acknowledger handlers.
- **Unit tests (pure decider):** `tests/Orders/Orders.UnitTests/Placement/OrderDecider*Tests.cs` — every transition tested as a pure function per ADR 0029. Includes `OrderDeciderShippingAddressChangeTests`, `OrderDeciderFraudReviewTests`, `OrderSagaCrossProductExchangeTests`.

## ADRs

- **ADR 0029** — Decider Pattern + Pure-Function Saga Logic. Cited as the rationale for splitting state-transition decisions into a static `OrderDecider`. File: `docs/decisions/0029-*.md`.
- **ADR 0001** — Checkout Migration to Orders BC. File: `docs/decisions/0001-checkout-migration-to-orders.md`.
- **ADR 0040** — `*Requested` naming convention for command-intent integration messages (e.g. `ReservationCommitRequested`, `RefundRequested`). File: `docs/decisions/0040-*.md`.
- **ADR 0061** — Cross-product exchange replacement reservation (Returns orchestrator). File: `docs/decisions/0061-cross-product-exchange-replacement-reservation.md`.
- **ADR 0062** — Cross-product exchange Payments choreography. File: `docs/decisions/0062-cross-product-exchange-payments-choreography.md`.

## Declared vs. implemented

- **Declared shape (Order saga retro narrative):** `ReleaseOrderFromHold` restores the saga to its exact pre-hold status.
- **Implemented shape:** Restoration uses a safe-default `PaymentConfirmed` recovery; exact-pre-hold-status restoration is deferred to the Orders remaster (source-commented at `Order.cs#L228-L241`, `OrderDecider.cs#L727-L750`).
- **Gap:** Functional gap noted in code; tracked as a forward-note.

- **Declared shape (per the historical M45.1 narrative):** `ExchangePartialRefundIssued` saga handler forwards `RefundRequested` to Payments.
- **Implemented shape:** Per ADR 0062 (M47.0 / S2), the saga handler is a no-op acknowledger; the Returns ↔ Payments choreography issues the refund directly.
- **Gap:** Source comments at `Order.cs#L738-L761` capture the transition; the dossier flags this as a deliberate refresh, not a gap.

## Source citations

- Dossier sections referenced: `bcs/orders.md#saga-shape`, `bcs/orders.md#state-machine`, `bcs/orders.md#publishes`, `bcs/orders.md#subscribes`, `bcs/orders.md#idempotency-invariants`, `bcs/orders.md#cross-product-exchange-acknowledgement-branch`, `bcs/payments.md#saga-replies-drive-the-order-saga-forward`, `bcs/inventory.md#published-by-inventory-outbound`, `bcs/inventory.md#subscribed-by-inventory-inbound`, `bcs/fulfillment.md#integration-events`, `bcs/returns.md#saga-acknowledger-relationship-with-the-order-saga`.
- ADRs: 0001, 0029, 0040, 0061, 0062.
- Tests: `tests/Orders/Orders.UnitTests/Placement/`, `tests/Orders/Orders.Api.IntegrationTests/Placement/`.
