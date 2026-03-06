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

The Fulfillment context owns the physical execution of getting items from warehouse to customer—picking, packing, shipping, and delivery tracking. It takes over once Orders has secured payment and committed inventory. This BC integrates with carriers for tracking numbers and manages warehouse/FC routing logic.

**Naming Note:** The folder is currently `Fulfillment Management/` but the BC is conceptually "Fulfillment" (simpler, industry standard). See `docs/BC-NAMING-ANALYSIS.md` for naming rationale.

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

The Customer Experience context is a **stateless BFF (Backend-for-Frontend)** that composes views from multiple domain BCs (Shopping, Orders, Catalog, Customer Identity). It does NOT contain domain logic or persist data—all state lives in upstream BCs. Real-time updates are pushed to Blazor clients via Server-Sent Events (SSE). This BC optimizes UI performance and provides a cohesive customer experience across web and future mobile channels.

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

Customer-facing web store built with Blazor Server, demonstrating full-stack C# development with real-time updates via Server-Sent Events (SSE) and Wolverine integration.

**Implemented Pages (Phase 3):**
- ✅ Home page (navigation cards)
- ✅ Shopping cart view (SSE-enabled for real-time updates)
- ✅ Checkout flow (MudStepper wizard with 4 steps)
- ✅ Order history (MudTable with order list)

**Future Pages:**
- Product listing page (queries Catalog BC)
- Account/address management (queries Customer Identity BC)

**Technology Stack:**
- **Blazor Server** - C# full-stack, component model, interactive render modes
- **MudBlazor** - Material Design component library (ADR 0005)
- **Server-Sent Events (SSE)** - Real-time cart/order updates pushed from domain BCs (ADR 0004)
- **Wolverine HTTP** - BFF endpoints for view composition
- **Alba** - Integration testing for BFF composition endpoints
- **JavaScript Interop** - EventSource API for SSE subscriptions

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
- `POST /api/admin/products` - Add new product
- `PUT /api/admin/products/{sku}` - Update product details
- `PUT /api/admin/products/{sku}/images` - Update product images
- `PUT /api/admin/products/{sku}/status` - Change product status
- `DELETE /api/admin/products/{sku}` - Soft delete (mark as discontinued)

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

**Status**: 🔜 Planned (Phase 1 of Vendor implementation)

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

### HTTP Endpoints (Planned)

**Authentication:**
- `POST /api/vendor-auth/login` - Authenticate vendor user, issue JWT + refresh token cookie
- `POST /api/vendor-auth/refresh` - Exchange refresh token for new access JWT
- `POST /api/vendor-auth/logout` - Invalidate refresh token
- `POST /api/vendor-auth/reset-password` - Initiate password reset

**Admin (Tenant Management):**
- `POST /api/admin/vendor-tenants` - Create new vendor organization
- `GET /api/admin/vendor-tenants` - List all vendor tenants
- `GET /api/admin/vendor-tenants/{tenantId}` - Get tenant details
- `POST /api/admin/vendor-tenants/{tenantId}/suspend` - Suspend tenant
- `POST /api/admin/vendor-tenants/{tenantId}/reinstate` - Reinstate suspended tenant
- `POST /api/admin/vendor-tenants/{tenantId}/terminate` - Terminate tenant (permanent)

**Admin (User Management):**
- `POST /api/admin/vendor-tenants/{tenantId}/users/invite` - Invite vendor user (carries Role)
- `POST /api/admin/vendor-tenants/{tenantId}/users/{userId}/invitation/resend` - Resend expired/pending invitation
- `POST /api/admin/vendor-tenants/{tenantId}/users/{userId}/invitation/revoke` - Revoke pending invitation
- `GET /api/admin/vendor-tenants/{tenantId}/users` - List users for tenant
- `PATCH /api/admin/vendor-users/{userId}/deactivate` - Deactivate user
- `PATCH /api/admin/vendor-users/{userId}/reactivate` - Reactivate deactivated user
- `PATCH /api/admin/vendor-users/{userId}/role` - Change user role

**User Self-Service:**
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

**Status**: 🔜 Planned (Phase 2 of Vendor implementation — Phase 1 is Vendor Identity + VendorProductCatalog foundation)

**Persistence Strategy**: Marten (document store for accounts/requests, projections for read models)

**Real-Time**: SignalR via Wolverine (`opts.UseSignalR()`) — dual hub groups: `vendor:{tenantId}` (shared tenant notifications) and `user:{userId}` (individual notifications)

