# CritterSupply Bounded Contexts

This document defines the bounded contexts within CritterSupply, an e-commerce reference architecture demonstrating event-driven systems using the Critter Stack (Wolverine, Marten, Alba).

## Important Note: Checkout Migration (Cycle 8)

**Checkout aggregate was migrated from Shopping BC to Orders BC** to establish clearer bounded context boundaries:
- **Shopping BC** now focuses on pre-purchase exploration (cart management, future: product browsing, wishlists)
- **Orders BC** owns the transactional commitment phase (checkout + order lifecycle)
- Integration: Shopping publishes `CheckoutInitiated` → Orders handles and creates Checkout aggregate

This architectural decision ensures each BC has a well-defined purpose and reduces cognitive load for developers working in either context.

## Shopping (Folder: Shopping Management)

The Shopping context owns the customer's pre-purchase experience—managing the cart lifecycle from initialization to checkout handoff. This BC focuses on the exploratory phase of shopping, before the customer commits to purchase. **Checkout was migrated to Orders BC in Cycle 8** to establish clearer boundaries: Shopping focuses on exploration (adding/removing items, building cart), while Orders owns transactional commitment (checkout → order placement).

**Naming Note:** The folder is currently `Shopping Management/` but the BC is conceptually "Shopping" (simpler, allows future expansion to wishlists/product browsing). See `docs/BC-NAMING-ANALYSIS.md` for naming rationale.

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
- `CartAssignedToCustomer` — anonymous cart merged after login, requires Customer Identity BC
- `ShippingEstimateRequested` — zip/postal for shipping preview (may belong in Checkout)

**Cart → Orders Handoff:**

The Cart aggregate's lifecycle ends with `CheckoutInitiated`, which triggers the handoff to Orders BC. Shopping BC publishes `CheckoutInitiated` integration message, which Orders BC handles to start the Checkout aggregate. This ensures **price-at-checkout immutability**—even if cart prices change, the checkout reflects the state when the user clicked "Proceed to Checkout."

```
Cart Stream (terminal states):
  CartInitialized → ... → CheckoutInitiated (happy path, integration message published)
                        → CartAbandoned (timeout, anonymous only)
                        → CartCleared (explicit user action)

[Orders BC handles CheckoutInitiated → creates Checkout aggregate]
```

### What it receives

None from other bounded contexts. Shopping initiates the flow based on customer actions.

### What it publishes

- `CheckoutInitiated` — signals cart is ready for checkout; Orders BC handles to create Checkout aggregate

### Core Invariants

**Cart Invariants:**
- A cart cannot contain items with zero or negative quantity
- A cart cannot transition to checkout if empty
- Unit prices captured at ItemAdded time (allows price drift detection)
- Anonymous carts expire after defined TTL (CartAbandoned event)
- Authenticated customer carts persist indefinitely (no abandonment)


### What it doesn't own

- Checkout process (moved to Orders BC in Cycle 8)
- Payment processing
- Order lifecycle management
- Inventory reservation or commitment
- Product catalog or pricing rules (queries Catalog/Pricing in future phases)

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
      └─> Publish CheckoutInitiated → Orders BC (triggers Checkout aggregate creation)
```

---

## Orders (Folder: Order Management)

The Orders context owns the commercial commitment and **orchestrates** the order lifecycle across Payments, Inventory, and Fulfillment using a stateful saga. It coordinates multi-step workflows from checkout through delivery or cancellation, ensuring eventual consistency across bounded contexts. This BC contains two key aggregates: **Checkout** (order finalization) and **Order** (order lifecycle saga). Checkout was migrated from Shopping BC in Cycle 8 to establish clearer bounded context boundaries—Shopping focuses on exploration, Orders focuses on transaction commitment. **Cycle 19.5 completed this migration** by implementing the integration handler (`CheckoutInitiatedHandler`) and RabbitMQ routing, and removing the now-obsolete `Shopping.Checkout` aggregate.

**Naming Note:** The folder is currently `Order Management/` but the BC is conceptually "Orders" (simpler, industry standard). See `docs/BC-NAMING-ANALYSIS.md` for naming rationale.

### Aggregates

**Checkout Aggregate:**

The Checkout aggregate owns the final steps of order submission—collecting shipping address, payment method, and completing the transaction. Created when Shopping BC publishes `CheckoutInitiated` integration message.

**Lifecycle Events:**
- `CheckoutStarted` — checkout stream begins (contains snapshot of cart items from checkout initiation)
- `ShippingAddressSelected` — customer selects saved address from Customer Identity BC (stores AddressId)
- `ShippingMethodSelected` — shipping method chosen
- `PaymentMethodProvided` — payment method token collected
- `CheckoutCompleted` — **terminal event**, publishes `CheckoutCompleted` integration message to start Order saga

**Address Handling (Cycle 11):**
- Checkout stores selected `AddressId` via `ShippingAddressSelected` event (not inline address fields)
- When completing checkout, Orders BC queries Customer Identity BC for immutable `AddressSnapshot`
- `CheckoutCompleted` integration message embeds the `AddressSnapshot` for Order saga
- This ensures temporal consistency - orders preserve address as it existed at checkout time

**Order Saga:**

The Order saga coordinates the order lifecycle across multiple bounded contexts (Payments, Inventory, Fulfillment). Implemented using Wolverine's saga pattern with the Decider pattern for pure business logic.

**Architecture (Cycle 9):**
- `OrderDecider` — pure functions for business logic (no side effects, easily testable)
- `Order` saga class — thin wrappers that delegate to Decider, handles Wolverine conventions
- Single entry point: `Order.Start(Shopping.CheckoutCompleted)` — maps integration message to domain command

**Saga States:**
See "Saga states" section below for complete state machine.

### What it receives

- `CheckoutInitiated` from Shopping — triggers Checkout aggregate creation
- `CheckoutCompleted` (internal) — triggers Order saga creation
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


### What it doesn't own

- Payment gateway integration (Payments)
- Stock level management (Inventory)
- Physical fulfillment operations (Fulfillment)
- Return eligibility rules or inspection (Returns)
- Customer notification delivery (Notifications)

### Core Invariants

**Checkout Invariants:**
- Checkout cannot start from an empty cart
- Checkout cannot complete without valid shipping address
- Checkout cannot complete without payment method reference
- Price-at-checkout must be captured at submission time (protects against price changes mid-checkout)
- Checkout items are immutable snapshot from cart at CheckoutInitiated time

**Order Saga Invariants:**
- An order cannot be placed without all checkout prerequisites (address, payment method)
- An order cannot proceed to fulfillment without confirmed payment
- An order cannot be cancelled after shipment has been dispatched
- A refund cannot exceed the original captured payment amount
- State transitions must follow valid paths (no jumping from Placed to Delivered)

### Integration Flows

**Checkout Lifecycle:**
```
CheckoutInitiated (from Shopping)
  └─> CheckoutInitiatedHandler (Orders BC)
      └─> CheckoutStarted (new Checkout stream)

SelectShippingAddress (command)
  └─> SelectShippingAddressHandler
      └─> ShippingAddressSelected

SelectShippingMethod (command)
  └─> SelectShippingMethodHandler
      └─> ShippingMethodSelected

ProvidePaymentMethod (command)
  └─> ProvidePaymentMethodHandler
      └─> PaymentMethodProvided

CompleteCheckout (command)
  └─> CompleteCheckoutHandler
      ├─> Queries Customer Identity BC for AddressSnapshot
      ├─> CheckoutCompleted (terminal)
      └─> Publish CheckoutCompleted → Order.Start() (internal)
```

**Order Saga Coordination:**
```
CheckoutCompleted (internal from Checkout aggregate)
  └─> Order.Start() [Saga Created via OrderDecider.Start()]
      ├─> Maps Shopping.CheckoutCompleted → PlaceOrder command
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

## Payments (Folder: Payment Processing)

The Payments context owns the financial transaction lifecycle—capturing funds, handling failures, and processing refunds. It knows how to talk to payment providers (Stripe, PayPal, etc.) but doesn't know why a payment is happening or make business decisions about retries.

**Naming Note:** The folder is currently `Payment Processing/` but the BC is conceptually "Payments" (simpler, industry standard like Stripe Payments API). See `docs/BC-NAMING-ANALYSIS.md` for naming rationale.

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

### Core Invariants

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

## Inventory (Folder: Inventory Management)

The Inventory context owns stock levels and availability per warehouse. It implements a **two-phase reservation pattern** (soft holds → committed allocations) to prevent overselling while supporting cancellations and payment failures. Stock is never decremented until a reservation is committed. This BC answers "do we have it?" and ensures no overselling through carefully managed reservation workflows.

**Naming Note:** The folder is currently `Inventory Management/` but the BC is conceptually "Inventory" (simpler, though "Management" is more justified here due to reservation complexity). See `docs/BC-NAMING-ANALYSIS.md` for naming rationale.

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

### Core Invariants

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

## Fulfillment (Folder: Fulfillment Management)

The Fulfillment context owns the physical execution of getting items from warehouse to customer—picking, packing, shipping, and delivery tracking. It takes over once Orders has secured payment and committed inventory. This BC integrates with carriers for tracking numbers and manages warehouse/FC routing logic through the **Order Routing Engine (ORE)**.

**Naming Note:** The folder is currently `Fulfillment Management/` but the BC is conceptually "Fulfillment" (simpler, industry standard). See `docs/BC-NAMING-ANALYSIS.md` for naming rationale.

**Full Workflow Reference:** See [`docs/fulfillment/FULFILLMENT-BUSINESS-WORKFLOWS.md`](docs/fulfillment/FULFILLMENT-BUSINESS-WORKFLOWS.md) for the comprehensive realistic workflow documentation including multi-warehouse routing, international flows, sad paths, and compensation events.

### Fulfillment Center Network

| FC | Location | Ownership | Timezone | Notes |
|----|----------|-----------|----------|-------|
| NJ FC | Newark, NJ | CritterSupply-owned | Eastern | Drop-and-hook; East Coast primary |
| OH FC | Columbus, OH | CritterSupply-owned | Eastern | Drop-and-hook; Midwest primary; returns capable |
| WA FC | Kent, WA | CritterSupply-owned | Pacific | No USPS commercial pickup; no returns processing |
| TX FC | Dallas, TX | Partially 3PL | Central | 3PL overflow; hazmat-certified; 8–24hr SLA |
| Toronto Hub | Toronto, ON | CritterSupply-owned | Eastern | Canada-only; USMCA corridor |
| Birmingham Hub | Birmingham, UK | CritterSupply-owned | GMT/BST | UK commercial import hub; domestic UK distribution |

### What it receives

- `FulfillmentRequested` from Orders — order reference, line items, committed inventory allocations, shipping address, shipping method
- Carrier webhook events — tracking updates (in transit, out for delivery, delivered, delivery failed, customs hold) translated to domain events at the integration boundary

### Internal lifecycle

The full Shipment lifecycle from work order to delivery includes intermediate states and exception branches:

**Happy Path States:**
- `WorkOrderCreated` — fulfillment request translated to work order for WMS
- `WaveReleased` — WMS batches work orders into an optimized pick wave
- `Picking` — picker assigned; items being pulled from bin locations (RF scanner / voice-directed)
- `PickCompleted` — all items picked and accounted for
- `Packing` — items at pack station; scan-verify-pack (SVP) in progress
- `PackCompleted` — carton sealed and weighed
- `Labeled` — rate shopping complete; shipping label generated; tracking number assigned (first availability)
- `Manifested` — shipment tendered to carrier system
- `CustomsDocsPrepared` — commercial invoice and HS codes prepared (international orders only)
- `Staged` — carton at outbound dock, organized by carrier pickup window
- `HandedToCarrier` — driver scans manifest; physical custody transfers to carrier
- `InTransit` — carrier facility first scan received
- `OutForDelivery` — package loaded on local delivery vehicle
- `Delivered` — carrier confirms delivery (terminal)

**Exception States (branches from happy path):**
- `ShortPickDetected` — bin empty or quantity insufficient; triggers alternative bin check or emergency re-route
- `Backordered` — no stock at any FC; customer notified; item held for replenishment
- `PackDiscrepancyDetected` — weight mismatch, damaged item, or no valid carton size; supervisor review required
- `GhostShipmentDetected` — no carrier scan 24+ hours after handoff; investigation opened
- `LostInTransit` — 5+ business days without carrier scan; carrier trace opened; reship dispatched immediately
- `CarrierPickupMissed` — driver no-show or volume cap; escalation to carrier relations
- `CustomsHeld` — package held by customs authority (international only); 2–15 business day resolution window
- `DeliveryAttemptFailed` — carrier delivery exception (no one home, access blocked, etc.); 1–3 attempts before RTS
- `ReturnToSender` — maximum delivery attempts exhausted; package returning to FC

### Order Routing Engine (ORE)

The ORE evaluates FCs in strict priority order. **Inventory is reserved at order confirmation time, not at pick time.**

1. **Tier 1 — Inventory availability** (hard constraint): FC must have sufficient available = on-hand minus already-reserved
2. **Tier 2 — Geographic proximity / shipping zone**: Minimize UPS/FedEx zone for cost and speed
3. **Tier 3 — Carrier service compatibility**: WA FC has no USPS commercial pickup; APO/FPO → USPS only
4. **Tier 4 — Throughput capacity**: 85% utilization triggers overflow to next-eligible FC
5. **Tier 5 — Split eligibility**: If no single FC can fulfill complete order, evaluate multi-FC split
6. **Tier 6 — 3PL overflow**: TX 3PL as last resort (higher cost, 8–24hr SLA)

**International routing is fixed:**
- Canada → always Toronto Hub (no direct US-to-Canada shipping)
- UK → always Birmingham Hub (bulk commercial import simplifies per-order customs)

### What it publishes

**Integration events (published to message bus):**
- `TrackingNumberAssigned` — first moment a tracking number exists; published to Orders BC and Customer Experience BC
- `ShipmentHandedToCarrier` — physical custody transferred; Orders BC updates saga state
- `ShipmentRerouted` — emergency re-route to alternate FC (transparent to customer)
- `BackorderCreated` — no stock at any FC; customer notification triggered
- `OrderSplitIntoShipments` — multi-FC split; separate tracking group per shipment
- `ShipmentInTransit` — carrier facility first scan (carrier-reported, translated)
- `ShipmentDelivered` — carrier confirms delivery; Orders saga transitions to Delivered
- `ShipmentDeliveryFailed` — delivery unsuccessful after maximum attempts; Orders saga notified
- `ShipmentLostInTransit` — 5+ business days no scan; reship dispatched
- `ReshipmentCreated` — new shipment stream opened for a replacement order

### Core Invariants

- A shipment cannot be created without committed inventory allocation
- A shipment cannot be assigned to a warehouse without sufficient available stock at that location
- **Tracking number does not exist until label generation** — it is NOT created at order placement
- A shipment cannot be marked Shipped before a tracking number is assigned
- Delivery confirmation requires carrier verification event
- A shipment can only be delivered once
- Hazmat items (ORM-D/Limited Quantity) cannot use air shipping services regardless of customer selection
- International orders cannot be routed to domestic US FCs (customs complexity)
- The WA FC cannot be selected when the order requires USPS service

### Pick/Pack SLAs

| Order Type | SLA | Notes |
|------------|-----|-------|
| Standard | 4 business hours | Measured against FC operating hours (6 AM–10 PM local) |
| Expedited | 2 business hours | Jumps to front of pick queue |
| Hazmat | 6 business hours | Extra labeling, ORM-D marking, declaration required |
| 3PL (TX) | 8–24 business hours | Contractual SLA with 3PL partner |
| Temperature-sensitive | 4 business hours | Cold pack confirmation required at pack station |

### Order Cutoff Times

| FC | Cutoff | Timezone |
|----|--------|----------|
| NJ FC | 2:00 PM | Eastern |
| OH FC | 1:00 PM | Eastern |
| WA FC | 12:00 PM | Pacific |
| TX FC | 1:00 PM | Central |
| Toronto Hub | 11:00 AM | Eastern |
| Birmingham Hub | 10:00 AM | GMT/BST |

### What it doesn't own

- Inventory levels (consumes committed allocations from Inventory BC)
- Payment status or order validity (Orders BC has already confirmed)
- Carrier contract negotiation or rate shopping decisions (informed by Logistics/Carrier config)
- Return eligibility or refund decisions (Returns BC and Orders BC)
- Customer communication content (Notifications layer)
- Duty and tax calculation for international orders (landed cost engine at checkout in Orders/Pricing)

### Integration Flows

**Choreography: Fulfillment Processing (Domestic)**
```
FulfillmentRequested (from Orders)
  └─> FulfillmentRequestedHandler
      └─> WorkOrderCreated (WMS notified)
          └─> [WMS: WaveReleased → PickListAssigned → PickStarted → ItemPicked]
              └─> PickCompleted
                  └─> [WMS: PackingStarted → ItemVerifiedAtPack]
                      └─> PackingCompleted
                          └─> [Rate shop → ShippingLabelGenerated]
                              └─> TrackingNumberAssigned → Orders, Customer Experience (integration)
                                  └─> ShipmentManifested
                                      └─> PackageStagedForPickup
                                          └─> CarrierPickupConfirmed
                                              └─> ShipmentHandedToCarrier → Orders (integration)

[Carrier webhook events — translated at integration boundary]
  └─> ShipmentInTransit → Customer Experience (integration)
  └─> OutForDelivery → Customer Experience (integration)
  └─> ShipmentDelivered → Orders (integration)
  └─> DeliveryAttemptFailed → [1–3 attempts → ReturnToSenderInitiated]
```

**Exception Path: Short Pick**
```
ShortPickDetected
  └─> [Alternative bin found?]
      ├─> Yes → PickResumed → continues normally
      └─> No → [Other FC has stock?]
              ├─> Yes → ShipmentRerouted (integration) → WorkOrderCreated at new FC
              └─> No → BackorderCreated (integration) → CustomerBackorderNotificationSent
```

**Exception Path: Lost in Transit**
```
ShipmentLostInTransit (5+ business days no scan)
  └─> CarrierTraceOpened
  └─> ReshipmentCreated (integration) → immediate reship
  └─> [Carrier trace completes]
      ├─> ShipmentRecovered (carrier locates package; customer may receive both)
      └─> CarrierClaimFiled → CarrierClaimSettled
```

**Exception Path: Delivery Failure**
```
DeliveryAttemptFailed (attempt 1 or 2)
  └─> Customer notified with redelivery options
  └─> Carrier retries next business day

DeliveryAttemptFailed (attempt 3 — final)
  └─> ReturnToSenderInitiated (integration) → Orders BC notified
  └─> [Package returns to FC in 5–15 business days]
      └─> ReturnReceived
          └─> [Customer choice: reship to corrected address or refund]
              ├─> ReshipmentCreated (integration)
              └─> RefundIssued (via Payments BC)
```

**Exception Path: Compensation — Carrier Intercept**
```
CarrierInterceptRequested ($15–20 fee)
  └─> CarrierInterceptConfirmed → package redirected
  └─> CarrierInterceptFailed
      └─> VoluntaryRecallInitiated (integration)
          └─> RecallReturnReceived
              └─> ReshipmentCreated OR RefundIssued
```

**International Path: Canada → Toronto Hub**
```
FulfillmentRequested (Canada destination)
  └─> OrderRoutingEngine → ShipmentAssigned (TORONTO-HUB)
      └─> [Pick → Pack → Label] at Toronto Hub
          └─> CustomsDocumentationPrepared
              └─> USMCACertificateOfOriginIssued (if US-origin goods, value < CAD $150)
                  └─> PackageStagedForPickup (DHL / FedEx International)
                      └─> ShipmentHandedToCarrier → Orders (integration)
                          └─> [DHL transit → CBSA customs clearance]
                              └─> CustomsHoldInitiated (if held — carrier-reported)
                              └─> ShipmentDelivered → Orders (integration)
```

### Notes

- **Carrier-reported events are translated at the integration boundary** — the Customer Experience BC must never consume raw carrier data directly. Fulfillment BC owns the translation layer.
- **SLA is in business hours** against FC operating hours (6 AM–10 PM local time, two shifts during peak). An order placed at 9 PM starts its SLA clock at 6 AM the following morning.
- **Split shipments** each have their own Shipment stream and tracking number. The Orders BC and Customer Experience BC must handle N shipments per order ID.
- **Hazmat classification** is maintained in the Product Catalog SKU master. The ORE checks hazmat status at routing time; the WMS enforces it again at pack station (secondary check).
- **3PL partners must hold hazmat certification** to handle ORM-D/Limited Quantity items. TX 3PL is hazmat-certified; other potential 3PL partners may not be.

---

## Returns

The Returns context owns the reverse logistics flow—handling customer return requests, validating eligibility, receiving items back, and determining disposition. It picks up after delivery when a customer wants to send something back.

> **Domain Specification:** See [`docs/returns/RETURNS-BC-SPEC.md`](docs/returns/RETURNS-BC-SPEC.md) for the comprehensive domain specification including requirements, risks, use cases, and full event/integration contracts.

### What it receives

- `Fulfillment.ShipmentDelivered` — establishes return eligibility window; triggers one-time Orders HTTP query to snapshot eligible line items
- `Fulfillment.ReturnShipmentInTransit` — carrier tracking updates for inbound return shipments

### Internal lifecycle

- Requested — customer initiated, awaiting validation
- Approved — eligible for return, return label generated; 30-day ship-by deadline scheduled
- Denied — outside window, non-returnable item, etc. (terminal)
- LabelGenerated — return shipping label created and provided to customer
- InTransit — carrier scan received; package on its way back
- Received — items arrived at warehouse/FC
- Inspecting — verifying condition and determining disposition
- Completed — inspection passed; `ReturnCompleted` published (terminal)
- Rejected — inspection failed; disposition applied (terminal)
- Expired — customer never shipped within approval window (terminal)

### What it publishes

- `ReturnRequested` — return request submitted; Customer Experience BC updates UI
- `ReturnApproved` — return authorized; includes return label, ship-by deadline
- `ReturnDenied` — request rejected; reason code included
- `ReturnExpired` — approval window closed; customer never shipped
- `ReturnCompleted` — inspection passed; **carries full item disposition** (SKU, qty, IsRestockable, warehouse, condition) for Orders BC (refund) and Inventory BC (restocking)
- `ReturnRejected` — inspection failed; disposition applied (Dispose, ReturnToCustomer, Quarantine)

### Core Invariants

- A return cannot be approved outside the 30-day eligibility window (established from `ShipmentDelivered`)
- A return cannot be approved for non-returnable items (personalized, opened consumables, final sale)
- A return cannot transition to Received without prior approval
- A return cannot be marked Completed without physical receipt and passed inspection
- A return cannot be processed for an order that has no `ReturnEligibilityWindow` record
- Restockable disposition requires inspection completion

### What it doesn't own

- Refund processing (Payments, orchestrated by Orders — Orders holds the PaymentId)
- Refund amount calculation disputes (Orders or policy service)
- Inventory restocking execution (Inventory BC reacts to `ReturnCompleted`)
- Customer communication (Notifications BC reacts to Returns integration events)
- Original order state management (Orders saga tracks return status)
- Carrier API integration (Fulfillment BC owns all carrier interactions)
- Store credit ledger (future dedicated BC)

---

## Customer Identity

The Customer Identity context owns customer identity, profiles, and persistent data like addresses and saved payment method tokens. It provides the foundation for personalized shopping experiences by maintaining customer preferences and frequently-used information across the system.

**Persistence Strategy (Cycle 13):**

Customer Identity uses **Entity Framework Core** with PostgreSQL for traditional relational persistence. This differs from other BCs (Orders, Payments, Inventory, Fulfillment) which use Marten for event sourcing.

