# CritterSupply Bounded Contexts

This document defines the bounded contexts within CritterSupply, an e-commerce reference architecture demonstrating event-driven systems using the Critter Stack (Wolverine, Marten, Alba).

## Shopping

The Shopping context owns the customer's pre-purchase experience—building a cart and converting it to a confirmed order request. Composed of two distinct aggregates: **Cart** (item management) and **Checkout** (order finalization).

### Aggregates

**Cart Aggregate:**

The Cart aggregate manages the customer's shopping session from initialization through checkout handoff. Each cart is an event-sourced stream tracking all item modifications.

**Lifecycle Events (Phase 1):**
- `CartInitialized` — cart created (captures CustomerId or SessionId for anonymous, timestamp)
- `ItemAdded` — item added to cart (SKU, quantity, unit price at add time)
- `ItemRemoved` — item removed from cart
- `ItemQuantityChanged` — item quantity updated (old/new quantity)
- `CartCleared` — all items removed (explicit user action, optional reason)
- `CartAbandoned` — cart expired (anonymous only, system timeout for cleanup/analytics)
- `CheckoutInitiated` — **terminal event**, handoff to Checkout aggregate

**Future Events (Phase 2+):**
- `CouponApplied` / `CouponRemoved` — requires Promotions BC
- `PriceRefreshed` — handles price drift during long sessions, requires Catalog BC
- `PromotionApplied` / `PromotionRemoved` — auto-applied promotions, requires Promotions BC
- `CartAssignedToCustomer` — anonymous cart merged after login, requires Customers BC
- `ShippingEstimateRequested` — zip/postal for shipping preview (may belong in Checkout)

**Checkout Aggregate:**

The Checkout aggregate owns the final steps of order submission—collecting shipping, payment, and completing the transaction. Created when the Cart aggregate emits `CheckoutInitiated`.

**Lifecycle Events:**
- `CheckoutStarted` — checkout stream begins (contains snapshot of cart items from checkout initiation)
- `ShippingAddressProvided` — shipping address collected
- `ShippingMethodSelected` — shipping method chosen
- `PaymentMethodProvided` — payment method token collected
- `CheckoutCompleted` — **terminal event**, publishes `CheckoutCompleted` integration message to Orders

**Cart → Checkout Handoff:**

The Cart aggregate's lifecycle ends with `CheckoutInitiated`, which triggers creation of a new Checkout aggregate stream. The Checkout stream begins with `CheckoutStarted` and contains a snapshot of cart items at checkout initiation time. This ensures **price-at-checkout immutability**—even if cart prices change, the checkout reflects the state when the user clicked "Proceed to Checkout."

```
Cart Stream (terminal states):
  CartInitialized → ... → CheckoutInitiated (happy path)
                        → CartAbandoned (timeout, anonymous only)
                        → CartCleared (explicit user action)

Checkout Stream:
  CheckoutStarted → ... → CheckoutCompleted (terminal)
```

### What it receives

None from other bounded contexts. Shopping initiates the flow based on customer actions.

### What it publishes

- `CheckoutCompleted` — contains cart items (SKU, quantity, price-at-purchase), customer ID, shipping address and method, payment method token, applied discounts

### Core invariants

**Cart Invariants:**
- A cart cannot contain items with zero or negative quantity
- A cart cannot transition to checkout if empty
- Unit prices captured at ItemAdded time (allows price drift detection)
- Anonymous carts expire after defined TTL (CartAbandoned event)
- Authenticated customer carts persist indefinitely (no abandonment)

**Checkout Invariants:**
- Checkout cannot start from an empty cart
- Checkout cannot complete without valid shipping address
- Checkout cannot complete without payment method reference
- Price-at-checkout must be captured at submission time (protects against price changes mid-checkout)
- Checkout items are immutable snapshot from cart at CheckoutInitiated time

### What it doesn't own

- Inventory reservation commitment (soft hold only, Orders commits)
- Payment processing
- Order lifecycle management
- Product catalog or pricing rules (queries Catalog/Pricing)

### Integration Flows

