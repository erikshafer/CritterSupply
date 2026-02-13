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

The Customer Experience context owns the customer-facing frontend orchestration using the Backend-for-Frontend (BFF) pattern. This BC composes data from multiple domain BCs to optimize UI performance and provide a cohesive customer experience across web and future mobile channels.

**Status**: 🚧 In Progress (Cycle 16 - Phase 3 Complete, Backend Integration Next)

### Architecture Pattern: Backend-for-Frontend (BFF)

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

### Architecture: Read-Heavy, Query-Optimized

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

The Vendor Identity context manages authentication, authorization, and user lifecycle for vendor personnel accessing CritterSupply systems. Structurally similar to Customer Identity, but serving a distinct user population with potentially different authentication requirements (SSO with vendor's corporate IdP, different MFA policies, etc.).

**Status**: 🔜 Planned (Future Cycles)

**Persistence Strategy**: Entity Framework Core (following Customer Identity pattern)

### Purpose

Authenticate vendor users, manage their lifecycle, and provide tenant-scoped claims for downstream contexts. Each vendor organization is a separate tenant with isolated user management.

### Multi-Tenancy Model

One tenant per vendor organization. User records are scoped to their vendor tenant. The issued claims include tenant identifier (`VendorTenantId`) for downstream authorization in Vendor Portal and potentially other contexts (e.g., Catalog for product associations).

### Aggregates

**VendorTenant (Aggregate Root):**
- `Id` (Guid) - Canonical tenant identifier used throughout Vendor Identity and Vendor Portal
- `OrganizationName` (string) - Vendor company name
- `Status` (TenantStatus enum) - Onboarding, Active, Suspended, Deactivated
- `OnboardedAt` (DateTimeOffset) - When vendor was added to CritterSupply
- `ContactEmail` (string) - Primary contact for vendor organization

**VendorUser (Entity):**
- `Id` (Guid) - User identifier
- `VendorTenantId` (Guid) - Foreign key to VendorTenant
- `Email` (string) - Login email, unique across all vendor users
- `PasswordHash` (string) - Bcrypt/Argon2 hash
- `FirstName`, `LastName` (string) - User profile
- `Status` (VendorUserStatus enum) - Invited, Active, Deactivated
- `MfaEnabled` (bool) - Multi-factor authentication enrollment
- `InvitedAt`, `ActivatedAt` (DateTimeOffset?) - Lifecycle timestamps
- `LastLoginAt` (DateTimeOffset?) - Audit trail

### What it receives

None from other bounded contexts. Vendor Identity initiates identity flows based on administrative actions (tenant creation, user invitation).

### What it publishes

**Integration Events:**
- `VendorTenantCreated` - New vendor organization onboarded (includes `VendorTenantId`, `OrganizationName`, timestamp)
- `VendorUserInvited` - Invitation sent to vendor personnel (includes `UserId`, `VendorTenantId`, `Email`)
- `VendorUserActivated` - User completed registration (includes `UserId`, `VendorTenantId`, activation timestamp)
- `VendorUserDeactivated` - User access revoked (includes `UserId`, `VendorTenantId`, reason)
- `VendorUserPasswordReset` - Password changed, for audit trail (includes `UserId`, timestamp)

### Core Invariants

**Tenant Invariants:**
- Vendor tenant IDs are immutable once created (canonical identifier)
- Organization names must be unique across all vendor tenants
- Tenants can only be deactivated, never deleted (preserve historical data)

**User Invariants:**
- Email addresses must be unique across all vendor users
- Users belong to exactly one vendor tenant (no multi-tenancy for users)
- Users can only be activated after invitation
- Deactivated users cannot be reactivated (must create new user)
- MFA enrollment cannot be disabled once enabled (security policy)

### What it doesn't own

- Vendor Portal access logic (Vendor Portal consumes tenant claims)
- Product-vendor associations (placeholder in Catalog BC for future design)
- Analytics or insights (Vendor Portal responsibility)
- Change request approval workflows (Catalog BC responsibility)

### Integration Points

| Context | Relationship | Notes |
|---------|--------------|-------|
| Vendor Portal | Downstream | Consumes identity events (`VendorUserActivated`, `VendorUserDeactivated`), relies on tenant claims (`VendorTenantId`) for data isolation |
| Catalog | Downstream (Future) | May consume `VendorTenantCreated` to associate products with vendors (design deferred, placeholder integration point) |

### Integration Flows

**Tenant Onboarding:**
```
CreateVendorTenant (command from admin UI)
  └─> CreateVendorTenantHandler
      ├─> VendorTenant entity persisted
      └─> Publish VendorTenantCreated → Vendor Portal, Catalog

VendorTenantCreated (integration message)
  └─> Vendor Portal: Initialize tenant-scoped projections
  └─> [Future] Catalog: Create vendor-product association records
```

**User Invitation and Activation:**
```
InviteVendorUser (command from admin UI)
  └─> InviteVendorUserHandler
      ├─> VendorUser entity created with Status=Invited
      ├─> Send invitation email (external service)
      └─> Publish VendorUserInvited → Vendor Portal

CompleteVendorUserRegistration (command from user)
  └─> CompleteVendorUserRegistrationHandler
      ├─> VendorUser.Status → Active
      ├─> Set password hash
      └─> Publish VendorUserActivated → Vendor Portal

DeactivateVendorUser (command from admin UI)
  └─> DeactivateVendorUserHandler
      ├─> VendorUser.Status → Deactivated
      └─> Publish VendorUserDeactivated → Vendor Portal
```

**Authentication Flow:**
```
AuthenticateVendorUser (command from login page)
  └─> AuthenticateVendorUserHandler
      ├─> Query VendorUser by email
      ├─> Verify password hash
      ├─> Issue JWT with claims:
      │   ├─> UserId
      │   ├─> VendorTenantId (for tenant isolation)
      │   ├─> Email
      │   └─> Roles (Admin, Viewer, Editor)
      └─> Update LastLoginAt timestamp
```

### HTTP Endpoints (Planned)

**Authentication:**
- `POST /api/vendor-auth/login` - Authenticate vendor user
- `POST /api/vendor-auth/logout` - Invalidate session
- `POST /api/vendor-auth/reset-password` - Initiate password reset

**Admin (Tenant Management):**
- `POST /api/admin/vendor-tenants` - Create new vendor organization
- `GET /api/admin/vendor-tenants` - List all vendor tenants
- `GET /api/admin/vendor-tenants/{tenantId}` - Get tenant details
- `PATCH /api/admin/vendor-tenants/{tenantId}/status` - Change tenant status

**Admin (User Management):**
- `POST /api/admin/vendor-tenants/{tenantId}/users/invite` - Invite vendor user
- `GET /api/admin/vendor-tenants/{tenantId}/users` - List users for tenant
- `PATCH /api/admin/vendor-users/{userId}/deactivate` - Deactivate user

**User Self-Service:**
- `POST /api/vendor-users/complete-registration` - Complete invited user registration
- `POST /api/vendor-users/change-password` - Change password (authenticated)
- `POST /api/vendor-users/enroll-mfa` - Enable multi-factor authentication

### Structural Similarity to Customer Identity

Vendor Identity follows the same patterns established in Customer Identity BC:
- Same aggregate root pattern (Tenant/User entities with navigation properties)
- Same authentication flows (or parallel implementations if vendor auth differs, e.g., SSO)
- Same event shapes where applicable (e.g., `UserActivated` pattern)
- Same persistence strategy (EF Core with PostgreSQL)
- Opportunity to extract shared infrastructure if patterns converge (e.g., password hashing, JWT issuance)

The separation exists because vendors and customers are distinct populations with different lifecycles, access patterns, and potentially different auth requirements (e.g., corporate SSO for vendors vs. social login for customers). Keeping them separate in the reference architecture demonstrates the bounded context pattern more clearly.

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

The Vendor Portal context provides partnered vendors with a private, tenant-isolated view into how their products perform within CritterSupply. Vendors can see sales analytics across configurable time periods, monitor inventory levels, and submit change requests for their product data. The portal captures vendor intent and displays status—actual approval decisions live in other contexts (primarily Catalog).

**Status**: 🔜 Planned (Future Cycles)

**Persistence Strategy**: Marten (document store for accounts/requests, projections for read models)

### Purpose

Present pre-aggregated sales and inventory analytics scoped to the vendor's products, allow vendors to save custom filters and views for their dashboards, accept and track product change requests, and display request status based on events from downstream contexts.

### Multi-Tenancy Model

One tenant per vendor organization (`VendorTenantId` from Vendor Identity BC). Each vendor's data is fully isolated using Marten's multi-tenancy capabilities. Users belong to a single tenant, though the model supports a list of tenant associations to accommodate future scenarios (acquisitions, portfolio companies, regional splits).

### Aggregates and Projections

**Aggregates (Write Models):**

- `VendorAccount` - Portal-specific settings, saved views, notification preferences
  - `VendorTenantId` (Guid) - Tenant identifier from Vendor Identity
  - `SavedViews` (IReadOnlyList<SavedDashboardView>) - Custom filter configurations
  - `NotificationPreferences` (NotificationSettings) - Email/SMS alert preferences
  - `ContactInfo` (VendorContactInfo) - Portal-specific contact (may differ from Vendor Identity)

- `ChangeRequest` - Tracks lifecycle of submitted product corrections (event-sourced)
  - `Id` (Guid) - Request identifier
  - `VendorTenantId` (Guid) - Tenant scope
  - `ProductSku` (string) - Target product
  - `RequestType` (ChangeRequestType enum) - DescriptionChange, ImageUpload, ProductCorrection
  - `Status` (ChangeRequestStatus enum) - Pending, Approved, Rejected
  - `SubmittedAt`, `ResolvedAt` (DateTimeOffset?) - Lifecycle timestamps

**Projections (Read Models, Pre-Aggregated):**

- `ProductPerformanceSummary` - Sales metrics by SKU, bucketed by time period
  - `VendorTenantId`, `ProductSku` (partition keys)
  - `DailySales`, `WeeklySales`, `MonthlySales`, `QuarterlySales`, `YearlySales` (aggregated metrics)
  - `Revenue`, `UnitsSold`, `AverageOrderValue` (per time bucket)

- `InventorySnapshot` - Current stock levels and recent movement
  - `VendorTenantId`, `ProductSku` (partition keys)
  - `AvailableQuantity`, `ReservedQuantity`, `WarehouseId`
  - `LastRestockedAt`, `LastSoldAt` (timestamps)

- `ChangeRequestStatus` - Current state of all pending and resolved requests
  - `VendorTenantId` (partition key)
  - `RequestId`, `ProductSku`, `RequestType`, `Status`, `SubmittedAt`, `ResolvedAt`

- `SavedDashboardView` - Vendor-configured filters and display preferences
  - `VendorTenantId`, `ViewId` (partition keys)
  - `Name`, `FilterCriteria` (JSON), `SortOrder`

### What it receives

**Integration Messages:**

From **Orders**:
- `OrderPlaced` - Feeds sales aggregation (includes line items, order total, timestamp)
- `OrderItemShipped` - Updates sales metrics (shipped units)
- `OrderItemReturned` - Adjusts sales metrics (returned units, refunds)

From **Inventory**:
- `InventoryAdjusted` - Updates inventory snapshots (quantity changes)
- `StockReplenished` - Feeds inventory snapshots (restock events)
- `LowStockDetected` - Alerts vendors of low inventory (threshold crossed)

From **Catalog**:
- `ProductCreated` - Maintains product reference data (new products)
- `ProductUpdated` - Updates product reference data (description changes)
- `ProductDiscontinued` - Marks products as discontinued in projections
- `DescriptionChangeApproved` - Updates change request status (approval)
- `DescriptionChangeRejected` - Updates change request status (rejection)
- `ImageChangeApproved` - Updates change request status (approval)
- `ImageChangeRejected` - Updates change request status (rejection)

From **Vendor Identity**:
- `VendorUserActivated` - Grants portal access to new users
- `VendorUserDeactivated` - Revokes portal access
- `VendorTenantCreated` - Initializes tenant-scoped projections

### What it publishes

**Integration Messages:**

- `DescriptionChangeRequested` - Vendor proposes updated description or bullet points (includes `ProductSku`, `NewDescription`, `RequestId`, `VendorTenantId`)
- `ImageUploadRequested` - Vendor submits new product images for review (includes `ProductSku`, `ImageUrls`, `RequestId`, `VendorTenantId`)
- `ProductCorrectionRequested` - Vendor flags an error (wrong weight, incorrect ingredients, etc.) (includes `ProductSku`, `CorrectionType`, `CorrectionDetails`, `RequestId`, `VendorTenantId`)
- `DashboardViewSaved` - Vendor saves custom filter/view configuration (includes `VendorTenantId`, `ViewId`, `FilterCriteria`)

### Core Invariants

**Multi-Tenancy Invariants:**
- All queries and projections must be scoped to `VendorTenantId`
- Vendors can only see data for products associated with their tenant (enforced by Marten tenant isolation)
- Change requests must reference products belonging to the requesting vendor's tenant

**Change Request Invariants:**
- A change request cannot be submitted without a valid `ProductSku`
- A change request can only be in one state at a time (Pending, Approved, Rejected)
- Change requests cannot be edited after submission (immutable once created)
- Only pending requests can transition to approved/rejected
- Approved/rejected requests are terminal states (no further transitions)

**Dashboard View Invariants:**
- View names must be unique per vendor tenant
- View configurations must be valid JSON
- Vendors can have unlimited saved views

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

**Read-Only Analytics (Sales Performance):**
```
[Orders BC domain logic]
OrderPlaced (integration message from Orders)
  └─> OrderPlacedHandler (Vendor Portal)
      ├─> Extract line items for vendor's products
      ├─> Update ProductPerformanceSummary projection (daily/weekly/monthly buckets)
      └─> Increment revenue, units sold, order count

GetProductPerformance (query from vendor dashboard)
  └─> GetProductPerformanceHandler
      ├─> Query ProductPerformanceSummary projection (scoped to VendorTenantId)
      ├─> Filter by date range (last 30 days, last quarter, etc.)
      └─> Return aggregated sales metrics
```

**Read-Only Analytics (Inventory Snapshot):**
```
[Inventory BC domain logic]
InventoryAdjusted (integration message from Inventory)
  └─> InventoryAdjustedHandler (Vendor Portal)
      ├─> Update InventorySnapshot projection (scoped to VendorTenantId)
      └─> Set AvailableQuantity, ReservedQuantity, LastRestockedAt

GetInventorySnapshot (query from vendor dashboard)
  └─> GetInventorySnapshotHandler
      ├─> Query InventorySnapshot projection (scoped to VendorTenantId)
      ├─> Filter by ProductSku, WarehouseId
      └─> Return current stock levels
```

**Change Request Flow (Description Update):**
```
SubmitDescriptionChange (command from vendor dashboard)
  └─> SubmitDescriptionChangeHandler
      ├─> Create ChangeRequest aggregate (Status=Pending)
      ├─> Persist to Marten
      └─> Publish DescriptionChangeRequested → Catalog BC

[Catalog BC processes approval workflow]

DescriptionChangeApproved (integration message from Catalog)
  └─> DescriptionChangeApprovedHandler (Vendor Portal)
      ├─> Update ChangeRequest aggregate (Status=Approved, ResolvedAt=now)
      └─> Update ChangeRequestStatus projection

GetChangeRequestStatus (query from vendor dashboard)
  └─> GetChangeRequestStatusHandler
      ├─> Query ChangeRequestStatus projection (scoped to VendorTenantId)
      └─> Return list of pending/approved/rejected requests
```

**Saved Dashboard Views:**
```
SaveDashboardView (command from vendor dashboard)
  └─> SaveDashboardViewHandler
      ├─> Update VendorAccount aggregate (add to SavedViews collection)
      ├─> Persist to Marten
      └─> Publish DashboardViewSaved (optional, for analytics)

GetSavedViews (query from vendor dashboard)
  └─> GetSavedViewsHandler
      ├─> Query VendorAccount aggregate (scoped to VendorTenantId)
      └─> Return list of saved view configurations
```

### HTTP Endpoints (Planned)

**Analytics Queries:**
- `GET /api/vendor-portal/performance?sku={sku}&period={period}` - Sales metrics by SKU and time period
- `GET /api/vendor-portal/inventory?sku={sku}` - Current inventory snapshot
- `GET /api/vendor-portal/dashboard` - Aggregated dashboard data (top products, alerts)

**Change Requests:**
- `POST /api/vendor-portal/change-requests/description` - Submit description change
- `POST /api/vendor-portal/change-requests/images` - Submit image upload
- `POST /api/vendor-portal/change-requests/correction` - Submit product correction
- `GET /api/vendor-portal/change-requests` - List all change requests (paginated, filterable)
- `GET /api/vendor-portal/change-requests/{requestId}` - Get change request details

**Account Management:**
- `GET /api/vendor-portal/account` - Get vendor account settings
- `PUT /api/vendor-portal/account/notifications` - Update notification preferences
- `POST /api/vendor-portal/account/views` - Save dashboard view configuration
- `GET /api/vendor-portal/account/views` - List saved views
- `DELETE /api/vendor-portal/account/views/{viewId}` - Delete saved view

### Phased Roadmap

**Phase 1 — Vendor Identity Foundation**
- `VendorTenant` and `VendorUser` aggregates
- Basic registration/authentication flow
- Tenant-scoped claim issuance
- Events: `VendorTenantCreated`, `VendorUserActivated`

**Phase 2 — Read-Only Analytics (Portal)**
- Subscribe to Orders and Inventory events
- Build `ProductPerformanceSummary` and `InventorySnapshot` projections
- Basic dashboard displaying sales by time period
- Tenant isolation via Marten multi-tenancy, driven by Vendor Identity claims

**Phase 3 — Saved Views (Portal)**
- `VendorAccount` aggregate with preferences
- `SavedDashboardView` projection
- Vendor can save and switch between custom filter configurations

**Phase 4 — Change Requests (Portal + Catalog)**
- `ChangeRequest` aggregate with saga-style lifecycle tracking
- Image upload flow (emit `ImageUploadRequested`, listen for approval)
- Description/correction request flow
- Request history and status display
- Catalog-side handlers for approval workflow

**Phase 5 — Full Identity Lifecycle**
- User invitation flow
- Password reset, MFA enrollment
- User deactivation
- Audit events

**Phase 6 — Account Management (Portal)**
- Vendor-editable account settings (notification preferences, contact info)
- Integration with Vendor Identity for user management visibility

---

## Customer Experience

**Type:** Backend-for-Frontend (BFF) Composition Layer

**Purpose:** Aggregate data from multiple bounded contexts for customer-facing UI, provide real-time updates via Server-Sent Events (SSE)

### Bounded Context Boundary

**In Scope:**
- View composition (aggregating data from multiple domain BCs)
- SSE notification delivery (pushing real-time updates to connected clients)
- HTTP client coordination (querying downstream BCs)
- Client connection management (tracking active SSE connections)

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
- `Shopping.ItemAdded` → triggers SSE push `cart-updated` to connected clients
- `Shopping.ItemRemoved` → triggers SSE push `cart-updated` to connected clients
- `Shopping.ItemQuantityChanged` → triggers SSE push `cart-updated` to connected clients
- `Orders.OrderPlaced` → triggers SSE push `order-status-changed` to connected clients
- `Payments.PaymentCaptured` → triggers SSE push `order-status-changed` to connected clients
- `Fulfillment.ShipmentDispatched` → triggers SSE push `shipment-status-changed` to connected clients
- `Fulfillment.ShipmentDelivered` → triggers SSE push `shipment-status-changed` to connected clients

**Publishes (Integration Messages):**
- None (BFF is read-only, commands sent via HTTP POST to domain BCs)

**Publishes (SSE Events to Connected Clients):**
- `cart-updated` → Blazor client re-renders cart with updated line items/totals
- `order-status-changed` → Blazor client updates order status display (pending → paid → shipped)
- `shipment-status-changed` → Blazor client updates tracking info (dispatched → in transit → delivered)

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

**Pattern:** HTTP (query) + RabbitMQ (notifications) + SSE (push to clients)

**Flow Example (Cart Update with Real-Time Notification):**
```
[Shopping BC - Domain Logic]
AddItemToCart (command) → AddItemToCartHandler
  ├─> ItemAdded (domain event, persisted to event store)
  └─> Publish Shopping.ItemAdded (integration message) → RabbitMQ

[Customer Experience BFF - Notification Handler]
Shopping.ItemAdded (integration message from RabbitMQ)
  └─> ItemAddedNotificationHandler
      ├─> Query Shopping BC: GET /api/carts/{cartId}
      ├─> Query Catalog BC: GET /api/products?skus={skus} (enrich with product details)
      ├─> Compose CartSummaryView (aggregated data)
      └─> SSE Push: StorefrontHub.PushCartUpdate(cartId, cartSummary)

[Blazor Frontend - SSE Client]
SSE Event Received ("cart-updated")
  └─> Blazor component re-renders with updated cart data
```

### Key Architectural Decisions

**[ADR 0004: SSE over SignalR](./docs/decisions/0004-sse-over-signalr.md)**
- **Decision:** Use .NET 10's native Server-Sent Events (SSE) instead of SignalR
- **Rationale:** Simpler one-way server→client push, native HTTP/2 support, no WebSocket complexity
- **Trade-off:** SSE is one-way only (but we don't need client→server push beyond HTTP POST commands)

**[ADR 0005: MudBlazor UI Framework](./docs/decisions/0005-mudblazor-ui-framework.md)**
- **Decision:** Use MudBlazor for Blazor UI components
- **Rationale:** Material Design, polished components, active community, aligns with future client work

### Current Status (Cycle 16)

**Phase 1: BFF Infrastructure - ✅ Complete (2026-02-05)**
- BFF project created (`Storefront/`) with Wolverine + Marten
- 3 composition handlers implemented (CartView, CheckoutView, ProductListing)
- 9 integration tests passing (3 deferred to Phase 3)
- HTTP client stub pattern established for testing

**Phase 2: SSE Real-Time Integration - 🚧 Next**
- SSE endpoint (`/sse/storefront`) to be implemented
- Integration message handlers for cart/order notifications
- RabbitMQ subscriptions for real-time updates

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
