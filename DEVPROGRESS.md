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

**Cycle 8: Checkout Migration - Moving to Orders BC (Completed - 2026-01-13)**
- **Objective**: Migrate Checkout aggregate from Shopping BC to Orders BC to establish clearer bounded context boundaries
- **Strategy**: Option A - Move only Checkout aggregate; Cart remains in Shopping BC
- **Rationale**:
    - Shopping BC focuses on pre-purchase experience (browsing, cart management, future: product search, wishlists)
    - Orders BC owns order lifecycle from checkout through delivery
    - Natural workflow split: Cart (exploratory) vs Checkout (transactional)
    - Future-proof: Shopping BC can grow with browsing features without affecting Orders BC
- **Integration Pattern**: `Shopping.CheckoutInitiated` (published by Cart) → `Orders.CheckoutStarted` (handled by Orders)
- **HTTP Endpoints**: Checkout endpoints remain at `/api/checkouts/*` (flat, resource-centric pattern)
- **Migration Phases**:
    1. ✅ Create `Shopping.CheckoutInitiated` integration message
    2. ✅ Move Checkout aggregate, commands, handlers, events to Orders BC
    3. ✅ Move `/api/checkouts/*` HTTP endpoints to Orders.Api
    4. ✅ Move checkout integration tests to Orders.Api.IntegrationTests
    5. ✅ Cleanup: delete checkout files from Shopping BC, update documentation
- **Status**: Complete - All 6 checkout tests passing in Orders BC
- **Key Learning**: Domain events must include aggregate ID as first parameter for Marten inline projections to work correctly

**Cycle 9: Checkout-to-Orders Integration (Completed - 2026-01-15)**
- **Objective**: Complete the integration between Shopping BC's checkout completion and Orders BC saga initialization
- **BC Work**: Established single entry point pattern and naming conventions
    - Removed dual `Start()` overloads (integration message + local command) - kept ONLY integration message entry point
    - Renamed local command from `CheckoutCompleted` (past tense) to `PlaceOrder` (imperative) to clarify X → Y transformation
    - Clear flow: `Shopping.CheckoutCompleted` (integration) → `Orders.PlaceOrder` (command) → `OrderPlaced` (event)
    - Colocated `PlaceOrderValidator` with command following established pattern
    - Updated CLAUDE.md to explicitly document validator colocation requirement
    - **Status**: ✅ 25/25 integration tests passing (27→25 after removing implementation-detail tests)
    - **Test Strategy**: Deleted property-based tests and validation tests that tested local command (no longer exists)
    - **Helper Method**: Added `TestFixture.CreateCheckoutCompletedMessage()` to create integration messages from test data
- **Orders Integration**: ✅ COMPLETED (2026-01-15)
    - ✅ `Order.Start(Shopping.CheckoutCompleted)` - Single entry point, maps to `PlaceOrder` command
    - ✅ Integration tests verify complete data mapping (line items, addresses, payment tokens)
    - **Key Achievement**: Orders saga now starts exclusively from Shopping BC's checkout completion
- **Architectural Decisions**:
    - **Single Entry Point Principle**: Aggregates have `Create()`, Sagas have `Start()` - ONE method only
    - **Clear Naming Convention**: Integration messages (past tense) map to domain commands (imperative verbs)
    - **Behavior-First Testing**: Removed unit tests testing implementation details; kept integration tests verifying actual system behavior
    - **Validator Colocation**: Commands, validators, and handlers all in same file (1:1:1 relationship)
- **Key Learnings**:
    - Having multiple `Start()` overloads creates confusion about "the right way" to initialize sagas
    - Past-tense command names (`CheckoutCompleted`) were misleading - commands should be imperative (`PlaceOrder`)
    - Property-based tests and validation tests became obsolete when local command entry point removed
    - CLAUDE.md documentation must explicitly call out all colocation requirements (not just imply via examples)

#### 🔄 In Progress

*No active work - ready for next cycle*

#### ✅ Recent Cycles

**Cycle 13: Customer Identity BC - EF Core Migration (Completed - 2026-01-19)**
- **Objective**: Refactor Customer Identity BC from Marten document store to Entity Framework Core to demonstrate relational modeling and EF Core + Wolverine integration
- **BC Work**: Full migration to EF Core with traditional aggregate root patterns
    - `Customer` aggregate root with navigation properties to `CustomerAddress` entities (one-to-many)
    - `CustomerIdentityDbContext` with explicit EF Core configuration (fluent API)
    - EF Core migrations for schema evolution (`InitialCreate` migration)
    - Foreign key relationships with cascade delete
    - Unique constraints: (CustomerId, Nickname), Customer.Email
    - `CustomerIdentityDbContextFactory` for design-time tooling (reads from appsettings.json)
    - Refactored all handlers to use `DbContext` instead of `IDocumentSession`
    - Preserved all existing functionality (add/update/delete addresses, address verification)
    - **Status**: ✅ 12/12 integration tests passing (migration complete)
    - **Files**: `/src/Customer Identity/Customers/AddressBook/` (EF Core entities, context, handlers)