**Cart Lifecycle:**
```
InitializeCart (command)
  └─> InitializeCartHandler
      └─> CartInitialized

AddItemToCart (command)
  └─> AddItemToCartHandler
      └─> ItemAdded

RemoveItemFromCart (command)
  └─> RemoveItemFromCartHandler
      └─> ItemRemoved

ChangeItemQuantity (command)
  └─> ChangeItemQuantityHandler
      └─> ItemQuantityChanged

ClearCart (command)
  └─> ClearCartHandler
      └─> CartCleared (terminal)

[System timeout for anonymous carts]
  └─> CartAbandoned (terminal)

InitiateCheckout (command)
  └─> InitiateCheckoutHandler
      ├─> CheckoutInitiated (terminal, appended to Cart stream)
      └─> Start new Checkout stream with CheckoutStarted
```

**Checkout Lifecycle:**
```
CheckoutStarted (stream begins, contains cart snapshot)

ProvideShippingAddress (command)
  └─> ProvideShippingAddressHandler
      └─> ShippingAddressProvided

SelectShippingMethod (command)
  └─> SelectShippingMethodHandler
      └─> ShippingMethodSelected

ProvidePaymentMethod (command)
  └─> ProvidePaymentMethodHandler
      └─> PaymentMethodProvided

CompleteCheckout (command)
  └─> CompleteCheckoutHandler
      ├─> CheckoutCompleted (terminal)
      └─> Publish CheckoutCompleted → Orders (triggers saga)
```

---

## Orders

The Orders context owns the commercial commitment and coordinates the lifecycle from placement through completion or cancellation. Implemented as a stateful saga to handle the variable complexity and duration of order lifecycles.

### What it receives

- `CheckoutCompleted` from Shopping — triggers saga creation
- `PaymentCaptured` from Payments — payment successful
- `PaymentFailed` from Payments — payment unsuccessful
- `ReservationCommitted` from Inventory — stock allocated
- `ReservationFailed` from Inventory — insufficient stock
- `ShipmentDispatched` from Fulfillment — order shipped
- `ShipmentDelivered` from Fulfillment — order delivered
- `ShipmentDeliveryFailed` from Fulfillment — delivery unsuccessful
- `ReturnApproved` from Returns — return accepted
- `ReturnCompleted` from Returns — return processed, refund eligible
- `ReturnRejected` from Returns — return inspection failed
- `RefundCompleted` from Payments — refund processed
- `RefundFailed` from Payments — refund unsuccessful

### Saga states

- Placed — order created, awaiting payment and inventory confirmation
- PendingPayment — awaiting async payment confirmation
- PaymentConfirmed — funds captured successfully
- PaymentFailed — payment declined (terminal or retry branch)
- OnHold — flagged for fraud review or inventory issues
- Fulfilling — handed off to Fulfillment BC
- Shipped — integration event from Fulfillment
- Delivered — integration event from Fulfillment
- Cancelled — compensation triggered (release inventory, refund if paid)
- ReturnRequested — customer initiated return
- Closed — terminal state after delivery window passes or return resolved

### What it publishes

- `OrderPlaced` — Inventory and Payments react
- `PaymentRequested` — Payments processes capture
- `ReservationCommitRequested` — Inventory confirms hard allocation
- `ReservationReleaseRequested` — cancellation or failure compensation
- `FulfillmentRequested` — Fulfillment begins processing
- `RefundRequested` — Payments processes refund
- `OrderCancelled` — triggers compensation across contexts
- `OrderCompleted` — terminal success state reached

### Core invariants

- An order cannot be placed without committed inventory reservation
- An order cannot proceed to fulfillment without confirmed payment
- An order cannot be cancelled after shipment has been dispatched
- A refund cannot exceed the original captured payment amount
- State transitions must follow valid paths (no jumping from Placed to Delivered)

### What it doesn't own

- Payment gateway integration (Payments)
- Stock level management (Inventory)
- Physical fulfillment operations (Fulfillment)
- Return eligibility rules or inspection (Returns)
- Customer notification delivery (Notifications)

### Integration Flows

**Saga Coordination: Order Lifecycle**
```
CheckoutCompleted (from Shopping)
  └─> Order.Start() [Saga Created]
      └─> OrderPlaced → Inventory + Payments

[Inventory responds]
ReservationConfirmed (from Inventory)
  └─> Order.Handle() → Status: InventoryReserved

ReservationFailed (from Inventory)
  └─> Order.Handle() → Status: InventoryFailed

[Payments responds]
PaymentCaptured (from Payments)
  └─> Order.Handle() → Status: PaymentConfirmed

PaymentFailed (from Payments)
  └─> Order.Handle() → Status: PaymentFailed

PaymentAuthorized (from Payments)
  └─> Order.Handle() → Status: PendingPayment

[Inventory commitment]
ReservationCommitted (from Inventory)
  └─> Order.Handle() → Status: InventoryCommitted
      └─> [Future: Trigger FulfillmentRequested]

[Compensation flows]
ReservationReleased (from Inventory)
  └─> Order.Handle() → [No status change, compensation tracking]

RefundCompleted (from Payments)
  └─> Order.Handle() → [No status change, financial tracking]

RefundFailed (from Payments)
  └─> Order.Handle() → [No status change, failure tracking]
```

