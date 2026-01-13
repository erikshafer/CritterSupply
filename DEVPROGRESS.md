# CritterSupply Development Progress

Notes about the current "dev status" or "dev progress" of the CritterSupply project. Highlighting the development style, current cycle, completed work, and next steps without implementation details.

## Development Style: Ping-Pong Integration Pattern

This project follows a **"ping-pong" development approach** where work alternates between polishing a bounded context and integrating it back with the Orders saga (the "integration hub").

### Current Development Cycle

**Cycle Pattern:**

Payments Context → Orders Integration → Inventory Context → Orders Integration → Fulfillment Context

### Completion Status

#### ✅ Completed Cycles

**Cycle 1: Payments Context (Completed - 2025-12-11)**
- **BC Work**: Full two-phase auth/capture flow, refund processing, comprehensive test coverage
    - Immediate capture: `PaymentRequested` → `PaymentCaptured`
    - Two-phase auth/capture: `AuthorizePayment` → `CapturePayment`
    - Refunds: `RefundRequested` → `RefundCompleted`/`RefundFailed`
    - Property-based testing with FsCheck
    - **Status**: ✅ 20/20 integration tests passing
    - **Files**: `/src/Payment Processing/Payments/` and `/tests/Payment Processing/Payments.Api.IntegrationTests/`
    - **Key Learnings**:
        - Integration tests verify aggregate state, not message tracking (no routes in test environment)
        - Pure function handlers return cascading messages naturally
        - `TestFixture` with `ExecuteAndWaitAsync()` is powerful for integration testing

- **Orders Integration**: ✅ COMPLETED (2025-12-11)
    - ✅ Handler 1: `Handle(PaymentCaptured)` - Transitions to PaymentConfirmed
    - ✅ Handler 2: `Handle(PaymentFailed)` - Transitions to PaymentFailed
    - ✅ Handler 3: `Handle(PaymentAuthorized)` - Transitions to PendingPayment
    - ✅ Handler 4: `Handle(RefundCompleted)` - Maintains current state (financial operation)
    - ✅ Handler 5: `Handle(RefundFailed)` - Maintains current state (failure tracking)
    - **Integration Test Results**: ✅ All 5 payment integration tests passing
    - **Key Achievement**: Orders saga now fully orchestrates payment lifecycle

- **Shared Contracts Refactoring**: ✅ COMPLETED (2025-12-11)
    - ✅ Created `src/Messages.Contracts/` project for cross-context communication
    - ✅ Moved 5 integration messages from Payments.Processing to Messages.Contracts.Payments
    - ✅ Improved naming: Removed "Integration" suffix (e.g., `PaymentCapturedIntegration` → `PaymentCaptured`)
    - ✅ Updated Orders to reference Messages.Contracts instead of Payments directly
    - ✅ Removed Orders → Payments project dependency (true bounded context separation)
    - ✅ Fixed 3 failing Payments unit tests (namespace updates)
    - **Final Result**: All 74 tests passing (23 unit + 20 Orders integration + 20 Payments integration + 11 Orders unit)
    - **Architectural Benefit**: Proper separation of concerns; no direct cross-context project references

**Cycle 2: Inventory Context (Completed - 2025-12-18)**
- **BC Work**: Reservation-based inventory with commit/release flows, event-sourced aggregates
    - Reservation flow: `ReserveStock` → `ReservationConfirmed`/`ReservationFailed`
    - Commitment: `CommitReservation` → `ReservationCommitted`
    - Release/Compensation: `ReleaseReservation` → `ReservationReleased`
    - Event-sourced `ProductInventory` aggregate with pure functions
    - **Status**: ✅ 16/16 integration tests passing (13 BC + 3 OrderPlaced choreography)
    - **Files**: `/src/Inventory Management/Inventory/` and `/tests/Inventory Management/Inventory.Api.IntegrationTests/`
    - **Key Learnings**:
        - Choreography for initiation: Inventory reacts to `OrderPlaced` autonomously
        - Event-sourced aggregates with `PendingEvents` pattern work well with Wolverine
        - Grouping line items by SKU prevents duplicate reservations

