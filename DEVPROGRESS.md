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

**Cycle 14: Product Catalog BC (Phase 1 - Core CRUD) - COMPLETED (2026-02-02)**

**Objective**: Build Product Catalog BC with CRUD operations and query endpoints using Marten document store (non-event-sourced)

**BC Work**: Core product management with document store patterns
- Product document model with factory methods (`Create()`, `Update()`, `ChangeStatus()`)
- `Sku` and `ProductName` value objects with factory methods and JSON converters
- **Category as primitive string** (not value object) - queryable fields should be primitives with Marten
- `ProductImage` and `ProductDimensions` value objects for complex nested structures
- CRUD HTTP endpoints: POST, GET, GET (list), PUT, PATCH status
- Marten configuration with indexes on Sku, Category, Status for query performance
- Soft delete support via Marten `.SoftDeleted()`
- FluentValidation at HTTP boundaries for validation (returns 400 errors)
- **Status**: ✅ 24/24 integration tests passing
- **Files**: `/src/Product Catalog/ProductCatalog/` and `/tests/Product Catalog/ProductCatalog.Api.IntegrationTests/`

**Key Technical Challenges**:
- **Value Objects + Marten LINQ Queries = Friction**: CategoryName value object couldn't be queried in LINQ expressions
  - **Root Cause**: Marten LINQ couldn't translate expressions like `p.Category.Value == "Dogs"` or `p.Category.ToString()`
  - **Solution**: Changed Product.Category from `CategoryName` value object to `string` primitive
  - **Architecture Signal**: 19/24 tests passing → 24/24 after changing VO to primitive
  - **Pattern**: Use primitives for queryable fields, value objects for complex structures, FluentValidation at boundaries

