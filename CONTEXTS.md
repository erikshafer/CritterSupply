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

## Customer Experience

The Customer Experience context owns the customer-facing frontend orchestration using the Backend-for-Frontend (BFF) pattern. This BC composes data from multiple domain BCs to optimize UI performance and provide a cohesive customer experience across web and future mobile channels.

**Status**: Planned (not yet implemented)

### Architecture Pattern: Backend-for-Frontend (BFF)

**Why BFF?**
- **Composition**: Aggregates data from multiple BCs for optimized frontend views
- **Real-Time**: Natural location for SignalR hub (cart updates, order notifications)
- **UI Orchestration**: Keeps frontend-specific logic separate from domain BCs
- **Independent Scaling**: Frontend concerns don't impact domain BC performance
- **Multiple Clients**: Easy to add mobile BFF later with different composition needs

### Subdomains

**Storefront (Web):**

Customer-facing web store built with Blazor Server, demonstrating full-stack C# development with real-time updates via SignalR and Wolverine integration.

**Key Pages (Minimal Implementation):**
- Product listing page (queries Catalog BC)
- Shopping cart view (queries Shopping BC, real-time updates via SignalR)
- Checkout flow (queries Orders + Customer Identity BCs, multi-step wizard)
- Order history (queries Orders BC)
- Account/address management (queries Customer Identity BC)

**Technology Stack:**
- **Blazor Server** - C# full-stack, component model, easier SignalR integration than WASM
- **SignalR** - Real-time cart/order updates pushed from domain BCs
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

**SignalR Messages (to connected clients):**
- `CartUpdated` — cart state changed (item added/removed/quantity changed)
- `OrderStatusChanged` — order progressed through lifecycle
- `InventoryAlert` — low stock warning for items in cart
- `CheckoutStepCompleted` — wizard progression feedback

### Core Invariants

**BFF Composition Invariants:**
- View models are optimized for frontend consumption, not domain purity
- BFF does NOT contain domain business logic (delegates to domain BCs)
- BFF does NOT maintain state (queries/commands only)
- SignalR notifications reflect domain events, don't replace them

**Real-Time Notification Invariants:**
- Only authenticated users receive notifications for their data
- SignalR connections scoped to customer ID (security boundary)
- Domain BCs publish integration messages; BFF transforms to SignalR messages
- Failed SignalR delivery does NOT fail domain operations (fire-and-forget)

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
      └─> Publish Shopping.ItemAdded (integration message) → RabbitMQ

[Customer Experience BFF]
Shopping.ItemAdded (integration message from RabbitMQ)
  └─> ItemAddedNotificationHandler
      ├─> Query Shopping BC for updated cart state
      ├─> Compose CartSummaryView
      └─> SignalR push to connected client
          └─> StorefrontHub.Clients.Group($"cart:{cartId}")
              └─> SendAsync("CartUpdated", cartSummary)

[Blazor Frontend]
StorefrontHub.On("CartUpdated")
  └─> Update UI component (cart icon badge, cart page refresh)
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

### Project Structure (Planned)

```
src/
  Customer Experience/
    Storefront/                       # BFF domain (view composition, SignalR hub)
      Composition/                    # View model composition from multiple BCs
        CheckoutView.cs
        ProductListingView.cs
        OrderHistoryView.cs
      Notifications/                  # SignalR hub + integration message handlers
        StorefrontHub.cs
        CartUpdateNotifier.cs
        OrderStatusNotifier.cs
      Queries/                        # BFF query handlers (composition)
        GetCheckoutView.cs
        GetProductListing.cs
        GetOrderHistory.cs
      Commands/                       # BFF command handlers (delegation to domain BCs)
        AddItemToCartCommand.cs
        CompleteCheckoutCommand.cs
      Clients/                        # HTTP clients for domain BC queries
        IShoppingClient.cs
        IOrdersClient.cs
        ICustomerIdentityClient.cs
        ICatalogClient.cs
    Storefront.Web/                   # Blazor Server app
      Pages/
        Index.razor                   # Product catalog landing
        Cart.razor                    # Shopping cart view
        Checkout.razor                # Checkout wizard
        OrderHistory.razor            # Customer order list
        OrderDetails.razor            # Order tracking page
        Account/
          Addresses.razor             # Address management
      Components/
        ProductCard.razor
        CartSummary.razor
        CheckoutProgress.razor
        AddressSelector.razor
      Shared/
        MainLayout.razor
        NavMenu.razor
      wwwroot/                        # Static assets (CSS, images)
      Program.cs                      # Blazor + SignalR + Wolverine setup

tests/
  Customer Experience/
    Storefront.IntegrationTests/      # Alba tests for BFF composition
      CheckoutViewCompositionTests.cs
      RealTimeNotificationTests.cs
```