---

## Payments

The Payments context owns the financial transaction lifecycle—capturing funds, handling failures, and processing refunds. It knows how to talk to payment providers but doesn't know why a payment is happening.

### What it receives

- `PaymentRequested` from Orders — amount, currency, payment method token, order reference
- `RefundRequested` from Orders — amount, original transaction reference

### Internal lifecycle

**Payment:**

- Pending — request received, awaiting provider response
- Authorized — funds held but not captured (if using auth/capture split)
- Captured — funds successfully collected
- Failed — declined, insufficient funds, fraud block, etc.

**Refund:**

- RefundPending — refund request received
- RefundCompleted — funds returned
- RefundFailed — refund unsuccessful

### What it publishes

- `PaymentAuthorized` — funds held (if using auth/capture flow)
- `PaymentCaptured` — funds secured, Orders can proceed
- `PaymentFailed` — includes reason code, Orders decides retry or cancel
- `RefundCompleted` — funds returned
- `RefundFailed` — may need manual intervention

### Core invariants

- A payment cannot be captured without a valid authorization (if using auth/capture)
- A refund cannot exceed the original captured amount
- A payment can only be in one terminal state (Captured, Failed, or Refunded)
- Payment method tokens must be valid and unexpired at capture time

### What it doesn't own

- Deciding whether to retry a failed payment (Orders saga logic)
- Knowing what the payment is for beyond a reference ID
- Customer payment method storage (Customers or Wallet context)
- Refund eligibility determination (Orders/Returns)

### Integration Flows

**Choreography: Payment Processing**
```
PaymentRequested (from Orders)
  └─> ProcessPaymentHandler
      ├─> PaymentCaptured → Orders
      ├─> PaymentFailed → Orders
      └─> PaymentAuthorized → Orders (two-phase flow)

CapturePayment (from Orders, after authorization)
  └─> CapturePaymentHandler
      ├─> PaymentCaptured → Orders
      └─> PaymentFailed → Orders

RefundRequested (from Orders)
  └─> RefundPaymentHandler
      ├─> RefundCompleted → Orders
      └─> RefundFailed → Orders
```

---

## Inventory

The Inventory context owns stock levels and availability. It answers "do we have it?" and manages the reservation flow that prevents overselling. Stock is tracked per warehouse/fulfillment center.

### What it receives

- `OrderPlaced` from Orders — triggers inventory reservation (choreography pattern)
- `ReservationCommitRequested` from Orders — convert soft hold to committed allocation
- `ReservationReleaseRequested` from Orders — cancellation or payment failure
- `InventoryReceived` from warehouse/purchasing systems — replenishment
- `ReturnCompleted` (restockable) from Returns — items to restock

### Internal lifecycle (per reservation)

- Reserved — stock earmarked (soft hold), not yet committed
- Committed — order confirmed, stock allocated for fulfillment
- Released — reservation cancelled, stock returned to available pool

### What it publishes

- `ReservationConfirmed` — stock successfully held
- `ReservationFailed` — insufficient stock
- `ReservationCommitted` — hard allocation complete
- `ReservationReleased` — stock back in pool
- `InventoryLow` — alerting or reorder triggers
- `InventoryAvailabilityChanged` — Catalog/Shopping can update displayed availability

### Core invariants

- Available stock cannot go negative at any warehouse
- A reservation cannot exceed available stock at the target warehouse
- A committed reservation cannot be released (only cancellation via Orders can trigger release before commit)
- Stock levels must be tracked per warehouse, not just globally
- Soft holds expire after a defined window if not committed

### What it doesn't own

- Pricing or product details (Catalog)
- Deciding what to do when stock is insufficient (Orders/Shopping handles UX)
- Physical warehouse location or bin assignments (Fulfillment)
- Purchasing or reorder decisions (Procurement/Supply Chain context)
- Forecasting and demand planning (future consideration)

### Integration Flows

