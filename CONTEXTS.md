# Bounded Contexts in CritterSupply

This document defines the bounded contexts within the CritterSupply e-commerce system, their responsibilities, invariants, key events, and integration points. Each context has clear ownership and well-defined boundaries. Communication principles, patterns, and technologies are also defined.

This document is meant to be kept up to date as the system evolves. Many segments are marked "To be determined" or "Future" to indicate ongoing design and implementation work.

## Order Management

**Purpose**: Orchestrate the entire order lifecycle from placement through delivery and potential returns.

**Current Status**: Developing a **Stateful Saga** pattern using Wolverine's built-in saga features.

### Responsibilities

✅ **Owns**:
- Order creation and lifecycle state (Placed → Confirmed → Paid → Shipped → Delivered)
- Order line items (products, quantities, pricing at order time)
- Order status transitions and validation rules
- Initiating coordination between dependent contexts
- Saga orchestration for multi-step workflows

❌ **Does Not Own**:
- Tax/shipping calculation (delegated to yet-to-be-implemented Pricing context)
- Payment capture/processing (delegated to Payment Processing)
- Physical inventory management (delegated to Inventory Management)
- Fulfillment/shipping coordination (delegated to yet-to-be-implemented Fulfillment context)
- Customer relationship data (delegated to yet-to-be-implemented Customer context)
- Returns/refunds workflows (initiated by Order, coordinated with Payment Processing and Inventory Management)

### Core Invariants

- **An order cannot transition to "Shipped" unless it is in "Paid" state**
- **An order cannot proceed without confirmed inventory allocation from Inventory Management**
- **Idempotency**: The same order event must never be applied twice; event-driven architecture ensures this through event sourcing
- **An order's line items are immutable** after placement (pricing and products locked at order creation time)

### Key Events Published

- `OrderPlaced` - Order created by customer (published by Shopping context, received by Order Management)
- `OrderConfirmed` - Order confirmed after payment/fulfillment validation
- `OrderPaid` - Payment successfully captured (published by Payment Processing)
- `OrderShipped` - Order dispatched from warehouse (published by Fulfillment context)
- `OrderDelivered` - Order received by customer (published by Fulfillment context)
- `OrderRefundRequested` - Refund initiated (triggers Payment Processing and Inventory Management coordination)
- `OrderCanceled` - Order canceled by customer or system
- 
### External Dependencies & Integration Points

| Context | Integration Type | Direction | Purpose |
|---------|------------------|-----------|---------|
| **Payment Processing** | Message-driven (async) | Consumes | Receives `PaymentCaptured` to transition to "Paid" state |
| **Inventory Management** | Message-driven (async) | Bidirectional | Requests inventory allocation; receives `InventoryReserved` confirmation |
| **Fulfillment** (Future) | Message-driven (async) | Consumes | Receives `OrderShipped` and `OrderDelivered` events |
| **Pricing** (Future) | Query/gRPC (future) or Sync HTTP | One-way | Fetches tax/shipping costs at order creation |
| **Customer Service** (Future) | Message-driven (async) | Bidirectional | Coordinates refunds and complaint handling |

### Saga Workflow (Current & Future)

To be determined.

#### Error Handling in Saga

To be determined.

### Known Considerations

This list will likely grow as development progresses. Ask questions regularly about the business and how the code should behave.

- **Multi-warehouse Fulfillment**: Saga may need to split orders across warehouses (future Fulfillment context concern)

---

## Payment Processing

**Purpose**: Handle payment authorization, capture, and refunds by delegating to third-party payment gateways while maintaining system consistency and audit trails.

**Current Status**: Needs to be implemented.  Address the stub implementation, which will mimic real payment gateways (Stripe, PayPal, Square).

### Responsibilities

✅ **Owns**:
- Payment gateway integration and abstraction
- Payment authorization and capture workflows
- Refund processing and reconciliation
- Currency conversion and localization (USD, CAD, EUR)
- **Idempotency key management** to prevent duplicate charges
- Payment state tracking and audit logs
- PCI-DSS compliance concerns (delegating card data storage to payment gateway)

❌ **Does Not Own**:
- Customer billing address validation (delegated to Shopping)
- Tax calculation (delegated to Pricing context)
- Order lifecycle (delegated to Order Management)
- Refund approval workflow (initiated by Order Management/Customer Service context)

### Core Invariants

