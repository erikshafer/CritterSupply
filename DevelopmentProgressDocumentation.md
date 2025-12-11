# CritterSupply Development Progress

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

- **Orders Integration**: *(Next - in progress)*
  - Add handlers for: `PaymentCaptured`, `PaymentFailed`, `RefundCompleted`, `RefundFailed`
  - Integrate saga state transitions with payment lifecycle

#### 🔄 In Progress

**Cycle 2: Payments → Orders Integration (Starting)**
- Orders saga will consume Payments integration messages
- Update Order saga state machine to handle payment events
- Add Orders integration tests for payment coordination

#### 🔜 Planned

**Cycle 3: Inventory Context**
- Build reservation-based inventory management
- Integrate with Orders saga

**Cycle 4: Fulfillment Context**
- Build shipment and tracking workflows
- Integrate with Orders saga

## Key Principles

1. **Orders is the Integration Hub**: Orders saga is the central orchestrator that coordinates all BC interactions
2. **Incremental Integration**: Each BC is polished independently, then integrated one at a time
3. **Full Test Coverage Before Moving**: Each BC must have passing integration tests before Orders integration
4. **State Verification Over Message Tracking**: Integration tests verify domain model state changes, not infrastructure concerns
5. **Pure Functions First**: Business logic isolated in pure functions, handlers delegate to them
6. **Cascading Messages**: Handlers return messages; Wolverine handles persistence and routing automatically

## Testing Strategy

- **Integration Tests**: Preferred over unit tests; use Alba + Marten + TestContainers
- **Property-Based Tests**: FsCheck for invariant validation (e.g., refund calculations)
- **No Message Mocking**: Real Marten event store; integration messages without routes are acceptable in tests
- **State-Focused Assertions**: Verify final aggregate state, not infrastructure side effects

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