**Choreography: Order Placement → Inventory Reservation**
```
OrderPlaced (from Orders)
  └─> OrderPlacedHandler
      └─> ReserveStock (internal command)
          └─> ReserveStockHandler
              ├─> ReservationConfirmed → Orders
              └─> ReservationFailed → Orders

ReservationCommitRequested (from Orders)
  └─> CommitReservationHandler
      └─> ReservationCommitted → Orders

ReservationReleaseRequested (from Orders)
  └─> ReleaseReservationHandler
      └─> ReservationReleased → Orders
```

---

## Fulfillment

The Fulfillment context owns the physical execution of getting items from warehouse to customer. It takes over once Orders has secured payment and committed inventory.

### What it receives

- `FulfillmentRequested` from Orders — order reference, line items, committed inventory allocations, shipping address, shipping method
- `ReturnShipmentReceived` from carrier integration — items arriving back at warehouse

### Internal lifecycle

- Pending — fulfillment request received, awaiting assignment
- Assigned — routed to a specific warehouse/FC
- Picking — items being pulled from bins
- Packing — items boxed, shipping label generated
- Shipped — handed to carrier, tracking number assigned
- InTransit — carrier updates (optional granularity)
- OutForDelivery — final mile
- Delivered — carrier confirmed delivery
- DeliveryFailed — attempted but unsuccessful

### What it publishes

- `ShipmentAssigned` — includes which FC is handling it
- `ShipmentPacked` — ready for carrier pickup
- `ShipmentDispatched` — tracking number available
- `ShipmentInTransit` — optional, for detailed tracking
- `ShipmentOutForDelivery` — optional
- `ShipmentDelivered` — delivery confirmed
- `ShipmentDeliveryFailed` — delivery unsuccessful

### Core invariants

- A shipment cannot be created without committed inventory allocation
- A shipment cannot be assigned to a warehouse without sufficient committed stock at that location
- Tracking number must exist before marking as Shipped
- Delivery confirmation requires carrier verification
- A shipment can only be delivered once

### What it doesn't own

- Inventory levels (consumes committed allocations from Inventory)
- Payment status or order validity (Orders has already confirmed)
- Carrier contract negotiation or rate shopping (Shipping/Logistics context)
- Return eligibility or refund decisions (Returns and Orders)
- Customer communication (Notifications)

### Notes

Warehouse/FC selection uses routing logic to select the optimal location—nearest to shipping address with available committed stock. More sophisticated rules can be added later.

---

## Returns

The Returns context owns the reverse logistics flow—handling customer return requests, validating eligibility, receiving items back, and determining disposition. It picks up after delivery when a customer wants to send something back.

### What it receives

- `ReturnInitiated` from customer-facing UI — order reference, line items to return, reason
- `ShipmentDelivered` from Fulfillment (via Orders) — establishes return eligibility window
- `ReturnShipmentReceived` from Fulfillment or warehouse systems — physical items arrived

### Internal lifecycle

- Requested — customer initiated, awaiting validation
- Approved — eligible for return, return label generated
- Denied — outside window, non-returnable item, etc.
- InTransit — customer shipped it back
- Received — items arrived at warehouse/FC
- Inspecting — verifying condition
- Completed — inspection passed, ready for refund/exchange (includes restockable disposition)
- Rejected — inspection failed (damaged, wrong item, etc.)

### What it publishes

- `ReturnApproved` — includes return label, instructions
- `ReturnDenied` — reason code
- `ReturnReceived` — items physically at FC
- `ReturnCompleted` — inspection passed, includes restockable flag for Inventory
- `ReturnRejected` — inspection failed

### Core invariants

- A return cannot be approved outside the eligibility window
- A return cannot be approved for non-returnable items
- A return cannot be marked completed without physical receipt and inspection
- A return cannot be processed for an order that was never delivered
- Restockable disposition requires inspection completion

### What it doesn't own

- Refund processing (Payments, triggered by Orders)
- Deciding refund amount or restocking fees (Orders or policy service)
- Inventory restocking (publishes completion with disposition, Inventory reacts)
- Customer communication (Notifications)
- Original order state management (Orders saga tracks return status)

---

## Future Considerations

The following contexts are acknowledged but not yet defined:

- **Catalog** — product information, categorization, search
- **Pricing** — price rules, promotions, discounts
- **Customers** — customer profiles, addresses, payment methods
- **Notifications** — email, SMS, push notifications
- **Procurement/Supply Chain** — purchasing, vendor management, forecasting
- **Shipping/Logistics** — carrier management, rate shopping
