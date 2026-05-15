# Orders

> **Source folder:** `src/Orders/`
> **Status:** Implemented
> **Most recent material milestone:** M45.1 — Cross-product exchange Returns-side + Order saga handler additions
> **Stub depth:** S1 — to be deepened in S2

## Purpose

Orders owns the moment commercial commitment is made. The bounded context contains both the checkout aggregate — where the customer fixes their shipping address, payment method, and shipping method before placing an order — and the order lifecycle saga that orchestrates Payments, Inventory, and Fulfillment after placement. Orders is also the BC that records cancellations, fraud-review hold/release/reject decisions, post-placement shipping-address changes, and the receipt of return-related state changes from the Returns BC.

## Top-level structure

### Aggregates

- `Checkout` — stream ID: UUID v7 (natural).
- `Order` — stream ID: UUID v7 (natural; numeric revisions enabled).

### Commands

- `ProvideShippingAddressRequest`
- `SelectShippingMethodRequest`
- `ProvidePaymentMethodRequest`
- `CompleteCheckout`
- `PlaceOrder`
- `CancelOrder`
- `ChangeShippingAddress`
- `PutOrderOnHold`
- `ReleaseOrderFromHold`
- `RejectOrderForFraud`
- `ReturnWindowExpired`

### Domain events

- `CheckoutStarted`
- `ShippingAddressProvided`
- `ShippingMethodSelected`
- `PaymentMethodProvided`
- `OrderCreated`
- `OrderPlaced`
- `OrderCancelled`
- `ShippingAddressChanged`
- `OrderPutOnHoldForFraudReview`
- `OrderReleasedFromFraudHold`
- `OrderRejectedForFraud`

### Projections

- `Checkout` snapshot — inline, keyed by `CheckoutId` (`Orders.Api/Program.cs`).
- `Order` lives via numeric-revision aggregate writes (no separate snapshot projection registered at S1 stub depth).

### Integration events

- `Orders.OrderPlaced` — publishes
- `Orders.OrderCancelled` — publishes
- `Orders.ShippingAddressChanged` — publishes
- `Orders.ReservationCommitRequested` — publishes
- `Orders.ReservationReleaseRequested` — publishes
- Fraud-review integration events (`OrderPutOnHoldForFraudReview`, `OrderReleasedFromFraudHold`, `OrderRejectedForFraud` per `FraudReviewEvents.cs`) — publish
- `Shopping.CheckoutInitiated` — subscribes
- `Payments.PaymentAuthorized` / `PaymentCaptured` / `PaymentFailed` / `RefundCompleted` — subscribes
- `Inventory.ReservationConfirmed` / `ReservationFailed` / `ReservationCommitted` / `ReservationReleased` — subscribes
- `Fulfillment.ShipmentHandedToCarrier` / `TrackingNumberAssigned` / `ShipmentDelivered` / `ReturnToSenderInitiated` / `ReshipmentCreated` / `BackorderCreated` / `FulfillmentCancelled` / `OrderSplitIntoShipments` — subscribes
- `Returns.ReturnApproved` / `ReturnDenied` / `ReturnCompleted` / `ReturnExpired` and the four cross-product exchange events (per M45.1) — subscribes

### HTTP / API surface (one line)

`Orders.Api` exposes the checkout-step commands, place / cancel / amend order endpoints, and the read-model endpoints used by the storefront and Backoffice.

### Frontend surface (if applicable)

Not applicable — no frontend in this BC.

### Identity / auth posture (if applicable)

JWT Bearer (Backoffice + Vendor schemes registered in `Orders.Api/Program.cs`); customer-initiated calls arrive via the Storefront BFF.

## Prior event modeling

- `docs/planning/saga-discovery-design-session.md`

## ADRs

- ADR 0014 — Checkout Migration Completion
- ADR 0029 — Order Saga Design Decisions
- ADR 0040 — `*Requested` Integration Event Convention

## Source citations (S1 stub)

- `src/Orders/`
- `src/Shared/Messages.Contracts/Orders/`
- `CONTEXTS.md` (section: `Orders`)
- `docs/decisions/0014-checkout-migration-completion.md`
- `docs/decisions/0029-order-saga-design-decisions.md`
- `docs/decisions/0040-requested-integration-event-convention.md`
- `docs/planning/saga-discovery-design-session.md`