**Why EF Core instead of Marten:**
- **Relational Model Fits Naturally** - Customer/Address is a classic one-to-many relationship with foreign keys
- **No Event Sourcing Needed** - Current state is all that matters (historical changes not valuable)
- **Navigation Properties** - `Customer.Addresses` collection simplifies queries
- **Database Constraints** - Foreign keys and unique constraints enforce invariants at database level
- **Pedagogical Value** - Demonstrates Wolverine works with existing EF Core codebases (not just Marten)
- **Entry Point for Learning** - Traditional DDD patterns (aggregate roots, entities) before introducing event sourcing

**Aggregate Root (Traditional DDD):**
- `Customer` — aggregate root with navigation property to `CustomerAddress` entities
- `CustomerAddress` — entity with foreign key to `Customer`
- Relationships enforced via EF Core foreign key constraints (cascade delete)
- EF Core migrations provide versioned schema evolution

### Subdomains

**AddressBook:**

Manages customer shipping and billing addresses with support for multiple saved addresses, nicknames, defaults, and address verification. Provides both the master address records (for CRUD operations) and immutable snapshots (for order/shipment records).

**Entities:**
- `CustomerAddress` — persisted address with metadata (nickname, type, default status, last used timestamp, verification status)
- `AddressSnapshot` — immutable point-in-time copy for integration messages
- `CorrectedAddress` — address with corrections from verification service (part of `AddressVerificationResult`)
- `AddressType` — enum (Shipping, Billing, Both)
- `VerificationStatus` — enum (Unverified, Verified, Corrected, Invalid, PartiallyValid)