- **Key Technical Challenges**:
    - **DbUpdateConcurrencyException**: Initial implementation loaded Customer entity unnecessarily via navigation properties
        - **Root Cause**: `dbContext.Customers.Include(c => c.Addresses)` caused EF Core to track Customer entity
        - **Solution**: Create `CustomerAddress` directly without loading Customer; use `dbContext.Addresses.Add(address)`
        - **Lesson**: Avoid loading aggregate roots when only adding child entities (foreign key is sufficient)
    - **JSON Serialization**: Returning entity directly from GET endpoints failed (no parameterless constructor)
        - **Solution**: Create `AddressSummary` DTO for API responses (id, type, nickname, displayLine, isDefault, isVerified)
        - **Lesson**: Always project entities to DTOs for HTTP responses (separation of persistence and API contracts)
    - **CustomerAddress.Update() Missing Type Parameter**: UpdateAddress handler wasn't updating address type
        - **Solution**: Add `type` parameter to `Update()` method signature
        - **Lesson**: EF Core change tracking requires explicit property updates (immutability patterns need `with` syntax for records)
- **Key Learnings: EF Core + Wolverine Integration**:
    - **DbContext Injection**: Wolverine injects `DbContext` into handlers just like `IDocumentSession` (same DI pattern)
    - **Change Tracking**: EF Core automatically tracks entity changes; call `SaveChangesAsync()` to persist
    - **Navigation Properties**: Use `.Include()` for eager loading, but avoid loading when not needed (performance + concurrency issues)
    - **Migrations**: EF Core migrations provide versioned schema evolution (better than manual SQL scripts)
    - **Foreign Keys**: Database-level referential integrity enforces aggregate boundaries (cascade deletes, constraint violations)
    - **Design-Time Factory**: `IDesignTimeDbContextFactory` must read connection string from appsettings.json (avoid hardcoded values)
    - **Testing**: Alba + TestContainers work seamlessly with EF Core (same pattern as Marten integration tests)
- **Key Learnings: EF Core vs Marten Decision Criteria**:
    - **Use EF Core when**: Traditional relational model fits naturally (Customer → Addresses), complex joins needed, foreign key constraints valuable, team familiar with EF Core
    - **Use Marten when**: Event sourcing beneficial (Orders, Payments, Inventory), document model fits (flexible schema, JSONB), high-performance queries with JSONB indexes
    - **Architectural Benefit**: Demonstrating both in same system shows when to use each persistence strategy
- **Pedagogical Achievement**:
    - Customer Identity BC now serves as **entry point** for developers learning Critter Stack
    - "Start with Wolverine + EF Core (familiar) → Move to event sourcing (new concepts)"
    - Traditional DDD patterns (aggregate roots, entities, navigation properties) shown alongside event-sourced BCs
    - Proves Wolverine works with existing EF Core codebases (not just Marten)
- **Testing Updates**:
    - All 12 integration tests passing (behavior preserved during migration)
    - Test database renamed: `customers_test_db` → `customer_identity_test_db` (consistent naming)
    - TestContainers pattern unchanged (PostgreSQL container lifecycle same for EF Core and Marten)
- **Documentation Updates**:
    - Added comprehensive EF Core + Wolverine patterns to CLAUDE.md
    - Documented when to use EF Core vs Marten in CLAUDE.md
    - Added package dependency guidance (`WolverineFx.EntityFrameworkCore` vs `WolverineFx.Http.Marten`)
    - Added EF Core entity patterns (immutability, change tracking, DTOs)
    - Connection string configuration best practices (appsettings.json, not hardcoded)
    - Updated CONTEXTS.md with Customer Identity entry points and integration patterns

**Cycle 10: Customer Identity BC - Address Management (Completed - 2026-01-15)**
- **Objective**: Create Customer Identity bounded context with AddressBook subdomain for realistic e-commerce address management
- **BC Work**: AddressBook subdomain
    - `CustomerAddress` entity (id, customerId, type, nickname, address fields, isDefault, lastUsedAt)
    - `AddressType` enum (Shipping, Billing, Both)
    - Commands: `AddAddress`, `UpdateAddress`, `SetDefaultAddress`
    - Queries: `GetCustomerAddresses`, `GetAddressSnapshot`
    - HTTP endpoints with Alba integration tests