- **Payments must be idempotent**: The same payment request with the same idempotency key must always result in the same outcome
- **A refund cannot exceed the original payment amount**
- **Payment state transitions are unidirectional**: Authorization → Capture or Authorization → Failed (no rollback to Authorization once Captured)
- **Currency conversion must be logged**: All conversions from order currency to gateway currency must be auditable
- **No card data is stored locally**: All sensitive payment data is tokenized and stored with the payment gateway

### Key Events Published

- `PaymentAuthorized` - Payment approved by gateway (not yet captured)
- `PaymentCaptured` - Payment successfully charged (published to Order Management)
- `PaymentFailed` - Payment declined or errored
- `PaymentRefunded` - Refund successfully processed
- `PaymentRefundFailed` - Refund attempt failed
- `CurrencyConverted` - Record of currency conversion for audit
- `IdempotencyKeyUsed` - Logging for duplicate detection

### External Dependencies & Integration Points

| Context/System | Integration Type | Direction | Purpose |
|---|---|---|---|
| **Order Management** | Message-driven (async) | Consumes | Receives payment requests; sends `PaymentCaptured` |
| **Stripe / PayPal / Square** | REST API (sync) | Bidirectional | Gateway integration for authorization, capture, refunds |
| **Customer Service** (Future) | Message-driven (async) | Receives | Receives refund requests from dispute handling |
| **Accounting** (Future, if exists) | Event streaming or API | Publishes | Reports payment events for financial reconciliation |

### Payment Flow & Idempotency

To be determined.


### Currency Conversion Strategy

- **At order time**: Order total is in customer's preferred currency (USD, CAD, EUR)
- **At payment time**: Payment Processing converts to gateway's native currency if needed
- **Logging**: All conversions logged with rate used and timestamp for audit
- **No rounding surprises**: Conversion logic uses proper decimal handling (avoid floating-point)

### Known Considerations

- **Webhook Handling**: Payment gateways send webhooks (e.g., "payment_intent.succeeded"); Payment Processing must validate and handle these idempotently
- **Reconciliation**: Future need for periodic reconciliation with payment gateway statements
- **Chargeback Handling**: Future consideration for dispute/chargeback workflows
- **Rate Limiting**: Payment gateway API rate limits must be handled gracefully

---

## Inventory Management

**Purpose**: Track product availability, manage reservations for pending orders, and coordinate stock allocation across multiple warehouses and fulfillment channels.

**Current Status**: Yet to be implemented. Address basic single-warehouse reservation logic first; multi-warehouse and backorder support designed but not yet implemented.

### Responsibilities

✅ **Owns**:
- Product availability tracking by warehouse
- Inventory allocation (reserving stock when orders are placed)
- Inventory depletion (committing stock when orders ship)
- Inventory release (returning stock to available when orders are cancelled)
- Multi-warehouse stock management (US, Mexico, Canada, UK warehouses)
- Backorder and pre-order logic (designed, not yet implemented)
- Query APIs for real-time stock availability

❌ **Does Not Own**:
- Cost of goods or pricing (delegated to Pricing context)
- Warehouse operations/logistics (delegated to Fulfillment context)
- Tax/regulatory compliance for goods movement (delegated to Pricing/Compliance contexts)
- Returns processing workflow (coordinated with Order Management and Payment Processing)

### Core Invariants

- **Reserved stock cannot exceed available stock** at time of reservation
- **Stock quantities are never negative** (invariant enforced at application level)
- **Reservations are temporary**: Must eventually transition to either "Committed" (shipped) or "Released" (cancelled)
- **A product cannot have reserved stock after it's fully committed or released**
- **Warehouse stock is fungible**: All units of a product in a warehouse are interchangeable

### Key Events Published

- `InventoryAdded` - New stock added to warehouse (replenishment)
- `InventoryReserved` - Stock allocated for an order (order is pending fulfillment)
- `InventoryCommitted` - Reserved stock confirmed as shipped (order shipped)
- `InventoryReleased` - Reserved stock returned to available (order cancelled)
- `InventoryDepleted` - Product marked as out of stock and will not be restocked (end-of-life product)
- `BackorderCreated` - Allocation requested but stock unavailable (future)
- `BackorderFulfilled` - Backorder fulfilled when stock arrives (future)

### External Dependencies & Integration Points