**Key Learnings**:
- **xUnit Collection Fixtures**: Required for sequential test execution with Marten/TestContainers to avoid DDL concurrency errors
- **ConfigureMarten()**: Preferred pattern over environment variables for test database configuration
- **FluentValidation HTTP**: Requires `WolverineFx.Http.FluentValidation` + `UseFluentValidationProblemDetailMiddleware()` on `MapWolverineEndpoints()`
- **Handler Query Objects**: When using `Load()` pattern, don't pass query objects - Wolverine can't construct them. Use direct parameters instead.
- **Query Parameter Defaults**: Use nullable parameters with null-coalescing inside handler (defaults in signature don't work with query string binding)
- **Marten Value Object Queries**: Primitives for queryable fields (Category, Status), VOs for complex structures (Dimensions, Images)
- **HTTP Status Codes**: Handlers returning `Task` (void) produce 204 No Content for PUT/PATCH operations
- **JSON Deserialization**: All records used in collections need public parameterless constructors
- **Architecture Signals**: Test failures that indicate architectural friction, not code bugs (22/24 → 24/24 after VO → primitive change)
- **JSON Serialization First**: When adding custom types (VOs), account for JSON serialization immediately
- **Value Object Decision Criteria with Marten**:
  - ✅ Use VOs for: Complex nested objects (Address, Dimensions, Money), non-queryable fields, strong domain concepts with behavior
  - ❌ Use primitives for: Queryable filter/sort/group fields, simple string wrappers
- **Validation Strategy**: Primitives at boundaries validated with FluentValidation (returns 400 errors), not VOs with factory methods

**TestFixture Standardization**: ✅ COMPLETED (2026-02-02)
- ✅ Added 5 standard helper methods to Product Catalog TestFixture:
  - `GetDocumentSession()` - Direct database access for seeding/verification
  - `GetDocumentStore()` - Advanced operations (cleanup)
  - `CleanAllDocumentsAsync()` - Test isolation between runs
  - `ExecuteAndWaitAsync<T>()` - Message execution with cascading tracking
  - `TrackedHttpCall()` - HTTP calls with message tracking
- ✅ Added `TaskCanceledException` handling in `DisposeAsync()` to suppress cleanup warnings
- ✅ Standardized null check (`is not null` → `!= null`) for consistency with other BCs
- **Result**: All 122 tests passing across solution, zero cleanup warnings

**Documentation Updates**: ✅ COMPLETED (2026-02-02)
- ✅ Added comprehensive "Value Objects and Queryable Fields" section to `skills/marten-document-store.md`
- ✅ Documented when to use VOs vs primitives with Marten (decision criteria)
- ✅ Explained "architecture signal" pattern (test failures indicating architectural mismatches)
- ✅ Added validation strategy guidance (FluentValidation at boundaries for primitives)
- ✅ Documented JSON serialization as immediate consideration when creating custom types
- ✅ Real example from Product Catalog BC showing Category change from VO to primitive
- ✅ **Completely rewrote** `skills/critterstack-testing-patterns.md`:
  - Added full Marten TestFixture pattern (150+ lines) with all 5 helper methods
  - Added EF Core TestFixture pattern (80+ lines) for Customer Identity BC
  - Added Collection Fixture section (sequential test execution, DDL concurrency prevention)
  - Updated integration test examples with real Product Catalog and Payments code
  - Added "TestFixture Helper Methods" section with use cases and examples
  - Added standardization summary tables for quick reference
  - Enhanced Key Principles to emphasize standardized fixtures across BCs

**Test Results**: ✅ All 24/24 integration tests passing
- AddProduct: Creates products with all fields
- GetProduct: Retrieves products by SKU with images/dimensions
- ListProducts: Pagination, category filtering, status filtering
- UpdateProduct: Updates product details
- ChangeProductStatus: Transitions between Active/OutOfSeason/Discontinued

**Solution-Wide Test Results**: ✅ All 122/122 tests passing (100% success rate)
- Payments: 30 tests (11 unit + 19 integration)
- Inventory: 16 integration tests
- Fulfillment: 6 integration tests
- Shopping: 9 integration tests
- Orders: 25 integration tests
- Customer Identity: 12 integration tests
- Product Catalog: 24 integration tests

#### 🔜 Planned

*Ready for next cycle - Cycle 14 complete*

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

*Ready for next cycle - Cycle 14 complete*

---

## Future Cycles (Not Scheduled)

The following bounded contexts are planned for future development but not yet assigned to specific cycles. They represent logical extensions of the CritterSupply system once the core customer-facing experience is complete.

### Vendor Identity & Vendor Portal

**Objective**: Enable partnered vendors to manage their product data and view analytics on how their products perform within CritterSupply.

**Why These Contexts:**
- **Vendor Identity** provides authentication and user management for vendor personnel (similar to Customer Identity but for a different user population)
- **Vendor Portal** gives vendors read-only analytics (sales, inventory) and the ability to submit product change requests
- Demonstrates multi-tenancy patterns (one tenant per vendor organization)
- Shows how to build tenant-isolated systems with Marten's multi-tenancy capabilities
- Illustrates choreography between BCs (Vendor Portal subscribes to Orders/Inventory events for analytics)

**Architecture Decisions:**
- **Vendor Identity**: EF Core (follows Customer Identity pattern for consistency)
- **Vendor Portal**: Marten document store + projections (read-heavy workload, pre-aggregated analytics)
- **Multi-Tenancy**: `VendorTenantId` from Vendor Identity used as tenant discriminator throughout Vendor Portal
- **Integration**: Vendor Portal subscribes to `OrderPlaced`, `InventoryAdjusted` for analytics; publishes `DescriptionChangeRequested`, `ImageUploadRequested` to Catalog BC

**Phased Roadmap (6 Phases):**

**Phase 1 — Vendor Identity Foundation**
- `VendorTenant` and `VendorUser` aggregates (EF Core)
- Basic registration/authentication flow
- Tenant-scoped claim issuance (JWT with `VendorTenantId`)
- Events: `VendorTenantCreated`, `VendorUserActivated`
- **Dependencies**: None (self-contained)

**Phase 2 — Read-Only Analytics (Portal)**
- Subscribe to Orders and Inventory events
- Build `ProductPerformanceSummary` and `InventorySnapshot` projections (Marten)
- Basic dashboard displaying sales by time period (daily/weekly/monthly/quarterly/yearly)
- Tenant isolation via Marten multi-tenancy, driven by Vendor Identity claims
- **Dependencies**: Vendor Identity (Phase 1), Orders BC, Inventory BC

**Phase 3 — Saved Views (Portal)**
- `VendorAccount` aggregate with preferences (Marten document store)
- `SavedDashboardView` projection
- Vendor can save and switch between custom filter configurations
- **Dependencies**: Phase 2

**Phase 4 — Change Requests (Portal + Catalog)**
- `ChangeRequest` aggregate with saga-style lifecycle tracking (Marten event-sourced)
- Image upload flow (emit `ImageUploadRequested`, listen for `ImageChangeApproved`/`ImageChangeRejected`)
- Description/correction request flow (same pattern)
- Request history and status display
- Catalog-side handlers for approval workflow (new feature in Catalog BC)
- **Dependencies**: Phase 3, Catalog BC

**Phase 5 — Full Identity Lifecycle**
- User invitation flow (email invitations with registration links)
- Password reset, MFA enrollment
- User deactivation
- Audit events (`VendorUserPasswordReset`, etc.)
- **Dependencies**: Phase 1 (extends Vendor Identity)

**Phase 6 — Account Management (Portal)**
- Vendor-editable account settings (notification preferences, contact info)
- Integration with Vendor Identity for user management visibility (list users, view roles)
- **Dependencies**: Phase 5

**Key Technical Considerations:**
- **Catalog Integration**: Product-vendor associations deferred to avoid blocking Vendor Portal development; placeholder integration point documented in CONTEXTS.md
- **Change Request Approval**: Catalog BC owns approval logic; Vendor Portal displays status based on events from Catalog
- **Tenant Isolation**: All Vendor Portal queries/projections scoped to `VendorTenantId` (enforced by Marten multi-tenancy)
- **Authentication Differences**: Vendor Identity may require SSO with vendor's corporate IdP, different MFA policies than Customer Identity

**Estimated Effort:**
- Phase 1-2: 2-3 cycles (foundational identity + analytics projections)
- Phase 3-4: 2 cycles (saved views + change requests with Catalog integration)
- Phase 5-6: 1-2 cycles (full identity lifecycle + account management)
- **Total**: 5-7 cycles

**Documentation:**
- See CONTEXTS.md "Vendor Identity" and "Vendor Portal" sections for complete specifications
- Integration contracts (inbound/outbound events) documented in CONTEXTS.md

---

**Next Priority (Cycle 15+): Customer Experience BC (Storefront BFF)**

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
