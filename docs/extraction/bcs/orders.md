# Orders

> **Source folder:** `src/Orders/`
> **Status:** Implemented
> **Most recent material milestone:** M47.0 — Cross-product exchange end-to-end (Slice 2 acknowledger refresh on the Order saga)
> **Dossier depth:** S2 — full

## Purpose

Orders owns the moment commercial commitment is made. The bounded context contains two aggregates: a `Checkout` event-sourced stream that captures the customer's shipping address, shipping method, and payment method before they place an order; and an `Order` saga that orchestrates Payments, Inventory, and Fulfillment from placement through delivery and the post-delivery return window. Orders is also the bounded context that records cancellations, fraud-review hold/release/reject decisions, post-placement shipping-address changes, and acknowledgements of return-related state changes received from the Returns BC.

## Aggregates

### `Checkout`

- **Stream ID:** `Guid` carried verbatim from the Shopping BC's `CheckoutInitiated.CheckoutId` integration message; the value is used as the Marten stream ID via `MartenOps.StartStream<Checkout>(message.CheckoutId, startedEvent)` (`src/Orders/Orders/Checkout/CheckoutInitiatedHandler.cs#L19-L29`). UUID v7 is generated upstream in the Shopping BC; Orders does not mint it.
- **Key state:** `CartId`, `CustomerId?`, `Items` (immutable line-item list with `Sku` / `Quantity` / `UnitPrice`), `StartedAt`, `ShippingAddress?`, `ShippingMethod?`, `ShippingCost?`, `PaymentMethodToken?`, `IsCompleted` (`src/Orders/Orders/Checkout/Checkout.cs#L7-L18`). The four optional fields drive the `CompleteCheckoutHandler.Before` invariants — all three of address, method, token must be present before completion is allowed (`src/Orders/Orders/Checkout/CompleteCheckout.cs#L13-L26`).
- **Lifecycle stages:**
  - `Started` → on `CheckoutStarted` from the Shopping cross-BC handoff (`src/Orders/Orders/Checkout/Checkout.cs#L20-L29`).
  - `Address provided` → on `ShippingAddressProvided` (`src/Orders/Orders/Checkout/Checkout.cs#L31-L41`).
  - `Method selected` → on `ShippingMethodSelected` (sets `ShippingMethod` + `ShippingCost`) (`src/Orders/Orders/Checkout/Checkout.cs#L43-L48`).
  - `Payment provided` → on `PaymentMethodProvided` (`src/Orders/Orders/Checkout/Checkout.cs#L50-L51`).
  - `Completed` → on `OrderCreated`, which sets `IsCompleted = true` and records the freshly-minted `OrderId`. This is a terminal state for the Checkout stream (`src/Orders/Orders/Checkout/Checkout.cs#L53-L54`, `CompleteCheckout.cs#L29-L60`).
- **File:** `src/Orders/Orders/Checkout/Checkout.cs`.

### `Order`

The `Order` aggregate is a Wolverine saga persisted as a mutable Marten document with numeric revisions (`opts.Schema.For<Order>().Identity(x => x.Id).UseNumericRevisions(true).Index(x => x.CustomerId)` at `src/Orders/Orders.Api/Program.cs#L42-L45`). The choice to model the saga as a document rather than an event-sourced stream is the central decision in ADR 0029 §Decision 1.

- **Stream ID:** Saga `Id` is the `OrderId` minted in `CompleteCheckoutHandler.Handle` via `Guid.CreateVersion7()` (`src/Orders/Orders/Checkout/CompleteCheckout.cs#L33`). The same value is propagated through `CartCheckoutCompleted.OrderId`, `PlaceOrder.OrderId`, and the resulting `Order.Id` so `OrderId` correlates every cross-BC integration message (`src/Orders/Orders/Placement/OrderDecider.cs#L28-L66`).
- **Key state:**
  - Identity / lifecycle: `Status` (`OrderStatus` enum), `PlacedAt`, `DeliveredAt?`, `TrackingNumber?`.
  - Customer + commercial: `CustomerId`, `LineItems` (`IReadOnlyList<OrderLineItem>`), `ShippingAddress`, `ShippingMethod`, `PaymentMethodToken`, `TotalAmount`.
  - Inventory orchestration counters: `ReservationIds` (`Dictionary<Guid, string>` of `ReservationId → Sku`), `ExpectedReservationCount`, `ConfirmedReservationCount`, derived `IsInventoryReserved`; `CommittedReservationIds` (`HashSet<Guid>` — see ADR 0029 §Decision 4 for why this is a set rather than an `int` counter), derived `CommittedReservationCount`, derived `IsAllInventoryCommitted`.
  - Payment orchestration: `IsPaymentCaptured`, `PaymentId?`.
  - Returns / post-delivery: `ActiveReturnIds` (`IReadOnlyList<Guid>`), `ReturnWindowFired`.
  - Fulfillment notifications: `ShipmentCount` (default 1), `ActiveReshipmentShipmentId?`.
  - Source: `src/Orders/Orders/Placement/Order.cs#L17-L148`.
- **Lifecycle stages:** the `OrderStatus` enum defines 16 saga states (`src/Orders/Orders/Placement/OrderStatus.cs#L9-L67`): `Placed`, `PendingPayment`, `PaymentConfirmed`, `PaymentFailed`, `InventoryReserved`, `OutOfStock`, `InventoryCommitted`, `OnHold`, `Fulfilling`, `Shipped`, `Delivered`, `Cancelled`, `Closed`, `DeliveryFailed`, `Reshipping`, `Backordered`. The transitions between them live in `OrderDecider`; the saga state machine is described in detail in §Sagas / orchestration below.
- **File:** `src/Orders/Orders/Placement/Order.cs`.

## Commands

Two categories: **external commands** received over HTTP or via the cross-BC integration message bus, and **saga-internal commands** the saga issues to itself or that flow into the saga from time-based scheduling. Saga state-change handlers triggered by integration messages from Payments / Inventory / Fulfillment / Returns are listed under §Integration events rather than here, because they have integration contracts as their on-the-wire shape, not local commands.

### Checkout aggregate (external)

- `ProvideShippingAddressRequest` — request body for `POST /api/checkouts/{checkoutId}/shipping-address`; appends `ShippingAddressProvided` to the Checkout stream. Handler: `src/Orders/Orders/Checkout/ProvideShippingAddress.cs#L40-L67`.
- `SelectShippingMethodRequest` — request body for `POST /api/checkouts/{checkoutId}/shipping-method`; appends `ShippingMethodSelected`. Handler: `src/Orders/Orders/Checkout/SelectShippingMethod.cs#L29-L56`.
- `ProvidePaymentMethodRequest` — request body for `POST /api/checkouts/{checkoutId}/payment-method`; appends `PaymentMethodProvided`. Handler: `src/Orders/Orders/Checkout/ProvidePaymentMethod.cs#L27-L52`.
- `CompleteCheckout` — issued via `POST /api/checkouts/{checkoutId}/complete`; the handler validates the Checkout has address, method, and token via `Before`, mints a new `OrderId` (UUID v7), appends `OrderCreated` to the Checkout stream, and emits `Messages.Contracts.Shopping.CartCheckoutCompleted` to start the Order saga. Handler: `src/Orders/Orders/Checkout/CompleteCheckout.cs#L13-L60`.