**Event Modeling Session**: See [docs/planning/vendor-portal-event-modeling.md](docs/planning/vendor-portal-event-modeling.md) for full planning output including event diagrams, risks, and phased roadmap.

### Purpose

Present pre-aggregated sales and inventory analytics scoped to the vendor's products, provide real-time notifications via SignalR, accept and track product change requests (full 7-state lifecycle), and allow vendors to save custom dashboard views.

### Multi-Tenancy Model

One tenant per vendor organization (`VendorTenantId` from Vendor Identity JWT claims). Each vendor's data is fully isolated using Marten's `ForTenant(tenantId)`. `VendorTenantId` is extracted from JWT claims only — never from request parameters.

**Critical exception:** `VendorProductCatalog` (SKU→Tenant lookup) is intentionally NOT tenant-isolated. It is the system-wide lookup that tells handlers which tenant to query. All other projections use `session.ForTenant(tenantId)`.

### The Load-Bearing Pillar: VendorProductCatalog

**This projection must exist before any analytics or change request invariants can work.**

`VendorProductCatalog` is populated by `VendorProductAssociated` events published by Catalog BC when an admin assigns a SKU to a vendor tenant. It provides the SKU→VendorTenantId lookup that all handlers use for tenant routing.

```
VendorProductCatalog document:
  Id: {Sku}                  (document ID is the SKU)
  Sku: string
  VendorTenantId: Guid
  AssociatedAt: DateTimeOffset
  IsActive: bool
```

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
  - `Type` (ChangeRequestType enum) - `DescriptionUpdate`, `ImageUpload`, `DataCorrection`
  - `Status` (ChangeRequestStatus enum) - see state machine below
  - `LatestContent` (string?) - description text or correction details
  - `ImageStorageKeys` (IReadOnlyList<string>) - object storage references (claim-check pattern)
  - `ReplacedByRequestId` (Guid?) - set when Status = `Replaced`; enables UI linkage
  - `SubmittedAt`, `ResolvedAt` (DateTimeOffset?)

**ChangeRequest State Machine:**
```
Draft ──────────────────────→ Submitted    (SubmitChangeRequest command)
Draft ──────────────────────→ Withdrawn    (WithdrawChangeRequest command)

Submitted ──────────────────→ NeedsMoreInfo (MoreInfoRequestedForChangeRequest from Catalog)
Submitted ──────────────────→ Approved      (DescriptionChangeApproved from Catalog)
Submitted ──────────────────→ Rejected      (DescriptionChangeRejected from Catalog)
Submitted ──────────────────→ Withdrawn     (WithdrawChangeRequest command)

NeedsMoreInfo ──────────────→ Submitted     (ProvideAdditionalInfo command)
NeedsMoreInfo ──────────────→ Withdrawn     (WithdrawChangeRequest command)

Approved ───────────────────→ Replaced      (system: newer request approved for same Sku+Type)

Rejected, Withdrawn, Replaced = TERMINAL (no further transitions)
```

**Key ChangeRequest invariant:** Only one active (Draft, Submitted, or NeedsMoreInfo) request per `VendorTenantId` + `Sku` + `Type` combination. Submitting a new one auto-withdraws any existing active request.

**Projections (Read Models, Pre-Aggregated):**

- `VendorProductCatalog` - SKU→Tenant mapping (NOT tenant-isolated; system-wide lookup)
  - `Id/Sku` (string), `VendorTenantId` (Guid), `AssociatedAt`, `IsActive`

- `ProductPerformanceSummary` - Sales metrics by SKU, bucketed by time period (tenant-isolated)
  - `VendorTenantId`, `Sku` (partition keys)
  - `DailySales`, `WeeklySales`, `MonthlySales`, `QuarterlySales`, `YearlySales`
  - `Revenue`, `UnitsSold`, `AverageOrderValue` (per time bucket)
  - Populated by `OrderPlacedHandler` fan-out via internal `UpdateTenantSalesSummary` command

- `InventorySnapshot` - Current stock levels (tenant-isolated)
  - `VendorTenantId`, `Sku` (partition keys)
  - `AvailableQuantity`, `ReservedQuantity`, `WarehouseId`
  - `LastRestockedAt`, `LastSoldAt`, `LastCalculatedAt` (shown in UI for data freshness)