- **Orders Integration**: ✅ COMPLETED (2025-12-18)
    - ✅ Choreography: `OrderPlaced` → Inventory creates reservations automatically
    - ✅ Handler 1: `Handle(ReservationConfirmed)` - Transitions to InventoryReserved, tracks reservation IDs
    - ✅ Handler 2: `Handle(ReservationFailed)` - Transitions to InventoryFailed
    - ✅ Handler 3: `Handle(ReservationCommitted)` - Transitions to InventoryCommitted
    - ✅ Handler 4: `Handle(ReservationReleased)` - Tracks compensation (no state change)
    - ✅ Orchestration: `PaymentCaptured` → publishes `ReservationCommitRequested` when inventory reserved
    - ✅ Compensation: `PaymentFailed` → publishes `ReservationReleaseRequested` to release inventory
    - **Integration Test Results**: ✅ All 5 inventory integration tests passing in Orders
    - **Key Achievement**: Orders saga orchestrates commit/release timing; Inventory reacts to initiation

- **Shared Contracts Expansion**: ✅ COMPLETED (2025-12-18)
    - ✅ Created `Messages.Contracts.Orders` namespace for `OrderPlaced`, `OrderLineItem`, `ShippingAddress`
    - ✅ Added `Messages.Contracts.Inventory` with 4 response messages
    - ✅ Added orchestration messages: `ReservationCommitRequested`, `ReservationReleaseRequested`
    - **Final Result**: All 95 tests passing (11 Orders unit + 23 Payments unit + 16 Inventory integration + 20 Payments integration + 25 Orders integration)
    - **Architectural Benefit**: Full bidirectional orchestration; Orders controls timing, Inventory executes

- **Documentation**: ✅ COMPLETED (2025-12-18)
    - ✅ Added "CONTEXTS.md as Architectural North Star" principle to CLAUDE.md
    - ✅ Added integration flows for Orders, Payments, and Inventory to CONTEXTS.md
    - Text-based flow diagrams document choreography vs orchestration patterns

**Cycle 3: Fulfillment Context (Completed - 2025-12-18)**
- **BC Work**: Shipment lifecycle with event-sourced aggregates, tracking workflow
    - Fulfillment flow: `FulfillmentRequested` → `ShipmentDispatched` → `ShipmentDelivered`/`ShipmentDeliveryFailed`
    - Warehouse assignment and tracking integration
    - Event-sourced `Shipment` aggregate with pure `Apply()` functions
    - **Status**: ✅ 6/6 integration tests passing
    - **Files**: `/src/Fulfillment Management/Fulfillment/` and `/tests/Fulfillment Management/Fulfillment.Api.IntegrationTests/`
    - **Key Learnings**:
        - Choreography: Fulfillment reacts autonomously to `FulfillmentRequested` from Orders
        - `MarkCompleted()` deletes saga documents (verified via WebSearch of Wolverine docs)
        - Integration tests confirm saga deletion as proof of completion

- **Orders Integration**: ✅ COMPLETED (2025-12-18)
    - ✅ Choreography: Fulfillment creates shipments when `FulfillmentRequested` received
    - ✅ Orchestration: Orders publishes `FulfillmentRequested` after payment + inventory confirmed
    - ✅ Handler 1: `Handle(ShipmentDispatched)` - Transitions to Shipped
    - ✅ Handler 2: `Handle(ShipmentDelivered)` - Transitions to Delivered, calls `MarkCompleted()`
    - ✅ Handler 3: `Handle(ShipmentDeliveryFailed)` - Remains in Shipped (no backward transition)
    - **Integration Test Results**: ✅ All 4 fulfillment integration tests passing in Orders
    - **Key Achievement**: Orders saga completes successfully with saga deletion on delivery

- **Shared Contracts Expansion**: ✅ COMPLETED (2025-12-18)
    - ✅ Created `Messages.Contracts.Fulfillment` namespace
    - ✅ Added 4 integration messages: `FulfillmentRequested`, `ShipmentDispatched`, `ShipmentDelivered`, `ShipmentDeliveryFailed`
    - ✅ Added shared value objects: `ShippingAddress`, `FulfillmentLineItem`
    - **Final Result**: All 105 tests passing (23 Payments unit + 6 Fulfillment integration + 16 Inventory integration + 29 Orders integration + 20 Payments integration + 11 Orders unit)
    - **Architectural Benefit**: Complete order-to-delivery orchestration with proper saga lifecycle