### Order saga (external)

- `CancelOrder` — sent by `Orders.Api.Placement.CancelOrderEndpoint` after a pre-flight `OrderDecider.CanBeCancelled` check (`src/Orders/Orders.Api/Placement/CancelOrderEndpoint.cs#L37-L50`); validated by `CancelOrderValidator` (`src/Orders/Orders/Placement/CancelOrder.cs#L13-L25`). The saga handler in `Order.Handle(CancelOrder)` re-runs the guard for at-least-once safety (`src/Orders/Orders/Placement/Order.cs#L161-L184`).
- `ChangeShippingAddress` (M45.1 / S4) — sent by `ChangeShippingAddressEndpoint`; eligibility window enforced by `OrderDecider.CanChangeShippingAddress` at both the HTTP layer (`src/Orders/Orders.Api/Placement/ChangeShippingAddressEndpoint.cs#L57-L65`) and inside the saga handler (`src/Orders/Orders/Placement/Order.cs#L194-L206`).
- `PutOrderOnHold` (M45.1 / S5) — no HTTP endpoint today; intentionally programmatic-only pending the Backoffice review-queue UI (`src/Orders/Orders/Placement/FraudReviewCommands.cs#L17-L33`). Handler: `src/Orders/Orders/Placement/Order.cs#L217-L226`.
- `ReleaseOrderFromHold` (M45.1 / S5) — same triggering surface as above; handler `src/Orders/Orders/Placement/Order.cs#L232-L241` returns the saga to `PaymentConfirmed` per ADR 0029-style "safe default" recovery.
- `RejectOrderForFraud` (M45.1 / S5) — same triggering surface; handler `src/Orders/Orders/Placement/Order.cs#L248-L262` reuses the cancellation compensation path and emits both `OrderRejectedForFraud` and `OrderCancelled`.

### Order saga (saga-internal)

- `PlaceOrder` — local domain command constructed inside `PlaceOrderHandler.Handle` by mapping the inbound `Messages.Contracts.Shopping.CartCheckoutCompleted` integration message; not dispatched through the message bus (so the FluentValidation middleware does not run — the contract is enforced by the upstream Shopping BC and the mapping itself) (`src/Orders/Orders/Placement/PlaceOrder.cs#L7-L20`, `PlaceOrderHandler.cs#L18-L41`). Drives the Wolverine saga-start handler shape `(Order, IntegrationMessages.OrderPlaced)`.
- `ReturnWindowExpired` — scheduled by the saga via `OutgoingMessages.Delay(...)` at `src/Orders/Orders/Placement/Order.cs#L453` after `ShipmentDelivered`; handler at `src/Orders/Orders/Placement/Order.cs#L558-L564` closes the saga unless return activity remains. The delay is governed by `OrderDecider.ReturnWindowDuration = TimeSpan.FromDays(30)` (`src/Orders/Orders/Placement/OrderDecider.cs#L19`).

**Reconciliation note on the S1 stub command count.** S1 enumerated 11 commands (`CompleteCheckout`, `PlaceOrder`, the three `Provide*Request` / `Select*Request` checkout step bodies, `CancelOrder`, `ChangeShippingAddress`, `PutOrderOnHold`, `ReleaseOrderFromHold`, `RejectOrderForFraud`, `ReturnWindowExpired`). S2 enumerates the same 11 — five Checkout commands (one of which is the cross-aggregate `CompleteCheckout` that mints the `OrderId`), one saga-start internal command (`PlaceOrder`), four external Order saga commands (`CancelOrder`, `ChangeShippingAddress`, `PutOrderOnHold`, `ReleaseOrderFromHold`, `RejectOrderForFraud`), and one saga-internal scheduled command (`ReturnWindowExpired`). Count = 11; matches S1.

## Domain events

### `Checkout` aggregate events

Persisted in the `orders` schema's Marten event store. Inline-projected into the `Checkout` snapshot.

- `CheckoutStarted` — the Shopping BC's `CheckoutInitiated` cross-BC handoff has been accepted; the Checkout stream is open. Carries `CheckoutId`, `CartId`, `CustomerId?`, the line-item snapshot, and `StartedAt`. File: `src/Orders/Orders/Checkout/CheckoutStarted.cs`.
- `ShippingAddressProvided` — customer supplied a delivery address. File: `src/Orders/Orders/Checkout/ShippingAddressProvided.cs`.
- `ShippingMethodSelected` — customer selected a shipping option and locked in `ShippingCost`. File: `src/Orders/Orders/Checkout/ShippingMethodSelected.cs`.
- `PaymentMethodProvided` — customer attached a tokenized payment instrument. File: `src/Orders/Orders/Checkout/PaymentMethodProvided.cs`.
- `OrderCreated` — Checkout was completed; carries the freshly-minted `OrderId` and `CompletedAt`. Closes the Checkout stream and is the trigger for the cross-BC `CartCheckoutCompleted` integration message. File: `src/Orders/Orders/Checkout/OrderCreated.cs`.

### `Order` saga events

The `Order` saga is a mutable document, not an event-sourced stream (ADR 0029 §Decision 1). The "events" it produces are the integration events it publishes through Wolverine; in addition there is one internal domain record:

- `OrderPlaced` — domain record inside `Orders.Placement` produced by `PlaceOrderHandler` as the cascaded message that starts the saga (`src/Orders/Orders/Placement/OrderPlaced.cs`). Its on-the-wire counterpart is `Messages.Contracts.Orders.OrderPlaced` (see §Integration events below).