- `LowStockAlert` - Active unacknowledged low-stock alerts (tenant-isolated)
  - `AlertId`, `VendorTenantId`, `Sku`, `CurrentQuantity`, `Threshold`
  - `Status` (Active | Acknowledged)
  - Deduplication: one active alert per Sku per tenant

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
- All queries and projections must use `session.ForTenant(tenantId)` — except `VendorProductCatalog` (system-wide lookup)
- `VendorTenantId` comes from JWT claims only — never from request parameters or body
- Vendors can only see data for products in their `VendorProductCatalog`
- Change requests must reference a Sku present in the vendor's `VendorProductCatalog`

**Change Request Invariants:**
- Only one active (Draft, Submitted, NeedsMoreInfo) request per `VendorTenantId` + `Sku` + `Type`
- Submitting a new request for same Sku+Type auto-withdraws any existing active request
- Change requests are immutable after submission (must withdraw and resubmit to change)
- `Replaced` state carries `ReplacedByRequestId` for UI linkage
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
| Catalog | Downstream | Receives change requests (`DescriptionChangeRequested`, `ImageUploadRequested`, `ProductCorrectionRequested`), publishes approval outcomes |
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
      ├─> Validate SKU is in VendorProductCatalog (session.ForTenant not needed for lookup)
      ├─> Check: active request for same SKU+Type exists? → auto-withdraw + warn user
      ├─> IStartStream<ChangeRequest>(ChangeRequestDrafted) → Status=Draft
      └─> Return new RequestId

SubmitChangeRequest (command from vendor)
  └─> [Load] ChangeRequest via [ReadAggregate]
  └─> [Before] Validate CanSubmit (Status must be Draft)
  └─> [Handle] Returns:
      ├─> ChangeRequestSubmitted domain event → Status=Submitted
      └─> DescriptionChangeRequested / ImageUploadRequested / DataCorrectionRequested
          → Catalog BC via Wolverine transactional outbox

[Catalog BC processes: approve / reject / needs-more-info]

DescriptionChangeApproved (integration message from Catalog)
  └─> DescriptionChangeApprovedHandler (Vendor Portal)
      ├─> Load ChangeRequest aggregate (session.ForTenant)
      ├─> Append ChangeRequestApproved event → Status=Approved (terminal)
      ├─> Check: other Approved requests for same SKU+Type → mark as Replaced
      └─> Publish ChangeRequestStatusUpdated → IVendorTenantMessage → "vendor:{tenantId}"
          Publish ChangeRequestDecisionPersonal → IVendorUserMessage → "user:{submitterUserId}"
          [Toast: "✅ Description update for SKU-1001 approved!"]

MoreInfoRequestedForChangeRequest (integration message from Catalog)
  └─> MoreInfoRequestedHandler
      ├─> Append MoreInfoRequested event → Status=NeedsMoreInfo
      └─> Publish notification → "user:{submitterUserId}"
          [Toast: "📋 Catalog team has a question about your request"]

ProvideAdditionalInfo (command from vendor)
  └─> [Load] ChangeRequest → validate Status=NeedsMoreInfo
  └─> [Handle] Returns AdditionalInfoProvided event → Status=Submitted (re-enters review)

WithdrawChangeRequest (command from vendor)
  └─> [Load] ChangeRequest → validate CanWithdraw (Draft, Submitted, or NeedsMoreInfo)
  └─> [Handle] Returns ChangeRequestWithdrawn event → Status=Withdrawn (terminal)
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
- `GET /api/vendor-portal/account` - Get vendor account settings
- `PUT /api/vendor-portal/account/notifications` - Update notification preferences (default: all on)
- `POST /api/vendor-portal/account/views` - Save dashboard view configuration
- `GET /api/vendor-portal/account/views` - List saved views
- `DELETE /api/vendor-portal/account/views/{viewId}` - Delete saved view

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

**Phase 5 — Saved Views + VendorAccount**
- `VendorAccount` aggregate; `SaveDashboardView` / `UpdateNotificationPreferences` commands
- VendorPortal.Web: saved views selector, notification preferences

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

## Future Considerations

The following contexts are acknowledged but not yet defined:

- **Pricing** — price rules, promotional pricing, regional pricing
- **Promotions** — buy-one-get-one, percentage discounts, coupon codes
- **Reviews** — customer product reviews, ratings, moderation
- **Notifications** — email, SMS, push notifications (may move to Customer Experience)
- **Procurement/Supply Chain** — purchasing, vendor management, forecasting
- **Shipping/Logistics** — carrier management, rate shopping