**Cycle 4: Shopping Context (Completed - 2025-12-22)**
- **BC Work**: Cart and Checkout aggregates with event-sourced lifecycle management
    - Cart flow: `CartInitialized` → `ItemAdded`/`ItemRemoved`/`ItemQuantityChanged` → `CheckoutInitiated` (terminal)
    - Checkout flow: `CheckoutStarted` → `ShippingAddressProvided` → `ShippingMethodSelected` → `PaymentMethodProvided` → `CheckoutCompleted` (terminal)
    - Single terminal event pattern for Cart → Checkout handoff
    - Event-sourced aggregates using modern Critter Stack idiom (pure write models with `[WriteAggregate]`)
    - **Status**: ✅ 15/15 integration tests passing (9 Cart + 6 Checkout)
    - **Files**: `/src/Shopping Management/Shopping/` and `/tests/Shopping Management/Shopping.Api.IntegrationTests/`
    - **Key Learnings**:
        - Commands and handlers colocated in same file (1:1 relationship made explicit)
        - `Status` enum preferred over boolean flags for aggregate state (single source of truth)
        - Pure function handlers with `Before()` for validation, `Handle()` for business logic
        - Modern Wolverine idiom: `[WriteAggregate]` instead of manual `IDocumentSession` usage
        - Integration tests verify aggregate state only, not outgoing messages (external transports disabled)

- **Shared Contracts Expansion**: ✅ COMPLETED (2025-12-22)
    - ✅ Created `Messages.Contracts.Shopping` namespace
    - ✅ Added integration message: `CheckoutCompleted` (published to Orders saga)
    - ✅ Added shared value objects: `CheckoutLineItem`, `ShippingAddress`
    - **Final Result**: All 120 tests passing (15 Shopping integration + previous 105)
    - **Architectural Benefit**: Shopping initiates Orders saga via `CheckoutCompleted` message

- **Documentation Updates**: ✅ COMPLETED (2025-12-22)
    - ✅ Added "File Organization for Commands, Queries, and Handlers" section to CLAUDE.md
    - ✅ Added "Use Status Enums for Aggregate State" section to CLAUDE.md
    - ✅ Shopping BC already documented in CONTEXTS.md with Cart/Checkout specifications

- **Orders Integration**: ✅ COMPLETED (2026-01-05)
    - ✅ Shopping publishes `CheckoutCompleted` → Orders starts saga with `Order.Start()`
    - ✅ Orders publishes `OrderPlaced` to downstream BCs (Payments, Inventory)
    - ✅ Implemented Decider + Saga hybrid pattern: `OrderDecider` (pure functions) + `Order.Start()` (Wolverine convention)
    - ✅ Integration handler maps Shopping contract to Orders command, directly invokes saga
    - ✅ Updated `CheckoutCompleted` signature: Added `OrderId`, `CheckoutId`, `ShippingCost` (9 parameters)
    - ✅ Fixed validator for nullable `CustomerId` (`.NotNull().NotEmpty()`)
    - ✅ Fixed all TotalAmount assertions to include shipping cost
    - **Integration Test Results**: ✅ All 31 Orders integration tests passing (2 Shopping + 29 existing)
    - **Final Result**: All 105 tests passing across solution (20 Payments integration + 16 Inventory integration + 6 Fulfillment integration + 15 Shopping integration + 31 Orders integration + 11 Payments unit + 6 Inventory unit)
    - **Key Achievement**: Complete Shopping → Orders → downstream flow working end-to-end
    - **Key Learnings**:
        - Wolverine saga `Start()` methods must be on saga class (convention-based discovery)
        - Saga Start methods work via direct invocation, not cascading messages
        - Integration handlers should directly call saga methods, not return messages for routing
        - Decider pattern pairs perfectly with sagas: pure logic in `OrderDecider`, thin wrappers on `Order`
        - Test simplification: Deleted premature unit tests; focus on integration tests during architecture flux

**Cycle 5: Payments BC Refactoring - Modern Critter Stack Patterns (Completed - 2026-01-06)**
- **BC Refactoring**: Applied modern Critter Stack idioms to existing Payments BC
    - Refactored to write-only aggregates (removed behavior methods, kept only `Create()` and `Apply()`)
    - Colocated commands, validators, and handlers in single files (1:1 relationship pattern)
    - Applied `[WriteAggregate]` pattern for existing aggregate handlers
    - Migrated from `MartenOps.StartStream()` to `session.Events.StartStream()` for message handlers
    - Changed handler return types from `object` to explicit `(Events, OutgoingMessages)` for clarity
    - **Status**: ✅ 19/19 integration tests passing
    - **Test Breakdown**: 11 Payments unit tests + 19 Payments integration tests = 30 total
    - **Files**: `/src/Payment Processing/Payments/Processing/` (all handlers refactored)
    - **Key Learnings**:
        - `Events` type is clearer than `object` for handler return values - developers immediately understand intent
        - `session.Events.StartStream()` for message handlers, `MartenOps.StartStream()` for HTTP endpoints only
        - Raw tuples `(event, event)` get misinterpreted by Marten as single tuple events - causes persistence failures
        - `OutgoingMessages` wrapper prevents tuple-as-event bugs by explicitly separating concerns
        - Property-based tests consume significant tokens; focus on integration tests during refactoring phases