### Implementation Notes

**SignalR + Wolverine Integration:**
```csharp
// Storefront/Notifications/StorefrontHub.cs
public class StorefrontHub : Hub
{
    public async Task SubscribeToCart(Guid cartId)
    {
        // Add connection to cart-specific group
        await Groups.AddToGroupAsync(Context.ConnectionId, $"cart:{cartId}");
    }

    public async Task SubscribeToOrderUpdates(Guid customerId)
    {
        // Add connection to customer-specific group
        await Groups.AddToGroupAsync(Context.ConnectionId, $"customer:{customerId}");
    }
}

// Storefront/Notifications/CartUpdateNotifier.cs
public static class CartUpdateNotifier
{
    public static async Task Handle(
        Shopping.ItemAdded message,
        IHubContext<StorefrontHub> hubContext,
        IShoppingClient shoppingClient,
        CancellationToken ct)
    {
        // Query Shopping BC for updated cart state
        var cart = await shoppingClient.GetCartAsync(message.CartId, ct);

        // Compose view model
        var cartSummary = new CartSummaryView(
            cart.Id,
            cart.Items.Count,
            cart.Items.Sum(i => i.Quantity * i.UnitPrice));

        // Push to connected clients in this cart's group
        await hubContext.Clients
            .Group($"cart:{message.CartId}")
            .SendAsync("CartUpdated", cartSummary, ct);
    }
}
```

**Blazor Component with Real-Time Updates:**
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

**Status**: Planned (not yet implemented)

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
- `Sku` (string) - Primary identifier, human-readable (e.g., "CBOWL-CER-LG-BLU")
- `Name` (string) - Display name (e.g., "Ceramic Dog Bowl - Large - Blue")
- `Description` (string) - Marketing copy
- `LongDescription` (string) - Full product details
- `Category` (ProductCategory) - Primary category (e.g., "Bowls & Feeders")
- `Subcategory` (string) - Optional subcategory (e.g., "Ceramic Bowls")
- `Brand` (string) - Manufacturer/brand name (e.g., "PetSupreme")
- `Tags` (List<string>) - Searchable tags (e.g., ["dishwasher-safe", "non-slip", "large-breed"])
- `Images` (List<ProductImage>) - Product photos (primary + additional angles)
- `Dimensions` (ProductDimensions) - Physical size/weight for shipping calculations
- `Status` (ProductStatus) - Active, Discontinued, ComingSoon, OutOfSeason
- `AddedAt` (DateTimeOffset) - When product was added to catalog
- `UpdatedAt` (DateTimeOffset) - Last modification timestamp

**ProductCategory (Value Object):**
- Hierarchical structure (e.g., "Dogs > Bowls & Feeders > Ceramic Bowls")
- Used for navigation menus and breadcrumbs
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

### Implementation Notes

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

## Future Considerations

The following contexts are acknowledged but not yet defined:

- **Pricing** — price rules, promotional pricing, regional pricing
- **Promotions** — buy-one-get-one, percentage discounts, coupon codes
- **Reviews** — customer product reviews, ratings, moderation
- **Notifications** — email, SMS, push notifications (may move to Customer Experience)
- **Procurement/Supply Chain** — purchasing, vendor management, forecasting
- **Shipping/Logistics** — carrier management, rate shopping