Saga state-machine progression beyond `OrderPlaced` is captured by mutations to `Status` plus the integration events listed in §Integration events (which double as the saga's externally-visible "what just happened" surface — the single source of truth for downstream BCs).

**Reconciliation note on the S1 stub event count.** S1 enumerated 11 events: 5 Checkout (`CheckoutStarted`, `ShippingAddressProvided`, `ShippingMethodSelected`, `PaymentMethodProvided`, `OrderCreated`) plus 6 Order-saga (`OrderPlaced`, `OrderCancelled`, `ShippingAddressChanged`, `OrderPutOnHoldForFraudReview`, `OrderReleasedFromFraudHold`, `OrderRejectedForFraud`). S2 reaches the same 11 with two naming clarifications: the on-the-wire integration contracts are `OrderPutOnHold` / `OrderReleasedFromHold` / `OrderRejectedForFraud` (`src/Shared/Messages.Contracts/Orders/FraudReviewEvents.cs`) — the S1 stub used the longer descriptive forms; and `OrderPlaced` exists as both a Marten-internal domain record (`src/Orders/Orders/Placement/OrderPlaced.cs`) and as an integration contract (`src/Shared/Messages.Contracts/Orders/OrderPlaced.cs`), counted once. Count = 11; matches S1.

## Projections

- `Checkout` snapshot — **inline**, keyed by `CheckoutId`. Source events: `CheckoutStarted`, `ShippingAddressProvided`, `ShippingMethodSelected`, `PaymentMethodProvided`, `OrderCreated`. Registered at `src/Orders/Orders.Api/Program.cs#L48` (`opts.Projections.Snapshot<Checkout>(SnapshotLifecycle.Inline)`). Served by `GetCheckoutEndpoint` which actually re-aggregates via `session.Events.AggregateStreamAsync<Checkout>(...)` rather than reading the snapshot table directly (`src/Orders/Orders.Api/Checkout/GetCheckoutEndpoint.cs#L20-L26`).
- `Order` saga document — **document-store, not a projection**. Persisted directly via Marten's document API with numeric revisions (`UseNumericRevisions(true)`) plus a non-unique `CustomerId` index (`src/Orders/Orders.Api/Program.cs#L42-L45`). Served by `GetOrderEndpoint`, `ListOrdersEndpoint` (filtered by `CustomerId`), `SearchOrdersEndpoint` (Guid lookup), and `GetReturnableItemsEndpoint`.

## Integration events

Grouped by direction. Saga-driving inbound events (those that advance the Order saga state machine) are flagged. All Orders-published contracts live under `src/Shared/Messages.Contracts/Orders/`.

### Publishes

- `Orders.OrderPlaced` — payload: `OrderId: Guid, CustomerId: Guid, LineItems: IReadOnlyList<OrderLineItem>, ShippingAddress: ShippingAddress, ShippingMethod: string, PaymentMethodToken: string, TotalAmount: decimal, PlacedAt: DateTimeOffset`. Cascaded from `PlaceOrderHandler` and routed to the `storefront-notifications` RabbitMQ queue (`src/Orders/Orders.Api/Program.cs#L122-L123`). Subscribers per CONTEXTS.md: Payments + Inventory (workflow init), Customer Experience + Correspondence (notification). File: `src/Shared/Messages.Contracts/Orders/OrderPlaced.cs`.
- `Orders.OrderCancelled` — payload: `OrderId: Guid, CustomerId: Guid, Reason: string, CancelledAt: DateTimeOffset`. Emitted by `OrderDecider.HandleCancelOrder`, `HandleFulfillmentCancelled`, and `HandleRejectForFraud`. Subscribers: Inventory, Fulfillment, Customer Experience. File: `src/Shared/Messages.Contracts/Orders/OrderCancelled.cs`.
- `Orders.ShippingAddressChanged` (M45.1 / S4) — payload: `OrderId: Guid, CustomerId: Guid, NewShippingAddress: ShippingAddress, Reason: string, ChangedAt: DateTimeOffset`. Routed to both `fulfillment-requests` and `storefront-notifications` (`src/Orders/Orders.Api/Program.cs#L132-L135`). File: `src/Shared/Messages.Contracts/Orders/ShippingAddressChanged.cs`.
- `Orders.OrderPutOnHold` (M45.1 / S5) — payload: `OrderId: Guid, CustomerId: Guid, Reason: string, ReviewerId: string, HeldAt: DateTimeOffset`. Routed to `storefront-notifications` (`src/Orders/Orders.Api/Program.cs#L142-L143`). File: `src/Shared/Messages.Contracts/Orders/FraudReviewEvents.cs#L14-L19`.
- `Orders.OrderReleasedFromHold` (M45.1 / S5) — payload: `OrderId: Guid, CustomerId: Guid, ReviewerId: string, ReleaseNotes: string?, ReleasedAt: DateTimeOffset`. File: `src/Shared/Messages.Contracts/Orders/FraudReviewEvents.cs#L25-L30`.
- `Orders.OrderRejectedForFraud` (M45.1 / S5) — payload: `OrderId: Guid, CustomerId: Guid, Reason: string, ReviewerId: string, RejectedAt: DateTimeOffset`. Co-emitted with `OrderCancelled` from `OrderDecider.HandleRejectForFraud` (`src/Orders/Orders/Placement/OrderDecider.cs#L791-L804`). File: `src/Shared/Messages.Contracts/Orders/FraudReviewEvents.cs#L38-L43`.
- `Orders.ReservationCommitRequested` — payload: `OrderId: Guid, ReservationId: Guid, RequestedAt: DateTimeOffset`. Saga-orchestration command-intent (per ADR 0040's `*Requested` convention); emitted by the saga when `IsPaymentCaptured` and `IsInventoryReserved` align. File: `src/Shared/Messages.Contracts/Orders/ReservationCommitRequested.cs`.
- `Orders.ReservationReleaseRequested` — payload: `OrderId: Guid, ReservationId: Guid, Reason: string, RequestedAt: DateTimeOffset`. Compensation command-intent emitted on `CancelOrder`, `PaymentFailed`, `ReservationFailed`, `RejectOrderForFraud`, `FulfillmentCancelled`. File: `src/Shared/Messages.Contracts/Orders/ReservationReleaseRequested.cs`.
- `Fulfillment.FulfillmentRequested` — payload: `OrderId: Guid, CustomerId: Guid, ShippingAddress: SharedShippingAddress, LineItems: IReadOnlyList<FulfillmentLineItem>, ShippingMethod: string, RequestedAt: DateTimeOffset`. Despite living in the Fulfillment contract namespace, it is published from Orders only (`src/Orders/Orders.Api/Program.cs#L106-L108`) — the saga emits it once `IsAllInventoryCommitted && IsPaymentCaptured` (`src/Orders/Orders/Placement/OrderDecider.cs#L446-L478`). Subscribers: Fulfillment.
- `Payments.RefundRequested` — payload: `OrderId: Guid, Amount: decimal, Reason: string, RequestedAt: DateTimeOffset`. Emitted from compensation paths (`HandleCancelOrder`, `HandleReservationFailed`, `HandleFulfillmentCancelled`, `HandleRejectForFraud`) and from the `ReturnCompleted` saga handler (`src/Orders/Orders/Placement/Order.cs#L601-L606`). The contract is owned by Payments; Orders is a publisher.

### Subscribes

Saga-driving inbound events are flagged ⮕ for at-a-glance topology. All other inbound events are passive saga state mutators or no-op acknowledgers.

- `Shopping.CheckoutInitiated` — payload: `CheckoutId: Guid, CartId: Guid, CustomerId: Guid?, Items: IReadOnlyList<CheckoutLineItem>, InitiatedAt: DateTimeOffset`. Listened on RabbitMQ queue `orders-checkout-initiated` (`src/Orders/Orders.Api/Program.cs#L94-L95`). Handler: `CheckoutInitiatedHandler` opens a new Checkout stream.
- `Shopping.CartCheckoutCompleted` ⮕ payload: `OrderId: Guid, CheckoutId: Guid, CustomerId: Guid?, Items: IReadOnlyList<CheckoutLineItem>, ShippingAddress: AddressSnapshot, ShippingMethod: string, ShippingCost: decimal, PaymentMethodToken: string, CompletedAt: DateTimeOffset`. Routed to local queue `order-placement` with durable inbox (`src/Orders/Orders.Api/Program.cs#L98-L100`). Saga-start handler: `PlaceOrderHandler.Handle` returns `(Order, OrderPlaced)`.
- `Payments.PaymentAuthorized` — payload: `PaymentId: Guid, OrderId: Guid, Amount: decimal, AuthorizationId: string, AuthorizedAt: DateTimeOffset, ExpiresAt: DateTimeOffset`. Saga reply that transitions to `PendingPayment`.
- `Payments.PaymentCaptured` ⮕ payload: `PaymentId: Guid, OrderId: Guid, Amount: decimal, TransactionId: string, CapturedAt: DateTimeOffset`. Drives `Status → PaymentConfirmed` and, if `IsInventoryReserved`, triggers the commit fan-out.
- `Payments.PaymentFailed` ⮕ payload: `PaymentId: Guid, OrderId: Guid, FailureReason: string, IsRetriable: bool, FailedAt: DateTimeOffset`. Drives `Status → PaymentFailed` and emits release-reservation compensation.
- `Payments.RefundCompleted` — payload: `PaymentId: Guid, OrderId: Guid, Amount: decimal, TransactionId: string, RefundedAt: DateTimeOffset`. Saga reply; closes the saga when the order is `Cancelled` or `OutOfStock`.
- `Payments.RefundFailed` — payload: `PaymentId: Guid, OrderId: Guid, FailureReason: string, FailedAt: DateTimeOffset`. Logged-only; no status change (`src/Orders/Orders/Placement/OrderDecider.cs#L301-L307`).
- `Inventory.ReservationConfirmed` ⮕ payload: `OrderId: Guid, InventoryId: Guid, ReservationId: Guid, Sku: string, WarehouseId: string, Quantity: int, ReservedAt: DateTimeOffset`. Per-SKU; only flips to `InventoryReserved` once `ConfirmedReservationCount == ExpectedReservationCount`. If payment is already captured, fans commit-requests for *all* known reservations to handle the payment-between-reservations race (`src/Orders/Orders/Placement/OrderDecider.cs#L352-L376`).
- `Inventory.ReservationFailed` ⮕ payload: `OrderId, ReservationId, Sku, WarehouseId, RequestedQuantity, AvailableQuantity, Reason, FailedAt`. Drives `Status → OutOfStock` and full compensation fan-out.
- `Inventory.ReservationCommitted` ⮕ payload: `OrderId, InventoryId, ReservationId, Sku, WarehouseId, Quantity, CommittedAt`. Per-SKU; only flips to `Fulfilling` and emits `FulfillmentRequested` once `CommittedReservationIds.Count == ExpectedReservationCount && IsPaymentCaptured` (`src/Orders/Orders/Placement/OrderDecider.cs#L425-L490`).
- `Inventory.ReservationReleased` — payload: `OrderId, InventoryId, ReservationId, Sku, WarehouseId, Quantity, Reason, ReleasedAt`. Acknowledger only — confirmation that compensation took effect, no status change.
- `Fulfillment.ShipmentHandedToCarrier` ⮕ payload: `OrderId, ShipmentId, Carrier, TrackingNumber, HandedAt`. Replaces the legacy `ShipmentDispatched` (M41.0/S4); transitions `Status → Shipped`.
- `Fulfillment.TrackingNumberAssigned` — payload: `OrderId, ShipmentId, TrackingNumber, Carrier, AssignedAt`. Stores `TrackingNumber` for customer-facing queries; no status change.
- `Fulfillment.ShipmentDelivered` ⮕ payload: `OrderId, ShipmentId, DeliveredAt, RecipientName?`. Transitions `Status → Delivered`, persists `DeliveredAt`, and schedules the `ReturnWindowExpired` self-message via `OutgoingMessages.Delay(...)` (`src/Orders/Orders/Placement/Order.cs#L434-L455`).
- `Fulfillment.ReturnToSenderInitiated` — payload: `OrderId, ShipmentId, Carrier, TotalAttempts, EstimatedReturnDays, InitiatedAt`. Transitions `Status → DeliveryFailed`.
- `Fulfillment.ReshipmentCreated` — payload: `OrderId, OriginalShipmentId, NewShipmentId, Reason, CreatedAt`. Transitions `Status → Reshipping`; records `ActiveReshipmentShipmentId`.
- `Fulfillment.BackorderCreated` — payload: `OrderId, ShipmentId, Reason, Items: IReadOnlyList<BackorderedItem>, CreatedAt`. Transitions `Status → Backordered`.
- `Fulfillment.FulfillmentCancelled` ⮕ payload: `OrderId, ShipmentId, Reason, CancelledAt`. Drives compensation (refund + reservation release + `OrderCancelled` emit) when the order is still cancellable.
- `Fulfillment.OrderSplitIntoShipments` — payload: `OrderId, ShipmentCount, SplitAt`. Records `ShipmentCount` for customer-facing display.
- `Returns.ReturnRequested` — payload: `ReturnId, OrderId, CustomerId, RequestedAt`. Adds `ReturnId` to `ActiveReturnIds` to prevent premature saga closure.
- `Returns.ReturnCompleted` ⮕ payload: `ReturnId, OrderId, CustomerId, FinalRefundAmount: decimal, Items: IReadOnlyList<ReturnedItem>, CompletedAt`. Removes the return from `ActiveReturnIds`, emits `Payments.RefundRequested` when `FinalRefundAmount > 0`, and closes the saga when no actives remain and the return window has fired (`src/Orders/Orders/Placement/Order.cs#L588-L616`).
- `Returns.ReturnDenied` — payload: `ReturnId, OrderId, CustomerId, Reason, Message?, DeniedAt`. Removes from `ActiveReturnIds`; closes saga if applicable.
- `Returns.ReturnRejected` — payload: `ReturnId, OrderId, CustomerId, Reason, Items: IReadOnlyList<ReturnedItem>, RejectedAt`. Same shape.
- `Returns.ReturnExpired` — payload: `ReturnId, OrderId, CustomerId, ExpiredAt`. Same shape.
- `Returns.CrossProductExchangeRequested` (M45.1 / S3) — payload: `ReturnId, OrderId, CustomerId, OriginalSku, ReplacementSku, OriginalUnitPrice, ReplacementUnitPrice, Quantity, RequestedAt`. **Acknowledger only** — adds the exchange `ReturnId` to `ActiveReturnIds` so the saga stays open. Orders is the *acknowledger*, not the orchestrator (see ADR 0061 below; the Returns BC orchestrates the cross-product exchange) (`src/Orders/Orders/Placement/Order.cs#L707-L715`).
- `Returns.ExchangeAdditionalPaymentRequired` (M45.1 / S3) — payload: `ReturnId, OrderId, CustomerId, AmountDue: decimal, RequiredAt`. **No-op acknowledger** today; full capture orchestration deferred to the Orders remaster (`src/Orders/Orders/Placement/Order.cs#L723-L726`).
- `Returns.ExchangeAdditionalPaymentCaptured` (M45.1 / S3) — payload: `ReturnId, OrderId, CustomerId, PaymentId, AmountCaptured: decimal, Currency: string, PaymentReference: string, CapturedAt`. **No-op acknowledger** (`src/Orders/Orders/Placement/Order.cs#L733-L736`).
- `Returns.ExchangePartialRefundIssued` (M45.1 / S3, refreshed M47.0 / S2) — payload: `ReturnId, OrderId, CustomerId, OriginalPaymentId, RefundAmount: decimal, Currency: string, TransactionId: string, IssuedAt`. **No-op acknowledger.** In M45.1 this handler forwarded a `Payments.RefundRequested`; per ADR 0062 the Returns ↔ Payments choreography now drives the partial refund directly, so the saga deliberately publishes nothing (forwarding would double-refund) (`src/Orders/Orders/Placement/Order.cs#L758-L761`).

**Subscribe direction summary:** 24 inbound integration contracts. 9 are saga-driving (advance state machine: `CartCheckoutCompleted`, `PaymentCaptured`, `PaymentFailed`, `ReservationConfirmed`, `ReservationFailed`, `ReservationCommitted`, `ShipmentHandedToCarrier`, `ShipmentDelivered`, `FulfillmentCancelled`); the remainder are passive replies, status-only mutators, or no-op acknowledgers. Publish direction summary: 10 outbound contracts (6 owned by `Messages.Contracts.Orders`, plus `Fulfillment.FulfillmentRequested` and `Payments.RefundRequested` published from Orders, plus the saga-internal scheduled `ReturnWindowExpired` which never crosses the bus).

## Sagas / orchestration

Orders owns one saga: the **Order saga** (`src/Orders/Orders/Placement/Order.cs`). It is the most-cited orchestration in the system and the canonical implementation of the **Decider pattern + pure-function saga logic** described in ADR 0029.

### Saga shape

The saga is a Wolverine `Saga` persisted as a Marten document with numeric revisions. The class itself contains only state plus thin `Handle(...)` methods; every state-transition decision is delegated to a static `OrderDecider` function that returns an `OrderDecision` record. The saga method then applies the decision (mutates `Status`, counters, identifiers) and forwards `decision.Messages` to the `OutgoingMessages` collection. ADR 0029 §Decision 3 captures the rationale: keeps business logic free of `IDocumentSession` / `IMessageBus` coupling, and makes the entire state machine unit-testable as pure functions (the `OrderDecider*Tests` suites under `tests/Orders/Orders.UnitTests/Placement/`). ADR 0029 §Decision 2 covers why saga initialization sits in a separate `PlaceOrderHandler` rather than in the saga class itself (Wolverine's saga-start handler shape `(Order, ...)`).

### State machine

The saga has 16 states (`OrderStatus`, `src/Orders/Orders/Placement/OrderStatus.cs#L9-L67`). The end-to-end happy-path narrative reads as follows; every transition cited below is sourced from `OrderDecider`:

1. **Start.** `PlaceOrderHandler.Handle(CartCheckoutCompleted)` calls `OrderDecider.Start` to construct an `Order` document at `Status = Placed` with `ExpectedReservationCount` set to the count of distinct SKUs (`OrderDecider.cs#L46-L66`); `OrderPlaced` is published.
2. **Payment.** `PaymentAuthorized` moves to `PendingPayment` (`OrderDecider.cs#L264-L272`). `PaymentCaptured` moves to `PaymentConfirmed`, sets `IsPaymentCaptured = true`, persists `PaymentId`, and — if `IsInventoryReserved` is already true — fans `ReservationCommitRequested` for every entry in `ReservationIds` (`OrderDecider.cs#L201-L227`).
3. **Inventory reservation.** Each `ReservationConfirmed` increments `ConfirmedReservationCount` and adds an entry to `ReservationIds`; only when the per-SKU count reaches `ExpectedReservationCount` does `Status` flip to `InventoryReserved`. Idempotency: an already-known `ReservationId` is dropped silently (`OrderDecider.cs#L316-L377`).
4. **Inventory commitment.** Each `ReservationCommitted` adds to `CommittedReservationIds`. `FulfillmentRequested` is dispatched once and only once — when all SKUs are committed AND `IsPaymentCaptured` is true AND `Status` is not already in a post-fulfillment or terminal state. The `Status` flips to `Fulfilling` at the same time (`OrderDecider.cs#L425-L490`).
5. **Carrier handoff and tracking.** `ShipmentHandedToCarrier` → `Shipped`. `TrackingNumberAssigned` stores `TrackingNumber` without changing status.
6. **Delivery.** `ShipmentDelivered` → `Delivered`, persists `DeliveredAt`, and the saga schedules `ReturnWindowExpired` 30 days out via `OutgoingMessages.Delay(...)` (`Order.cs#L434-L455`, `OrderDecider.cs#L19`).
7. **Closure.** `ReturnWindowExpired` sets `ReturnWindowFired = true`. If `ActiveReturnIds.Count == 0`, `Status → Closed` and `MarkCompleted()` deletes the saga document (`Order.cs#L558-L564`); otherwise the saga stays open and is closed by the last `ReturnCompleted` / `ReturnDenied` / `ReturnRejected` / `ReturnExpired` after the window has fired.

Compensation and divergent paths:

- **Cancellation (`CancelOrder`).** Eligibility is `OrderDecider.CanBeCancelled` — disallowed in `Delivered`, `Closed`, `Cancelled`, `OutOfStock`, `PaymentFailed` (`OrderDecider.cs#L99-L102`). Emits one `ReservationReleaseRequested` per known reservation, a `RefundRequested` if `IsPaymentCaptured`, and `OrderCancelled`. If no payment was captured, the saga `MarkCompleted()`s immediately because there is no `RefundCompleted` to await (`Order.cs#L177-L183`).
- **Payment failure.** `PaymentFailed` → `PaymentFailed`; releases any known reservations.
- **Stock failure.** `ReservationFailed` → `OutOfStock`; refunds captured payment and releases other reservations (`OrderDecider.cs#L384-L416`).
- **Fulfillment cancellation.** `FulfillmentCancelled` reuses the cancellation compensation shape and gates on `CanBeCancelled` (`OrderDecider.cs#L595-L635`).
- **Return-to-sender / reshipment / backorder.** `DeliveryFailed`, `Reshipping`, `Backordered` are non-terminal observation states reached on the corresponding Fulfillment integration events. `ReshipmentCreated` records `ActiveReshipmentShipmentId`; the saga returns to `Shipped` on the next `ShipmentHandedToCarrier` for the new shipment.
- **Refund completion.** `RefundCompleted` only closes the saga when the prior `Status` was `Cancelled` or `OutOfStock` (`OrderDecider.cs#L279-L295`).

Fraud-review branch (M45.1 / S5):

- **`PutOrderOnHold`** (eligible only in `Placed`, `PendingPayment`, `PaymentConfirmed`, `InventoryReserved` — `CanBePutOnHold`, `OrderDecider.cs#L127-L131`) → `OnHold`; emits `OrderPutOnHold`.
- **`ReleaseOrderFromHold`** (eligible only when `Status == OnHold`) → `PaymentConfirmed` as a safe-default recovery; emits `OrderReleasedFromHold`. Restoring the exact pre-hold status is documented in the source as deferred to the Orders remaster (`Order.cs#L228-L241`, `OrderDecider.cs#L727-L750`).
- **`RejectOrderForFraud`** (eligible in any pre-fulfillment status plus `OnHold` — `CanBeRejectedForFraud`, `OrderDecider.cs#L144-L149`) → `Cancelled`; reuses the cancellation compensation shape and emits both `OrderRejectedForFraud` (for Customer Experience messaging + Backoffice account-flagging) and `OrderCancelled` (so downstream BCs react via the existing cancellation choreography). Same immediate-`MarkCompleted` rule applies when no payment was captured (`Order.cs#L243-L262`).

Shipping-address change branch (M45.1 / S4):

- **`ChangeShippingAddress`** (eligible in `Placed`, `PendingPayment`, `PaymentConfirmed`, `InventoryReserved`, `OnHold` — `CanChangeShippingAddress`, `OrderDecider.cs#L112-L117`). HTTP layer returns 409 when ineligible; saga handler re-validates and silently no-ops for at-least-once safety. Mutates the saga's `ShippingAddress` in place and emits `ShippingAddressChanged` to the `fulfillment-requests` and `storefront-notifications` queues (`OrderDecider.cs#L655-L688`).

Cross-product exchange acknowledgement branch (M45.1 / S3, refreshed M47.0 / S2):

The Returns BC publishes four exchange-related integration events to the `orders-returns-events` queue. The Order saga is the **acknowledger**, not the orchestrator. The orchestration of the cross-product exchange — replacement-inventory reservation (ADR 0061), additional-payment capture, partial-refund issuance (ADR 0062 — Returns ↔ Payments choreography) — lives in the Returns BC and the Returns ↔ Payments choreography respectively. The Order saga's role is limited to:

- **`CrossProductExchangeRequested`** — register the exchange `ReturnId` in `ActiveReturnIds` so the saga doesn't close while the exchange is in flight (`Order.cs#L707-L715`).
- **`ExchangeAdditionalPaymentRequired` / `ExchangeAdditionalPaymentCaptured` / `ExchangePartialRefundIssued`** — no-op acknowledgers. Their presence keeps Wolverine from logging "no handler" for the queue subscription. The `ExchangePartialRefundIssued` handler in particular *previously* forwarded a `RefundRequested` to Payments; per ADR 0062 the Returns ↔ Payments choreography now issues the refund directly, so the saga publishes nothing (forwarding would double-refund). The handler comment in source captures the M47.0 transition (`Order.cs#L738-L761`).

Per `OrderDecider`, this distinction also surfaces in §Integration events as the "acknowledger" flag on those four contracts.

### Idempotency invariants

Several handlers re-validate eligibility inside the decider as a defensive measure under at-least-once delivery — `CancelOrder` (`Order.cs#L165-L168`), `ChangeShippingAddress` (`OrderDecider.cs#L660-L663`), `PutOrderOnHold` (`OrderDecider.cs#L700-L703`), `RejectOrderForFraud` (`OrderDecider.cs#L764-L767`), `ShipmentHandedToCarrier` (`OrderDecider.cs#L530-L532`), `ReturnToSenderInitiated` (`OrderDecider.cs#L558-L560`). `ReservationConfirmed` deduplicates on `ReservationId` (`OrderDecider.cs#L332-L333`); `ReservationCommitted` deduplicates against `CommittedReservationIds` (`OrderDecider.cs#L434-L435`); `ShipmentDelivered` is gated by the `Delivered`/`Closed` status check at `Order.cs#L439-L440` to prevent re-scheduling `ReturnWindowExpired`. The `CommittedReservationIds` set (vs. an `int` counter) is itself an idempotency mechanism described in ADR 0029 §Decision 4.

## HTTP / API surface

All endpoints are in `src/Orders/Orders.Api/`. Wolverine HTTP discovers handlers via `app.MapWolverineEndpoints(...)` (`Program.cs#L264-L267`).

### Checkout step endpoints

- `POST /api/checkouts/{checkoutId}/shipping-address` — append `ShippingAddressProvided`. Handler: `ProvideShippingAddressHandler.Handle`. Auth: anonymous (no policy attribute applied). Consumed by Customer Experience (storefront) checkout flow.
- `POST /api/checkouts/{checkoutId}/shipping-method` — append `ShippingMethodSelected`. Handler: `SelectShippingMethodHandler.Handle`. Auth: anonymous.
- `POST /api/checkouts/{checkoutId}/payment-method` — append `PaymentMethodProvided`. Handler: `ProvidePaymentMethodHandler.Handle`. Auth: anonymous.
- `POST /api/checkouts/{checkoutId}/complete` — append `OrderCreated`, mint `OrderId`, publish `Shopping.CartCheckoutCompleted` to start the Order saga. Handler: `CompleteCheckoutHandler.Handle` at `src/Orders/Orders/Checkout/CompleteCheckout.cs`. Auth: anonymous.
- `GET /api/checkouts/{checkoutId}` — re-aggregates the Checkout stream and returns `CheckoutResponse`. Handler: `GetCheckoutEndpoint.Get`. Auth: anonymous.

### Order endpoints

- `POST /api/orders/{orderId}/cancel` — guards via `OrderDecider.CanBeCancelled` then publishes `CancelOrder` to the saga. Handler: `CancelOrderEndpoint.Handle`. Auth: anonymous (Backoffice + Customer Experience both reach this through their respective layers).
- `POST /api/orders/{orderId}/shipping-address` — guards via `OrderDecider.CanChangeShippingAddress` then publishes `ChangeShippingAddress`. Handler: `ChangeShippingAddressEndpoint.Handle`. Auth: anonymous.
- `GET /api/orders/{orderId}` — load saga document, return `OrderResponse`. Handler: `GetOrderEndpoint.Get`. Auth: anonymous. Consumed by Customer Experience for order detail; by Backoffice for order management.
- `GET /api/orders?customerId=...` — list saga documents by `CustomerId` index (`OrderSummaryResponse`). Handler: `ListOrdersEndpoint.List`. Auth: anonymous.
- `GET /api/orders/search?query=...` — Guid-only search (no partial match), capped at the single matching saga. Handler: `SearchOrdersEndpoint.Search`. Auth: anonymous. Consumed by Backoffice order search.
- `GET /api/orders/{orderId}/returnable-items` — gated to `Delivered` or `Closed` orders; returns line items + `DeliveredAt` so the Returns BC and Customer Experience can compute return-window deadlines. Handler: `GetReturnableItemsEndpoint.Handle`. Auth: anonymous. Consumed by the Returns BC at the start of a return flow.

### Auth schemes registered (but not currently applied to endpoints)

`Orders.Api/Program.cs#L165-L255` registers Backoffice + Vendor JWT schemes plus authorization policies (`CustomerService`, `WarehouseClerk`, `OperationsManager`, `VendorAdmin`, `AnyAuthenticated`) under ADR 0032. None of the endpoints above bind a `[Authorize(...)]` policy today; the policies are wired so that Backoffice + Vendor BCs can apply them when those callers land. Cross-cutting authentication and authorization middleware is mounted via `app.UseAuthentication()` and `app.UseAuthorization()` (`Program.cs#L242-L243`).

## Frontend surface

Not applicable — no frontend in this BC. The customer-facing checkout, order detail, and cancellation flows live in Customer Experience (Storefront.Web); the Backoffice order-management surface lives in the Backoffice BC. Both consume Orders strictly over HTTP and via the integration message bus.

## Identity / auth posture

- Scheme registration: multi-issuer JWT — `Backoffice` (Authority `https://localhost:5249`) and `Vendor` (Authority `https://localhost:5240`) per ADR 0032 (`src/Orders/Orders.Api/Program.cs#L165-L196`). `RoleClaimType = "role"` so JWT `role` claims map to `ClaimTypes.Role`.
- Policies: `CustomerService`, `WarehouseClerk`, `OperationsManager`, `VendorAdmin`, `AnyAuthenticated` (`Program.cs#L199-L240`). None of the registered endpoints currently bind a policy; the registration is ahead of caller binding.
- Customer-initiated calls reach Orders via the Storefront BFF (Customer Experience) which holds the Customer Identity cookie and propagates only the Customer correlation needed inside the request payload (`CustomerId` is carried explicitly on `Shopping.CartCheckoutCompleted` rather than reconstructed from the cookie). Orders does not consume the Customer Identity cookie directly.
- Cross-BC posture: Orders queries Customer Identity for the address snapshot at checkout completion (per `CONTEXTS.md` Customer Identity row). The snapshot is forwarded as part of `CartCheckoutCompleted.ShippingAddress: AddressSnapshot` (`src/Shared/Messages.Contracts/CustomerIdentity/AddressSnapshot.cs`) so Orders never re-queries Customer Identity later — the address is materialized into the Order saga at placement.
- Source: `src/Orders/Orders.Api/Program.cs#L165-L255`.

## Tests as behavioral evidence

### Gherkin features

- `docs/features/orders/order-modifications.feature` — 8 scenarios covering the M45.1 / S4 shipping-address change flow (eligibility windows in pre-handoff statuses, 409 after warehouse hand-off, missing-reason / missing-address validation, unknown-order 404, late-arriving message silently ignored). 0 `@pending` / 0 `@wip` scenarios.

### Integration tests (Alba) — `tests/Orders/Orders.Api.IntegrationTests/`

Checkout suite (`Checkout/`):

- `CheckoutInitiatedHandlerHttpTests` — 4 tests; verifies the cross-BC `Shopping.CheckoutInitiated` handoff opens a Checkout stream end-to-end through Wolverine's RabbitMQ subscription.
- `CheckoutWorkflowTests` — 6 tests; full happy-path Checkout step-by-step (address → method → payment → complete) plus state transitions.
- `GetCheckoutTests` — 3 tests; the `GET /api/checkouts/{id}` projection query.

Placement suite (`Placement/`):

- `OrderPlacementFlowTests` — 1 test; saga-start through `CartCheckoutCompleted`.
- `CheckoutToOrderIntegrationTests` — 2 tests; verifies the bridge from Checkout completion to Order saga initialization.
- `ShoppingIntegrationTests` — 2 tests; subscription wiring for Shopping → Orders.
- `PaymentIntegrationTests` — 5 tests; saga reactions to `PaymentAuthorized` / `PaymentCaptured` / `PaymentFailed` / `RefundCompleted` / `RefundFailed`.
- `InventoryIntegrationTests` — 7 tests; per-SKU `ReservationConfirmed` / `ReservationFailed` / `ReservationCommitted` orchestration including the multi-SKU race covered in `OrderDecider.HandleReservationConfirmed`.
- `FulfillmentIntegrationTests` — 4 tests; `ShipmentDelivered`, `ShipmentHandedToCarrier`, `OrderSplitIntoShipments`, `BackorderCreated`.
- `FulfillmentMigrationTests` — 8 tests; the M41.0 transition from legacy `ShipmentDispatched` to `ShipmentHandedToCarrier` and from `ShipmentDeliveryFailed` to `ReturnToSenderInitiated`.
- `CancellationIntegrationTests` — 6 tests; `CancelOrder` HTTP endpoint and saga compensation.
- `ListOrdersTests` — 4 tests; `GET /api/orders` filtered by `CustomerId`.
- `GetReturnableItemsTests` — 3 tests; `Delivered`/`Closed` gating and `DeliveredAt` propagation.

### Unit tests — `tests/Orders/Orders.UnitTests/Placement/`

The `OrderDecider` unit tests are the canonical behavior-verification surface for the saga because the decider is a pure-function module (per ADR 0029 §Decision 3). 138 tests across 11 files:

- `OrderDeciderStartTests` — 17 tests; saga-start construction.
- `OrderDeciderPaymentTests` — 17 tests; `HandlePaymentAuthorized` / `HandlePaymentCaptured` / `HandlePaymentFailed` / `HandleRefundCompleted` / `HandleRefundFailed`.
- `OrderDeciderPaymentIdTests` — 3 tests; `PaymentId` capture on `PaymentCaptured`.
- `OrderDeciderInventoryTests` — 23 tests; per-SKU `HandleReservationConfirmed` / `HandleReservationFailed` / `HandleReservationCommitted` / `HandleReservationReleased` including idempotency and the payment-between-reservations race.
- `OrderDeciderFulfillmentTests` — 18 tests; the seven Fulfillment-side handlers including the `ShipmentHandedToCarrier` / `ReturnToSenderInitiated` legacy-replacement pair.
- `OrderDeciderCancellationTests` — 11 tests; `CanBeCancelled` matrix and compensation fan-out.
- `OrderDeciderFraudReviewTests` — 20 tests; `CanBePutOnHold` / `CanBeReleasedFromHold` / `CanBeRejectedForFraud` matrices and the dual `OrderRejectedForFraud` + `OrderCancelled` emit shape.
- `OrderDeciderShippingAddressChangeTests` — 8 tests; `CanChangeShippingAddress` matrix and the in-place address mutation.
- `OrderSagaCrossProductExchangeTests` — 5 tests; the four M45.1 / S3 acknowledger handlers and the M47.0 / S2 no-op refresh of `ExchangePartialRefundIssued`.
- `OrderSagaReturnWindowTests` — 12 tests; `ReturnWindowExpired` scheduling, deferred saga closure with active returns, and the multi-concurrent-return path.
- `CancelOrderValidatorTests` — 6 tests; FluentValidation rules.

## ADRs

- **ADR 0029** — Order Saga Design Decisions. The central ADR for this BC. Captures the four design choices: (1) document-based saga rather than event-sourced, (2) separate `PlaceOrderHandler` for saga initialization rather than a self-starting saga method, (3) Decider pattern with a static `OrderDecider` for pure-function business logic, (4) `CommittedReservationIds: HashSet<Guid>` rather than an `int` counter for idempotency. File: `docs/decisions/0029-order-saga-design-decisions.md`.
- **ADR 0001** — Checkout Migration to Orders BC. Records the Cycle 8 decision to move the checkout aggregate from Shopping to Orders so commercial commitment and orchestration sit in a single bounded context. File: `docs/decisions/0001-checkout-migration-to-orders.md`.
- **ADR 0014** — Checkout Migration to Orders BC, Completion. Closes out the Cycle 19.5 migration; finalizes the Checkout aggregate and the Shopping → Orders cross-BC handoff via `CheckoutInitiated`. File: `docs/decisions/0014-checkout-migration-completion.md`.
- **ADR 0040** — `*Requested` Integration Event Convention. Establishes that command-intent integration messages take past-tense `*Requested` suffixes. Orders BC follows this for `ReservationCommitRequested`, `ReservationReleaseRequested`, `RefundRequested` (Payments-owned), `FulfillmentRequested` (Fulfillment-owned). File: `docs/decisions/0040-requested-integration-event-convention.md`.
- **ADR 0061** — Cross-Product Exchange Replacement Reservation. Names Returns as the orchestrator of the replacement-inventory reservation in the cross-product exchange flow. Orders is an **acknowledger** — the saga keeps `CrossProductExchangeRequested`-tagged returns in `ActiveReturnIds` to prevent premature closure, but does not orchestrate the reservation. File: `docs/decisions/0061-cross-product-exchange-replacement-reservation.md`.
- **ADR 0062** — Cross-Product Exchange Payments Choreography. Establishes the Returns ↔ Payments choreography for the additional-payment capture and partial-refund flows. Orders is an **acknowledger** — the saga's `ExchangePartialRefundIssued` handler is intentionally a no-op because forwarding a `RefundRequested` would double-refund now that Payments handles it directly. File: `docs/decisions/0062-cross-product-exchange-payments-choreography.md`.
- **ADR 0032** — Multi-issuer JWT Strategy. Source for the Backoffice + Vendor authentication scheme registration in `Orders.Api/Program.cs`. File: `docs/decisions/0032-multi-issuer-jwt-strategy.md`.
- **ADR 0002** — EF Core for Customer Identity. Cited from this BC because the Customer Identity address snapshot is the temporal-consistency mechanism that lets Orders capture an immutable shipping address at placement without later re-querying Customer Identity. File: `docs/decisions/0002-ef-core-for-customer-identity.md`.

## Prior event modeling

- `docs/planning/saga-discovery-design-session.md` — a saga-pattern review session (2026-03-29). Treats the Order saga as the canonical reference implementation for Wolverine's saga pattern in CritterSupply, and identifies the cross-product exchange handler gap that M45.1 / S3 closed. The review's enumeration of Order saga capabilities (document-backed state, message correlation by `OrderId`, optimistic concurrency via numeric revisions, delayed timeout via `ReturnWindowExpired`, compensation, idempotency guards, separation between `PlaceOrderHandler` / `Order` / `OrderDecider`) maps 1:1 against the current code. The one descriptive divergence: the planning document treats the four Returns exchange handlers as "missing"; in the current code base they exist as acknowledgers per M45.1 / S3 with the `ExchangePartialRefundIssued` handler additionally refreshed in M47.0 / S2 to a no-op (per ADR 0062). The full cross-product exchange orchestration on the Orders side remains scoped to a future Orders remaster per the source comments in `src/Orders/Orders/Placement/Order.cs#L675-L699`.

## Source citations (S2 full)

- `src/Orders/` (folder root)
- `src/Orders/Orders/Checkout/Checkout.cs`, `CheckoutInitiatedHandler.cs`, `CompleteCheckout.cs`, `CheckoutStarted.cs`, `OrderCreated.cs`, `ProvideShippingAddress.cs`, `SelectShippingMethod.cs`, `ProvidePaymentMethod.cs`, `ShippingAddressProvided.cs`, `ShippingMethodSelected.cs`, `PaymentMethodProvided.cs`, `ShippingAddress.cs`
- `src/Orders/Orders/Placement/Order.cs`, `OrderDecider.cs`, `OrderStatus.cs`, `PlaceOrder.cs`, `PlaceOrderHandler.cs`, `OrderPlaced.cs`, `OrderResponse.cs`, `OrderLineItem.cs`, `CheckoutLineItem.cs`, `CancelOrder.cs`, `ChangeShippingAddress.cs`, `FraudReviewCommands.cs`, `ReturnWindowExpired.cs`, `ShippingAddress.cs`, `AppliedDiscount.cs`
- `src/Orders/Orders.Api/Program.cs`
- `src/Orders/Orders.Api/Checkout/GetCheckoutEndpoint.cs`
- `src/Orders/Orders.Api/Placement/CancelOrderEndpoint.cs`, `ChangeShippingAddressEndpoint.cs`, `GetOrderEndpoint.cs`, `ListOrdersEndpoint.cs`, `SearchOrdersEndpoint.cs`
- `src/Orders/Orders.Api/Returns/GetReturnableItems.cs`
- `src/Shared/Messages.Contracts/Orders/` (full folder: `OrderPlaced`, `OrderCancelled`, `ShippingAddressChanged`, `FraudReviewEvents`, `ReservationCommitRequested`, `ReservationReleaseRequested`, `OrderLineItem`, `ShippingAddress`)
- `src/Shared/Messages.Contracts/Shopping/CartCheckoutCompleted.cs`, `CheckoutInitiated.cs`, `CheckoutLineItem.cs`
- `src/Shared/Messages.Contracts/Payments/PaymentAuthorized.cs`, `PaymentCaptured.cs`, `PaymentFailed.cs`, `RefundCompleted.cs`, `RefundFailed.cs`, `RefundRequested.cs`
- `src/Shared/Messages.Contracts/Inventory/ReservationConfirmed.cs`, `ReservationFailed.cs`, `ReservationCommitted.cs`, `ReservationReleased.cs`
- `src/Shared/Messages.Contracts/Fulfillment/FulfillmentRequested.cs`, `ShipmentHandedToCarrier.cs`, `TrackingNumberAssigned.cs`, `ShipmentDelivered.cs`, `ReturnToSenderInitiated.cs`, `ReshipmentCreated.cs`, `BackorderCreated.cs`, `FulfillmentCancelled.cs`, `OrderSplitIntoShipments.cs`
- `src/Shared/Messages.Contracts/Returns/ReturnRequested.cs`, `ReturnCompleted.cs`, `ReturnDenied.cs`, `ReturnRejected.cs`, `ReturnExpired.cs`, `CrossProductExchangeRequested.cs`, `ExchangeAdditionalPaymentRequired.cs`, `ExchangeAdditionalPaymentCaptured.cs`, `ExchangePartialRefundIssued.cs`
- `src/Shared/Messages.Contracts/CustomerIdentity/AddressSnapshot.cs`
- `src/Shared/Messages.Contracts/Common/SharedShippingAddress.cs`
- `CONTEXTS.md` (section: `Orders`)
- `docs/decisions/0029-order-saga-design-decisions.md`
- `docs/decisions/0001-checkout-migration-to-orders.md`
- `docs/decisions/0014-checkout-migration-completion.md`
- `docs/decisions/0040-requested-integration-event-convention.md`
- `docs/decisions/0061-cross-product-exchange-replacement-reservation.md`
- `docs/decisions/0062-cross-product-exchange-payments-choreography.md`
- `docs/decisions/0032-multi-issuer-jwt-strategy.md`
- `docs/decisions/0002-ef-core-for-customer-identity.md`
- `docs/planning/saga-discovery-design-session.md`
- `docs/features/orders/order-modifications.feature`
- `tests/Orders/Orders.Api.IntegrationTests/Checkout/CheckoutInitiatedHandlerHttpTests.cs`, `CheckoutWorkflowTests.cs`, `GetCheckoutTests.cs`
- `tests/Orders/Orders.Api.IntegrationTests/Placement/OrderPlacementFlowTests.cs`, `CheckoutToOrderIntegrationTests.cs`, `ShoppingIntegrationTests.cs`, `PaymentIntegrationTests.cs`, `InventoryIntegrationTests.cs`, `FulfillmentIntegrationTests.cs`, `FulfillmentMigrationTests.cs`, `CancellationIntegrationTests.cs`, `ListOrdersTests.cs`, `GetReturnableItemsTests.cs`
- `tests/Orders/Orders.UnitTests/Placement/OrderDeciderStartTests.cs`, `OrderDeciderPaymentTests.cs`, `OrderDeciderPaymentIdTests.cs`, `OrderDeciderInventoryTests.cs`, `OrderDeciderFulfillmentTests.cs`, `OrderDeciderCancellationTests.cs`, `OrderDeciderFraudReviewTests.cs`, `OrderDeciderShippingAddressChangeTests.cs`, `OrderSagaCrossProductExchangeTests.cs`, `OrderSagaReturnWindowTests.cs`, `CancelOrderValidatorTests.cs`
