# CritterSupply Bounded Contexts

This document defines the bounded contexts within CritterSupply, an e-commerce reference architecture demonstrating event-driven systems using the Critter Stack (Wolverine, Marten, Alba).

## Important Note: Checkout Migration (Cycle 8)

**Checkout aggregate was migrated from Shopping BC to Orders BC** to establish clearer bounded context boundaries:
- **Shopping BC** now focuses on pre-purchase exploration (cart management, future: product browsing, wishlists)
- **Orders BC** owns the transactional commitment phase (checkout + order lifecycle)
- Integration: Shopping publishes `CheckoutInitiated` → Orders handles and creates Checkout aggregate

This architectural decision ensures each BC has a well-defined purpose and reduces cognitive load for developers working in either context.

## Shopping

The Shopping context owns the customer's pre-purchase experience—building a cart prior to order commitment. This BC focuses on the exploratory phase of shopping, before the customer commits to purchase. Checkout was migrated to Orders BC in Cycle 8 to establish clearer bounded context boundaries.

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

## Orders

The Orders context owns the commercial commitment and coordinates the lifecycle from checkout through delivery or cancellation. This BC contains two key aggregates: **Checkout** (order finalization) and **Order** (order lifecycle saga). Checkout was migrated from Shopping BC in Cycle 8 to establish clearer bounded context boundaries—Shopping focuses on exploration, Orders focuses on transaction commitment.

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

### Core Invariants

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

### Integration Flows

**Choreography: Fulfillment Processing**
```
FulfillmentRequested (from Orders)
  └─> FulfillmentRequestedHandler
      └─> RequestFulfillment (internal command)
          └─> RequestFulfillmentHandler
              └─> ShipmentStarted (Shipment stream created)

AssignWarehouse (command)
  └─> AssignWarehouseHandler
      └─> WarehouseAssigned

DispatchShipment (command)
  └─> DispatchShipmentHandler
      ├─> ShipmentDispatched → Orders
      └─> [Carrier integration for tracking]

ConfirmDelivery (command)
  └─> ConfirmDeliveryHandler
      ├─> ShipmentDelivered → Orders
      └─> [Carrier confirmed delivery]

[Delivery failure scenario]
  └─> ShipmentDeliveryFailed → Orders
```

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

### Core Invariants

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

## Customer Identity

The Customer Identity context owns customer identity, profiles, and persistent data like addresses and saved payment method tokens. It provides the foundation for personalized shopping experiences by maintaining customer preferences and frequently-used information across the system.

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

**Query Endpoints (HTTP):**
- `GET /api/customers/{customerId}/addresses` — returns all addresses for customer (optionally filtered by type)
- `GET /api/addresses/{addressId}/snapshot` — returns immutable `AddressSnapshot` for integration (updates `LastUsedAt`)

**Integration Flow (Cycle 11):**
1. Shopping BC queries `GetCustomerAddresses` during checkout → presents address list to customer
2. Customer selects address → Shopping BC stores `AddressId` in Checkout aggregate
3. On checkout completion → Shopping BC queries `GetAddressSnapshot` → receives immutable snapshot
4. Shopping BC publishes `CheckoutCompleted` with embedded `AddressSnapshot` → Orders BC receives and persists

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

## Future Considerations

The following contexts are acknowledged but not yet defined:

- **Catalog** — product information, categorization, search
- **Pricing** — price rules, promotions, discounts
- **Notifications** — email, SMS, push notifications
- **Procurement/Supply Chain** — purchasing, vendor management, forecasting
- **Shipping/Logistics** — carrier management, rate shopping