**Address Verification (Cycle 12):**
- Addresses are automatically verified against postal service databases when added or updated
- `IAddressVerificationService` interface allows pluggable verification providers (SmartyStreets, Google Address Validation, etc.)
- Development uses `StubAddressVerificationService` (always returns verified status)
- Verification results include suggested corrections and confidence scores
- If verification service is unavailable, addresses are saved as unverified (doesn't block customer flow)
- `IsVerified` boolean tracks whether address has been validated

**Profile** (future):
- Customer personal info (name, email, phone)
- Authentication/identity integration
- Preferences and settings

**PaymentMethods** (future):
- Saved payment method tokens (Stripe, PayPal, etc.)
- Default payment method selection
- PCI-compliant token storage (not actual card data)

### What it receives

- `OrderPlaced` — from Orders BC, updates `LastUsedAt` timestamp on addresses used in order (future - Cycle 12+)

### What it publishes

None. Customer Identity BC is primarily query-driven (other BCs query for address data).

**HTTP Endpoints (Added in Cycle 17):**

**Customer CRUD:**
- `POST /api/customers` — create customer (email, firstName, lastName)
- `GET /api/customers/{customerId}` — retrieve customer details

**Address CRUD:**
- `POST /api/customers/{customerId}/addresses` — add address to customer
- `GET /api/customers/{customerId}/addresses` — list all addresses for customer (optionally filtered by type)
- `GET /api/customers/{customerId}/addresses/{addressId}` — get address details
- `PUT /api/customers/{customerId}/addresses/{addressId}` — update address
- `DELETE /api/customers/{customerId}/addresses/{addressId}` — delete address

**Integration Flow (Cycle 17 - Shopping BC Integration):**
1. **Customer Creation:** Before initializing cart, create customer via `POST /api/customers`
   - Request: `{ "email": "alice@example.com", "firstName": "Alice", "lastName": "Smith" }`
   - Response: `{ "customerId": "019c591d-..." }`

2. **Cart Initialization:** Shopping BC's `InitializeCart` now accepts real `customerId` parameter
   - Before Cycle 17: Used hardcoded stub (`00000000-0000-0000-0000-000000000001`)
   - After Cycle 17: Accepts legitimate `customerId` from Customer Identity BC
   - Foreign key validation ensures customer exists before cart creation

3. **Checkout Integration:** Checkout aggregate references real customer records
   - Checkout stores `customerId` with foreign key constraint
   - Orders BC can query Customer Identity for customer/address data during order processing

**End-to-End Flow (Customer → Cart → Checkout → Order):**
```
POST /api/customers
  ↓
customerId (Guid)
  ↓
POST /api/carts/initialize (with customerId)
  ↓
POST /api/carts/{cartId}/items (add items)
  ↓
POST /api/checkouts/initiate (from cart)
  ↓
POST /api/checkouts/{checkoutId}/shipping-address (with address details)
  ↓
POST /api/orders/place (from checkout)
```

### Core Invariants

**AddressBook Invariants:**
- A customer can have multiple addresses of each type (Shipping, Billing, Both)
- Each address type can have at most one default per customer
- Address nicknames must be unique per customer
- Addresses must have valid country codes (ISO 3166-1 alpha-2)
- State/province codes must be valid for the country (US states, Canadian provinces, etc.)
- Setting a new default automatically unsets the previous default for that type

**Future Profile Invariants:**
- Email addresses must be unique across all customers
- Phone numbers must be in E.164 format
- Customer IDs are immutable once created

### What it doesn't own

- Order history (Orders BC)
- Shopping cart state (Shopping BC)
- Authentication/authorization logic (Identity BC or external IdP)
- Payment processing (Payments BC)
- Address validation services (integrates with 3rd party, doesn't own validation logic)

### Integration Flows

**Address Management:**
```
AddAddress (command)
  └─> AddAddressHandler
      └─> CustomerAddress persisted (no event published)

UpdateAddress (command)
  └─> UpdateAddressHandler
      └─> CustomerAddress updated (no event published)

SetDefaultAddress (command)
  └─> SetDefaultAddressHandler
      └─> Previous default cleared, new default set

DeleteAddress (command)
  └─> DeleteAddressHandler
      └─> CustomerAddress soft-deleted (preserves history)
```

**Query Patterns:**
```
GetCustomerAddresses (query) ← Shopping BC during checkout
  └─> Returns list of AddressSummary (id, nickname, display line)

GetAddressSnapshot (query) ← Shopping BC when checkout completes
  └─> Returns AddressSnapshot (immutable copy for order record)

GetAddressByType (query) ← Returns BC for return shipping labels
  └─> Returns addresses filtered by type (Shipping, Billing, Both)
```

**Integration Message Handling:**
```
OrderPlaced (integration message from Orders)
  └─> UpdateAddressLastUsed handler
      └─> Updates LastUsedAt timestamp on shipping address
      └─> Used for analytics (most-used addresses)
```

### Address Snapshot Pattern

**Why snapshots instead of references?**

When Shopping BC completes checkout, it doesn't pass an `AddressId` to Orders BC. Instead, it requests an `AddressSnapshot` from Customer Identity BC and embeds that snapshot in the `CheckoutCompleted` message.

**Rationale:**
- **Temporal Consistency**: Orders/Fulfillment need the address *as it was* when the order was placed, not the current state
- **BC Autonomy**: Orders BC doesn't need to query Customer Identity BC during fulfillment (might be days/weeks later)
- **Auditability**: If customer updates their "Home" address, historical orders still show the original address
- **Resilience**: Orders can fulfill even if Customer Identity BC is temporarily unavailable

**Flow:**
```
1. Customer selects "Home" address (id: abc-123) during checkout
2. Shopping BC → Customer Identity BC: GetAddressSnapshot(abc-123)
3. Customer Identity BC returns immutable AddressSnapshot with all fields
4. Shopping BC embeds snapshot in CheckoutCompleted integration message
5. Orders BC persists snapshot as part of Order saga (no reference to Customer Identity BC)
```

### Privacy and Compliance Considerations

**Billing Address:**
- Full billing address stored in Customer Identity BC only
- Orders BC receives minimal billing info (City, State/Province, Country) for regional analytics
- Actual payment processing (card data) handled by 3rd party (Stripe, PayPal) — never stored in our system

**Data Deletion (GDPR/CCPA):**
- Customer Identity BC owns customer data deletion workflows
- When customer requests deletion:
  - Personal data deleted from Customer Identity BC
  - Order snapshots retain address data (legitimate interest: legal compliance, tax records)
  - Analytics aggregated data remains (no PII)

---

## Customer Experience

The Customer Experience context is a **stateless BFF (Backend-for-Frontend)** that composes views from multiple domain BCs (Shopping, Orders, Catalog, Customer Identity). It does NOT contain domain logic or persist data—all state lives in upstream BCs. Real-time updates are pushed to Blazor clients via **SignalR** (via Wolverine's native transport — migrated from SSE in Cycle 18, see ADR 0013).

> ⚠️ **Documentation Note:** This section contains historical implementation details from Cycles 16-18. Some references to "SSE" in this section reflect the original implementation that was subsequently migrated to SignalR (ADR 0013). The authoritative architecture description for the current SignalR-based implementation is in the second Customer Experience section below and in ADR 0013.

**Status**: 🚧 In Progress (Cycle 16 - Phase 3 Complete, Backend Integration Next)

### Architecture Pattern: Backend-for-Frontend (BFF) - Stateless Composition

**Why BFF?**
- **Composition**: Aggregates data from multiple BCs for optimized frontend views
- **Real-Time**: Natural location for SignalR hub (cart updates, order notifications)
- **UI Orchestration**: Keeps frontend-specific logic separate from domain BCs
- **Independent Scaling**: Frontend concerns don't impact domain BC performance
- **Multiple Clients**: Easy to add mobile BFF later with different composition needs

### Subdomains

**Storefront (Web):**

Customer-facing web store built with Blazor Server, demonstrating full-stack C# development with real-time updates via **SignalR** (Wolverine native transport, ADR 0013) and Wolverine integration.

**Implemented Pages (Phase 3):**
- ✅ Home page (navigation cards)
- ✅ Shopping cart view (SignalR-enabled for real-time updates)
- ✅ Checkout flow (MudStepper wizard with 4 steps)
- ✅ Order history (MudTable with order list)

**Future Pages:**
- Product listing page (queries Catalog BC)
- Account/address management (queries Customer Identity BC)

**Technology Stack:**
- **Blazor Server** - C# full-stack, component model, interactive render modes
- **MudBlazor** - Material Design component library (ADR 0005)
- **SignalR** (via Wolverine transport) - Real-time cart/order updates pushed from domain BCs (ADR 0013, supersedes ADR 0004)
- **Wolverine HTTP** - BFF endpoints for view composition
- **Alba** - Integration testing for BFF composition endpoints

**Future Expansion:**
- Mobile BFF (different composition needs than web)
- Progressive Web App (PWA) support
- Customer notification preferences UI

### What it receives

**Integration Messages (for real-time UI updates):**
- `ItemAdded` from Shopping — push cart updates to connected clients
- `ItemRemoved` from Shopping — push cart updates to connected clients
- `OrderPlaced` from Orders — notify customer of successful order placement
- `PaymentCaptured` from Payments — update order status UI in real-time
- `ShipmentDispatched` from Fulfillment — notify customer with tracking number
- `ShipmentDelivered` from Fulfillment — notify customer of delivery

### What it queries (HTTP/gRPC)

**Shopping BC:**
- `GET /api/carts/{cartId}` — retrieve cart state for display

**Orders BC:**
- `GET /api/checkouts/{checkoutId}` — retrieve checkout state for wizard UI
- `GET /api/orders/{orderId}` — retrieve order details for tracking page
- `GET /api/orders?customerId={customerId}` — retrieve order history

**Customer Identity BC:**
- `GET /api/customers/{customerId}/addresses` — retrieve saved addresses for checkout
- `GET /api/addresses/{addressId}/snapshot` — get address snapshot when needed

**Catalog BC (future):**
- `GET /api/products` — retrieve product listing with filters/pagination
- `GET /api/products/{sku}` — retrieve product details page data
- `GET /api/products/search?q={query}` — search products

**Inventory BC:**
- `GET /api/inventory/availability?skus={skus}` — check stock levels for product display

### What it publishes

**Commands (mutations via Wolverine):**
- `AddItemToCart` → Shopping BC
- `RemoveItemFromCart` → Shopping BC
- `InitiateCheckout` → Shopping BC
- `SelectShippingAddress` → Orders BC
- `ProvidePaymentMethod` → Orders BC
- `CompleteCheckout` → Orders BC
- `AddAddress` → Customer Identity BC
- `UpdateAddress` → Customer Identity BC

**SSE Events (to connected clients via EventBroadcaster):**
- `cart-updated` — cart state changed (item added/removed/quantity changed)
- `order-placed` — order successfully placed (checkout complete)

**Future SSE Events:**
- `order-status-changed` — order progressed through lifecycle
- `inventory-alert` — low stock warning for items in cart
- `shipment-dispatched` — shipment tracking notification

### Core Invariants

**BFF Composition Invariants:**
- View models are optimized for frontend consumption, not domain purity
- BFF does NOT contain domain business logic (delegates to domain BCs)
- BFF does NOT maintain state (queries/commands only)
- SignalR notifications reflect domain events, don't replace them

**Real-Time Notification Invariants:**
- Only authenticated users receive notifications for their data
- SSE streams scoped to customer ID (security boundary)
- Domain BCs publish integration messages; BFF transforms to SSE events via EventBroadcaster
- Failed SSE delivery does NOT fail domain operations (fire-and-forget)
- EventBroadcaster uses `Channel<T>` for in-memory pub/sub (no persistence)

### What it doesn't own

- Shopping cart domain logic (Shopping BC)
- Checkout/order domain logic (Orders BC)
- Product catalog data (Catalog BC)
- Customer profile/address master data (Customer Identity BC)
- Payment processing (Payments BC)
- Inventory levels (Inventory BC)

### Integration Flows

**View Composition Example (Checkout Page):**
```
GetCheckoutView (BFF query)
  └─> GetCheckoutViewHandler
      ├─> HTTP GET → Orders BC: /api/checkouts/{checkoutId}
      ├─> HTTP GET → Customer Identity BC: /api/customers/{customerId}/addresses
      └─> Compose CheckoutView (ViewModel)
          ├─> CheckoutSummary (from Orders)
          ├─> AddressList (from Customer Identity)
          └─> Return optimized view for Blazor component
```

**Real-Time Update Flow (Cart Item Added):**
```
[Shopping BC domain logic]
AddItemToCart (command)
  └─> AddItemToCartHandler
      ├─> ItemAdded (domain event, persisted)
      └─> Publish Shopping.ItemAdded (integration message) → RabbitMQ (Phase 4)

[Customer Experience BFF - Storefront.Api]
Shopping.ItemAdded (integration message from RabbitMQ)
  └─> ItemAddedHandler (Storefront/Notifications/)
      ├─> Map to StorefrontEvent discriminated union
      └─> Publish to EventBroadcaster (Channel<StorefrontEvent>)
          └─> SSE endpoint consumes channel
              └─> Filter by customerId
                  └─> Serialize to JSON with "$type" discriminator
                      └─> Stream to EventSource client

[Blazor Frontend - Storefront.Web]
JavaScript EventSource (/sse/storefront?customerId={id})
  └─> onmessage event received
      └─> Parse JSON event
          └─> Invoke C# callback via JSInvokable
              └─> OnSseEvent(JsonElement eventData)
                  └─> Update Blazor component state
                      └─> StateHasChanged() triggers UI refresh
```

**Command Flow (Customer Adds Item to Cart):**
```
[Blazor Component]
<button @onclick="AddToCart">Add to Cart</button>

[Blazor Code-Behind]
async Task AddToCart()
  └─> HTTP POST → BFF: /api/storefront/cart/{cartId}/items
      └─> StorefrontController.AddItemToCart
          └─> Wolverine: Send AddItemToCart command → Shopping BC
              └─> [Shopping BC handles, publishes integration event]
                  └─> [BFF receives event, pushes SignalR update - see above]
```

### Project Structure (Implemented - Phase 3)

```
src/
  Customer Experience/
    Storefront/                       # BFF domain (regular SDK)
      Storefront.csproj               # References: Messages.Contracts only
      Clients/                        # HTTP client interfaces (domain)
        IShoppingClient.cs            # ✅ Implemented
        IOrdersClient.cs              # ✅ Implemented
        ICustomerIdentityClient.cs    # ✅ Implemented
        ICatalogClient.cs             # ✅ Implemented
      Composition/                    # View models
        CartView.cs                   # ✅ Implemented
        CheckoutView.cs               # ✅ Implemented
        ProductListingView.cs         # ✅ Implemented
      Notifications/                  # Integration message handlers + EventBroadcaster
        IEventBroadcaster.cs          # ✅ Implemented
        EventBroadcaster.cs           # ✅ Implemented (Channel<T> pub/sub)
        StorefrontEvent.cs            # ✅ Implemented (discriminated union)
        ItemAddedHandler.cs           # ✅ Implemented
        ItemRemovedHandler.cs         # ✅ Implemented
        ItemQuantityChangedHandler.cs # ✅ Implemented
        OrderPlacedHandler.cs         # ✅ Implemented

    Storefront.Api/                   # API project (Web SDK)
      Storefront.Api.csproj           # References: Storefront, Messages.Contracts
      Program.cs                      # ✅ Wolverine + Marten + DI setup
      appsettings.json                # ✅ Connection strings
      Properties/launchSettings.json  # ✅ Port 5237
      Queries/                        # HTTP endpoints (BFF composition)
        GetCartView.cs                # ✅ Implemented (namespace: Storefront.Api.Queries)
        GetCheckoutView.cs            # ✅ Implemented
        GetProductListing.cs          # ✅ Implemented
      Clients/                        # HTTP client implementations
        ShoppingClient.cs             # ✅ Implemented (namespace: Storefront.Api.Clients)
        OrdersClient.cs               # ✅ Implemented
        CustomerIdentityClient.cs     # ✅ Implemented
        CatalogClient.cs              # ✅ Implemented
      StorefrontHub.cs                # ✅ SSE endpoint (IAsyncEnumerable<T>)

    Storefront.Web/                   # Blazor Server app (Web SDK)
      Storefront.Web.csproj           # ✅ MudBlazor
      Program.cs                      # ✅ MudBlazor + HttpClient config
      Properties/launchSettings.json  # ✅ Port 5238
      Components/
        App.razor                     # ✅ MudBlazor CSS/JS references
        _Imports.razor                # ✅ MudBlazor namespace
        Layout/
          MainLayout.razor            # ✅ MudLayout with AppBar + Drawer
          InteractiveAppBar.razor     # ✅ Interactive component (render mode fix)
        Pages/
          Home.razor                  # ✅ Landing page (navigation cards)
          Cart.razor                  # ✅ SSE-enabled cart page
          Checkout.razor              # ✅ MudStepper wizard (4 steps)
          OrderHistory.razor          # ✅ MudTable with orders
      wwwroot/
        js/
          sse-client.js               # ✅ JavaScript EventSource client
        app.css                       # ✅ Minimal CSS (MudBlazor handles styling)

tests/
  Customer Experience/
    Storefront.IntegrationTests/      # Alba tests for BFF composition
      CartViewCompositionTests.cs     # ✅ Implemented
      CheckoutViewCompositionTests.cs # ✅ Implemented
      ProductListingCompositionTests.cs # ✅ Implemented
      SseEndpointTests.cs             # ✅ Implemented
      EventBroadcasterTests.cs        # ✅ Implemented
```

### Implementation Notes (Phase 3)

**EventBroadcaster Pattern (In-Memory Pub/Sub with Channel<T>):**
```csharp
// Storefront/Notifications/IEventBroadcaster.cs
public interface IEventBroadcaster
{
    void Publish(StorefrontEvent @event);
    IAsyncEnumerable<StorefrontEvent> SubscribeAsync(Guid customerId, CancellationToken ct);
}

// Storefront/Notifications/EventBroadcaster.cs
public sealed class EventBroadcaster : IEventBroadcaster
{
    private readonly Channel<StorefrontEvent> _channel = Channel.CreateUnbounded<StorefrontEvent>();

    public void Publish(StorefrontEvent @event) => _channel.Writer.TryWrite(@event);

    public async IAsyncEnumerable<StorefrontEvent> SubscribeAsync(
        Guid customerId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var @event in _channel.Reader.ReadAllAsync(ct))
        {
            // Customer isolation - only stream events for this customer
            if (@event.CustomerId == customerId)
                yield return @event;
        }
    }
}

// Storefront/Notifications/ItemAddedHandler.cs (Wolverine handler)
public static class ItemAddedHandler
{
    public static void Handle(Shopping.ItemAdded message, IEventBroadcaster broadcaster)
    {
        broadcaster.Publish(new StorefrontEvent.CartUpdated(
            message.CustomerId, message.CartId, message.Sku, message.Quantity));
    }
}

// Storefront.Api/StorefrontHub.cs (SSE endpoint)
public static class StorefrontHub
{
    [WolverineGet("/sse/storefront")]
    public static IAsyncEnumerable<StorefrontEvent> Subscribe(
        Guid customerId,
        IEventBroadcaster broadcaster,
        CancellationToken ct)
    {
        return broadcaster.SubscribeAsync(customerId, ct);
    }
}
```

**Blazor Component with SSE Real-Time Updates:**
```razor
@* Storefront.Web/Pages/Cart.razor *@
@page "/cart/{cartId:guid}"
@inject IStorefrontClient StorefrontClient
@inject NavigationManager Navigation
@implements IAsyncDisposable

<h1>Shopping Cart</h1>

@if (cart is null)
{
    <p>Loading...</p>
}
else
{
    <CartSummary Cart="@cart" />

    @foreach (var item in cart.Items)
    {
        <CartLineItem
            Item="@item"
            OnRemove="@(() => RemoveItem(item.Sku))" />
    }
}

    <button @onclick="Checkout">Proceed to Checkout</button>
}

@code {
    [Parameter] public Guid CartId { get; set; }

    private HubConnection? hubConnection;
    private CartView? cart;

    protected override async Task OnInitializedAsync()
    {
        // Initial cart load
        cart = await StorefrontClient.GetCartViewAsync(CartId);

        // Connect to SignalR hub
        hubConnection = new HubConnectionBuilder()
            .WithUrl(Navigation.ToAbsoluteUri("/storefronthub"))
            .Build();

        // Subscribe to cart updates
        hubConnection.On<CartSummaryView>("CartUpdated", async (updatedCart) =>
        {
            // Refresh cart state from server
            cart = await StorefrontClient.GetCartViewAsync(CartId);
            StateHasChanged(); // Re-render component
        });

        await hubConnection.StartAsync();
        await hubConnection.InvokeAsync("SubscribeToCart", CartId);
    }

    private async Task RemoveItem(string sku)
    {
        await StorefrontClient.RemoveItemFromCartAsync(CartId, sku);
        // SignalR notification will trigger UI update
    }
}

    public async ValueTask DisposeAsync()
    {
        if (hubConnection is not null)
            await hubConnection.DisposeAsync();
    }
}
```

### Testing Strategy

**Integration Tests (Alba):**
- BFF composition endpoints return correct view models
- SignalR hub receives integration messages and pushes to correct groups
- HTTP client delegation to domain BCs works correctly

**UI Tests (bUnit - optional):**
- Blazor components render correctly with mocked data
- SignalR updates trigger component re-renders
- Form submissions send correct commands to BFF

**No Unit Tests:**
- BFF is composition/orchestration only (no domain logic to unit test)
- Integration tests provide sufficient coverage

### Dependencies

**Required BCs (for initial implementation):**
- Shopping BC (cart queries/commands)
- Orders BC (checkout queries/commands)
- Customer Identity BC (address queries)

**Future Dependencies:**
- Catalog BC (product listing/search)
- Inventory BC (availability checks)

### Open Questions for Implementation

1. **Authentication**: Use ASP.NET Core Identity or external IdP (Auth0, Azure AD B2C)?
2. **Caching Strategy**: Redis for BFF view model caching? TTL policies?
3. **Error Handling**: How to display domain BC errors to customers (friendly messages vs technical details)?
4. **Offline Support**: PWA capabilities for cart persistence when offline?
5. **Mobile BFF**: Separate project or shared composition logic with different endpoints?

---

## Product Catalog

The Product Catalog context owns the master product data—SKUs, descriptions, images, categorization, and searchability. This BC is the source of truth for "what we sell" but does NOT own pricing, inventory levels, or promotional rules (those are separate concerns).

**Status**: ✅ Phase 1 Complete (Core CRUD) - 24/24 integration tests passing

### Architecture: Read-Heavy, Query-Optimized (NOT Event Sourced)

**Unlike Orders, Payments, and Inventory, this BC uses Marten as a document store (NOT event sourced)** because product data is read-heavy with infrequent changes, and current state matters more than historical events.

Product Catalog is a **read-heavy** BC with very different access patterns than transactional BCs like Orders or Payments:
- **90%+ reads** - Product listing pages, search, detail pages
- **Rare writes** - Product data changes infrequently (merchandising team updates)
- **High traffic** - Every customer browsing session hits catalog repeatedly
- **Query complexity** - Full-text search, faceted filtering, categorization

**Persistence Strategy:**
- **NOT Event Sourced** - Product Catalog uses Marten as a document database (like MongoDB), NOT an event store
- **Why not event sourcing?** - Products are master data with infrequent changes; current state matters more than historical changes; read-heavy workload benefits from direct document queries
- **Document Store** - Marten stores Product documents directly; CRUD operations update documents in-place
- **Integration Events** - Publish integration messages (ProductAdded, ProductUpdated) to notify other BCs, but don't persist as domain events
- **Future Search Index** - Consider Elasticsearch/Meilisearch for advanced search features (fuzzy matching, faceted navigation)

### Entities

**Product (Aggregate Root):**
- `Sku` (Sku value object) - Primary identifier, strongly-typed with validation (uppercase A-Z, 0-9, hyphens only, max 24 chars)
  - Examples: "DOG-BOWL-001", "CAT-TOY-LASER", "FISH-TANK-20G"
  - Factory method: `Sku.From("DOG-BOWL-001")`
  - Serializes as plain string in JSON/Marten
- `Name` (ProductName value object) - Display name, strongly-typed with validation (mixed case, special chars `. , ! & ( ) -`, max 100 chars)
  - Examples: "Premium Stainless Steel Dog Bowl", "Interactive Cat Toy - Laser Pointer & Feathers!", "20 Gallon Fish Tank (Includes Filter)"
  - Factory method: `ProductName.From("Premium Dog Bowl")`
  - Serializes as plain string in JSON/Marten
- `Description` (string) - Marketing copy
- `LongDescription` (string) - Full product details
- `Category` (string) - Primary category (primitive string for Marten LINQ queryability - see Architecture Signal below)
- `Subcategory` (string) - Optional subcategory (e.g., "Ceramic Bowls")
- `Brand` (string) - Manufacturer/brand name (e.g., "PetSupreme")
- `Tags` (IReadOnlyList<string>) - Searchable tags (e.g., ["dishwasher-safe", "non-slip", "large-breed"])
- `Images` (IReadOnlyList<ProductImage>) - Product photos (primary + additional angles)
- `Dimensions` (ProductDimensions) - Physical size/weight for shipping calculations
- `Status` (ProductStatus) - Active, Discontinued, ComingSoon, OutOfSeason
- `AddedAt` (DateTimeOffset) - When product was added to catalog
- `UpdatedAt` (DateTimeOffset?) - Last modification timestamp

**ProductCategory / CategoryName - Architecture Signal:**
- **✅ Phase 1 Implementation**: Primitive `string` (NOT a value object)
- **Architecture Signal Discovered (Cycle 14)**: Value objects + Marten LINQ queries = friction
  - **Initial Plan**: `CategoryName` value object with `Value` property
  - **Problem**: Marten LINQ couldn't translate `p.Category.Value == "Dogs"` or `p.Category.ToString()`
  - **Test Failure**: 19/24 tests passing → filtering by category failed with 500 errors
  - **Solution**: Changed `Category` from `CategoryName` value object to primitive `string`
  - **Result**: 24/24 tests passing, clean LINQ queries (`p.Category == "Dogs"`)
  - **Pattern**: Use primitives for queryable fields, value objects for complex structures, FluentValidation at boundaries
  - **Validation**: FluentValidation at HTTP boundary (returns 400 errors) instead of factory method
- **Future Vision (Post-Cycle 15)**: Full Category subdomain with marketplace mapping
  - Internal categories (our categorization scheme)
  - Marketplace mappings (Ebay categories ≠ Amazon categories ≠ Walmart categories ≠ Target categories)
  - Many-to-many relationships (one internal category maps to multiple marketplace categories)
  - Category hierarchy management (parent/child relationships)
  - **Why separate subdomain?** Category mapping is complex enough to warrant its own bounded context within Product Catalog
  - **Why not its own BC?** Categories are tightly coupled to products; not valuable as standalone service
  - **Note**: Even with future Category subdomain, Product.Category will likely remain a primitive string for queryability
- Examples: Dogs, Cats, Birds, Fish, Reptiles, Small Animals

**ProductImage (Value Object):**
- `Url` (string) - CDN path to image
- `AltText` (string) - Accessibility description
- `SortOrder` (int) - Display order (0 = primary image)

**ProductDimensions (Value Object):**
- `Length`, `Width`, `Height` (decimal) - In inches
- `Weight` (decimal) - In pounds
- Used by Fulfillment BC for shipping cost calculations

**ProductStatus (Enum):**
- `Active` - Currently available for sale
- `Discontinued` - No longer sold (but still in system for historical orders)
- `ComingSoon` - Announced but not yet available
- `OutOfSeason` - Seasonal items (e.g., holiday-themed products)

### What it receives

**Integration Messages (rare updates):**
- `InventoryDepleted` from Inventory — may trigger automatic status change to OutOfStock (future feature)
- `ProductRestocked` from Inventory — reverse of above (future feature)

**Commands (from merchandising team or admin UI):**
- `AddProduct` - Create new product in catalog
- `UpdateProduct` - Modify product details
- `UpdateProductImages` - Add/remove/reorder images
- `ChangeProductStatus` - Activate, discontinue, or mark as coming soon
- `CategorizeProduct` - Assign to category/subcategory
- `TagProduct` - Add/remove searchable tags

### What it publishes

**Integration Messages:**
- `ProductAdded` - New product available in catalog (Inventory may create stock record)
- `ProductUpdated` - Product details changed (Customer Experience may invalidate cached listings)
- `ProductDiscontinued` - Product no longer available (Orders may prevent new purchases)
- `VendorProductAssociated` - Published to `vendor-portal-product-associated` exchange when an admin assigns or reassigns a SKU to a vendor. Contains `PreviousVendorTenantId` (null on first assignment, non-null on reassignment) so subscribers can clean up old associations. Contains `ReassignmentNote?` for audit trail. VendorPortal.Api subscribes via `vendor-portal-product-associated` queue.

**Note on Domain Events:**
Product Catalog does NOT use event sourcing, so there are no persisted domain events. Changes to products are direct document updates. Only integration messages (listed above) are published to notify other BCs.

### What it queries (future dependencies)

**Pricing BC (future):**
- `GET /api/pricing/products/{sku}` - Retrieve current price for product
- `GET /api/pricing/products?skus={skus}` - Bulk price lookup for listing pages

**Inventory BC:**
- `GET /api/inventory/availability?sku={sku}` - Check if product is in stock at any warehouse
- `GET /api/inventory/availability?skus={skus}` - Bulk availability check for listing pages

**Promotions BC (future):**
- `GET /api/promotions/products/{sku}` - Check for active promotions on product
- `GET /api/promotions/products?skus={skus}` - Bulk promotion lookup

### Core Invariants

**Product Invariants:**
- SKU must be unique across all products
- SKU cannot be changed once assigned (immutable identifier)
- Product name must not be empty
- At least one image is required for Active products
- Discontinued products cannot be reactivated (one-way transition)
- Products cannot be deleted (soft delete only, preserve for historical orders)

**Category Invariants:**
- Category hierarchy must be valid (no orphaned subcategories)
- Category names must be unique within their parent category
- Products must have at least one category assignment

**Image Invariants:**
- Image URLs must be valid CDN paths
- SortOrder 0 is reserved for primary image (only one primary)
- Alt text required for accessibility compliance

### What it doesn't own

- **Pricing** - Current price, promotional pricing, regional pricing (Pricing BC)
- **Inventory levels** - Available stock quantity, warehouse locations (Inventory BC)
- **Product reviews/ratings** - Customer feedback (Reviews BC - future)
- **Promotional rules** - Buy-one-get-one, discounts, etc. (Promotions BC - future)
- **Purchase history** - Sales data, trending products (Analytics BC - future)

### Integration Flows

**Product Lifecycle (Merchandising Team):**
```
AddProduct (command from admin UI)
  └─> AddProductHandler
      ├─> Validate SKU uniqueness
      ├─> ProductCreated (domain event)
      └─> Publish ProductAdded → Inventory BC (may create stock record)

UpdateProduct (command from admin UI)
  └─> UpdateProductHandler
      ├─> ProductDetailsUpdated (domain event)
      └─> Publish ProductUpdated → Customer Experience BC (invalidate cache)

ChangeProductStatus (command from admin UI)
  └─> ChangeProductStatusHandler
      ├─> Validate status transition (cannot reactivate discontinued)
      ├─> ProductStatusChanged (domain event)
      └─> [If discontinued] Publish ProductDiscontinued → Orders BC
```

**Query Patterns (Customer Experience BFF):**
```
GetProductListing (query from Storefront)
  └─> GetProductListingHandler
      ├─> Query Catalog BC: GET /api/products?category={category}&page={page}
      ├─> Query Inventory BC: GET /api/inventory/availability?skus={skus}
      ├─> [Future] Query Pricing BC: GET /api/pricing/products?skus={skus}
      └─> Compose ProductListingView
          ├─> Product details (from Catalog)
          ├─> In-stock status (from Inventory)
          └─> Price (from Pricing, or hardcoded for now)

GetProductDetail (query from Storefront)
  └─> GetProductDetailHandler
      ├─> Query Catalog BC: GET /api/products/{sku}
      ├─> Query Inventory BC: GET /api/inventory/availability?sku={sku}
      ├─> [Future] Query Pricing BC: GET /api/pricing/products/{sku}
      ├─> [Future] Query Reviews BC: GET /api/reviews/products/{sku}
      └─> Compose ProductDetailView
          ├─> Full product details + images (from Catalog)
          ├─> In-stock status (from Inventory)
          ├─> Price (from Pricing)
          └─> Average rating (from Reviews)
```

**Search Flow (Customer Experience):**
```
SearchProducts (query from Storefront)
  └─> SearchProductsHandler
      ├─> Query Catalog BC: GET /api/products/search?q={query}&category={category}
      ├─> [Future] Elasticsearch query for advanced full-text search
      └─> Return ProductSearchResults
          ├─> Matching products (SKU, name, thumbnail)
          ├─> Facets (categories, brands, tags for filtering)
          └─> Total result count for pagination
```

### HTTP Endpoints (Query-Centric)

**Product Queries:**
- `GET /api/products` - List products (paginated, filterable by category/brand/status)
- `GET /api/products/{sku}` - Get product details by SKU
- `GET /api/products/search?q={query}` - Full-text search products
- `GET /api/products/category/{category}` - Products in specific category
- `GET /api/products/tags/{tag}` - Products with specific tag

**Admin Commands (merchandising team):**
- `POST /api/products` - Add new product
- `PATCH /api/products/{sku}/status` - Change product status
- `PUT /api/products/{sku}` - Update product details

**Admin Vendor Assignment (JWT Bearer + Admin role required):**
- `POST /api/admin/products/{sku}/vendor-assignment` - Assign or reassign SKU to a vendor tenant. Body: `{ vendorTenantId: Guid, reassignmentNote?: string }`. Idempotent (same vendor → 200 no-op). Discontinued/deleted products → 400. Publishes `VendorProductAssociated` on success. **Note (Phase 2):** `VendorTenantId` is not validated against VendorIdentity.Api; invalid GUIDs create orphaned entries.
- `GET /api/admin/products/{sku}/vendor-assignment` - Retrieve current vendor assignment for a SKU. Returns 404 if product does not exist. Returns 200 with `IsAssigned: false` if product exists but has no assignment. Returns 200 with `IsAssigned: true` and full assignment details if assigned.
- `POST /api/admin/products/vendor-assignments/bulk` - Bulk assign up to 100 SKUs. Body: `{ assignments: [{ sku, vendorTenantId, reassignmentNote? }] }`. Returns HTTP 200 on full success, HTTP 207 (Multi-Status) on partial success, HTTP 400 if the request is invalid (empty list, >100 items). Each successful item publishes an individual `VendorProductAssociated` event. Succeeded items include `PreviousVendorTenantId` (null = new assignment, non-null = reassignment).

**Category Queries:**
- `GET /api/categories` - List all categories (hierarchical tree)
- `GET /api/categories/{category}/products` - Products in category

### Project Structure (Planned)

```
src/
  Product Catalog/
    Catalog/                          # Domain logic
      Products/                       # Product aggregate + handlers
        Product.cs                    # Aggregate root (write model)
        ProductStatus.cs              # Enum
        ProductImage.cs               # Value object
        ProductDimensions.cs          # Value object
        AddProduct.cs                 # Command + handler + validator
        UpdateProduct.cs              # Command + handler + validator
        ChangeProductStatus.cs        # Command + handler + validator
      Categories/                     # Category management
        ProductCategory.cs            # Value object
        CategoryHierarchy.cs          # Category tree structure
      Search/                         # Search optimization
        ProductSearchIndex.cs         # Denormalized read model
        RebuildSearchIndex.cs         # Command to rebuild search index
    Catalog.Api/                      # HTTP hosting
      Program.cs                      # Wolverine + Marten setup
      AdminEndpoints/                 # Admin-only endpoints (require auth)
        ProductAdminEndpoints.cs

tests/
  Product Catalog/
    Catalog.IntegrationTests/         # Alba tests for query/command endpoints
      ProductQueryTests.cs
      ProductSearchTests.cs
      ProductAdminTests.cs
    Catalog.UnitTests/                # Unit tests for domain logic
      ProductValidationTests.cs
      SkuGenerationTests.cs
```

### Implementation Notes (Phase 3)

**1. SKU Generation Strategy**

SKUs are human-readable identifiers, not auto-generated GUIDs. Follow a consistent pattern:

```
{CATEGORY}-{BRAND}-{SIZE}-{COLOR}
Example: CBOWL-CER-LG-BLU (Ceramic Bowl - Large - Blue)
```

**Implementation:**
```csharp
public static class SkuGenerator
{
    public static string Generate(
        string categoryCode,    // CBOWL (Ceramic Bowl)
        string brandCode,       // CER (Ceramic brand abbreviation)
        string sizeCode,        // LG (Large)
        string? colorCode)      // BLU (Blue, optional)
    {
        var parts = new[] { categoryCode, brandCode, sizeCode, colorCode }
            .Where(p => !string.IsNullOrEmpty(p));

        return string.Join("-", parts).ToUpperInvariant();
    }
}
```

**2. Document CRUD Pattern (NOT Event Sourced)**

Product Catalog uses Marten as a document database. Store and query Product documents directly:

```csharp
// AddProduct.cs - Create new product document
public static class AddProductHandler
{
    public static ProblemDetails Before(
        AddProduct command,
        IDocumentSession session)
    {
        // Check SKU uniqueness
        var existing = session.Query<Product>()
            .FirstOrDefault(p => p.Sku == command.Sku);

        if (existing is not null)
            return new ProblemDetails
            {
                Detail = $"Product with SKU {command.Sku} already exists",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }
}

    [WolverinePost("/api/admin/products")]
    public static async Task<(CreationResponse, OutgoingMessages)> Handle(
        AddProduct command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var product = new Product
        {
            Sku = command.Sku,
            Name = command.Name,
            Description = command.Description,
            Category = command.Category,
            Brand = command.Brand,
            Tags = command.Tags,
            Images = command.Images,
            Price = command.Price,  // Hardcoded for v1
            Status = ProductStatus.Active,
            AddedAt = now,
            UpdatedAt = now
        };

        // Store document (NOT event sourcing)
        session.Store(product);
        await session.SaveChangesAsync(ct);

        // Publish integration event (notify other BCs)
        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Catalog.ProductAdded(
            product.Sku,
            product.Name,
            product.Category,
            now));

        return (new CreationResponse($"/api/products/{product.Sku}"), outgoing);
    }
}

// UpdateProduct.cs - Update product document
public static class UpdateProductHandler
{
    [WolverinePut("/api/admin/products/{sku}")]
    public static async Task Handle(
        string sku,
        UpdateProduct command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var product = await session.LoadAsync<Product>(sku, ct);

        if (product is null)
            throw new InvalidOperationException($"Product {sku} not found");

        // Update document (immutable pattern with 'with' expression)
        var updated = product with
        {
            Name = command.Name ?? product.Name,
            Description = command.Description ?? product.Description,
            Price = command.Price ?? product.Price,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        session.Store(updated);
        await session.SaveChangesAsync(ct);
    }
}

// GetProducts.cs - Query documents directly
public static class GetProductsHandler
{
    [WolverineGet("/api/products")]
    public static async Task<PagedResult<ProductSummary>> Handle(
        string? category,
        int page = 1,
        int pageSize = 20,
        IDocumentSession session,
        CancellationToken ct = default)
    {
        var query = session.Query<Product>()
            .Where(p => p.Status == ProductStatus.Active);

        if (!string.IsNullOrEmpty(category))
            query = query.Where(p => p.Category == category);

        var total = await query.CountAsync(ct);

        var products = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductSummary(p.Sku, p.Name, p.Price, p.Images.First().Url))
            .ToListAsync(ct);

        return new PagedResult<ProductSummary>(products, total, page, pageSize);
    }
}
```

**3. Category Hierarchy**

Use adjacency list pattern for category tree:

```csharp
public sealed record ProductCategory(
    string Name,
    string Slug,              // URL-friendly (e.g., "bowls-and-feeders")
    string? ParentSlug,       // null for root categories
    int SortOrder)
{
    public static ProductCategory Root(string name, string slug, int sortOrder) =>
        new(name, slug, null, sortOrder);

    public static ProductCategory Subcategory(string name, string slug, string parentSlug, int sortOrder) =>
        new(name, slug, parentSlug, sortOrder);
}

// Query example - get all categories with parent relationships
public static async Task<List<CategoryNode>> GetCategoryTree(IDocumentSession session)
{
    var categories = await session.Query<ProductCategory>().ToListAsync();

    var roots = categories.Where(c => c.ParentSlug == null)
        .OrderBy(c => c.SortOrder)
        .Select(c => new CategoryNode(c, BuildChildren(c.Slug, categories)))
        .ToList();

    return roots;
}
```

**4. Image Storage Strategy**

Products don't store raw image bytes - only CDN URLs:

```csharp
public sealed record ProductImage(
    string Url,           // https://cdn.crittersupply.com/products/cbowl-cer-lg-blu-01.jpg
    string AltText,       // "Blue ceramic dog bowl - front view"
    int SortOrder)        // 0 = primary image
{
    public bool IsPrimary => SortOrder == 0;
}
```

**Admin UI uploads images** → CDN → **Catalog BC stores URL only**.

**5. Query Performance Considerations**

Product listings are high-traffic, high-concurrency queries:

```csharp
// Good - Paginated, indexed query
public static async Task<PagedResult<ProductSummary>> GetProducts(
    IDocumentSession session,
    string? category,
    int page,
    int pageSize,
    CancellationToken ct)
{
    var query = session.Query<Product>()
        .Where(p => p.Status == ProductStatus.Active);

    if (!string.IsNullOrEmpty(category))
        query = query.Where(p => p.Category == category);

    var total = await query.CountAsync(ct);

    var products = await query
        .OrderBy(p => p.Name)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(p => new ProductSummary(p.Sku, p.Name, p.PrimaryImageUrl))
        .ToListAsync(ct);

    return new PagedResult<ProductSummary>(products, total, page, pageSize);
}
```

**Consider caching** frequently-accessed data:
- Homepage featured products (Redis, 1-hour TTL)
- Category trees (Redis, 24-hour TTL, invalidate on category changes)
- Product detail pages (CDN edge caching for anonymous users)

### Testing Strategy

**Integration Tests (Alba):**
- Product CRUD operations via admin endpoints
- Product listing queries with filters
- Search queries return correct results
- Category hierarchy queries

**Unit Tests:**
- SKU generation logic
- Product validation rules
- Status transition validation (cannot reactivate discontinued)
- Category hierarchy building

**Performance Tests (future):**
- Load test product listing pages (simulate 1000+ concurrent users)
- Search query performance under load
- Cache hit rates for frequently-accessed products

### Dependencies

**Required BCs (for initial implementation):**
- None - Catalog BC is self-contained for initial version

**Future Dependencies:**
- Inventory BC (availability checks, denormalized into product listings)
- Pricing BC (product prices, promotional pricing)
- Promotions BC (active deals, badges on product cards)
- Reviews BC (customer ratings, average score)

### Seed Data Strategy

For development and demo purposes, seed realistic product data:

```csharp
// Catalog/SeedData/ProductSeeder.cs
public static class ProductSeeder
{
    public static async Task SeedProducts(IDocumentSession session)
    {
        var products = new[]
        {
            new Product
            {
                Sku = "CBOWL-CER-LG-BLU",
                Name = "Ceramic Dog Bowl - Large - Blue",
                Description = "Dishwasher-safe ceramic bowl for large breeds",
                Category = "Dogs > Bowls & Feeders",
                Brand = "PetSupreme",
                Tags = new[] { "dishwasher-safe", "non-slip", "large-breed" },
                Images = new[]
                {
                    new ProductImage("https://via.placeholder.com/400x400?text=Blue+Bowl", "Blue ceramic bowl - front", 0),
                    new ProductImage("https://via.placeholder.com/400x400?text=Blue+Bowl+Side", "Blue ceramic bowl - side", 1)
                },
                Status = ProductStatus.Active
            },
            // Add 20-30 realistic products across different categories
        };

        session.Store(products);
        await session.SaveChangesAsync();
    }
}
```

### Open Questions for Implementation

1. **Search Technology**: Use Marten's built-in search or integrate Elasticsearch/Meilisearch for advanced features (fuzzy search, typo tolerance, faceted navigation)?
2. **Image CDN**: Use Azure Blob Storage, AWS S3, Cloudflare Images, or local storage for development?
3. **Pricing Strategy**: Hardcode prices in Catalog for v1, or wait for separate Pricing BC?
4. **Admin UI**: Build simple Blazor admin pages in Catalog.Api, or assume external CMS/PIM system?
5. **Product Variants**: Should "Blue Bowl" and "Red Bowl" be separate products or variants of a parent product? (Impacts SKU strategy)

### Recommended Implementation Order

1. **Phase 1 - Core Product CRUD** (Cycle 13 candidate)
   - Product aggregate with basic fields (SKU, name, description, images, category, status)
   - Admin endpoints for adding/updating products
   - Query endpoints for product listing and detail
   - Seed data with 20-30 realistic products
   - Integration tests for CRUD operations

2. **Phase 2 - Category Management** (Cycle 14 candidate)
   - Category hierarchy structure
   - Category-based navigation
   - Breadcrumbs for product detail pages

3. **Phase 3 - Search** (Cycle 15 candidate)
   - Simple text search across product names/descriptions
   - Search result ranking
   - Faceted filtering (by category, brand, tags)

4. **Phase 4 - Customer Experience Integration** (After Cycle 15)
   - BFF queries Catalog BC for product listings
   - Product detail pages in Storefront.Web
   - Add to cart from product pages

---

## Vendor Identity

The Vendor Identity context manages authentication, authorization, and user lifecycle for vendor personnel accessing CritterSupply systems. Structurally similar to Customer Identity, but serving a distinct user population with **JWT Bearer authentication** (diverges from Customer Identity's session cookies — required for SignalR hub security and cross-service tenant claim propagation).

**Status**: ✅ Implemented (Cycle 22 Phases 1 + 6)

**Persistence Strategy**: Entity Framework Core (following Customer Identity pattern)

**Authentication**: JWT Bearer tokens — see [ADR 0015: JWT for Vendor Identity](docs/decisions/0015-jwt-for-vendor-identity.md)

**Event Modeling Session**: See [docs/planning/vendor-portal-event-modeling.md](docs/planning/vendor-portal-event-modeling.md) for full planning output.

### Purpose

Authenticate vendor users, manage their lifecycle, and issue tenant-scoped JWT claims for downstream contexts. Each vendor organization is a separate tenant with isolated user management.

### Multi-Tenancy Model

One tenant per vendor organization. User records are scoped to their vendor tenant. The issued JWT carries `VendorTenantId` as a cryptographically-verified claim — **`VendorTenantId` must NEVER come from request parameters**; it is extracted from the JWT only.

### Aggregates

**VendorTenant (Aggregate Root):**
- `Id` (Guid) - Canonical tenant identifier used throughout Vendor Identity and Vendor Portal
- `OrganizationName` (string) - Vendor company name; unique across all tenants
- `Status` (VendorTenantStatus enum) - `Onboarding`, `Active`, `Suspended`, `Terminated`
- `OnboardedAt` (DateTimeOffset) - When vendor was added to CritterSupply
- `ContactEmail` (string) - Primary contact for vendor organization
- `SuspendedAt` (DateTimeOffset?) - Set on suspension; displayed to users attempting login
- `SuspensionReason` (string?) - Shown to suspended users at login
- `TerminatedAt` (DateTimeOffset?) - Set on permanent termination

**VendorUser (Entity):**
- `Id` (Guid) - User identifier
- `VendorTenantId` (Guid) - Foreign key to VendorTenant
- `Email` (string) - Login email, unique across ALL vendor users (system-wide index)
- `PasswordHash` (string) - **Argon2id** hash via `Microsoft.AspNetCore.Identity.PasswordHasher<T>`
- `FirstName`, `LastName` (string) - User profile
- `Role` (VendorRole enum) - `Admin`, `CatalogManager`, `ReadOnly`
- `Status` (VendorUserStatus enum) - `Invited`, `Active`, `Deactivated`
- `InvitedAt`, `ActivatedAt`, `DeactivatedAt` (DateTimeOffset?) - Lifecycle timestamps
- `LastLoginAt` (DateTimeOffset?) - Audit trail

**VendorUserInvitation (separate EF Core table — critical for invitation lifecycle):**
- `Id` (Guid) - Invitation identifier
- `VendorUserId` (Guid) - FK to VendorUser
- `VendorTenantId` (Guid)
- `Token` (string) - **Cryptographic hash** of the token sent in email (raw token never stored)
- `InvitedRole` (VendorRole)
- `Status` (InvitationStatus enum) - `Pending`, `Accepted`, `Expired`, `Revoked`
- `InvitedAt` (DateTimeOffset)
- `ExpiresAt` (DateTimeOffset) - `InvitedAt + 72 hours`; enforced as background job
- `AcceptedAt` (DateTimeOffset?)
- `RevokedAt` (DateTimeOffset?)
- `ResendCount` (int) - Each resend issues new token, increments this

### What it receives

None from other bounded contexts. Vendor Identity initiates identity flows based on administrative actions (tenant creation, user invitation).

### What it publishes

**Integration Events (all published via Wolverine transactional outbox):**

Tenant lifecycle:
- `VendorTenantCreated` - New vendor organization onboarded (`VendorTenantId`, `OrganizationName`, timestamp)
- `VendorTenantSuspended` - Tenant suspended (`VendorTenantId`, `Reason`, `SuspendedAt`)
- `VendorTenantReinstated` - Suspension lifted (`VendorTenantId`, `ReinstatedAt`)
- `VendorTenantTerminated` - Permanent contract termination (`VendorTenantId`, `TerminatedAt`)

User lifecycle:
- `VendorUserInvited` - Invitation sent (`UserId`, `VendorTenantId`, `Email`, `Role`, `ExpiresAt`)
- `VendorUserInvitationExpired` - TTL passed without acceptance (`InvitationId`, `UserId`, `VendorTenantId`)
- `VendorUserInvitationResent` - Admin resent invitation (`InvitationId`, `UserId`, `ResendCount`, `NewExpiresAt`)
- `VendorUserInvitationRevoked` - Admin cancelled before acceptance (`InvitationId`, `Reason`)
- `VendorUserActivated` - User completed registration (`UserId`, `VendorTenantId`, `Role`, `ActivatedAt`)
- `VendorUserDeactivated` - User access revoked (`UserId`, `VendorTenantId`, `Reason`, `DeactivatedAt`)
- `VendorUserReactivated` - Deactivated user restored (`UserId`, `VendorTenantId`, `ReactivatedAt`)
- `VendorUserRoleChanged` - Role updated (`UserId`, `VendorTenantId`, `OldRole`, `NewRole`)
- `VendorUserPasswordReset` - Password changed, for audit trail (`UserId`, `VendorTenantId`, timestamp)

### Core Invariants

**Tenant Invariants:**
- Vendor tenant IDs are immutable once created (canonical identifier)
- Organization names must be unique across all vendor tenants
- Tenants can only be Suspended or Terminated, never deleted (preserve historical data)
- A Terminated tenant cannot be reinstated

**User Invariants:**
- Email addresses must be unique across ALL vendor users (system-wide, not per-tenant)
- Users belong to exactly one vendor tenant (no multi-tenancy for users)
- Users can only be activated after a valid, non-expired invitation
- Deactivated users **can** be reactivated (`Deactivated → Active` is allowed)
- Each tenant must always have at least one Admin user (cannot deactivate the last Admin)
- Invitation tokens are stored as cryptographic hashes — raw tokens are never persisted

**Role Invariants:**
- `Admin` — can invite/deactivate/reactivate users, change roles, submit change requests, view analytics
- `CatalogManager` — can submit/withdraw change requests, acknowledge alerts, view analytics; cannot manage users
- `ReadOnly` — can view analytics and change request history only; cannot submit or acknowledge

### What it doesn't own

- Vendor Portal access logic (Vendor Portal consumes tenant claims)
- Product-vendor associations (placeholder in Catalog BC for future design)
- Analytics or insights (Vendor Portal responsibility)
- Change request approval workflows (Catalog BC responsibility)

### Integration Points

| Context | Relationship | Notes |
|---------|--------------|-------|
| Vendor Portal | Downstream | Consumes all Vendor Identity events for access control and SignalR force-logout |
| Catalog | Downstream (Future) | May consume `VendorTenantCreated` to associate products with vendors |

### Integration Flows

**Tenant Onboarding:**
```
CreateVendorTenant (command from admin UI)
  └─> CreateVendorTenantHandler
      ├─> VendorTenant entity persisted (Status=Onboarding)
      └─> Publish VendorTenantCreated → Vendor Portal (initialize projections), Catalog

AssignProductToVendorTenant (command in Catalog BC admin)
  └─> Catalog BC publishes VendorProductAssociated → Vendor Portal
      (Vendor Portal builds VendorProductCatalog projection — prerequisite for all analytics)
```

**User Invitation and Activation:**
```
InviteVendorUser (command from admin UI)
  └─> InviteVendorUserHandler
      ├─> VendorUser entity created (Status=Invited)
      ├─> VendorUserInvitation persisted (token stored as hash, ExpiresAt=now+72h)
      ├─> Send invitation email (external service)
      └─> Publish VendorUserInvited → Vendor Portal

[72-hour Wolverine scheduled message fires]
  └─> VendorUserInvitationExpiryJob
      ├─> VendorUserInvitation.Status → Expired
      └─> Publish VendorUserInvitationExpired → Vendor Portal

CompleteVendorUserRegistration (command from user — uses token from email link)
  └─> CompleteVendorUserRegistrationHandler
      ├─> Validate token hash matches invitation
      ├─> Validate invitation is not Expired or Revoked
      ├─> VendorUser.Status → Active
      ├─> Set Argon2id password hash
      └─> Publish VendorUserActivated → Vendor Portal

DeactivateVendorUser (command from Admin)
  └─> DeactivateVendorUserHandler
      ├─> Validate not the last Admin in tenant
      ├─> VendorUser.Status → Deactivated
      └─> Publish VendorUserDeactivated → Vendor Portal (triggers force-logout via SignalR)

ReactivateVendorUser (command from Admin)
  └─> ReactivateVendorUserHandler
      ├─> VendorUser.Status → Active
      └─> Publish VendorUserReactivated → Vendor Portal
```

**Authentication Flow (JWT issuance):**
```
AuthenticateVendorUser (command from login page)
  └─> AuthenticateVendorUserHandler
      ├─> Query VendorUser by email
      ├─> Verify Argon2id password hash
      ├─> Verify VendorTenant.Status is Active
      ├─> Issue JWT with claims:
      │   ├─> VendorUserId
      │   ├─> VendorTenantId (for tenant isolation — JWT claim only, never from request)
      │   ├─> VendorTenantStatus (Active/Suspended/Terminated)
      │   ├─> Email
      │   ├─> Role (Admin | CatalogManager | ReadOnly)
      │   └─> exp = now + 15 minutes
      ├─> Issue 7-day refresh token (stored in HttpOnly cookie)
      └─> Update LastLoginAt timestamp
```

**Tenant Suspension:**
```
SuspendVendorTenant (command from admin)
  └─> SuspendVendorTenantHandler
      ├─> VendorTenant.Status → Suspended
      └─> Publish VendorTenantSuspended → Vendor Portal
          (Vendor Portal triggers ForceLogout for all tenant users via SignalR)
          (In-flight change requests remain frozen in their current state)

ReinstateVendorTenant (command from admin)
  └─> ReinstateVendorTenantHandler
      ├─> VendorTenant.Status → Active
      └─> Publish VendorTenantReinstated → Vendor Portal

TerminateVendorTenant (command from admin — permanent)
  └─> TerminateVendorTenantHandler
      ├─> VendorTenant.Status → Terminated
      └─> Publish VendorTenantTerminated → Vendor Portal
          (Vendor Portal auto-rejects all in-flight change requests)
```

### HTTP Endpoints

**Authentication (Implemented — Phase 1):**
- `POST /api/vendor-auth/login` - Authenticate vendor user, issue JWT + refresh token cookie
- `POST /api/vendor-auth/refresh` - Exchange refresh token for new access JWT
- `POST /api/vendor-auth/logout` - Invalidate refresh token
- `POST /api/vendor-auth/reset-password` - Initiate password reset (planned)

**Tenant Management (Implemented — Phases 1 + 6):**
- `POST /api/vendor-identity/tenants` - Create new vendor organization
- `POST /api/vendor-identity/tenants/{tenantId}/suspend` - Suspend tenant (requires reason)
- `POST /api/vendor-identity/tenants/{tenantId}/reinstate` - Reinstate suspended tenant
- `POST /api/vendor-identity/tenants/{tenantId}/terminate` - Terminate tenant (permanent, requires reason)

**User Invitation Management (Implemented — Phases 1 + 6):**
- `POST /api/vendor-identity/tenants/{tenantId}/users/invite` - Invite vendor user (carries Role)
- `POST /api/vendor-identity/tenants/{tenantId}/users/{userId}/invitation/resend` - Resend invitation (new token, +72h TTL)
- `POST /api/vendor-identity/tenants/{tenantId}/users/{userId}/invitation/revoke` - Revoke pending invitation (requires reason)

**User Lifecycle Management (Implemented — Phase 6):**
- `POST /api/vendor-identity/tenants/{tenantId}/users/{userId}/deactivate` - Deactivate user (last-admin protected, requires reason)
- `POST /api/vendor-identity/tenants/{tenantId}/users/{userId}/reactivate` - Reactivate deactivated user
- `PATCH /api/vendor-identity/tenants/{tenantId}/users/{userId}/role` - Change user role (last-admin demotion protected)

**User Self-Service (Planned):**
- `POST /api/vendor-users/complete-registration` - Complete invited user registration
- `POST /api/vendor-users/change-password` - Change password (authenticated)

### Comparison with Customer Identity

Vendor Identity follows many patterns established in Customer Identity, but diverges on authentication:

| Aspect | Customer Identity | Vendor Identity |
|--------|------------------|-----------------|
| Persistence | EF Core | EF Core ✅ Same |
| Schema | `customeridentity` | `vendoridentity` ✅ Same |
| EF Core patterns | DbContext + navigation properties | DbContext + navigation properties ✅ Same |
| Auth mechanism | Session cookies (ADR 0012) | **JWT Bearer** (ADR 0015) ❌ Diverges |
| Password hashing | Plaintext (dev convenience) | **Argon2id from day one** |
| Multi-tenant model | N/A (single user type) | VendorTenantId on every entity |
| Invitation flow | N/A | VendorUserInvitation table with TTL |
| Roles | N/A | VendorRole enum (Admin/CatalogManager/ReadOnly) |
| Published events | None | All lifecycle events via RabbitMQ |
| User reactivation | N/A | Allowed (Deactivated → Active) |

The divergence on authentication (JWT vs cookies) is **intentional** — required for SignalR hub security and cross-service claim propagation. See ADR 0015 for rationale.

### Privacy and Compliance Considerations

**Vendor User Data:**
- Email addresses required for login (not PII by most regulations, but still sensitive)
- Password hashes never exposed or logged
- MFA enrollment status tracked for security audits

**Data Deletion (GDPR/CCPA):**
- Vendor users can request deletion
- Personal data deleted from Vendor Identity BC
- Audit logs retain user IDs and timestamps (legitimate interest: security, compliance)
- Vendor Portal projections may retain aggregated data (no PII)

---

## Vendor Portal

The Vendor Portal context provides partnered vendors with a private, tenant-isolated view into how their products perform within CritterSupply. Vendors can see real-time sales analytics, monitor inventory levels, and submit product change requests. The portal uses **SignalR** (via Wolverine's native transport) for bidirectional real-time communication — live analytics updates, change request decisions, and inventory alerts.

**Status**: ✅ Implemented (Cycle 22 — Phases 1–6 complete: VendorProductCatalog subscription, JWT auth + SignalR hub, live analytics dashboard, Change Request full lifecycle, VendorAccount + Saved Views + Notification Preferences, Full Identity Lifecycle + Admin Tools)

**Persistence Strategy**: Marten (document store for accounts/requests, projections for read models)

**Real-Time**: SignalR via Wolverine (`opts.UseSignalR()`) — dual hub groups: `vendor:{tenantId}` (shared tenant notifications) and `user:{userId}` (individual notifications)

**Frontend**: **Blazor WASM** — `VendorPortal.Web` (port 5241)
- SDK: `Microsoft.NET.Sdk.BlazorWebAssembly`, `net10.0`
- UI library: MudBlazor (consistent with ADR 0005) + ApexCharts.Blazor for streaming analytics charts
- JWT token stored in WASM memory via `ITokenService` singleton; background refresh timer (every 13 min); `AccessTokenProvider` factory on `HubConnectionBuilder`
- `VendorHubService` singleton pattern: owns `HubConnection` lifecycle, handles reconnect-and-catch-up, exposes observable `State` for Live indicator
- Hosted by Nginx serving static files; no .NET runtime in the Web container
- **Intentionally diverges from `Storefront.Web` (Blazor Server)** — VP requires 8–12 hour session stability, JWT-native hub auth, and single WebSocket connection per user. See [ADR 0021](docs/decisions/0021-blazor-wasm-for-vendor-portal-web.md).

**Comparison: Storefront.Web vs VendorPortal.Web**

| Dimension | Storefront.Web | VendorPortal.Web |
|---|---|---|
| Blazor hosting model | Server | **WASM** |
| Session duration | Minutes (browse-and-buy) | Hours (ambient awareness, B2B) |
| Auth mechanism | Session cookies (ADR 0012) | JWT Bearer (ADR 0015) |
| Hub identity source | Query-string GUID (session-backed) | JWT claims only (cryptographic) |
| Deployment | Kestrel (.NET container) | Nginx (static files) |
| WebSocket connections/user | 2 (circuit + hub) | 1 (hub only) |

**Event Modeling Session**: See [docs/planning/vendor-portal-event-modeling.md](docs/planning/vendor-portal-event-modeling.md) for full planning output including event diagrams, risks, and phased roadmap.

**ADRs**: [ADR 0015: JWT for Vendor Identity](docs/decisions/0015-jwt-for-vendor-identity.md) · [ADR 0021: Blazor WASM for VendorPortal.Web](docs/decisions/0021-blazor-wasm-for-vendor-portal-web.md)

### Purpose

Present pre-aggregated sales and inventory analytics scoped to the vendor's products, provide real-time notifications via SignalR, accept and track product change requests (full 7-state lifecycle), and allow vendors to save custom dashboard views.

### Multi-Tenancy Model

One tenant per vendor organization (`VendorTenantId` from Vendor Identity JWT claims). Each vendor's data is fully isolated via **application-level tenant filtering**: composite document IDs (`{VendorTenantId}:{Sku}` or `{VendorTenantId}:{Sku}:{WarehouseId}`) combined with LINQ predicates on `VendorTenantId`. `VendorTenantId` is extracted from JWT claims only — never from request parameters.

> **Implementation note:** Marten's built-in `session.ForTenant(tenantId)` is **not** currently used. Tenant isolation is enforced at the application level via composite IDs and query predicates. `VendorProductCatalog` is intentionally not tenant-isolated (it is the system-wide SKU→Tenant lookup used by all handlers).

**Critical exception:** `VendorProductCatalog` (SKU→Tenant lookup) is intentionally NOT tenant-isolated. It is the system-wide lookup that tells handlers which tenant to query. All other projections filter by `VendorTenantId` in their query predicates.

### The Load-Bearing Pillar: VendorProductCatalog

**This projection must exist before any analytics or change request invariants can work.**

`VendorProductCatalog` is populated by `VendorProductAssociated` events published by Catalog BC when an admin assigns a SKU to a vendor tenant. It provides the SKU→VendorTenantId lookup that all handlers use for tenant routing.

```
VendorProductCatalogEntry document (Marten, schema: vendorportal):
  Id: {Sku}                  (document ID is the SKU — O(1) lookup)
  Sku: string
  VendorTenantId: Guid
  AssociatedBy: string       (admin username who made the assignment)
  AssociatedAt: DateTimeOffset
  IsActive: bool
```

**Implemented (Cycle 22 Phase 1):** `VendorProductAssociatedHandler` in `VendorPortal` domain assembly
upserts this document when it receives `VendorProductAssociated` from the
`vendor-portal-product-associated` RabbitMQ queue. Supports both new assignments
(`PreviousVendorTenantId = null`) and reassignments (non-null `PreviousVendorTenantId`).

A bulk-assignment backfill admin command must exist alongside the individual assignment endpoint to handle existing SKUs when the portal first deploys.

### Aggregates and Projections

**Aggregates (Write Models, event-sourced in Marten):**

- `VendorAccount` - Portal-specific settings, saved views, notification preferences (initialized by `VendorTenantCreated`)
  - `VendorTenantId` (Guid)
  - `SavedViews` (IReadOnlyList<SavedDashboardView>)
  - `NotificationPreferences` - **Default: all notifications ON** (vendor opts out, not opts in)

- `ChangeRequest` - Full 7-state lifecycle (event-sourced)
  - `Id` (Guid)
  - `VendorTenantId` (Guid)
  - `Sku` (string)
  - `Type` (ChangeRequestType enum) - `Description`, `Image`, `DataCorrection`
  - `Status` (ChangeRequestStatus enum) - see state machine below
  - `LatestContent` (string?) - description text or correction details
  - `ImageStorageKeys` (IReadOnlyList<string>) - object storage references (claim-check pattern)
  - `ReplacedByRequestId` (Guid?) - set when Status = `Superseded`; enables UI to show "replaced by request #xxx" linkage
  - `SubmittedAt`, `ResolvedAt` (DateTimeOffset?)

**ChangeRequest State Machine:**
```
Draft ──────────────────────→ Submitted    (SubmitChangeRequest command)
Draft ──────────────────────→ Withdrawn    (WithdrawChangeRequest command)
Draft ──────────────────────→ Superseded   (system: new request submitted for same Sku+Type by same tenant)

Submitted ──────────────────→ NeedsMoreInfo (MoreInfoRequestedForChangeRequest from Catalog)
Submitted ──────────────────→ Approved      (DescriptionChangeApproved / ImageChangeApproved / DataCorrectionApproved from Catalog)
Submitted ──────────────────→ Rejected      (DescriptionChangeRejected / ImageChangeRejected / DataCorrectionRejected from Catalog)
Submitted ──────────────────→ Withdrawn     (WithdrawChangeRequest command)
Submitted ──────────────────→ Superseded   (system: new request submitted for same Sku+Type by same tenant)

NeedsMoreInfo ──────────────→ Submitted     (ProvideAdditionalInfo command — re-enters Catalog review)
NeedsMoreInfo ──────────────→ Withdrawn     (WithdrawChangeRequest command)
NeedsMoreInfo ──────────────→ Superseded   (system: new request submitted for same Sku+Type by same tenant)

Approved, Rejected, Withdrawn, Superseded = TERMINAL (no further transitions)
```

**Key ChangeRequest invariant:** Only one active (Draft, Submitted, or NeedsMoreInfo) request per `VendorTenantId` + `Sku` + `Type` combination. Submitting a new one auto-supersedes any existing active request.

**Projections (Read Models, Pre-Aggregated):**

- `VendorProductCatalog` - SKU→Tenant mapping (NOT tenant-isolated; system-wide lookup)
  - `Id/Sku` (string), `VendorTenantId` (Guid), `AssociatedAt`, `IsActive`

- `ProductPerformanceSummary` - Sales metrics by SKU, bucketed by time period (tenant-isolated)
  - `VendorTenantId`, `Sku` (partition keys)
  - `DailySales`, `WeeklySales`, `MonthlySales`, `QuarterlySales`, `YearlySales`
  - `Revenue`, `UnitsSold`, `AverageOrderValue` (per time bucket)
  - **Phase 4+:** `OrderPlacedHandler` fan-out via internal `UpdateTenantSalesSummary` command
  - **Phase 3 (implemented):** `OrderPlacedAnalyticsHandler` sends a lightweight `SalesMetricUpdated` SignalR notification (one per affected vendor tenant via HashSet dedup); full `ProductPerformanceSummary` aggregation is deferred to Phase 4

- `InventorySnapshot` - Current stock levels (application-level tenant isolation via composite ID)
  - `Id` (string) — composite: `{VendorTenantId}:{Sku}:{WarehouseId}` (one document per vendor+SKU+warehouse)
  - `VendorTenantId` (Guid), `Sku` (string), `WarehouseId` (string)
  - `CurrentQuantity` (int)
  - `LastUpdatedAt` (DateTimeOffset)
  - **Implemented (Cycle 22 Phase 3):** `InventoryAdjustedHandler` + `StockReplenishedHandler`

- `LowStockAlert` - Active unacknowledged low-stock alerts (application-level tenant isolation via composite ID)
  - `Id` (string) — composite: `{VendorTenantId}:{Sku}` (one active alert per vendor+SKU; O(1) dedup)
  - `VendorTenantId` (Guid), `Sku` (string), `WarehouseId` (string)
  - `CurrentQuantity` (int), `ThresholdQuantity` (int)
  - `IsActive` (bool) — `true` when stock is below threshold; cleared by `AcknowledgeLowStockAlert`
  - `FirstDetectedAt` (DateTimeOffset) — preserved across quantity updates
  - `LastUpdatedAt` (DateTimeOffset) — event timestamp of the triggering `LowStockDetected` event
  - **Implemented (Cycle 22 Phase 3):** `LowStockDetectedHandler` with deduplication

- `ChangeRequestSummary` - Current state of all change requests (tenant-isolated)
  - `VendorTenantId` (partition key)
  - `RequestId`, `Sku`, `Type`, `Status`, `SubmittedAt`, `ResolvedAt`

- `SavedDashboardView` - Vendor-configured filter preferences (tenant-isolated)
  - `VendorTenantId`, `ViewId` (partition keys); `Name`, `FilterCriteria` (JSON)

### What it receives

**Integration Messages:**

From **Catalog** (prerequisite — must be set up first):
- `VendorProductAssociated` - **Load-bearing pillar**: establishes SKU→VendorTenant mapping in `VendorProductCatalog`
- `ProductCreated` - Maintains product reference data
- `ProductUpdated` - Updates product reference data
- `ProductDiscontinued` - Marks products as inactive in `VendorProductCatalog`
- `DescriptionChangeApproved` - Updates ChangeRequest status + triggers SignalR notification
- `DescriptionChangeRejected` - Updates ChangeRequest status + triggers SignalR notification
- `ImageChangeApproved` - Updates ChangeRequest status + triggers SignalR notification
- `ImageChangeRejected` - Updates ChangeRequest status + triggers SignalR notification
- `DataCorrectionApproved` - Updates ChangeRequest status + triggers SignalR notification
- `DataCorrectionRejected` - Updates ChangeRequest status + triggers SignalR notification
- `MoreInfoRequestedForChangeRequest` - Transitions ChangeRequest to NeedsMoreInfo + SignalR notification

From **Orders**:
- `OrderPlaced` - Fan-out via `VendorProductCatalog` lookup; updates `ProductPerformanceSummary` per tenant

From **Inventory**:
- `InventoryAdjusted` - Updates `InventorySnapshot` projections (via `VendorProductCatalog` lookup)
- `StockReplenished` - Updates `InventorySnapshot` projections
- `LowStockDetected` - Creates `LowStockAlert` (with deduplication) + triggers SignalR notification

From **Vendor Identity**:
- `VendorTenantCreated` - Initializes `VendorAccount` aggregate (default-on notification preferences)
- `VendorUserActivated` - Sends welcome notification to `user:{userId}` hub group
- `VendorUserDeactivated` - Sends `ForceLogout` to `user:{userId}` hub group
- `VendorUserReactivated` - Restores portal access
- `VendorTenantSuspended` - Sends `TenantSuspended` to `vendor:{tenantId}` hub group; freezes in-flight change requests
- `VendorTenantReinstated` - Sends `TenantReinstated` to hub; resumes frozen change requests
- `VendorTenantTerminated` - Auto-rejects all in-flight change requests; sends `TenantTerminated` to hub

### What it publishes

**Integration Messages (to Catalog BC via Wolverine transactional outbox):**

- `DescriptionChangeRequested` - Vendor proposes updated description (`RequestId`, `Sku`, `NewDescription`, `VendorTenantId`, `SubmittedAt`)
- `ImageUploadRequested` - Vendor submits new product images (`RequestId`, `Sku`, `ImageStorageKeys` — object storage keys via claim-check pattern, NOT raw bytes)
- `DataCorrectionRequested` - Vendor flags a product data error (`RequestId`, `Sku`, `CorrectionType`, `CorrectionDetails`, `VendorTenantId`)

**Note:** `DashboardViewSaved` is **NOT** published as an integration event. It is an internal domain event on the `VendorAccount` event stream only — no other BC consumes it.

### Core Invariants

**Multi-Tenancy Invariants:**
- Tenant isolation uses composite document IDs and LINQ predicates on `VendorTenantId` — not `session.ForTenant()` (Marten built-in multi-tenancy is not configured)
- `VendorTenantId` comes from JWT claims only — never from request parameters or body
- Vendors can only see data for products in their `VendorProductCatalog`
- Change requests must reference a Sku present in the vendor's `VendorProductCatalog`
- Suspended or Terminated vendors are rejected at both the SignalR hub and REST endpoints (403 Forbidden)

**Change Request Invariants:**
- Only one active (Draft, Submitted, NeedsMoreInfo) request per `VendorTenantId` + `Sku` + `Type`
- Submitting a new request for same Sku+Type auto-supersedes any existing active request (sets their status to `Superseded`, records `ReplacedByRequestId`)
- Change requests are immutable after submission (must withdraw and resubmit to change)
- `Superseded` state carries `ReplacedByRequestId` for UI linkage ("replaced by request #xxx")
- Image uploads use claim-check pattern: `ImageStorageKeys` (not raw bytes) in both aggregate and integration event

**Dashboard View Invariants:**
- View names must be unique per vendor tenant
- `DashboardViewSaved` is a domain event only (not an integration event)

**Alert Invariants:**
- One active `LowStockAlert` per Sku per tenant (deduplication enforced)
- Explicit `AcknowledgeLowStockAlert` command required to clear; not auto-dismissed on restock

**Suspension Invariants:**
- Suspended tenants: in-flight change requests freeze in current state (not rejected)
- Terminated tenants: all in-flight change requests are auto-rejected (compensating action)

### What it doesn't own

- Product approval workflows (Catalog BC owns approval logic)
- Sales data (Orders BC is source of truth)
- Inventory levels (Inventory BC is source of truth)
- User authentication (Vendor Identity BC owns auth)
- Product master data (Catalog BC owns product definitions)

### Integration Points

| Context | Relationship | Notes |
|---------|--------------|-------|
| Vendor Identity | Upstream | Authenticates vendor users, provides tenant claims (`VendorTenantId`) for data isolation |
| Catalog | Downstream | Receives change requests (`DescriptionChangeRequested`, `ImageUploadRequested`, `DataCorrectionRequested`), publishes approval outcomes |
| Orders | Upstream | Source of sales events (`OrderPlaced`, `OrderItemShipped`, `OrderItemReturned`) for analytics aggregation |
| Inventory | Upstream | Source of stock-level events (`InventoryAdjusted`, `StockReplenished`, `LowStockDetected`) for inventory snapshots |

### Integration Flows

**OrderPlaced Fan-Out (Analytics):**
```
OrderPlaced (integration message from Orders)
  └─> OrderPlacedHandler (Vendor Portal)
      ├─> Query VendorProductCatalog (NOT tenant-isolated) for all SKUs in order
      ├─> Group line items by VendorTenantId
      └─> Publish UpdateTenantSalesSummary (internal command) per tenant
          └─> UpdateTenantSalesSummaryHandler
              ├─> session.ForTenant(tenantId)
              ├─> Upsert ProductPerformanceSummary per SKU (daily/weekly/monthly buckets)
              └─> Wolverine outbox guarantees per-tenant delivery

SKU with no VendorProductCatalog entry → silently skipped (log warning, do not throw)
```

**LowStockDetected → SignalR:**
```
LowStockDetected (integration message from Inventory)
  └─> LowStockDetectedHandler (Vendor Portal)
      ├─> Lookup VendorProductCatalog for SKU → tenantId
      ├─> If no mapping: silently return (internal product, no vendor to notify)
      ├─> Check deduplication: active LowStockAlert exists for this SKU+tenant?
      │   └─> If yes: update CurrentQuantity only, no new alert
      ├─> Create LowStockAlert document (session.ForTenant(tenantId))
      └─> Publish LowStockAlertRaised → IVendorTenantMessage → "vendor:{tenantId}" hub group
          [Vendor sees: toast notification + badge increment]
```

**Change Request Full Lifecycle:**
```
DraftChangeRequest (command from vendor)
  └─> DraftChangeRequestHandler
      ├─> Idempotency check: if RequestId already exists → return (at-least-once delivery safe)
      ├─> Create ChangeRequest document (Status=Draft) via session.Store()
      └─> session.SaveChangesAsync() — no outgoing messages at draft time

SubmitChangeRequest (command from vendor)
  └─> SubmitChangeRequestHandler
      ├─> session.LoadAsync<ChangeRequest>(RequestId)
      ├─> Guard: not found → return [] silently
      ├─> Guard: VendorTenantId mismatch → return [] silently (never reveal existence)
      ├─> Guard: Status != Draft → return [] silently (idempotent / invalid transition)
      ├─> Auto-supersede invariant: query for other active (Draft/Submitted/NeedsMoreInfo) requests
      │   for same VendorTenantId + Sku + Type → set Status=Superseded, ReplacedByRequestId=RequestId
      ├─> Transition request → Status=Submitted, SubmittedAt=now
      ├─> session.Store() all changed documents, session.SaveChangesAsync()
      └─> Return [ChangeRequestStatusUpdated (hub), DescriptionChangeRequested / ImageUploadRequested
          / DataCorrectionRequested (Catalog BC via Wolverine publish rules → RabbitMQ exchange)]

[Catalog BC processes: approve / reject / needs-more-info]

DescriptionChangeApproved / ImageChangeApproved / DataCorrectionApproved
  (integration message from Catalog BC → vendor-portal-*-change-approved RabbitMQ queues)
  └─> ApprovedHandler (VendorPortal domain)
      ├─> session.LoadAsync<ChangeRequest>(RequestId)
      ├─> Guard: not found, VendorTenantId mismatch, or !IsActive → return [] silently
      ├─> Mutate: Status=Approved, ResolvedAt=now → session.Store(), session.SaveChangesAsync()
      └─> Return [ChangeRequestStatusUpdated (→ "vendor:{tenantId}" hub group),
                  ChangeRequestDecisionPersonal (→ "user:{submitterUserId}" hub group)]
          [Toast: "✅ Description update for SKU-1001 approved!"]

DescriptionChangeRejected / ImageChangeRejected / DataCorrectionRejected
  (integration message from Catalog BC → vendor-portal-*-change-rejected RabbitMQ queues)
  └─> RejectedHandler (VendorPortal domain)
      ├─> session.LoadAsync<ChangeRequest>(RequestId)
      ├─> Guard: not found, VendorTenantId mismatch, or !IsActive → return [] silently
      ├─> Mutate: Status=Rejected, RejectionReason=reason, ResolvedAt=now → session.Store(), SaveChangesAsync()
      └─> Return [ChangeRequestStatusUpdated, ChangeRequestDecisionPersonal with Reason]

MoreInfoRequestedForChangeRequest (integration message from Catalog BC)
  └─> MoreInfoRequestedForChangeRequestHandler
      ├─> session.LoadAsync<ChangeRequest>(RequestId)
      ├─> Guard: not found, VendorTenantId mismatch → return [] silently
      ├─> Guard: Status != Submitted → return [] silently (only valid from Submitted)
      ├─> Mutate: Status=NeedsMoreInfo, Question=question → session.Store(), SaveChangesAsync()
      └─> Return [ChangeRequestStatusUpdated, ChangeRequestDecisionPersonal with Question as Reason]
          [Toast: "📋 Catalog team has a question about your request"]

ProvideAdditionalInfo (command from vendor)
  └─> ProvideAdditionalInfoHandler
      ├─> session.LoadAsync<ChangeRequest>(RequestId)
      ├─> Guard: not found, VendorTenantId mismatch → return [] silently
      ├─> Guard: Status != NeedsMoreInfo → return [] silently (invalid transition)
      ├─> Append VendorInfoResponse(response, now) to InfoResponses list (structured Q&A history)
      ├─> Mutate: Status=Submitted, SubmittedAt=now → session.Store(), SaveChangesAsync()
      └─> Return [ChangeRequestStatusUpdated (hub), DescriptionChangeRequested / ImageUploadRequested
          / DataCorrectionRequested with AdditionalNotes=vendorResponse (Catalog BC via RabbitMQ)]
          Note: InfoResponses list is stored locally on the document for UI history display;
          the vendor's specific response is forwarded to Catalog BC via AdditionalNotes.

WithdrawChangeRequest (command from vendor)
  └─> WithdrawChangeRequestHandler
      ├─> session.LoadAsync<ChangeRequest>(RequestId)
      ├─> Guard: not found → return null silently
      ├─> Guard: VendorTenantId mismatch → return null silently
      ├─> Guard: !IsActive → return null silently (already terminal)
      ├─> Mutate: Status=Withdrawn, ResolvedAt=now → session.Store(), SaveChangesAsync()
      └─> Return ChangeRequestStatusUpdated (→ "vendor:{tenantId}" hub group)
```

**Vendor Identity Events → SignalR:**
```
VendorUserDeactivated (from Vendor Identity)
  └─> VendorUserDeactivatedHandler
      ├─> Revoke VendorUserAccess read model (ForTenant)
      └─> IHubContext.Clients.Group("user:{userId}").SendAsync("ForceLogout", ...)
          [Client: disconnect hub, clear JWT, redirect to "Access Revoked" page]

VendorTenantSuspended (from Vendor Identity)
  └─> VendorTenantSuspendedHandler
      ├─> Freeze: in-flight change requests remain in current state
      └─> IHubContext.Clients.Group("vendor:{tenantId}").SendAsync("TenantSuspended", ...)
          { reason, vendorSupportContact }

VendorTenantTerminated (from Vendor Identity)
  └─> VendorTenantTerminatedHandler
      ├─> Auto-reject all Submitted/NeedsMoreInfo change requests
      │   └─> Append ChangeRequestRejected (reason="Vendor contract ended") per request
      └─> IHubContext.Clients.Group("vendor:{tenantId}").SendAsync("TenantTerminated", ...)
```

### HTTP Endpoints (Planned)

**Analytics Queries (all require JWT auth; VendorTenantId from token):**
- `GET /api/vendor-portal/performance?sku={sku}&period={period}` - Sales metrics by SKU and time period
- `GET /api/vendor-portal/inventory?sku={sku}` - Current inventory snapshot
- `GET /api/vendor-portal/dashboard` - Aggregated dashboard data (top products, alerts, pending requests count)

**Alerts:**
- `GET /api/vendor-portal/alerts` - List active low-stock alerts for tenant
- `POST /api/vendor-portal/alerts/{alertId}/acknowledge` - Acknowledge a low-stock alert

**Change Requests:**
- `POST /api/vendor-portal/change-requests/description` - Start a description update (Draft)
- `POST /api/vendor-portal/change-requests/images/upload-url` - Get pre-signed upload URL (claim-check)
- `POST /api/vendor-portal/change-requests/images` - Submit image change with `ImageStorageKeys`
- `POST /api/vendor-portal/change-requests/data-correction` - Submit a data correction
- `POST /api/vendor-portal/change-requests/{requestId}/submit` - Submit a Draft for review
- `POST /api/vendor-portal/change-requests/{requestId}/withdraw` - Withdraw a pending request
- `POST /api/vendor-portal/change-requests/{requestId}/respond` - Respond to NeedsMoreInfo
- `GET /api/vendor-portal/change-requests` - List change requests (filter by status)
- `GET /api/vendor-portal/change-requests/{requestId}` - Get request detail with full audit trail

**Account Management:**
- `GET /api/vendor-portal/account/preferences` - Get notification preferences (defaults to all-on if no account)
- `PUT /api/vendor-portal/account/preferences` - Update notification preferences (opt-out model: all on by default)
- `GET /api/vendor-portal/account/dashboard-views` - List saved dashboard views
- `POST /api/vendor-portal/account/dashboard-views` - Save dashboard view configuration (409 on duplicate name)
- `DELETE /api/vendor-portal/account/dashboard-views/{viewId}` - Delete saved view

**SignalR Hub:**
- `WS /hub/vendor-portal?access_token={jwt}` - SignalR WebSocket connection
  - Hub groups established from JWT claims only: `vendor:{tenantId}` + `user:{userId}`

### Phased Roadmap

**Phase 1 — The Load-Bearing Foundation** (no vendor UI yet; internal name: "Vendor Infrastructure Foundation")
- `VendorProductAssociated` event + `AssignProductToVendor` command in Catalog BC
- Bulk-assignment backfill command in Catalog BC
- `VendorProductCatalog` projection in VendorPortal domain project
- VendorIdentity EF Core: entities, migrations, create/invite commands
- `VendorTenantCreated` + `VendorUserInvited` events published to RabbitMQ
- VendorPortal.Api skeleton with `VendorProductAssociatedHandler`

**Phase 2 — JWT Auth + SignalR Hub + Static Analytics Dashboard** (first vendor-visible value)
- `CompleteVendorUserRegistration` + `AuthenticateVendorUser` with JWT issuance (Argon2id)
- Refresh token endpoint (HttpOnly cookie, 7-day)
- `VendorPortalHub` with `[Authorize]` + dual group membership
- `IVendorTenantMessage` + `IVendorUserMessage` marker interfaces + Wolverine publish rules
- Force-logout on deactivation; tenant suspension notifications
- `OrderPlacedHandler` fan-out + `ProductPerformanceSummary` projection
- `LowStockAlert` document with deduplication + `AcknowledgeLowStockAlert` command
- Static analytics dashboard in VendorPortal.Web (HTTP queries, no real-time yet)
- SignalR from day one (welcome notification on activation) — do not defer

**Phase 3 — Live Analytics via SignalR**
- `LowStockAlertRaised`, `SalesMetricUpdated`, `InventoryLevelUpdated` SignalR messages
- Hub reconnection: catch-up query for missed alerts on reconnect
- Visual "Live" connection indicator in portal header
- Blazor components wired to `HubConnectionBuilder`

**Phase 4 — Change Request Full Lifecycle**
- `ChangeRequest` aggregate (7 states, all commands and transitions)
- Image claim-check: pre-signed URL + `ImageStorageKeys`
- Subscribe to Catalog BC: approve/reject/moreInfo
- `ChangeRequestStatusUpdated` → `vendor:{tenantId}` + `ChangeRequestDecisionPersonal` → `user:{userId}`
- Catalog BC stubs for approval workflow
- VendorPortal.Web: change request pages

**Phase 5 — Saved Views + VendorAccount** ✅ (Cycle 22)
- `VendorAccount` Marten document (Id = VendorTenantId, initialized by `VendorTenantCreated`)
- `NotificationPreferences` with opt-out model (4 toggles, all default true)
- `SaveDashboardView` / `DeleteDashboardView` / `UpdateNotificationPreferences` commands
- Duplicate view name guard (case-insensitive, returns 409 Conflict)
- VendorPortal.Web: Settings page (notification preferences, saved views management)
- RabbitMQ queue: `vendor-portal-tenant-created`
- 27 integration tests (86 total across all phases)

**Phase 6 — Full Identity Lifecycle + Admin Tools**
- Invitation expiry job, resend/revoke, reactivation, role changes
- Tenant suspension/reinstatement/termination with compensation
- Last-admin protection; VendorPortal.Web user management page

---

## Customer Experience

**Type:** Backend-for-Frontend (BFF) Composition Layer

**Purpose:** Aggregate data from multiple bounded contexts for customer-facing UI, provide real-time updates via **SignalR** (via Wolverine's native transport — migrated from SSE, see ADR 0013)

### Bounded Context Boundary

**In Scope:**
- View composition (aggregating data from multiple domain BCs)
- SignalR notification delivery (pushing real-time updates to connected Blazor clients)
- HTTP client coordination (querying downstream BCs)

**Out of Scope:**
- Domain logic (all business rules live in upstream BCs)
- Data persistence (stateless composition only, no database)
- Command execution (commands sent via HTTP POST to domain BCs, BFF doesn't modify state)

### Integration Contracts

**Receives (HTTP Queries to Downstream BCs):**
- Shopping BC: `GET /api/carts/{cartId}` → cart state for composition
- Orders BC: `GET /api/checkouts/{checkoutId}` → checkout wizard state
- Orders BC: `GET /api/orders?customerId={customerId}` → order history listing
- Customer Identity BC: `GET /api/customers/{customerId}/addresses` → saved addresses for checkout
- Product Catalog BC: `GET /api/products?category={category}&page={page}` → product listing with filters/pagination
- Inventory BC: `GET /api/inventory/availability?skus={skus}` → stock levels (future Phase 3 enhancement)

**Receives (Integration Messages via RabbitMQ):**
- `Shopping.ItemAdded` → triggers SignalR push `CartUpdated` to `customer:{customerId}` group
- `Shopping.ItemRemoved` → triggers SignalR push `CartUpdated` to `customer:{customerId}` group
- `Shopping.ItemQuantityChanged` → triggers SignalR push `CartUpdated` to `customer:{customerId}` group
- `Orders.OrderPlaced` → triggers SignalR push `OrderStatusChanged` to `customer:{customerId}` group
- `Payments.PaymentCaptured` → triggers SignalR push `OrderStatusChanged` to `customer:{customerId}` group
- `Fulfillment.ShipmentDispatched` → triggers SignalR push `ShipmentStatusChanged` to `customer:{customerId}` group
- `Fulfillment.ShipmentDelivered` → triggers SignalR push `ShipmentStatusChanged` to `customer:{customerId}` group

**Publishes (Integration Messages):**
- None (BFF is read-only, commands sent via HTTP POST to domain BCs)

**Publishes (SignalR Messages via `IStorefrontWebSocketMessage`):**
- `CartUpdated` → Blazor client re-renders cart with updated line items/totals
- `OrderStatusChanged` → Blazor client updates order status display
- `ShipmentStatusChanged` → Blazor client updates tracking info

### View Models (Composition)

**CartView:**
- Aggregates Shopping BC (cart state) + Product Catalog BC (product details, images)
- Enriches cart line items with product names, images, current prices

**CheckoutView:**
- Aggregates Orders BC (checkout state) + Customer Identity BC (saved addresses) + Product Catalog BC (product details)
- Multi-step wizard state (current step, validation, can proceed)

**ProductListingView:**
- Aggregates Product Catalog BC (products) + Inventory BC (availability - future)
- Paginated product cards with images, prices, stock indicators

**OrderHistoryView (Future Phase 3):**
- Aggregates Orders BC (order list) + Fulfillment BC (shipment status)
- Order summaries with tracking numbers and delivery estimates

### Integration Pattern

**Pattern:** HTTP (query) + RabbitMQ (notifications) + SignalR (push to clients via Wolverine)

**SignalR hub:** `StorefrontHub` at `/hub/storefront` — group: `customer:{customerId}`  
**Marker interface:** `IStorefrontWebSocketMessage` with `CustomerId` property  
**Wolverine config:** `opts.UseSignalR()` + `opts.Publish(x => x.MessagesImplementing<IStorefrontWebSocketMessage>().ToSignalR())`

**Flow Example (Cart Update with Real-Time Notification):**
```
[Shopping BC - Domain Logic]
AddItemToCart (command) → AddItemToCartHandler
  ├─> ItemAdded (domain event, persisted to event store)
  └─> Publish Shopping.ItemAdded (integration message) → RabbitMQ

[Customer Experience BFF - Notification Handler]
Shopping.ItemAdded (integration message from RabbitMQ)
  └─> ItemAddedHandler
      ├─> Query Shopping BC: GET /api/carts/{cartId}
      ├─> Compose CartUpdated (typed SignalR message implementing IStorefrontWebSocketMessage)
      └─> Return CartUpdated → Wolverine routes to StorefrontHub → "customer:{customerId}" group

[Blazor Frontend - SignalR Client]
SignalR message received (CartUpdated)
  └─> Blazor component re-renders with updated cart data
```

### Key Architectural Decisions

**[ADR 0013: SignalR Migration from SSE](./docs/decisions/0013-signalr-migration-from-sse.md)**
- **Decision:** Migrated from SSE to SignalR (via Wolverine's native transport) in Cycle 18+
- **Rationale:** Bidirectional capabilities needed; Wolverine's `opts.UseSignalR()` eliminates boilerplate; better integration with the rest of the stack
- **Note:** ADR 0004 (SSE over SignalR) is superseded by ADR 0013

**[ADR 0005: MudBlazor UI Framework](./docs/decisions/0005-mudblazor-ui-framework.md)**
- **Decision:** Use MudBlazor for Blazor UI components
- **Rationale:** Material Design, polished components, active community

**Phase 3: Blazor Frontend - 📋 Planned**
- Blazor Server app (`Storefront.Web/`)
- Pages: Cart, Checkout, OrderHistory
- SSE client integration for real-time updates

### Notes

- **Stateless:** BFF does not persist data, all state lives in domain BCs
- **Composition Only:** No domain logic in BFF - aggregation and presentation only
- **Real-Time:** SSE provides uni-directional push for live updates without polling
- **Testing:** Alba + TestContainers + stub clients (no mocks, clean test data setup)

---

## Pricing

The Pricing context owns the authoritative retail price for every SKU — including setting and changing prices, scheduling future price changes, maintaining a complete price history for audit, enforcing floor/ceiling policies, and accepting vendor-submitted price suggestions.

**Status:** 🟡 Event Modeling Complete — Awaiting Implementation Cycle Assignment
**Event Modeling Doc:** [`docs/planning/pricing-event-modeling.md`](docs/planning/pricing-event-modeling.md)
**Architecture:** Event-sourced domain (one `ProductPrice` stream per SKU)

### Why Event-Sourced (Not CRUD)

Pricing decisions are a historical stream of business facts. Unlike Product Catalog (which is read-heavy master data), prices change constantly for competitive, promotional, vendor, and regulatory reasons. Event sourcing gives us:
- Complete audit trail of every price change (who, when, from what, to what, why)
- Temporal queries ("what was the price at a specific point in time?")
- Scheduled price changes that survive process restarts via Wolverine durable scheduling
- Safe compensation if a price correction is needed (append `PriceCorrected`, never mutate history)

### Aggregates

**`ProductPrice` (Event-Sourced, One Per SKU):**

The authoritative price record for a single SKU. Created when `ProductAdded` arrives from Catalog BC.

**Stream key:** Deterministic UUID v5 derived from the SKU string (not MD5 — see pricing-event-modeling.md for implementation).

**Status lifecycle:**
- `Unpriced` — stream created from `ProductAdded`; no price set yet; unavailable for purchase
- `Published` — price is live; storefront can display it; Shopping BC can accept add-to-cart
- `Discontinued` — terminal; fires when `ProductDiscontinued` arrives from Catalog

**Domain Events:**
- `ProductRegistered` — stream created (from `ProductAdded` integration event)
- `ProductPriced` — first price set (Unpriced → Published); carries price, floor, ceiling atomically
- `PriceChanged` — subsequent price mutations; carries `OldPrice`, `PreviousPriceSetAt` (for Was/Now)
- `PriceChangeScheduled` — scheduled future change; Wolverine durable message queued
- `ScheduledPriceChangeCancelled` — schedule cancelled (renamed from `PriceChangeScheduleCancelled` for naming consistency with `ScheduledPriceActivated`); stale-message guard discards the Wolverine message when it fires
- `ScheduledPriceActivated` — Wolverine delivers the scheduled message; system-driven (distinct from user-driven `PriceChanged`)
- `FloorPriceSet` — minimum allowed retail price (Merchandising Manager only)
- `CeilingPriceSet` — maximum allowed retail price (MAP compliance)
- `PriceCorrected` — retroactive correction; updates `CurrentPriceView` immediately and publishes `PriceUpdated` integration event (does NOT trigger marketing notifications)
- `PriceDiscontinued` — terminal; clears pending schedules

**`VendorPriceSuggestion` (Event-Sourced, One Per Suggestion):**

Lifecycle of a single vendor-submitted price suggestion, from submission through approval or rejection.

**States:** `Pending | Approved | Rejected | Expired`

**Domain Events:** `VendorPriceSuggestionReceived`, `VendorPriceSuggestionApproved`, `VendorPriceSuggestionRejected`, `VendorPriceSuggestionExpired`

**Auto-expiry:** 7 business days with no review action → `VendorPriceSuggestionExpired` fired by background job.

**`BulkPricingJob` (Wolverine Saga):**

Stateful orchestration of bulk price updates (many SKUs in one job). Approval gate required for jobs exceeding 100 SKUs. Each SKU's price change is recorded on that SKU's own `ProductPrice` event stream. Approval events must be persisted durably with approver identity and timestamp.

### Read Models (Projections)

**`CurrentPriceView`** — Hot path. `ProjectionLifecycle.Inline` (zero lag). Keyed by SKU string for direct O(1) lookup. Serves `GET /api/pricing/products/{sku}` and bulk endpoint. Fields: `Sku`, `BasePrice`, `Currency`, `FloorPrice`, `CeilingPrice`, `PreviousBasePrice`, `PreviousPriceSetAt`, `Status`, `HasPendingSchedule`, `ScheduledChangeAt`, `LastUpdatedAt`.

**`ScheduledChangesView`** — Upcoming price changes calendar. Async daemon. Keyed by `ScheduleId`. Status: `Pending | Activated | Cancelled`.

**`PendingPriceSuggestionsView`** — Pricing Manager review queue. Async daemon. Includes `SuggestedPriceBelowFloor` flag.

**Price History** — Phase 1: raw event stream query (`FetchStreamAsync`). Phase 2: `PriceHistoryView` projection.

### What it receives

**Integration Messages (inbound via RabbitMQ):**
- `ProductAdded` from Product Catalog — creates `ProductPrice` stream in `Unpriced` status
- `ProductDiscontinued` from Product Catalog — transitions to `Discontinued` (terminal), cancels pending schedules
- `VendorPriceSuggestionSubmitted` from Vendor Portal — creates `VendorPriceSuggestion` stream for manager review

### What it publishes

**Integration Messages (outbound via RabbitMQ):**
- `PricePublished` — first price published for a SKU; Shopping BC can now accept add-to-cart; BFF can display price
- `PriceUpdated` — price changed (includes corrections); Shopping BC refreshes cart prices; BFF invalidates cache

### Core Invariants

| Invariant | Type |
|---|---|
| Price > $0.00 | Hard block — no bypass at any entry point |
| Price ≥ FloorPrice (when set) | Hard block |
| Price ≤ CeilingPrice (when set) | Hard block |
| FloorPrice < CeilingPrice (when both set) | Hard block |
| ScheduledFor > UtcNow | Hard block |
| SKU must be registered (ProductRegistered received) before SetPrice | 404 otherwise |
| Price history is append-only | Never mutate historical events; corrections are new events |
| Actor identity on every command | `Guid ChangedBy` required; `Guid.Empty` rejected by FluentValidation |
| >30% price change requires explicit confirmation | HTTP 202 with `requiresConfirmation: true`; re-submit with `ConfirmedAnomaly: true` |

### What it doesn't own

- Product master data — names, descriptions, images, categories (Product Catalog)
- Promotional discounts, campaign rules, BOGO, coupon codes (Promotions BC — future)
- Inventory levels or stock availability (Inventory BC)
- Vendor credentials and tenant management (Vendor Identity BC)

### The Price Snapshot for Orders (Critical Contract)

**Price is frozen at add-to-cart time**, not at checkout time. This is the industry-standard approach (Amazon, Shopify, WooCommerce). Key design decisions:

- **Phase 1:** `CheckoutLineItem.UnitPrice` = price captured when item was added to cart (no changes to existing contract)
- **Phase 2:** Shopping BC calls `IPricingClient.GetCurrentPriceAsync(sku)` internally on `AddItemToCart` (server-authoritative pricing). Client-supplied `UnitPrice` becomes informational only.
- **Cart price TTL:** Cart price snapshots are valid for a configurable window (default: 1 hour). After TTL, BFF refreshes via Pricing BC. Shopping BC fires `PriceRefreshed` event.
- **ADR required:** Price freeze policy contradicts current "price-at-checkout immutability" wording — see ADR for resolution.

Current `AddItemToCart` accepts client-supplied `UnitPrice` — this is a security gap that Phase 1 Pricing BC closes.

### Was/Now Strikethrough Display

`CurrentPriceView` exposes `PreviousBasePrice` and `PreviousPriceSetAt`. The BFF applies display logic:
- Show strikethrough if current price < previous price AND previous price was set within last **30 days**
- Clear strikethrough immediately if price goes back up
- Reset 30-day clock on each subsequent drop
- Log first-shown date per SKU (FTC compliance for reference pricing claims)

The BFF owns the display decision. Pricing BC owns the data.

### Integration with Promotions BC (Future)

- Promotions is a **separate bounded context** — not a sub-domain of Pricing
- Promotions queries Pricing synchronously (`GET /api/pricing/products/{sku}`) for `BasePrice` and `FloorPrice`
- Promotions **cannot** cause effective price to go below `FloorPrice` (hard reject, not silent clip)
- Effective price displayed to customer = BasePrice − PromotionalDiscount (computed by BFF)
- Promotions re-validates floor price at redemption time (not only at promotion creation time)

### Integration Flows

```
[Catalog BC publishes ProductAdded]
  └─> ProductAddedHandler
      └─> ProductRegistered (creates ProductPrice stream, Status: Unpriced)

[Catalog BC publishes ProductDiscontinued]
  └─> ProductDiscontinuedHandler
      └─> PriceDiscontinued (terminal event, cancels pending schedule)

[Pricing Manager — Admin UI]
SetPrice (command)
  └─> SetPriceHandler
      ├─> ProductPriced (Unpriced → Published)
      └─> PricePublished integration event → Shopping BC, BFF, search index

ChangePrice (command)
  └─> ChangePriceHandler
      ├─> [If >30% change and not confirmed] → HTTP 202 requiresConfirmation
      ├─> PriceChanged
      └─> PriceUpdated integration event → Shopping BC, BFF

SchedulePriceChange (command)
  └─> SchedulePriceChangeHandler
      ├─> PriceChangeScheduled
      └─> outgoing.Delay(ActivateScheduledPriceChange, scheduledFor - now)
                     → stored in wolverine_incoming_envelopes (survives restart)

[At scheduled time — Wolverine delivers]
ActivateScheduledPriceChange (internal Wolverine scheduled message)
  └─> ActivateScheduledPriceChangeHandler
      ├─> [Guard: PendingSchedule.ScheduleId == command.ScheduleId? else discard]
      ├─> ScheduledPriceActivated
      └─> PriceUpdated integration event → Shopping BC, BFF

[Vendor Portal publishes VendorPriceSuggestionSubmitted]
  └─> VendorPriceSuggestionSubmittedHandler
      └─> VendorPriceSuggestionReceived (creates VendorPriceSuggestion stream)

[Pricing Manager — Admin UI]
ReviewPriceSuggestion (command — Approve)
  └─> ReviewPriceSuggestionHandler
      ├─> VendorPriceSuggestionApproved
      ├─> Cascades into ChangePrice → PriceChanged + PriceUpdated
      └─> Notification to Vendor Portal (Phase 1: integration event)

ReviewPriceSuggestion (command — Reject)
  └─> ReviewPriceSuggestionHandler
      ├─> VendorPriceSuggestionRejected
      └─> Notification to Vendor Portal (Phase 1: integration event)

[Storefront BFF — query]
GET /api/pricing/products/{sku}
  └─> session.LoadAsync<CurrentPriceView>(sku)
      └─> Returns CurrentPriceView directly (inline projection, zero lag)

GET /api/pricing/products?skus=...
  └─> session.LoadManyAsync<CurrentPriceView>(skuList)
      └─> Single PostgreSQL WHERE id = ANY(@ids) query
          → Sub-100ms p95 for 50 SKUs
```

### HTTP Endpoints

**Query endpoints (hot path — < 100ms p95):**
- `GET /api/pricing/products/{sku}` — single SKU price lookup
- `GET /api/pricing/products?skus=...` — bulk price lookup (comma-separated, 20-50 SKUs)
- `GET /api/pricing/products/{sku}/history` — admin audit trail

**Admin command endpoints:**
- `POST /api/pricing/products/{sku}/price` — set initial price
- `PUT /api/pricing/products/{sku}/price` — change price
- `POST /api/pricing/products/{sku}/scheduled-changes` — schedule future change
- `DELETE /api/pricing/products/{sku}/scheduled-changes/{scheduleId}` — cancel scheduled change
- `PUT /api/pricing/products/{sku}/floor-price` — set floor price
- `PUT /api/pricing/products/{sku}/ceiling-price` — set ceiling price
- `POST /api/pricing/products/{sku}/correct` — retroactive correction

**Vendor suggestions:**
- `GET /api/pricing/suggestions?status=pending` — Pricing Manager review queue
- `POST /api/pricing/suggestions/{suggestionId}/review` — approve or reject

**Bulk pricing:**
- `POST /api/pricing/bulk-jobs` — submit bulk pricing job
- `POST /api/pricing/bulk-jobs/{jobId}/approve` — approve bulk job (≥100 SKUs require this)
- `GET /api/pricing/bulk-jobs/{jobId}` — job status

### Project Structure (Planned)

```
src/
  Pricing/
    Pricing/          # Domain logic (aggregates, events, commands, handlers, projections)
    Pricing.Api/      # HTTP hosting (port 5242)

tests/
  Pricing/
    Pricing.IntegrationTests/
```

See [`docs/planning/pricing-event-modeling.md`](docs/planning/pricing-event-modeling.md) for complete project structure, event definitions, and implementation guidance.

### ADRs Required Before Implementation

1. **[ADR 0016](docs/decisions/0016-uuid-v5-for-natural-key-stream-ids.md) ✅ Written** — UUID v5 for deterministic natural-key event stream IDs (vs. UUID v7 used elsewhere)
2. **ADR: Add-to-cart vs. checkout-time price freeze** — resolves contradiction with current CONTEXTS.md "price-at-checkout immutability" wording; defines cart price TTL
3. **ADR: `Money` value object as canonical monetary representation** — establishes `Money` across all CritterSupply BCs; references Shopping BC `decimal UnitPrice` as technical debt
4. **ADR: `BulkPricingJob` audit trail approach** — event-sourced saga vs. explicit `BulkApprovalRecord` document
5. **ADR: MAP vs. Floor price distinction** — deferred to Phase 2+, but documents the design decision to keep them separate

**UX Review:** See [`docs/planning/pricing-ux-review.md`](docs/planning/pricing-ux-review.md) for the UX Engineer's DX analysis, component decisions (MudDataGrid, side-sheet editing, MudTimeline), and real-time update strategy — these artifacts inform the Pricing section of the Admin Portal.

---

## Future Considerations

The following bounded contexts have been identified as high-value additions for CritterSupply's next phase of growth. They are ordered by business priority: customer-facing gaps first, then commercial infrastructure, then operational intelligence and internal tooling.

> **Note:** The first four (Notifications, Pricing, Promotions, Search) are recommended for the near-term roadmap. Recommendations, Store Credit, and Analytics follow once foundational commercial infrastructure is in place. **Admin Portal** and **Operations Dashboard** address internal tooling needs for the executive team and engineering team — both leveraging the richness of our event-sourced data streams via SignalR and SSR-capable frontends. Reviews, Procurement/Supply Chain, and Shipping/Logistics round out the long-term vision.

---

## Notifications

The Notifications context owns all customer-facing transactional communication — order confirmations, shipping updates, delivery confirmations, return status changes, and refund notices. It reacts to integration events already published by existing BCs and delivers messages through the customer's preferred channel (email, SMS, push notification).

**Why this matters:** CritterSupply's saga already fires every event a Notifications BC needs (`OrderPlaced`, `ShipmentDispatched`, `ShipmentDelivered`, `RefundCompleted`). Without this BC, customers receive zero communication after checkout unless they are actively watching the Blazor storefront — a gap that drives support tickets and erodes trust. Transactional emails see 40–50% open rates. This is the single highest-impact gap in the current customer experience.

**Priority:** 🔴 High — implement immediately after Returns BC is live. Pure choreography: no orchestration needed.

### What it receives

- `OrderPlaced` from Orders — trigger order confirmation message
- `OrderCancelled` from Orders — notify customer of cancellation + refund details
- `ShipmentDispatched` from Fulfillment — send tracking number and carrier link
- `ShipmentDelivered` from Fulfillment — confirm delivery, invite product review
- `ShipmentDeliveryFailed` from Fulfillment — alert customer, initiate re-delivery flow
- `RefundCompleted` from Payments — confirm refund amount and timeline
- `ReturnApproved` from Returns — send return label and instructions
- `ReturnDenied` from Returns — explain rejection reason
- `ReturnCompleted` from Returns — confirm return received and refund triggered
- `ReturnExpired` from Returns — remind customer the return window has closed

### What it publishes

- `NotificationQueued` — notification scheduled for delivery (internal tracking)
- `NotificationDelivered` — message successfully delivered by provider
- `NotificationFailed` — delivery failed; includes reason and retry count

### Core Invariants

- A notification cannot be sent to an opted-out channel (check preferences before dispatch)
- Notification delivery must be idempotent — re-processing an event must not send duplicate messages
- Failed notifications must be retried with exponential backoff before marking as permanently failed
- Notification content must never include raw payment details (PCI compliance)

### What it doesn't own

- Real-time in-app updates (Customer Experience BC via SignalR — see ADR 0013)
- Marketing/promotional email campaigns (Promotions BC or a future Marketing BC)
- Notification preference storage — preferences live in Customer Identity BC
- Template content management (could be a future content management concern)

### Integration Flows

```
OrderPlaced (from Orders via RabbitMQ)
  └─> OrderPlacedNotificationHandler
      ├─> Query Customer Identity: GET /api/customers/{customerId} (email address + prefs)
      ├─> Compose order confirmation email (order summary, items, shipping address)
      └─> Send via email provider (SendGrid, Postmark, etc.)
          └─> NotificationDelivered OR NotificationFailed

ShipmentDispatched (from Fulfillment via RabbitMQ)
  └─> ShipmentDispatchedNotificationHandler
      ├─> Query Customer Identity: GET /api/customers/{customerId}
      ├─> Compose shipping notification (carrier, tracking number, estimated delivery)
      └─> Send via email + optional SMS (if customer opted in)
```

### Key Design Decisions

- **Idempotency:** Handler stores `MessageId` to prevent duplicate sends (at-least-once delivery guarantee from Wolverine)
- **Channel abstraction:** `INotificationChannel` interface (email, SMS, push) — swap providers without changing handlers
- **Template rendering:** Razor templates or external template service (Sendgrid Dynamic Templates) for rich HTML emails
- **Preference check:** Always consult Customer Identity BC before dispatching — never cache preferences

---

## Pricing

The Pricing context owns the rules that determine what customers pay for a product. It separates *commercial truth* (what something costs) from *product truth* (what something is) — a distinction that becomes essential the moment you need to run a sale, apply a volume discount, enforce a vendor's Minimum Advertised Price (MAP) contract, or offer loyalty pricing to repeat customers.

**Why this matters:** Currently, price is a field on the Product Catalog document. This means running a flash sale requires direct database edits, and there is no audit trail of price history. Pricing BC is also the prerequisite for Promotions BC — you cannot calculate a percentage discount without knowing the authoritative base price.

**Priority:** 🔴 High — foundational commercial infrastructure; blocks Promotions BC. Requires Product Catalog BC to be publishing `ProductAdded` integration events (already implemented).

### What it receives

- `ProductAdded` from Product Catalog — create an initial base price record for the new SKU
- `ProductDiscontinued` from Product Catalog — archive pricing rules for the SKU
- Price rule commands from internal merchandising tooling: `SetBasePrice`, `SetMAPFloor`, `SchedulePriceChange`, `ExpirePriceRule`

### Internal lifecycle (per PriceRule)

- Draft — price rule created but not yet active
- Active — currently applied to matching SKUs
- Scheduled — will become active at a future timestamp
- Expired — past its effective window (retained for audit history)

### What it publishes

- `PricePublished` — a new effective price is active for a SKU (other BCs should reflect this)
- `PriceExpired` — a time-limited price has lapsed; base price resumes
- `MAPViolationDetected` — a proposed price is below the vendor's MAP floor (alert to merchandising)

### Core Invariants

- A SKU must have exactly one active base price at any point in time
- Promotional prices cannot be set below the MAP floor (if one is configured for that SKU)
- Price history must be immutable — past prices are never modified, only superseded
- Price changes must take effect at the scheduled time, regardless of system load (use durable scheduling)

### What it doesn't own

- Product descriptions, images, or catalog data (Product Catalog BC)
- Coupon codes or promotion eligibility (Promotions BC)
- Vendor cost basis or margin targets (Vendor Portal BC)
- Display of prices in the storefront (Customer Experience queries Pricing BC)

### Integration Flows

```
SetBasePrice (command from merchandising admin)
  └─> SetBasePriceHandler
      ├─> PriceRule persisted (event-sourced recommended — full audit trail)
      └─> PricePublished → Product Catalog (update displayed price), Shopping (PriceRefreshed for in-cart items)

SchedulePriceChange (command: "set DOG-BOWL-001 to $12.99 starting Friday 00:00 UTC")
  └─> SchedulePriceChangeHandler
      ├─> PriceRule persisted with effective window
      └─> Wolverine delayed message: PriceActivationDue scheduled for effective date
          └─> PricePublished at scheduled time

GetCurrentPrice (query from Shopping / Checkout / Customer Experience)
  └─> Returns: PriceResponse(Sku, BasePrice, EffectivePrice, MAPFloor?, EffectiveFrom, EffectiveTo?)
```

### Key Design Decisions

- **Event sourcing recommended:** Price history has strong audit requirements — every price change should be a persisted event (who changed it, when, why)
- **Price snapshot on cart add:** Shopping BC captures `ItemAdded.UnitPrice` from this BC at the time the item is added (already planned in existing Cart design)
- **Temporal pricing:** Support `EffectiveFrom` / `EffectiveTo` windows for scheduled sales without manual intervention

---

## Promotions

The Promotions context owns the rules for business incentives that influence customer purchase behavior — coupon codes, percentage discounts, fixed-dollar discounts, buy-one-get-one deals, and free shipping thresholds. It validates and applies promotion logic at checkout time, and tracks usage to enforce per-customer and global limits.

**Why this matters:** Promotions are the primary lever for customer acquisition and retention in competitive e-commerce. The Shopping BC already anticipates this BC: `CouponApplied`, `CouponRemoved`, `PromotionApplied`, and `PromotionRemoved` are pre-defined future events on the Cart aggregate (see Shopping BC "Future Events" above). The hooks are already in the domain model — Promotions BC is what gives them authority.

**Priority:** 🔴 High — direct revenue impact; requires Pricing BC to be live first (Promotions BC reads authoritative base prices for discount calculations).

### What it receives

- `CouponApplied` from Shopping — validate the coupon code (is it valid? has this customer used it?)
- `CheckoutInitiated` from Shopping — evaluate which auto-applied promotions are eligible for this cart
- Commands from merchandising tooling: `CreatePromotion`, `CreateCoupon`, `DeactivatePromotion`, `SetUsageLimit`

### Internal lifecycle (per Promotion)

- Draft — promotion configured but not yet live
- Active — currently applicable to eligible carts
- Paused — temporarily suspended (e.g., budget cap reached)
- Expired — past its effective end date (terminal)

### What it publishes

- `PromotionActivated` — promotion is now live (Shopping BC may apply automatically to existing carts)
- `PromotionExpired` — promotion has ended (remove from any active cart applications)
- `CouponValidated` — specific coupon code confirmed valid for this customer and cart
- `CouponRejected` — coupon invalid, expired, already used, or not applicable (includes reason code)
- `DiscountCalculated` — response to cart discount query (which promotions apply, final discount amount)

### Core Invariants

- A coupon code can only be redeemed once per customer (unless explicitly configured for multi-use)
- Promotional discounts cannot reduce item price below MAP floor (requires coordination with Pricing BC)
- Stacking rules must be enforced: some promotions are mutually exclusive (coupon cannot combine with sale price)
- Usage limits must be enforced atomically — high-traffic flash sales can create race conditions
- Promotions must respect their effective date windows precisely

### What it doesn't own

- Base price authority (Pricing BC)
- Coupon delivery or marketing campaign orchestration (future Marketing BC)
- Payment processing of the discounted amount (Payments BC)
- Customer loyalty point accrual (future Loyalty BC)

### Integration Flows

```
CouponApplied (from Shopping via RabbitMQ)
  └─> CouponAppliedHandler
      ├─> Validate: exists, not expired, usage limit not reached
      ├─> Validate: customer eligibility (not already redeemed)
      ├─> CouponValidated → Shopping (apply discount to cart display)
      └─> CouponRejected → Shopping (remove from cart, show error)

CheckoutInitiated (from Shopping via RabbitMQ)
  └─> CheckoutInitiatedPromotionHandler
      ├─> Evaluate all Active promotions for cart contents + customer segment
      ├─> Resolve stacking conflicts (only best promotion, or all stackable ones)
      └─> DiscountCalculated → Orders (embed in CheckoutCompleted for audit trail)
```

---

## Search

The Search context provides fast, relevant product discovery with full-text search and faceted filtering. It owns a denormalized read-model optimized for search queries — separate from the Product Catalog write model — and keeps it eventually consistent by subscribing to product lifecycle events.

**Why this matters:** As the Vendor Portal brings more suppliers and SKUs into the catalog, browse-by-category navigation stops scaling. Customers buying pet products search by natural language ("grain-free puppy food", "self-cleaning litter box"), life stage, dietary need, brand, and price range. Search abandonment (searching, finding nothing, leaving) is one of the highest-cost failure modes in e-commerce. The good news: this BC is nearly pure read-side — it consumes events already published by Product Catalog and Pricing BC, builds a search index, and serves queries.

**Priority:** 🟡 Medium — grows in urgency as the product catalog expands through the Vendor Portal. Sequencing note: integration message contracts (`ProductAdded`, `ProductUpdated`, `ProductDiscontinued`) are already defined in `Messages.Contracts/ProductCatalog/`; Product Catalog handlers will need to publish them before Search BC can subscribe. Optionally depends on Pricing BC for price facets.

### What it receives

- `ProductAdded` from Product Catalog — add new document to search index
- `ProductUpdated` from Product Catalog — refresh existing document in search index
- `ProductDiscontinued` from Product Catalog — remove from search index (or mark as unavailable)
- `PricePublished` from Pricing — update price facet in search index
- `InventoryAvailabilityChanged` from Inventory — update in-stock facet (future Phase 2)

### What it publishes

None. Search BC is a pure read-side BC. It serves `SearchResults` in response to queries but publishes no integration events.

### Core Invariants

- Search index must reflect catalog changes within a bounded staleness window (e.g., < 30 seconds after event)
- Search results must only return Active products (Discontinued products must be excluded)
- Facet counts must reflect the current filtered result set, not the entire catalog
- Search BC must degrade gracefully — if the index is unavailable, fall back to Product Catalog direct query

### What it doesn't own

- Product master data (Product Catalog BC)
- Pricing authority (Pricing BC)
- Inventory levels (Inventory BC — provides data, Search BC projects it)
- Recommendation ranking (Recommendations BC)

### Integration Flows

```
ProductAdded (from Product Catalog via RabbitMQ)
  └─> ProductIndexingHandler
      ├─> Fetch full product details: GET /api/products/{sku}
      ├─> Fetch current price: GET /api/pricing/products/{sku}
      └─> Upsert SearchDocument (Meilisearch / Elasticsearch / Marten full-text)
          Fields: sku, name, description, category, subcategory, brand, tags,
                  price, petType[], lifeStage[], dietaryClaims[], inStock, status

SearchProducts (query from Customer Experience BFF)
  └─> SearchProductsHandler
      ├─> Full-text search + facet filters → SearchIndex query
      └─> Returns: SearchResults(products: SearchDocument[], facets: Facet[], totalCount)
```

### Key Design Decisions

- **Separate search index:** Meilisearch or Elasticsearch recommended for production full-text search with typo tolerance; Marten full-text indexes acceptable for MVP
- **Projection pattern:** Search documents are projections — rebuilt by replaying product events if the index is corrupted or stale
- **Faceted search:** Pet type (Dogs, Cats, Fish, Birds, Reptiles, Small Animals), brand, price range, dietary claims, life stage — all critical for pet supply discovery

---

## Recommendations

The Recommendations context increases average order value by surfacing products that other customers commonly bought alongside the items a customer is viewing or has in their cart. It learns from order co-occurrence patterns — no ML required for an effective initial implementation.

**Why this matters:** In pet supply, cross-sell patterns are highly predictable. A customer buying dry dog food also needs treats, dental chews, and eventually a new bowl. A kitten food buyer needs litter, a litter box, and toys. Surfacing these associations at the right moment (product page, cart sidebar, post-purchase email via Notifications BC) drives measurable revenue lift. Industry benchmarks consistently show recommendations as a top revenue driver for e-commerce platforms — notably for driving cart attachment rate on repeat-purchase consumable categories like pet food.

**Priority:** 🟡 Medium — implement once there is enough order history to produce meaningful associations (typically after first few hundred orders).

### What it receives

- `OrderPlaced` from Orders — extract item co-occurrence pairs from line items (strong purchase signal)
- `ItemAdded` from Shopping — extract cart co-occurrence pairs (weaker browse signal)

### What it publishes

None. Recommendations BC is a pure read-side BC serving synchronous queries.

### Core Invariants

- Recommendations must not surface Discontinued products
- Association scores must decay over time (older orders count less than recent ones)
- Minimum co-occurrence threshold before surfacing a recommendation (avoid spurious associations from single orders)

### What it doesn't own

- Product master data (Product Catalog BC)
- Personalization based on individual customer history (future ML/personalization layer)
- A/B testing of recommendation placement (future experimentation platform)

### Integration Flows

```
OrderPlaced (from Orders via RabbitMQ)
  └─> OrderCoOccurrenceHandler
      ├─> Extract all item pairs from order line items
      └─> Increment co-occurrence score for each pair in ProductAssociation projection

GetRecommendations (query from Customer Experience BFF)
  └─> GetRecommendationsHandler
      ├─> Look up ProductAssociation for given SKU(s)
      ├─> Rank by co-occurrence score (descending)
      ├─> Filter out Discontinued products
      └─> Returns: RecommendationList(sku, recommendations: [{sku, score, name, thumbnail}])
```

---

## Store Credit

The Store Credit context maintains an immutable ledger of credit balances per customer and applies them as a first-class payment instrument at checkout. Credit can originate from return resolutions, order cancellations, promotional grants, or gift card purchases.

**Why this matters:** Offering store credit instead of a cash refund on returns tends to have strong acceptance rates in e-commerce (industry benchmarks suggest the majority of customers will accept store credit when the amount is at or above refund parity). This keeps money in the CritterSupply ecosystem, drives repeat purchases, and reduces the net cost of the returns program. It also unlocks gift cards and referral bonuses as growth mechanics. The Returns BC spec already identifies store credit as a future feature — this BC is the logical next step after Returns is live.

**Priority:** 🟢 Low — high retention value; requires Returns BC to be live first. Lower priority than acquiring new customers (Notifications, Pricing, Promotions) but high impact on repeat-purchase retention.

### What it receives

- `ReturnCompleted` from Returns — issue credit when customer chose store credit resolution
- `OrderCancelled` from Orders — optionally issue store credit instead of triggering a Payments refund
- Commands: `IssueStoreCredit` (from customer service tooling), `RedeemStoreCredit` (from checkout), `ExpireStoreCredit` (scheduled job for credits with expiry)

### Internal lifecycle (per CreditTransaction)

- Issued — credit granted to customer account
- Applied — credit used against a checkout
- Expired — reached expiry date without being applied (terminal)
- Reversed — credit reversed due to order cancellation or fraud (terminal)

### What it publishes

- `StoreCreditIssued` — credit added to customer account (Customer Experience may notify customer)
- `StoreCreditApplied` — credit deducted at checkout (Orders uses to adjust Payments charge)
- `StoreCreditExpired` — credit lapsed; customer may be notified by Notifications BC
- `StoreCreditBalanceUpdated` — current balance changed (Customer Experience may display)

### Core Invariants

- Credit balance cannot go negative (cannot over-redeem)
- Each credit transaction must carry an immutable source reference (return ID, order ID, promo ID)
- Credit redemption must be atomic with checkout — partial redemption (credit + payment card split) must be handled
- The ledger must be append-only — corrections are new transactions, not edits (event sourcing ideal)

### What it doesn't own

- Refund processing to original payment method (Payments BC)
- Return eligibility or inspection (Returns BC)
- Checkout orchestration (Orders BC — Store Credit is a payment instrument it can include)

### Integration Flows

```
ReturnCompleted (from Returns via RabbitMQ, with resolution = StoreCredit)
  └─> ReturnCompletedStoreCreditHandler
      ├─> IssueStoreCredit (internal command: customerId, amount, sourceReturnId)
      └─> StoreCreditIssued → Customer Experience (notify customer of credit)

RedeemStoreCredit (command from Orders BC at checkout)
  └─> RedeemStoreCreditHandler
      ├─> Validate: sufficient balance available
      ├─> Deduct from ledger (append CreditApplied transaction)
      └─> StoreCreditApplied → Orders (reduce amount owed to Payments BC)
```

---

## Analytics

The Analytics context aggregates the event streams from all bounded contexts into business intelligence: conversion funnel performance, product sales velocity, inventory demand forecasting, and customer lifetime value signals. It is the operational nervous system that helps CritterSupply make data-driven decisions as volume grows.

**Why this matters:** Your event-driven architecture is already capturing every meaningful business signal — `CartAbandoned`, `CheckoutInitiated`, `ReservationFailed`, `OrderCancelled`, `ReturnRequested`. Without Analytics BC, this data lives only in event streams and never becomes actionable intelligence. As the business grows, gut-feel decision-making stops working. The buying team needs to know which SKUs are moving, the marketing team needs funnel conversion rates, and the ops team needs to know where orders are failing.

**Priority:** 🟢 Low — high strategic value but invisible to customers; build once there is real order history to learn from. No time-sensitive business loss if deferred — event data is already being captured in existing BC streams.

### What it receives

- Integration events from **all BCs**: Shopping, Orders, Payments, Inventory, Fulfillment, Returns, Customer Experience

### Key read-side projections

- **SalesFunnel** — Cart → Checkout initiated → Order placed → Delivered conversion rates
- **ProductPerformance** — units sold, revenue, return rate, average return reason per SKU
- **InventoryVelocity** — units sold per day per SKU (feeds reorder point calculations)
- **AbandonmentSummary** — cart abandonment rate, checkout abandonment rate, and cart value at abandonment
- **CustomerLifetimeValue** — total spend, order frequency, churn risk signal per customer

### What it publishes

- `TopSellingProductsUpdated` — refreshed list for homepage merchandising (future)
- `InventoryVelocityUpdated` — velocity metrics for Procurement/Supply Chain reorder decisions
- `ReorderPointReached` — SKU velocity indicates stock will run out; triggers Procurement workflow (future)

### Core Invariants

- Analytics projections are eventually consistent — acceptable lag of minutes to hours
- Analytics never becomes a source of truth for transactional decisions (read-only, advisory)
- Customer-level analytics must respect GDPR/CCPA deletion requests (aggregate data survives, PII does not)

### What it doesn't own

- Any transactional data (all state lives in source BCs)
- ML model training (future dedicated data science platform)
- Vendor-specific analytics (Vendor Portal BC owns vendor-scoped views)

---

## Admin Portal

The Admin Portal is **its own Bounded Context** — a dedicated BFF (Backend-for-Frontend) that sits between an internal-facing web application and CritterSupply's portfolio of domain BCs. It does not contain business logic: it acts as an **authenticated internal gateway** that composes reads from multiple domain BCs into role-tailored views, and routes write commands to the appropriate domain BC on behalf of authenticated internal users.

**Is it its own BC?** Yes. Just as Customer Experience BC is a dedicated BFF for customers that aggregates Shopping, Orders, Catalog, and Payments into a unified storefront, Admin Portal is a dedicated BFF for internal users that aggregates all domain BCs into role-tailored admin tooling. This is the BFF (Backend-for-Frontend) pattern applied to the internal domain.

**Does it need a gateway API?** Yes — the `AdminPortal.Api` project *is* that gateway. It exposes a unified REST + SignalR surface for the admin frontend without leaking domain BC internals. The API knows which domain BC to call for each operation, handles authentication and authorization (RBAC), and merges results into admin-specific view models. Domain BCs never need to know about the Admin Portal — they just serve their own HTTP endpoints and publish their events.

**Why this matters:** CritterSupply's event-sourced architecture captures every meaningful business event with a granularity that most traditional e-commerce stacks cannot match. `OrderPlaced`, `ReservationFailed`, `ShipmentDeliveryFailed`, `ReturnRequested`, `PaymentFailed` — these are not just audit logs, they are real-time signals about what is happening in the business *right now*. An Admin Portal surfaces these signals as actionable dashboards and management tools, enabling the team to spot issues, manage escalations, and make decisions without waiting for a nightly batch report.

This context is also an opportunity to explore **non-Blazor frontend technology**: because internal users run on desktop browsers in a managed environment, it is well-suited to a React or Vue.js SPA with SSR (Next.js or Nuxt.js), while still benefiting from Wolverine's SignalR transport for real-time metric pushes. The backend API and SignalR hub are framework-agnostic — swapping the frontend technology does not change the .NET side.

**Priority:** 🟡 Medium — unlocks significant operational value once Analytics BC is providing projections; doubles as a reference architecture showcase for non-Blazor + SignalR patterns.

**Event Modeling:** See [docs/planning/admin-portal-event-modeling.md](docs/planning/admin-portal-event-modeling.md) for the full planning output.

---

### Architecture: Gateway / BFF Pattern

```
┌─────────────────────────────────────────────────────┐
│              AdminPortal.Web (Frontend)              │
│    React (Next.js SSR) / Vue (Nuxt SSR) / Blazor    │
│                                                      │
│  - Role-aware sidebar (only shows permitted views)   │
│  - SignalR client: connects to /hub/admin            │
│  - HTTP client: calls /api/admin/* endpoints         │
└───────────────────────┬─────────────────────────────┘
                        │ HTTPS
┌───────────────────────▼─────────────────────────────┐
│           AdminPortal.Api  (Gateway / BFF)           │
│                Port: 5243                            │
│                                                      │
│  Authentication: JWT Bearer (AdminIdentity BC)       │
│  Authorization: Role-based (RBAC policy per route)   │
│  SignalR Hub: /hub/admin (Wolverine opts.UseSignalR) │
│                                                      │
│  ┌──────────────────────────────────────────────┐   │
│  │  Queries (HTTP GET → domain BC HTTP clients) │   │
│  │  Commands (HTTP POST → domain BC HTTP POST)  │   │
│  │  Notification Handlers (RabbitMQ → SignalR)  │   │
│  └──────────────────────────────────────────────┘   │
└───────────────────────┬─────────────────────────────┘
         │              │              │
         ▼              ▼              ▼  (HTTP calls to domain BCs)
  ┌─────────────┐ ┌──────────┐ ┌───────────────┐
  │ Product     │ │ Pricing  │ │  Inventory    │
  │ Catalog BC  │ │   BC     │ │     BC        │
  │   :5133     │ │ (future) │ │   :5233       │
  └─────────────┘ └──────────┘ └───────────────┘
         │              │              │
  ┌─────────────┐ ┌──────────┐ ┌───────────────┐
  │ Orders BC   │ │ Customer │ │  Analytics    │
  │   :5231     │ │ Identity │ │     BC        │
  └─────────────┘ └──────────┘ └───────────────┘
```

**Key gateway behaviors:**
- Every request is authenticated (JWT Bearer from AdminIdentity BC)
- Every mutating request (POST/PUT/DELETE) carries `adminUserId` for audit trail
- Gateway never stores business data — it queries domain BCs on each request (or reads its own cached event-driven projections for dashboard metrics)
- Gateway never transforms business rules — it passes commands to domain BCs and relays the result
- Error responses from domain BCs are translated into admin-facing problem details (not raw BC errors)

---

### Internal User Roles (RBAC)

Admin Portal uses **role-based access control** with discrete personas matching real job functions at CritterSupply. Each role has a fixed permission set enforced at the API layer.

| Role | Persona | Primary Job |
|---|---|---|
| `CopyWriter` | Content / copy writer | Manage product copy — descriptions, marketing text, display names |
| `PricingManager` | Pricing / vendor manager | Set and schedule product prices |
| `WarehouseClerk` | Warehouse / inventory worker | Adjust stock levels, receive inbound goods |
| `CustomerService` | Customer service representative | Look up customers and orders, issue credits and cancellations |
| `OperationsManager` | Operations manager | Cross-system dashboard, alert acknowledgement, fulfillment oversight |
| `Executive` | Business executive / leadership | Read-only strategic dashboards and report exports |
| `SystemAdmin` | Internal platform administrator | User management, BC health status, all capabilities |

#### Role Permission Matrix

| Capability | CopyWriter | PricingManager | WarehouseClerk | CustomerService | OperationsManager | Executive | SystemAdmin |
|---|---|---|---|---|---|---|---|
| Edit product description / copy | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ |
| Publish / unpublish product | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ | ✅ |
| Set / schedule product price | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | ✅ |
| Adjust inventory quantity (manual) | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ | ✅ |
| Receive inbound stock | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ | ✅ |
| Acknowledge low-stock alert | ❌ | ❌ | ✅ | ❌ | ✅ | ❌ | ✅ |
| Customer lookup (read) | ❌ | ❌ | ❌ | ✅ | ✅ | ❌ | ✅ |
| View order details | ❌ | ❌ | ❌ | ✅ | ✅ | ❌ | ✅ |
| Cancel order | ❌ | ❌ | ❌ | ✅ | ✅ | ❌ | ✅ |
| Issue store credit | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ | ✅ |
| View executive dashboard | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ |
| Export reports | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ |
| View operations alerts | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ | ✅ |
| Manage admin users | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ |

---

### Role-Specific Workflows

#### 🖊️ Copy Writer — Product Content Management

A content writer's job is to ensure every product has accurate, compelling copy. They do not manage pricing, inventory, or orders.

**Primary view:** Product content editor — searchable SKU list with inline edit capability

**Typical workflow:**
```
1. Search: GET /api/admin/products?q={term}&status=published
   └─> AdminPortal.Api: query Product Catalog BC: GET /api/catalog/products?q={term}
       └─> Return ProductContentListView (sku, name, currentDescription, lastEditedAt, lastEditedBy)

2. Edit: PUT /api/admin/products/{sku}/content
   Body: { "description": "...", "displayName": "...", "adminUserId": "..." }
   └─> AdminPortal.Api validates (non-empty, max length)
       └─> POST /api/catalog/products/{sku}/description (Product Catalog BC admin endpoint)
           └─> Product Catalog BC: UpdateProductDescription command
               └─> ProductDescriptionUpdated event → search index rebuilt (Search BC, when live)
```

**What they see:** Product name, current description, last-edited-by, last-edited-at. **They do not see:** price, inventory counts, order history, customer data.

**BCs involved:** Product Catalog (write), Product Catalog (read)

---

#### 💰 Pricing Manager — Price Management

A pricing manager sets base prices and schedules future price changes (sale pricing, price increases). They do not manage product copy or inventory.

**Primary view:** Pricing dashboard — SKU list with current price, next scheduled change, and price history

**Typical workflows:**
```
Set a base price immediately:
PUT /api/admin/products/{sku}/price
Body: { "newPrice": 24.99, "reason": "...", "adminUserId": "..." }
└─> AdminPortal.Api
    └─> POST /api/pricing/products/{sku}/price (Pricing BC admin endpoint)
        └─> Pricing BC: SetBasePrice command → BasePriceSet event
            └─> Shopping BC reads updated price on next AddToCart (price frozen at add-time)

Schedule a future price change (e.g., holiday sale):
POST /api/admin/products/{sku}/price/schedule
Body: { "newPrice": 19.99, "effectiveAt": "2026-11-29T00:00:00Z", "expiresAt": "2026-12-02T23:59:59Z", "reason": "Black Friday" }
└─> AdminPortal.Api
    └─> POST /api/pricing/products/{sku}/price/schedule (Pricing BC admin endpoint)
        └─> Pricing BC: SchedulePriceChange command → PriceChangeScheduled event
            └─> Background job applies at effectiveAt; reverts at expiresAt

View price history for audit:
GET /api/admin/products/{sku}/price/history
└─> AdminPortal.Api
    └─> GET /api/pricing/products/{sku}/history (Pricing BC)
        └─> Return PriceHistoryView (price, effectiveAt, reason, setBy)
```

**What they see:** Current price, price history with reasons, scheduled changes, upcoming expirations. **They do not see:** product descriptions, inventory counts, customer or order data.

**BCs involved:** Pricing (write + read)

---

#### 📦 Warehouse Clerk — Inventory Management

A warehouse worker records physical inventory reality: adjusting quantities after cycle counts, receiving inbound purchase orders, and acknowledging low-stock alerts.

**Primary view:** Inventory dashboard — SKU list with current quantities, reservation holds, warehouse location

**Typical workflows:**
```
Manual inventory adjustment (cycle count correction):
POST /api/admin/inventory/{sku}/adjust
Body: { "warehouseId": "...", "adjustmentQuantity": -3, "reason": "DamagedGoods", "adminUserId": "..." }
└─> AdminPortal.Api
    └─> POST /api/inventory/{sku}/adjust (Inventory BC admin endpoint)
        └─> Inventory BC: AdjustInventory command → InventoryAdjusted event
            └─> Available quantity updated; LowStockDetected emitted if threshold crossed

Receive inbound stock (purchase order arrival):
POST /api/admin/inventory/{sku}/receive
Body: { "warehouseId": "...", "quantity": 100, "purchaseOrderRef": "PO-12345", "adminUserId": "..." }
└─> AdminPortal.Api
    └─> POST /api/inventory/{sku}/replenish (Inventory BC admin endpoint)
        └─> Inventory BC: ReplenishStock command → StockReplenished event
            └─> Analytics BC updates InventoryVelocity; Search BC may update in-stock filter

Acknowledge a low-stock alert:
POST /api/admin/inventory/alerts/{alertId}/acknowledge
Body: { "notes": "Reorder placed, ETA 5 days", "adminUserId": "..." }
└─> AdminPortal.Api
    └─> POST /api/inventory/alerts/{alertId}/acknowledge (Inventory BC)
        └─> Inventory BC: AcknowledgeLowStockAlert → LowStockAlertAcknowledged
            └─> Alert removed from active alert list; audit record preserved
```

**What they see:** Available quantity, reserved quantity, low-stock alerts, inbound receiving history, per-warehouse breakdown. **They do not see:** prices, product descriptions, customer or order data.

**BCs involved:** Inventory (write + read)

---

#### 🎧 Customer Service Representative — Order & Customer Tooling

A customer service rep handles inbound customer issues — looking up orders, cancelling unfulfillable orders, and issuing goodwill store credit when things go wrong.

**Primary view:** Customer search → order history → order detail with current saga state

**Typical workflows:**
```
Customer lookup:
GET /api/admin/customers?email={email}
└─> AdminPortal.Api
    ├─> GET /api/identity/customers?email={email} (Customer Identity BC)
    └─> GET /api/orders?customerId={id} (Orders BC — recent orders)
        └─> Return CustomerServiceView: customer profile + recent order summaries

Order detail:
GET /api/admin/orders/{orderId}
└─> AdminPortal.Api
    ├─> GET /api/orders/{orderId} (Orders BC — current saga state)
    └─> GET /api/payments/{orderId} (Payments BC — payment attempts)
        └─> Return OrderDetailView: saga timeline, payment status, fulfillment status, return info

Cancel an order (customer requested, pre-shipment):
POST /api/admin/orders/{orderId}/cancel
Body: { "reason": "CustomerRequested", "notes": "...", "adminUserId": "..." }
└─> AdminPortal.Api
    └─> POST /api/orders/{orderId}/cancel (Orders BC)
        └─> Order saga: CancelOrder command → OrderCancelled → compensation chain begins
            └─> Payments BC: RefundInitiated → customer refunded
            └─> Inventory BC: ReservationReleased

Issue store credit (goodwill gesture):
POST /api/admin/customers/{customerId}/credit
Body: { "amount": 15.00, "reason": "ShipmentDelay", "expiresInDays": 180, "adminUserId": "..." }
└─> AdminPortal.Api
    └─> POST /api/credits (Store Credit BC)
        └─> Store Credit BC: IssueStoreCredit → StoreCreditIssued event
            └─> Credit appears in customer's wallet on next checkout
```

**What they see:** Customer profile, order history, current order saga state, payment attempts, return history. **They do not see:** price management tools, inventory adjustments, executive dashboards.

**BCs involved:** Customer Identity (read), Orders (read + write), Payments (read), Returns (read), Store Credit (write)

---

#### 📊 Operations Manager — Live Operations Dashboard

An operations manager monitors the entire system health in real-time and can intervene on alerts. They have the broadest operational scope but are still not a system administrator.

**Primary view:** Live operations dashboard with SignalR-pushed metric cards

**Capabilities:**
- All Customer Service Rep capabilities (order management, customer lookup)
- View and acknowledge inventory alerts across all SKUs (not just their products)
- Live order pipeline: how many orders are in each saga state right now
- Fulfillment throughput: picked/packed/shipped per hour across all warehouses
- Payment failure rate trend: flag provider issues or fraud spikes
- Export operational reports (CSV/Excel) for any date range

**Real-time updates via SignalR:**
```
Every OrderPlaced event:
  └─> OrderPlacedAdminHandler → Publish OrderCountUpdated → SignalR role:operations → increment counter

Every PaymentFailed event:
  └─> PaymentFailedAdminHandler → Publish AlertRaised (severity=Warning) → SignalR role:operations

Every InventoryLow event:
  └─> InventoryLowAdminHandler → Publish AlertRaised (severity=Info) → SignalR role:operations + role:warehouseclerk
```

---

#### 📈 Executive — Strategic Dashboard & Reports

An executive sees only pre-aggregated, high-level metrics. They cannot modify any data. Their primary value is understanding business health at a glance.

**Primary view:** Executive dashboard with key business KPIs

**What they see:**
- Revenue today / this week / this month / this year (live counter via SignalR for today's value)
- Orders placed, fulfilled, cancelled (pipeline view — percentages, not saga states)
- Top-selling SKUs by revenue and by units (from Analytics BC `ProductPerformance` projection)
- Customer acquisition this week/month (new registrations from Customer Identity BC)
- Inventory health summary: how many SKUs have active low-stock alerts
- Return rate this month (returns / orders ratio)

**Report exports:**
```
GET /api/admin/reports/sales?from={date}&to={date}&format=csv
└─> AdminPortal.Api
    └─> GET /api/analytics/sales?from={date}&to={date} (Analytics BC)
        └─> Stream CSV response to browser download
```

**What they do NOT see:** Individual order details, customer PII, product content edit tools, price management, inventory detail.

---

### What it receives

**Integration Messages (consumed from RabbitMQ — for real-time dashboard pushes via SignalR):**
- `OrderPlaced` — increment live order counter; push to `role:operations` and `role:executive`
- `OrderCancelled` — update active order count; flag if cancellation rate spikes
- `PaymentFailed` — push `AlertRaised` to `role:operations`
- `ReservationFailed` — push stockout alert to `role:operations` and `role:warehouseclerk`
- `InventoryLow` — push low-stock alert to `role:warehouseclerk` and `role:operations`
- `ShipmentDeliveryFailed` — push to `role:customerservice` alert queue
- `ReturnRequested` — add to return review queue for `role:customerservice`
- `RefundFailed` — push high-severity alert to `role:operations` (requires manual intervention)

**HTTP Queries routed to domain BCs (on demand, per request):**
- Product Catalog BC: product list, product detail, content history
- Pricing BC: current prices, price history, scheduled changes
- Inventory BC: stock levels, reservation pipeline, alert history
- Orders BC: order detail, order search by customer, saga state history
- Customer Identity BC: customer lookup by email, address history
- Payments BC: payment attempts per order
- Returns BC: return requests per order/customer
- Analytics BC: `SalesFunnel`, `ProductPerformance`, `InventoryVelocity`, `CustomerLifetimeValue`

### What it publishes

**Commands sent to domain BCs (HTTP POST — admin initiates, domain BC executes):**
- `UpdateProductDescription` → Product Catalog BC (CopyWriter role)
- `ChangeProductStatus` → Product Catalog BC (OperationsManager, SystemAdmin)
- `SetBasePrice` / `SchedulePriceChange` → Pricing BC (PricingManager role)
- `AdjustInventory` → Inventory BC (WarehouseClerk role)
- `ReplenishStock` → Inventory BC (WarehouseClerk role)
- `AcknowledgeLowStockAlert` → Inventory BC (WarehouseClerk, OperationsManager)
- `CancelOrder` → Orders BC (CustomerService, OperationsManager)
- `IssueStoreCredit` → Store Credit BC (CustomerService role)

**SignalR Messages pushed to connected admin clients (via Wolverine `opts.UseSignalR()`):**
- `LiveMetricUpdated` — revenue counter, order count (pushed to `role:executive` and `role:operations`)
- `AlertRaised` — payment failure, stockout, delivery failure (pushed to appropriate role groups)
- `OrderCountUpdated` — saga state distribution update (pushed to `role:operations`)
- `LowStockAlertRaised` — specific SKU below threshold (pushed to `role:warehouseclerk` and `role:operations`)

### Core Invariants

- Admin Portal does NOT contain business logic — all mutations are delegated to domain BCs
- Every mutating request includes `adminUserId` in the payload; domain BCs record it in their events for audit trail
- RBAC is enforced at the `AdminPortal.Api` layer (authorization policies per endpoint); frontend role-scoping is cosmetic only
- SignalR hub (`/hub/admin`) requires authenticated JWT; role claims determine which hub groups the connection joins
- Admin Portal does not persist its own transactional state — dashboard projections are cached event-driven read models; all source-of-truth data lives in domain BCs
- PII accessed via Admin Portal (customer emails, addresses) must be logged per access for GDPR compliance audit

### What it doesn't own

- Business domain logic (all in respective domain BCs)
- Analytics projections (Analytics BC — Admin Portal consumes them)
- Customer-facing UI (Customer Experience BC)
- Vendor-facing portal (Vendor Portal BC)
- Admin user identity and authentication (Admin Identity BC — separate, analogous to Customer Identity and Vendor Identity)

### Integration Flows

**Live Executive Dashboard — Revenue Counter:**
```
[Orders BC domain logic]
CheckoutCompleted → Order saga starts → OrderPlaced published to RabbitMQ

[Admin Portal notification handler]
OrderPlaced (from RabbitMQ exchange)
  └─> OrderPlacedAdminHandler (Wolverine handler in AdminPortal domain assembly)
      ├─> Update AdminMetrics projection (today's order count + revenue estimate)
      └─> Wolverine SignalR transport:
          └─> Publish LiveMetricUpdated to hub groups: role:executive, role:operations
              └─> React/Vue dashboard: revenue counter animates, order pipeline bar updates
```

**Copy Writer — Edit Product Description:**
```
CopyWriter logs in → JWT issued with role:CopyWriter

POST /api/admin/products/{sku}/content
Authorization: Bearer {jwt}
Body: { "description": "Updated product copy...", "adminUserId": "uuid" }

AdminPortal.Api:
├─> [Authorize(Policy = "RequireCopyWriter")] attribute — rejects other roles with 403
├─> Validate: description non-empty, max 5000 chars
└─> HTTP POST /api/catalog/products/{sku}/description (Product Catalog BC)
    └─> Product Catalog BC: UpdateProductDescription command
        └─> ProductDescriptionUpdated event
            └─> Event carries adminUserId — audit trail in Product Catalog event stream
            └─> Search BC handler updates search index (when live)
```

**Warehouse Clerk — Receive Inbound Stock:**
```
WarehouseClerk scans inbound pallet → enters PO reference in portal

POST /api/admin/inventory/{sku}/receive
Authorization: Bearer {jwt}  (role:WarehouseClerk)
Body: { "warehouseId": "WH-01", "quantity": 50, "purchaseOrderRef": "PO-2026-0042" }

AdminPortal.Api:
├─> [Authorize(Policy = "RequireWarehouseClerk")]
├─> Validate: quantity > 0, warehouseId exists
└─> HTTP POST /api/inventory/{sku}/replenish (Inventory BC)
    └─> Inventory BC: ReplenishStock command
        └─> StockReplenished event (carries adminUserId, purchaseOrderRef)
            └─> LowStockAlert deactivated if threshold now exceeded
            └─> Analytics BC: InventoryVelocity projection updated
```

**Customer Service — Order Cancellation:**
```
Customer calls CS rep, wants to cancel order

GET /api/admin/customers?email=customer@example.com
└─> AdminPortal.Api: fan-out
    ├─> Customer Identity BC: GET /api/identity/search?email=...
    └─> Orders BC: GET /api/orders?customerId={id}&limit=10
        └─> Return CustomerServiceView (customer info + recent orders)

Rep confirms order is still cancellable (saga state check)

POST /api/admin/orders/{orderId}/cancel
Body: { "reason": "CustomerRequested", "adminUserId": "cs-rep-uuid" }
└─> AdminPortal.Api → Orders BC: POST /api/orders/{orderId}/cancel
    └─> Order saga handles CancelOrder:
        ├─> OrderCancelled event published
        ├─> ReservationReleased → Inventory BC restores stock
        └─> RefundInitiated → Payments BC processes refund
```

### Project Structure (Planned)

```
src/
  Admin Portal/
    AdminPortal/                          # Domain project (regular SDK)
      AdminPortal.csproj                  # References: Messages.Contracts only
      Clients/                            # HTTP client interfaces (to domain BCs)
        IProductCatalogAdminClient.cs     # product list, content update
        IPricingAdminClient.cs            # current price, history, schedule
        IInventoryAdminClient.cs          # stock levels, adjust, replenish
        IOrdersAdminClient.cs             # order search, detail, cancel
        ICustomerIdentityAdminClient.cs   # customer search
        IPaymentsAdminClient.cs           # payment history per order
        IReturnsAdminClient.cs            # return history per order
        IAnalyticsClient.cs              # aggregated projections
        IStoreCreditAdminClient.cs        # issue credit
      Composition/                        # Admin view models (aggregated from domain BCs)
        AdminDashboardView.cs
        CustomerServiceView.cs            # customer profile + recent orders
        OrderDetailView.cs                # saga timeline + payment + fulfillment
        ProductContentView.cs             # sku, name, description, lastEditedBy
        PricingDashboardView.cs           # sku, currentPrice, history, scheduled
        InventoryDashboardView.cs         # sku, available, reserved, alerts
        ReportView.cs                     # CSV/Excel export models
      Notifications/                      # RabbitMQ integration message handlers → SignalR
        IAdminPortalMessage.cs            # Wolverine SignalR marker interface
        OrderPlacedAdminHandler.cs        # → LiveMetricUpdated to role:executive, role:operations
        PaymentFailedAdminHandler.cs      # → AlertRaised to role:operations
        InventoryLowAdminHandler.cs       # → LowStockAlertRaised to role:warehouseclerk + role:operations
        ShipmentDeliveryFailedAdminHandler.cs  # → AlertRaised to role:customerservice
        ReturnRequestedAdminHandler.cs    # → ReturnQueueUpdated to role:customerservice
      Projections/                        # Lightweight cached metrics (not authoritative)
        AdminMetricsProjection.cs         # today's order count, revenue estimate (for live counter)

    AdminPortal.Api/                      # API project (Web SDK)
      AdminPortal.Api.csproj              # References: AdminPortal, Messages.Contracts
      Program.cs                          # Wolverine + Marten + SignalR + RBAC auth setup
      appsettings.json                    # connection strings, downstream BC base URLs
      Properties/launchSettings.json      # Port: 5243
      Authorization/
        AdminRoles.cs                     # role name constants
        AdminPolicies.cs                  # ASP.NET Core policy definitions per role
      Queries/                            # HTTP GET endpoints (compose from domain BCs)
        GetDashboardView.cs               # executive dashboard
        GetOperationsView.cs              # live operations
        GetCustomerServiceView.cs         # customer + order lookup
        GetProductContentList.cs          # copy writer view
        GetPricingDashboard.cs            # pricing manager view
        GetInventoryDashboard.cs          # warehouse clerk view
        GetReportExport.cs                # CSV/Excel export
      Commands/                           # HTTP POST endpoints (route to domain BCs)
        UpdateProductContent.cs           # → Product Catalog BC
        SetProductPrice.cs                # → Pricing BC
        SchedulePriceChange.cs            # → Pricing BC
        AdjustInventory.cs                # → Inventory BC
        ReceiveStock.cs                   # → Inventory BC
        AcknowledgeAlert.cs               # → Inventory BC
        CancelOrderAdmin.cs               # → Orders BC
        IssueCreditAdmin.cs               # → Store Credit BC
      Clients/                            # HTTP client implementations
        ProductCatalogAdminClient.cs
        PricingAdminClient.cs
        InventoryAdminClient.cs
        OrdersAdminClient.cs
        CustomerIdentityAdminClient.cs
        AnalyticsClient.cs
        StoreCreditAdminClient.cs
      AdminPortalHub.cs                   # SignalR hub at /hub/admin

    AdminPortal.Web/                      # Frontend (choose one)
      # Option A: Blazor Server — consistent with existing C# stack
      # Option B: React (Next.js SSR) — recommended; richer component ecosystem for dashboards
      # Option C: Vue.js (Nuxt.js SSR) — strong alternative to React
      # Port: 5244
      # Connects to AdminPortal.Api SignalR hub at /hub/admin
      # Uses @microsoft/signalr npm package (Options B/C) or Microsoft.AspNetCore.SignalR.Client (Option A)
```

### Admin Identity BC (Prerequisite)

Admin Portal requires its own internal user identity system — separate from Customer Identity and Vendor Identity. This is a lightweight additional BC:

**Why separate?** Admin users are employees/contractors, not customers. They have role-based access, may require corporate SSO integration, and their authentication audit trail must be kept separate from customer data.

**Options (to decide during implementation cycle):**
1. **Lightweight internal user store** — ASP.NET Core Identity backed by a separate Postgres schema (`adminidentity`); same pattern as Customer Identity and Vendor Identity
2. **Corporate SSO / IdP integration** — Microsoft Entra ID (Azure AD), Okta, or Auth0; admin users log in with their corporate credentials; JWT issued by IdP and validated by AdminPortal.Api. Recommended for teams > 20 people.
3. **Hybrid** — Local admin store for initial development; corporate SSO integration in a later cycle

For reference architecture purposes, Option 1 (local store, same pattern as Customer Identity) is the simplest starting point and avoids external service dependencies in local development.

### Key Design Decisions

- **Gateway, not proxy:** AdminPortal.Api composes and translates — it does not merely proxy requests to domain BCs. It may fan out to multiple BCs in parallel and merge results (e.g., customer lookup fans out to Customer Identity + Orders).
- **SignalR from day one:** Real-time alerts and metric updates are primary value for OperationsManager and WarehouseClerk roles. Use `opts.UseSignalR()` from the start — lesson learned from Customer Experience BC's SSE → SignalR migration debt.
- **Role-scoped hub groups:** Each SignalR connection joins groups matching its JWT role claims (`role:executive`, `role:operations`, `role:customerservice`, `role:warehouseclerk`). Alerts are targeted to the role(s) that need to act on them.
- **Audit trail via domain events:** Admin mutations include `adminUserId` in the command body. Domain BCs record this in their events. Admin Portal does not need its own audit log — the event streams are the audit trail.
- **SSR for initial load:** Executive and operations dashboards are data-heavy; SSR (Next.js/Nuxt.js) ensures meaningful content renders before JavaScript hydrates. Live metric updates layer on top via SignalR hydration.
- **No PII in SignalR messages:** SignalR push messages contain IDs and counts, never customer names, emails, or order details. PII is fetched on demand via authenticated HTTP GET only.

---

## Operations Dashboard

The Operations Dashboard context provides **developer-facing and SRE-facing observability** into the event-driven system itself — not the business metrics (that's Admin Portal / Analytics), but the *technical health* of the platform: message processing rates, saga state distribution, dead-letter queues, handler failure rates, and event replay tooling.

**Why this matters:** As CritterSupply grows, the engineering team needs visibility into how the event-driven infrastructure is behaving — not just whether services are up, but whether messages are flowing, sagas are completing, and compensating transactions are working as expected. Wolverine and Marten already expose rich instrumentation via OpenTelemetry; this context is the UI layer that makes that data actionable for on-call engineers, not just a Jaeger trace dump.

This is also a developer experience (DevEx) investment: giving new engineers joining the team a live view of the event flow helps them learn the system faster. Watching an order saga progress in real-time — `OrderPlaced` → `ReservationConfirmed` → `PaymentCaptured` → `ReservationCommitted` → `FulfillmentRequested` — is far more instructive than reading documentation.

**Priority:** 🟢 Low — primarily a DevEx and operational maturity concern; Jaeger + OpenTelemetry covers immediate observability needs. Build when the team is large enough that on-call rotation needs better tooling.

### Architecture Pattern: Event Stream Visualization + System Health BFF

**Frontend technology:**
- **React (Next.js) recommended** — real-time event stream visualization benefits from a rich component ecosystem (recharts, d3) that is more mature in React/Vue than Blazor; SSR ensures the dashboard renders quickly on load
- **SignalR** for live event stream feed (push events from Wolverine message bus to the dashboard as they process)

### Dashboard Views (Engineering / DevEx Audience)

**Event Stream Monitor:**
- Live feed of integration messages flowing through RabbitMQ, annotated with handler name and processing result
- Filter by BC, message type, or correlation ID (e.g., trace a specific `orderId` through all handlers)
- Color-coded: success (green), retry (yellow), dead-letter (red)

**Saga State Explorer:**
- Visual state machine diagram for Order saga, showing current state distribution (how many sagas are in each state right now)
- Drill into a specific saga by orderId: full state transition history with timestamps
- Flag sagas that have been in a non-terminal state longer than expected (configurable threshold; suggested default: 1 hour for most states, 24 hours for `PendingPayment`)

**Dead Letter Queue (DLQ) Dashboard:**
- List of messages in Wolverine's dead-letter queue (failed after all retries)
- View message content, failure reason, retry history
- Manual replay trigger (requeue a dead-lettered message for reprocessing)
- Alert when DLQ depth exceeds threshold

**Handler Performance:**
- P50/P95/P99 handler processing latency per message type
- Handler error rate over time (rolling 1h, 24h, 7d)
- Message throughput per queue (messages/second)

**Database Health (Marten / EF Core):**
- Marten event store stream counts per BC
- EF Core migration status per bounded context
- Projection rebuild status (if a projection is being rebuilt, show progress)

### What it receives

**OpenTelemetry spans (from Wolverine / Marten instrumentation):**
- Wolverine message processing spans (handler name, message type, processing time, success/failure)
- Marten event store write spans (stream id, event type, sequence number)
- RabbitMQ publish/consume spans

**Wolverine Dead Letter Events:**
- Dead-lettered message notifications from Wolverine's persistence layer

### What it publishes

**SignalR Messages (to connected engineers):**
- `MessageProcessed` — a handler completed (success or failure); stream to the live event feed
- `SagaStateChanged` — an Order saga transitioned state; update the state machine visualization
- `DlqMessageAdded` — a new dead-letter appeared; alert the engineer on duty
- `AlertRaised` — DLQ depth, handler error rate, or saga stuck threshold exceeded

### Core Invariants

- This context is **read-only** for most operations; only DLQ replay is a write operation
- DLQ replay requires elevated role (on-call engineer or SRE) — not available to all developers
- Event stream data is ephemeral (no long-term storage in this BC) — historical data lives in OpenTelemetry backends (Jaeger, Prometheus)
- This BC must not have a meaningful performance footprint on the production message bus — use a separate subscription or tap

### What it doesn't own

- OpenTelemetry data storage (Jaeger, Prometheus — external infrastructure)
- Alerting and on-call routing (PagerDuty, OpsGenie — external)
- Business metrics (Admin Portal BC, Analytics BC)
- Wolverine persistence or dead-letter policy (those are Wolverine configuration concerns)

### Integration Flows

```
[Wolverine middleware / OpenTelemetry instrumentation]
Every handler invocation
  └─> OTel span emitted → Jaeger (persisted tracing)
      └─> Operations Dashboard subscribes to OTel span events (or Wolverine's ISagaListener)
          └─> MessageProcessedHandler
              └─> Publish MessageProcessed → SignalR → live event stream feed

[DLQ polling or Wolverine event hook]
DeadLetteredMessageDetected
  └─> DlqMessageHandler
      ├─> Store in Operations Dashboard ephemeral read model (last 24h)
      └─> Publish DlqMessageAdded → SignalR → DLQ dashboard alert

[Engineer triggers replay from dashboard]
POST /api/ops/dlq/{messageId}/replay
  └─> RequeueDlqMessage (Wolverine API call to requeue message)
      └─> MessageRequeuedConfirmation → dashboard UI
```

### Key Design Decisions

- **React (Next.js) strongly preferred** for this BC: the data visualization ecosystem (recharts, d3, react-flow for state machine diagrams) is significantly more mature than what's available in Blazor's component libraries
- **SSR for initial state:** On page load, the dashboard renders the current DLQ contents and saga state distribution from a server-rendered snapshot, then hydrates with live SignalR updates — no loading spinner on the critical-path on-call view
- **Separate deployment from Admin Portal:** Operations Dashboard is a developer tool, not a business tool. Different auth requirements (engineer credentials vs. business user credentials), different deployment cadence, different audience.
- **OpenTelemetry integration:** Wolverine 5+ and Marten 8+ emit OTel spans natively. This BC can subscribe to those spans via an OTel collector pipeline rather than intercepting the message bus directly.

---

The Reviews context owns customer product ratings and reviews — the social proof layer that influences purchase decisions and surfaces quality signals about products and vendors.

**Status:** 📋 Future — lower priority than transactional and commercial infrastructure.

**Key concepts:** `ReviewSubmitted`, `ReviewApproved` / `ReviewRejected` (moderation), `ReviewFlagged`, star rating aggregate per SKU. Integration with Notifications BC (trigger review request after `ShipmentDelivered`), Product Catalog BC (display average rating on listings).

---

## Procurement / Supply Chain

The Procurement context owns the inbound supply chain — purchase orders to vendors, receiving inventory at warehouses, and reorder automation triggered by Analytics BC velocity signals.

**Status:** 📋 Future — becomes relevant once Vendor Portal is live and Analytics BC provides velocity data.

**Key concepts:** `PurchaseOrderCreated`, `PurchaseOrderConfirmed` (vendor accepts), `GoodsReceived` → triggers `InventoryReceived` in Inventory BC. Integrates with Vendor Portal (vendors see and acknowledge POs), Inventory BC (replenishment).

---

## Shipping / Logistics

The Shipping context owns carrier relationship management, rate shopping (comparing UPS vs FedEx vs USPS in real time), and shipping label procurement. Currently, Fulfillment BC contains stub carrier integration — this BC would own that concern properly.

**Status:** 📋 Future — extract from Fulfillment BC once carrier complexity warrants a dedicated context.

**Key concepts:** `RateQuoteRequested` / `RateQuoteReceived`, `ShippingLabelPurchased`, carrier API adapters (UPS, FedEx, USPS, DHL). Fulfillment BC delegates to Shipping BC for label generation instead of implementing it directly.