- **Documentation Updates**: ✅ COMPLETED (2026-01-06)
    - ✅ Added comprehensive "Handler Return Patterns for Event Sourcing" section to CLAUDE.md (lines 775-1016)
    - ✅ Three distinct patterns documented with full examples:
        - Pattern 1: `[WriteAggregate]` handlers return `(Events, OutgoingMessages)`
        - Pattern 2: Message handler stream starts return `OutgoingMessages`, use `session.Events.StartStream()`
        - Pattern 3: HTTP endpoint stream starts return `(IStartStream, HttpResponse)`, use `MartenOps.StartStream()`
    - ✅ Real code examples with `CapturePaymentHandler` and `AuthorizePaymentHandler`
    - ✅ Side-by-side ❌/✅ comparison showing why to avoid `object` returns
    - ✅ Summary table for quick pattern reference
    - **Final Result**: All 98 tests passing across solution (11 Payments unit + 19 Payments integration + 16 Inventory integration + 6 Fulfillment integration + 31 Orders integration + 15 Shopping integration)

**Cycle 6: Inventory BC Refactoring - Modern Critter Stack Patterns (Completed - 2026-01-13)**
- **BC Refactoring**: Applied modern Critter Stack idioms to existing Inventory BC
    - Refactored to write-only aggregates (removed `PendingEvents`, kept only `Create()` and `Apply()`)
    - Colocated commands, validators, and handlers in single files (1:1 relationship pattern)
    - Applied `Load()` pattern for handlers with computed aggregate IDs (e.g., `CombinedGuid(Sku, WarehouseId)`)
    - Used manual `session.Events.Append()` for event persistence when Wolverine can't auto-resolve IDs
    - Renamed `SKU` to `Sku` following .NET naming conventions (only two-letter abbreviations capitalized)
    - **Status**: ✅ 16/16 integration tests passing
    - **Test Breakdown**: 16 Inventory integration tests
    - **Files**: `/src/Inventory Management/Inventory/Management/` (all handlers refactored)
    - **Key Learnings**:
        - `Load()` pattern required when aggregate ID is computed property, not direct command parameter
        - When using `Load()`, return `OutgoingMessages` only (not `(Events, OutgoingMessages)`)
        - Never use `Load()` + `[WriteAggregate]` together - causes double database hits
        - `[WriteAggregate]` is ALWAYS preferred when Wolverine can auto-resolve the aggregate ID

- **Documentation Updates**: ✅ COMPLETED (2026-01-13)
    - ✅ Added "Preference Hierarchy" section to CLAUDE.md explaining when to use `[WriteAggregate]` vs `Load()` pattern
    - ✅ Added decision table: direct ID property → `[WriteAggregate]`, computed ID → `Load()`, query needed → `Load()`
    - ✅ Clarified that `[WriteAggregate]` is the cleanest, most efficient, and idiomatic pattern
    - ✅ Added warning about double database hits when mixing `Load()` + `[WriteAggregate]`
    - **Final Result**: All 98 tests passing across solution

**Cycle 7: Fulfillment BC Refactoring - Modern Critter Stack Patterns (Completed - 2026-01-13)**
- **BC Refactoring**: Applied modern Critter Stack idioms to existing Fulfillment BC
    - Removed `PendingEvents` collection from `Shipment` aggregate (already write-only model)
    - Colocated commands, validators, and handlers in single files:
        - `RequestFulfillment.cs`, `AssignWarehouse.cs`, `DispatchShipment.cs`, `ConfirmDelivery.cs`
    - Handlers already using `[WriteAggregate]` pattern correctly (no changes needed)
    - Deleted old separated handler files (colocation complete)
    - **Status**: ✅ 6/6 integration tests passing
    - **Test Breakdown**: 6 Fulfillment integration tests
    - **Files**: `/src/Fulfillment Management/Fulfillment/Shipments/` (handlers colocated)
    - **Key Learnings**:
        - Fulfillment BC was already following modern patterns (minimal refactoring needed)
        - Only needed: remove `PendingEvents` and colocate handlers with commands
        - `[WriteAggregate]` works perfectly when command has direct `ShipmentId` property

#### 🔄 In Progress

None - Cycle 7 Fulfillment BC refactoring complete!

#### 🔜 Planned

**Future Enhancements:**
- Returns Context (reverse logistics)
- Notifications Context (customer communication)
- Product Catalog Context (pricing and availability queries)