- **Persistence**: Relational (Marten document store, not event-sourced)
- **Status**: ✅ Implementation complete - 7/7 tests passing
- **Key Learnings**:
    - GET endpoint handlers need direct parameters, not query objects (Wolverine can't construct from URL)
    - Handler discovery requires `IncludeType<>` with actual type (commands work, static classes don't)
    - Physical folder renamed from "Customer Management" → "Customer Identity" for clarity

**Cycle 11: Shopping ↔ Customer Identity Integration (Completed - 2026-01-15)**
- **Objective**: Wire Customer Identity BC into the checkout flow so customers can select saved addresses instead of entering them manually
- **Implementation Summary**:
    - **Shopping BC**: Created Checkout aggregate with `ShippingAddressSelected` event storing `AddressId`
    - **Shopping BC**: `SelectShippingAddress` command/handler for address selection
    - **Shopping BC**: `CompleteCheckout` handler queries Customer Identity BC via HTTP for `AddressSnapshot`
    - **Shopping BC**: Configured HttpClient for Customer Identity BC integration
    - **Customer Identity BC**: Moved `AddressSnapshot` to shared `Messages.Contracts.CustomerIdentity`
    - **Orders BC**: Updated to receive `CheckoutCompleted` with embedded `AddressSnapshot`
    - **Orders BC**: Saga `Start()` method maps `AddressSnapshot` to internal `ShippingAddress`
    - **Integration Contracts**: Created `Messages.Contracts.CustomerIdentity.AddressSnapshot`
    - **Integration Contracts**: Updated `CheckoutCompleted` to embed `AddressSnapshot` instead of inline fields
    - **Test Updates**: Fixed CheckoutToOrderIntegrationTests and ShoppingIntegrationTests to use `AddressSnapshot`
    - **Cleanup**: Removed obsolete `Messages.Contracts.Shopping.ShippingAddress`
- **Integration Flow**:
    1. Shopping BC → Customer Identity BC: HTTP GET `/api/addresses/{addressId}/snapshot`
    2. Customer Identity BC returns immutable `AddressSnapshot`
    3. Shopping BC → Orders BC: Publishes `CheckoutCompleted` with embedded `AddressSnapshot`
    4. Orders BC persists snapshot (temporal consistency preserved)
- **Status**: ✅ Implementation complete - All 86 tests passing
    - Customer Identity: 7/7
    - Orders: 25/25
    - Shopping: 9/9
    - Payments: 30/30
    - Inventory: 16/16
    - Fulfillment: 6/6
- **Key Learnings**:
    - HTTP integration between BCs via `IHttpClientFactory` works cleanly with Wolverine handlers
    - Snapshot pattern ensures temporal consistency (orders preserve address as it was at checkout time)
    - `[WriteAggregate]` pattern is preferred over manual `Load()` when aggregate ID is directly resolvable
    - Integration contracts must be truly shared (no BC-specific types in Messages.Contracts)

**Cycle 12: Customer Identity BC - Address Verification (Completed - 2026-01-15)**
- **Objective**: Implement address verification service to validate shipping/billing addresses when customers add or update them
- **BC Work**: Address verification infrastructure
    - `VerificationStatus` enum (Unverified, Verified, Corrected, Invalid, PartiallyValid)
    - `AddressVerificationResult` and `CorrectedAddress` records
    - `IAddressVerificationService` interface for pluggable verification providers
    - `StubAddressVerificationService` implementation (always returns verified for development)
    - Updated `AddAddressHandler` and `UpdateAddressHandler` to call verification service
    - Verification results include suggested corrections and confidence scores
    - Fallback strategy: if verification service unavailable, save address as unverified (doesn't block customer)
- **Testing**:
    - 5 new unit tests for `StubAddressVerificationService` (100% coverage)
    - Updated 3 integration tests to assert `IsVerified = true` for verified addresses
    - **Status**: ✅ All 98 tests passing (12 Customer Identity + 86 others)
- **Key Learnings**:
    - Strategy pattern with DI enables easy swapping between stub (dev) and real (prod) verification services
    - Address verification should never block customer flow - unverified addresses still save if service fails
    - Corrected addresses from verification service improve deliverability and reduce fulfillment costs
    - `IsVerified` boolean provides clear signal for downstream processes (e.g., fraud detection, tax calculations)

#### 🔜 Planned

*Ready for next cycle - Cycle 13 complete*

---

**Next Priority (Cycle 14): Product Catalog BC (Phase 1 - Core CRUD)**

**Objective**: Build product catalog with CRUD operations and query endpoints for Customer Experience BC integration

**Why Second:**
- Required dependency for Customer Experience BC (product listings, search)
- Self-contained BC (no dependencies on other BCs for Phase 1)
- Demonstrates read-heavy, query-optimized architecture patterns
- Seed data enables realistic Customer Experience demos

**Key Deliverables:**
- Product aggregate (SKU, name, description, images, category, brand, tags, status)
- Admin endpoints for merchandising team (add/update/status change)
- Query endpoints for product listing and detail (paginated, filterable)
- Seed data with 20-30 realistic products across multiple categories
- Integration tests for CRUD and query operations

**Dependencies:**
- None for Phase 1 (self-contained)
- Future phases integrate with Inventory BC (availability), Pricing BC (prices)

**Implementation Phases:**
1. **Phase 1 (Cycle 13)** - Core Product CRUD + query endpoints
2. **Phase 2 (Cycle 14)** - Category hierarchy management
3. **Phase 3 (Cycle 15)** - Product search functionality
4. **Phase 4 (Post Cycle 15)** - Customer Experience integration

**Implementation Notes:**
- See CONTEXTS.md "Product Catalog" section for complete specification
- **Persistence**: Marten document store (NOT event sourcing) - master data, read-heavy
- **Value Objects**: Strongly-typed identity and domain values with validation
  - `Sku` - Product identifier with constraints (A-Z uppercase, 0-9, hyphens only, max 24 chars)
  - `ProductName` - Product display name with constraints (mixed case, letters/numbers/spaces, special chars `. , ! & ( ) -`, max 100 chars)
  - `CategoryName` - Simple string wrapper for now, future subdomain handles marketplace mapping
  - `ProductImage` - URL validation via `IImageValidator` interface (stub for now)
  - All value objects use factory methods (`Sku.From(string)`) + JSON converters for ergonomics
  - Implicit string operators for Marten queries and serialization transparency
- **Soft Delete**: Use Marten's `.SoftDeleted()` feature - preserve audit trail
- **Collections**: Use `IReadOnlyList<T>` for immutability
- **Integration Messages**: Scaffold `ProductAdded`, `ProductUpdated`, `ProductDiscontinued` - publish but don't test receipt
- **Pricing**: NO hardcoded prices - deferred to Pricing BC entirely
- **Seed Data**: C# code (type-safe) with 20-30 products, placeholder images (via.placeholder.com)

---

**Third Priority (Cycle 15+): Customer Experience BC (Storefront BFF)**

**Objective**: Build customer-facing web store using Backend-for-Frontend pattern with Blazor Server and SignalR

**Why Third:**
- Depends on Product Catalog BC (product listings, search)
- Demonstrates complete end-to-end flow (Shopping → Orders → Fulfillment with UI)
- Showcases Blazor + SignalR + Wolverine integration (real-time cart/order updates)
- BFF pattern shows proper UI composition over multiple BCs

**Key Deliverables:**
- Storefront BFF project (view composition, SignalR hub)
- Blazor Server web app (Cart, Checkout, Order History pages)
- Real-time notifications (cart updates, order status changes)
- Integration tests for BFF composition endpoints

**Dependencies:**
- ✅ Shopping BC (cart queries/commands) - complete
- ✅ Orders BC (checkout + order lifecycle) - complete
- ✅ Customer Identity BC (address queries) - complete with EF Core (Cycle 13)
- ✅ Product Catalog BC (product listing) - complete after Cycle 14

**Implementation Notes:**
- See CONTEXTS.md "Customer Experience" section for complete specification
- Focus on 3-4 key pages (product listing, cart, checkout, order history)
- SignalR hub receives integration messages from domain BCs, pushes to connected clients
- BFF does NOT contain domain logic (composition/orchestration only)

---

**Future Enhancements (Lower Priority):**
- Pricing BC (price rules, promotional pricing, regional pricing)
- Returns Context (reverse logistics)
- Notifications Context (email, SMS, push notifications - may move to Customer Experience)
- Mobile BFF (Customer Experience expansion)
- Reviews BC (customer product reviews, ratings)

**Future BC Naming Review:**
- Review all BC physical folder names ending in "Management" for creativity and clarity
- Candidates for review:
  - Order Management → ? (Orders?)
  - Shopping Management → ? (Shopping Experience?)
  - Fulfillment Management → ? (Fulfillment?)
  - Inventory Management → ? (Inventory?)
  - Payment Processing → (already good)
- Goal: Physical folder names should convey BC purpose without redundant suffixes
- Document naming rationale (similar to Customer Identity analysis)

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

**Last Updated**: 2026-01-15
**Current Developer(s)**: Erik Shafer / Claude AI Assistant
**Development Status**: Cycle 12 Complete (Customer Identity BC - Address Verification)