| Context | Integration Type | Direction | Purpose |
|---------|------------------|-----------|---------|
| **Order Management** | Message-driven (async) + Query (future gRPC) | Bidirectional | Receives allocation requests; publishes `InventoryReserved`; receives commit/release requests |
| **Fulfillment** (Future) | Message-driven (async) | Consumes | Receives `OrderShipped` notification to commit inventory |
| **Pricing** (Future) | Query only | One-way | May query historical cost data if needed for analytics |
| **Warehouse Management** (Future) | Sync HTTP or gRPC | Bidirectional | Physical warehouse systems for picking/packing validation |

### Reservation State Machine

To be determined.


### Multi-Warehouse Allocation Strategy

**Single Warehouse (Future)**:
- All products have one inventory stream
- Reservations are straightforward

**Multi-Warehouse (Future)**:
- Each warehouse has its own ProductInventory stream (or nested structure)
- Allocation logic must decide which warehouse fulfills the order:
    1. Warehouse selection by proximity to customer
    2. Warehouse with sufficient stock
    3. Fallback to backorder if no warehouse has stock
    4. Split orders across warehouses if needed

### Query APIs (Future)

**Current Queries** (HTTP endpoint):
- `GET /api/products/{productId}/inventory` → Current stock levels

**Future Queries** (gRPC for performance-critical paths):
- `CheckStockAvailability(productId, quantity)` → Boolean
- `GetInventoryByWarehouse(productId)` → Warehouse-level details
- `GetBackorderStatus(orderId)` → Backorder status and ETA

### Backorder/Pre-order Design

To be determined.

### Known Considerations

This list is in development.

- **Reservation Timeout**: Reservations older than X days (configurable) should auto-release if order not confirmed
- **Warehouse Rebalancing**: Future need to move stock between warehouses to optimize fulfillment
- **Cycle Counting**: Reconciliation between event-sourced inventory and physical warehouse counts
- **Returns Reintegration**: When a return is processed, inventory must be added back (separate workflow, not yet implemented)
- **Dead Stock**: Products that never sell; inventory tracking should help identify these for deprecation


---

## Future Contexts (Planned)

These contexts are identified but not yet implemented. They are mentioned here for architectural awareness.

### Pricing
- Tax calculation by jurisdiction
- Shipping cost calculation
- Discount/coupon application
- Currency localization

### Fulfillment
- Warehouse operations (picking, packing)
- Shipping carrier integration
- Delivery tracking
- Return logistics coordination

### Shopping (formerly Cart & Checkout)
- Shopping cart management
- Promo code validation
- Checkout flow orchestration
- Triggers `OrderPlaced` event to Order Management

### Customer (Name TBD)
- Customer profiles and preferences
- Address management
- Contact information
- Wishlist/saved items
- Customer Service workflows (complaints, disputes)

### Pricing (Name TBD)
- Historical pricing records
- Cost tracking for analytics
- Revenue recognition
- Financial reporting

---

## Context Communication Patterns

### Message-Driven (Async)

Used for non-blocking, eventual consistency workflows:
- Order Management → Inventory Management: "Reserve inventory"
- Inventory Management → Order Management: "Inventory reserved"
- Order Management → Payment Processing: "Capture payment"
- Payment Processing → Order Management: "Payment captured"

**Tools**: Wolverine (command execution + event publishing), RabbitMQ (AMQP message broker)

### Query APIs (Sync)

Used for immediate consistency or performance-critical reads:
- Shopping context → Inventory Management: "Check stock availability"
- Order Management → Pricing context: "Get tax/shipping costs"

**Current Tools**: HTTP REST  
**Future Tools**: gRPC for high-throughput queries

### Event Streams (Async Subscribe & Replay)

Used for projections, analytics, and event sourcing:
- All contexts publish domain events to Marten event store
- Sagas and projections consume and react to events
- Idempotency ensures events can be replayed safely

---

## Integration Principles

1. **Prefer Async Over Sync**: Use message-driven communication to decouple contexts
2. **Idempotency First**: All event handlers must be idempotent (safe to replay)
3. **Event Sourcing as Source of Truth**: Marten event store is the authoritative record
4. **Sagas for Long-Running Workflows**: Use Wolverine sagas to orchestrate multi-step processes
5. **Clear Boundaries**: Each context owns its data model; no shared databases
6. **Versioning**: API contracts and event schemas must support evolution without breaking other contexts