## Key Principles

1. **Orders is the Integration Hub**: Orders saga is the central orchestrator that coordinates all BC interactions
2. **Incremental Integration**: Each BC is polished independently, then integrated one at a time
3. **Full Test Coverage Before Moving**: Each BC must have passing integration tests before Orders integration
4. **State Verification Over Message Tracking**: Integration tests verify domain model state changes, not infrastructure concerns
5. **Decider + Saga Hybrid Pattern**: Pure business logic in `*Decider` classes, thin wrappers on saga for Wolverine conventions
6. **Pure Functions First**: Business logic isolated in pure functions, handlers delegate to them
7. **Cascading Messages**: Handlers return messages; Wolverine handles persistence and routing automatically
8. **Shared Contracts**: Integration messages live in neutral `Messages.Contracts` project; no direct cross-context references
9. **Saga Methods on Saga Class**: Wolverine convention requires `Start()` and handler methods on saga class itself for discovery

## Testing Strategy

- **Integration Tests**: Preferred over unit tests; use Alba + Marten + TestContainers
- **Property-Based Tests**: FsCheck for invariant validation (e.g., refund calculations)
- **No Message Mocking**: Real Marten event store; integration messages without routes are acceptable in tests
- **State-Focused Assertions**: Verify final aggregate state, not infrastructure side effects
- **Full Test Suite After Refactoring**: Run all tests when adding/removing project references or moving code between contexts

## Local Development Setup

### Prerequisites

- **.NET 10**: [Download](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- **Docker Desktop**: [Download](https://docs.docker.com/engine/install/)
- **JetBrains Rider** (recommended): [Download](https://www.jetbrains.com/rider/)

### Quick Start

1. **Start Infrastructure**
   ```bash
   # Launch PostgreSQL, RabbitMQ, and other services
   docker-compose --profile all up -d
   ```

2. **Build Solution**
   ```bash
   dotnet build
   ```

3. **Run Tests**
   ```bash
   # Run all tests
   dotnet test
   
   # Run specific BC tests
   dotnet test "tests/Payment Processing/Payments.Api.IntegrationTests/Payments.Api.IntegrationTests.csproj"
   dotnet test "tests/Order Management/Orders.Api.IntegrationTests/Orders.Api.IntegrationTests.csproj"
   ```

4. **Run Application** (when ready)
   ```bash
   # Terminal 1: Start Payments API
   dotnet run --project "src/Payment Processing/Payments.Api"
   
   # Terminal 2: Start Orders API
   dotnet run --project "src/Order Management/Orders.Api"
   ```

### Database Migrations

Marten handles schema creation automatically. When starting a new bounded context:

1. Marten scans your aggregate types and event types
2. On first run, it creates the necessary PostgreSQL tables and functions
3. Subsequent changes to event types are handled via Marten's versioning

**No manual migrations required**—Marten is event-sourcing-first. Schema evolves with your events.

## Inter-BC Messaging: RabbitMQ Configuration

Bounded contexts communicate asynchronously via **RabbitMQ** using AMQP protocol. Wolverine handles message routing and serialization automatically.

### RabbitMQ Setup

RabbitMQ runs in Docker via `docker-compose`:
```bash
# Already running if you did: docker-compose --profile all up -d
# Access Management UI: http://localhost:15672
# Default credentials: guest / guest
```

## File Structure Reference

- **Messages.Contracts**: `src/Shared/Messages.Contracts/` - Shared integration messages (neutral location, no direct BC dependencies)
- **Shopping BC**: `src/Shopping Management/Shopping/` (Production) + `tests/Shopping Management/Shopping.Api.IntegrationTests/` (Tests)
- **Payments BC**: `src/Payment Processing/Payments/` (Production) + `tests/Payment Processing/Payments.Api.IntegrationTests/` (Tests)
- **Inventory BC**: `src/Inventory Management/Inventory/` (Production) + `tests/Inventory Management/Inventory.Api.IntegrationTests/` (Tests)
- **Fulfillment BC**: `src/Fulfillment Management/Fulfillment/` (Production) + `tests/Fulfillment Management/Fulfillment.Api.IntegrationTests/` (Tests)
- **Orders BC**: `src/Order Management/Orders/` (Saga) + `tests/Order Management/Orders.Api.IntegrationTests/` (Tests)

---

**Last Updated**: 2026-01-13
**Current Developer(s)**: Erik Shafer / Claude AI Assistant
**Development Status**: Cycle 7 Complete → Fulfillment BC Refactored (Modern Critter Stack Patterns)
