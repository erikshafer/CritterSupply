# Wolverine Saga Persistence Issue in Multi-Host Integration Tests

**Date**: 2026-03-13
**Reporter**: Erik Shafer (JasperFx Core Team)
**Repository**: [erikshafer/CritterSupply](https://github.com/erikshafer/CritterSupply)
**Branch**: `claude/cycle-27-returns-bc-phase-3`
**Commit**: 2816e1d

## Executive Summary

We've encountered a reproducible issue where Wolverine sagas created via `IMessageBus.InvokeAsync()` in a multi-host integration test environment are not being found by subsequent message handlers, despite the handler invocation completing successfully without exceptions.

**Error**: `Wolverine.Persistence.Sagas.UnknownSagaException: Could not find an expected saga document of type Orders.Placement.Order for id 'xxx'. Note: new Sagas will not be available in storage until the first message succeeds.`

## Test Environment Architecture

### Overview
We're testing cross-bounded-context (BC) integration using Alba + TestContainers with 3 separate ASP.NET Core hosts running simultaneously:
- **Returns BC** (Returns.Api)
- **Orders BC** (Orders.Api) - Contains the saga in question
- **Fulfillment BC** (Fulfillment.Api)

Each host has:
- Its own isolated PostgreSQL database (via TestContainers)
- Its own Marten document store with custom schema (e.g., `orders`, `returns`, `fulfillment`)
- Shared RabbitMQ instance (via TestContainers)
- Wolverine configured with `AutoApplyTransactions()` and `UseDurableOutboxOnAllSendingEndpoints()`

### Test Fixture Code Structure

**Location**: `tests/Returns/Returns.Api.IntegrationTests/CrossBcSmokeTests/CrossBcTestFixture.cs`

```csharp
public sealed class CrossBcTestFixture : IAsyncLifetime
{
    public IAlbaHost ReturnsHost { get; private set; } = null!;
    public IAlbaHost OrdersHost { get; private set; } = null!;
    public IAlbaHost FulfillmentHost { get; private set; } = null!;

    private readonly PostgreSqlContainer _returnsPostgres = /* TestContainer */;
    private readonly PostgreSqlContainer _ordersPostgres = /* TestContainer */;
    private readonly PostgreSqlContainer _fulfillmentPostgres = /* TestContainer */;
    private readonly RabbitMqContainer _rabbitMq = /* TestContainer */;

    public async Task InitializeAsync()
    {
        // Start all 3 Alba hosts in parallel
        var ordersTask = AlbaHost.For<OrdersApi::Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.ConfigureMarten(opts =>
                {
                    opts.Connection(_ordersPostgres.GetConnectionString());

                    // CRITICAL: Orders BC uses custom "orders" schema
                    opts.DatabaseSchemaName = "orders";
                });
                services.ConfigureWolverine(opts =>
                {
                    opts.UseRabbitMq(rabbitMqUri);
                    opts.Policies.AutoApplyTransactions();
                    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

                    // Handler discovery for Order saga
                    var ordersAssembly = typeof(OrdersApi::Program).Assembly
                        .GetReferencedAssemblies()
                        .Select(Assembly.Load)
                        .First(a => a.GetName().Name == "Orders");
                    opts.Discovery.IncludeAssembly(ordersAssembly);
                });
            });
        });
        // ... similar for Returns and Fulfillment hosts

        var hosts = await Task.WhenAll(returnsTask, ordersTask, fulfillmentTask);
        OrdersHost = hosts[1];
    }
}
```

**Key Configuration Details**:
1. Custom database schemas are applied (`orders`, `returns`, `fulfillment`)
2. Handler discovery includes domain assemblies via reflection (due to extern aliases)
3. All 3 hosts share the same RabbitMQ instance but have isolated Postgres databases

## The Saga in Question

**Location**: `src/Orders/Orders/Placement/Order.cs`

```csharp
public sealed class Order : Saga
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public OrderStatus Status { get; set; }
    // ... other properties

    // Saga handler for ShipmentDelivered (the handler that fails to find the saga)
    public OutgoingMessages Handle(FulfillmentMessages.ShipmentDelivered message)
    {
        if (Status is OrderStatus.Delivered or OrderStatus.Closed)
            return new OutgoingMessages();

        DeliveredAt = message.DeliveredAt;
        var decision = OrderDecider.HandleShipmentDelivered(this, message);
        if (decision.Status.HasValue) Status = decision.Status.Value;

        var outgoing = new OutgoingMessages();
        outgoing.Delay(new ReturnWindowExpired(Id), OrderDecider.ReturnWindowDuration);
        return outgoing;
    }
}
```

**Marten Configuration** (from `src/Orders/Orders.Api/Program.cs`):
```csharp
builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = Constants.Orders.ToLowerInvariant(); // "orders"

    // Configure Order saga document storage
    opts.Schema.For<Order>()
        .Identity(x => x.Id)
        .UseNumericRevisions(true)
        .Index(x => x.CustomerId);
})
.AddAsyncDaemon(DaemonMode.Solo)
.UseLightweightSessions()
.IntegrateWithWolverine(config =>
{
    config.UseWolverineManagedEventSubscriptionDistribution = true;
});
```

## Saga Creation Pattern

### The Handler That Creates the Saga

**Location**: `src/Orders/Orders/Placement/PlaceOrderHandler.cs`

```csharp
public static class PlaceOrderHandler
{
    // Wolverine recognizes (Order, ...) return type as saga start handler
    public static (Order, IntegrationMessages.OrderPlaced) Handle(
        Messages.Contracts.Shopping.CheckoutCompleted message)
    {
        var command = new PlaceOrder(/* map message fields */);

        // Pure decider function returns (Order saga, OrderPlaced event)
        return OrderDecider.Start(command, DateTimeOffset.UtcNow);
    }
}
```

**Wolverine Routing Configuration** (from `src/Orders/Orders.Api/Program.cs`):
```csharp
// CheckoutCompleted is routed to INTERNAL local queue (NOT via RabbitMQ)
opts.PublishMessage<Messages.Contracts.Shopping.CheckoutCompleted>()
    .ToLocalQueue("order-placement")
    .UseDurableInbox();
```

**Why This Matters**: In production, `CheckoutCompleted` is published from Shopping BC to RabbitMQ, then consumed by Orders BC via the local queue. In our test environment, Shopping BC is not running, so we must invoke the handler directly.

### Test Helper Method (Current Implementation)

**Location**: `tests/Returns/Returns.Api.IntegrationTests/CrossBcSmokeTests/CrossBcTestFixture.cs:295-323`

```csharp
public async Task<Guid> CreateOrderSagaAsync(
    Guid orderId,
    Guid customerId,
    Messages.Contracts.Shopping.CheckoutCompleted checkoutCompleted)
{
    // Invoke CheckoutCompleted directly on Orders host WITHOUT tracking
    // This triggers PlaceOrderHandler which returns (Order saga, OrderPlaced event)
    using var scope = OrdersHost.Services.CreateScope();
    var messageContext = scope.ServiceProvider.GetRequiredService<Wolverine.IMessageBus>();

    try
    {
        await messageContext.InvokeAsync(checkoutCompleted);
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException(
            $"Failed to create Order saga {orderId} via CheckoutCompleted message. " +
            $"Handler invocation failed: {ex.Message}", ex);
    }

    // Give Wolverine/Marten a moment to persist the saga
    await Task.Delay(500); // Tried 100ms, 500ms - no difference

    return orderId;
}
```

**Key Points**:
1. Uses scoped `IMessageBus` (correctly resolved via `CreateScope()`)
2. `InvokeAsync()` completes successfully without throwing exceptions
3. Added delay to allow for async persistence - no effect
4. Does NOT use `TrackActivity()` to avoid timeout waiting for `OrderPlaced` cascading messages (Inventory/Storefront BCs not running)

## The Failing Test

**Location**: `tests/Returns/Returns.Api.IntegrationTests/CrossBcSmokeTests/FulfillmentToReturnsPipelineTests.cs:25-43`

```csharp
[Fact]
public async Task ShipmentDelivered_Creates_ReturnEligibilityWindow_In_Returns_BC()
{
    // Arrange
    var orderId = Guid.NewGuid();
    var customerId = Guid.NewGuid();
    var shipmentId = Guid.NewGuid();
    var deliveredAt = DateTimeOffset.UtcNow;

    // STEP 1: Create Order saga in Orders BC
    var checkoutCompleted = CreateCheckoutCompletedMessage(orderId, customerId);
    await _fixture.CreateOrderSagaAsync(orderId, customerId, checkoutCompleted);
    // ✅ This completes successfully without exceptions

    // STEP 2: Publish ShipmentDelivered from Fulfillment BC
    var shipmentDelivered = new ShipmentDelivered(
        orderId,
        shipmentId,
        deliveredAt,
        RecipientName: "John Doe");

    // ❌ This fails with UnknownSagaException
    var tracked = await _fixture.ExecuteOnHostAndWaitAsync(
        _fixture.FulfillmentHost,
        shipmentDelivered,
        timeoutSeconds: 30);

    // Assert - never reached
    tracked.Sent.SingleMessage<ShipmentDelivered>()
        .ShouldNotBeNull();
}
```

### Execution Flow

1. **Saga Creation** (STEP 1):
   ```
   Test → CreateOrderSagaAsync()
        → IMessageBus.InvokeAsync(CheckoutCompleted)
        → PlaceOrderHandler.Handle(CheckoutCompleted)
        → Returns: (Order saga, OrderPlaced event)
        → Wolverine stores saga + publishes OrderPlaced to RabbitMQ
        → InvokeAsync() completes successfully (no exception)
        → 500ms delay
   ```

2. **Saga Lookup** (STEP 2):
   ```
   Test → ExecuteOnHostAndWaitAsync(FulfillmentHost, ShipmentDelivered)
        → Wolverine publishes ShipmentDelivered to RabbitMQ
        → Orders BC receives ShipmentDelivered
        → Order.Handle(ShipmentDelivered) invoked
        → Wolverine tries to load Order saga by orderId
        → ❌ UnknownSagaException: Could not find saga document
   ```

### Error Details

**Full Exception**:
```
Wolverine.Persistence.Sagas.UnknownSagaException:
Could not find an expected saga document of type Orders.Placement.Order for id '019ce87a-f260-7c9f-99f3-3f69e8872834'.
Note: new Sagas will not be available in storage until the first message succeeds.

Stack Trace:
   at Internal.Generated.WolverineHandlers.ShipmentDeliveredHandler1543603913.HandleAsync(MessageContext context, CancellationToken cancellation)
   at Wolverine.Runtime.Handlers.Executor.InvokeAsync(MessageContext context, CancellationToken cancellation)
   at Wolverine.Tracking.TrackedSession.ExecuteAndTrackAsync()
```

## What We've Tried

### ✅ Verified Working
1. **Custom Schema Configuration**: Ensured all 3 hosts apply correct database schemas
2. **Handler Discovery**: Confirmed Orders domain assembly is included in Wolverine discovery
3. **Scoped Service Resolution**: Fixed `IMessageBus` resolution using `CreateScope()`
4. **Message Routing**: Verified `CheckoutCompleted` is routed to internal queue in production config
5. **Exception Handling**: Added try-catch around `InvokeAsync()` - no exceptions thrown
6. **Persistence Delay**: Tried 100ms, 500ms delays - no difference

### ❌ Still Failing
- Order saga is not found by subsequent `ShipmentDelivered` handler
- All 6 Cross-BC smoke tests fail with same `UnknownSagaException`
- 44/50 other integration tests pass (Exchange workflow tests work fine)

## Hypotheses

### Hypothesis 1: Transaction Scope Issue
**Theory**: `InvokeAsync()` might use a transaction scope that doesn't commit until the scope is disposed, but the scope is disposed synchronously while the async commit hasn't completed.

**Evidence**:
- Error message says "new Sagas will not be available in storage until the first message succeeds"
- The 500ms delay doesn't help, suggesting it's not just a flush timing issue
- Marten uses `UseLightweightSessions()` which creates new sessions per operation

**Counter-Evidence**:
- `InvokeAsync()` should complete the full message pipeline including commit
- Wolverine's `AutoApplyTransactions()` should handle transaction management

### Hypothesis 2: Saga Registry / Cache Issue
**Theory**: Wolverine maintains a saga registry or cache that isn't being updated when sagas are created via `InvokeAsync()` outside of the normal message pipeline.

**Evidence**:
- Saga creation via message handler should be the standard pattern
- Multi-host scenario with isolated services might not sync saga metadata

**Counter-Evidence**:
- Each host has its own Marten document store and Wolverine runtime
- Sagas should be loaded from Marten on-demand, not cached

### Hypothesis 3: Message Context / Correlation Issue
**Theory**: `InvokeAsync()` without a parent message context might not properly correlate the saga creation with the saga identity tracking.

**Evidence**:
- We're invoking from a test context, not from within a message handler
- Saga correlation relies on message metadata

**Counter-Evidence**:
- `PlaceOrderHandler` explicitly returns `(Order, ...)` tuple which Wolverine should recognize
- The saga has a `Guid Id` property that should be used for identity

### Hypothesis 4: Storage Table / Schema Creation Timing
**Theory**: The Marten schema objects (tables) might not be created or auto-provisioned correctly in the multi-host test environment.

**Evidence**:
- Custom schemas are applied after Alba host creation
- Marten's `AutoCreate.All` might need explicit triggering

**Counter-Evidence**:
- Other tests create and query documents successfully
- Exchange workflow tests work fine (creating/reading Return aggregates)

## Comparison: What Works vs. What Doesn't

### ✅ Working Pattern: Exchange Workflow Tests
**Location**: `tests/Returns/Returns.Api.IntegrationTests/ExchangeWorkflowEndpointTests.cs`

```csharp
// Single-host test - Returns BC only
[Fact]
public async Task POST_approve_exchange_returns_200_and_updates_saga()
{
    // Create return via event sourcing (works fine)
    var returnId = Guid.NewGuid();
    var command = new RequestReturn(/* params */);

    // Direct invocation works
    var tracked = await _fixture.Host.ExecuteAndWaitAsync(ctx =>
    {
        return ctx.InvokeAsync(command);
    });

    // Saga is found on subsequent operations
    var result = await _fixture.Host.Scenario(scenario =>
    {
        scenario.Post.Json(approveCommand).ToUrl($"/returns/{returnId}/approve-exchange");
        scenario.StatusCodeShouldBe(200);
    });
}
```

**Why It Works**:
- Single host (Returns BC only)
- Event-sourced aggregate (not a saga)
- Uses Alba's `Scenario()` which includes HTTP round-trip
- All within same Wolverine runtime

### ❌ Failing Pattern: Cross-BC Saga Tests
**Location**: `tests/Returns/Returns.Api.IntegrationTests/CrossBcSmokeTests/`

```csharp
// Multi-host test - Orders, Returns, Fulfillment BCs
[Fact]
public async Task ShipmentDelivered_Creates_ReturnEligibilityWindow_In_Returns_BC()
{
    // Create saga in Orders BC via direct invocation (appears to work)
    await _fixture.CreateOrderSagaAsync(orderId, customerId, checkoutCompleted);

    // Publish message from Fulfillment BC (fails to find saga in Orders BC)
    var tracked = await _fixture.ExecuteOnHostAndWaitAsync(
        _fixture.FulfillmentHost,
        shipmentDelivered);
    // ❌ UnknownSagaException
}
```

**Why It Fails**:
- Multi-host environment (3 separate Alba hosts)
- Saga pattern (not event-sourced aggregate)
- Cross-host message routing via RabbitMQ
- Saga created via `InvokeAsync()` in one host, read in same host but from different message

## Alternative Approaches Considered

### Option 1: Use TrackActivity() with Timeout
```csharp
var tracked = await OrdersHost.TrackActivity(TimeSpan.FromSeconds(10))
    .DoNotAssertOnExceptionsDetected()
    .ExecuteAndWaitAsync(ctx => ctx.InvokeAsync(checkoutCompleted));
```

**Problem**: `TrackActivity()` waits for ALL cascading messages including `OrderPlaced` which goes to Inventory/Storefront BCs (not running in this fixture). This causes 30-second timeouts.

### Option 2: Manual Saga Storage via Marten
```csharp
// Original attempt - FAILED
var order = PlaceOrderHandler.Handle(checkoutCompleted).Item1;
using var session = GetOrdersSession();
session.Store(order);
await session.SaveChangesAsync();
```

**Problem**: Error message explicitly says "new Sagas will not be available in storage until the first message succeeds." Manual storage bypasses Wolverine's saga lifecycle management.

### Option 3: Start Inventory/Storefront BCs in Test Fixture
**Problem**: Would require 5 Alba hosts + significant complexity. Tests become too slow and unwieldy.

### Option 4: Mock RabbitMQ and Use In-Memory Queues
**Problem**: Defeats the purpose of integration testing. We want to verify actual RabbitMQ routing.

## Questions for Wolverine Maintainers

1. **Is `IMessageBus.InvokeAsync()` the correct approach for creating sagas in test scenarios where the initiating BC (Shopping) is not running?**

2. **Does Wolverine maintain any in-memory saga registry or cache that might not be updated when using `InvokeAsync()` outside of a message pipeline?**

3. **Are there transaction commit timing issues when using `InvokeAsync()` with Marten sagas in a multi-host environment?**

4. **Is there a recommended pattern for cross-BC integration testing where:
   - Saga is created in BC1 (Orders)
   - Saga is updated by message from BC2 (Fulfillment)
   - BC3 (Shopping) that normally initiates the saga is not running**

5. **Should we use `TrackActivity()` even if it causes timeouts due to unhandled cascading messages? Is there a way to "complete" tracking when specific messages are published, ignoring unhandled ones?**

6. **Is there a difference in saga persistence behavior between:
   - Saga created via message handler returning `(Saga, Event)` tuple
   - Saga created via `session.Store(saga)` directly
   - Saga created via `InvokeAsync()` from a test context**

## Relevant Wolverine/Marten Versions

From `Directory.Packages.props`:
```xml
<PackageVersion Include="Wolverine" Version="5.17.0" />
<PackageVersion Include="Wolverine.Marten" Version="5.17.0" />
<PackageVersion Include="Marten" Version="8.22.2" />
<PackageVersion Include="Marten.AspNetCore" Version="8.22.2" />
```

## Reproducible Test Case

**Repository**: https://github.com/erikshafer/CritterSupply
**Branch**: `claude/cycle-27-returns-bc-phase-3`
**Commit**: 2816e1d

**To Reproduce**:
```bash
git clone https://github.com/erikshafer/CritterSupply
cd CritterSupply
git checkout claude/cycle-27-returns-bc-phase-3
git checkout 2816e1d

# Requires: Docker Desktop, .NET 10 SDK
dotnet build
dotnet test tests/Returns/Returns.Api.IntegrationTests/Returns.Api.IntegrationTests.csproj \
    --filter "FullyQualifiedName~CrossBcSmokeTests"
```

**Expected**: All 6 tests pass
**Actual**: All 6 tests fail with `UnknownSagaException`

**Relevant Files**:
- Test fixture: `tests/Returns/Returns.Api.IntegrationTests/CrossBcSmokeTests/CrossBcTestFixture.cs`
- Failing test: `tests/Returns/Returns.Api.IntegrationTests/CrossBcSmokeTests/FulfillmentToReturnsPipelineTests.cs`
- Order saga: `src/Orders/Orders/Placement/Order.cs`
- Saga start handler: `src/Orders/Orders/Placement/PlaceOrderHandler.cs`
- Orders BC config: `src/Orders/Orders.Api/Program.cs`

## Additional Context

**Domain**: CritterSupply is a reference architecture for event-driven .NET systems using Wolverine + Marten ("Critter Stack"). It demonstrates pragmatic DDD, CQRS, sagas, and cross-BC integration in a realistic e-commerce domain.

**Test Philosophy**: We prefer integration tests over unit tests, testing complete vertical slices with Alba + TestContainers. The Exchange workflow tests (single BC) work perfectly. The Cross-BC smoke tests are the first time we're testing actual multi-BC message flows with sagas.

**Business Context**: The failing test verifies that when Fulfillment BC delivers a shipment, a return eligibility window is created in Returns BC. This requires the Order saga in Orders BC to handle `ShipmentDelivered` and update its state before Returns BC can create the return window.

## Contact

**Erik Shafer**
- GitHub: [@erikshafer](https://github.com/erikshafer)
- Repository: [erikshafer/CritterSupply](https://github.com/erikshafer/CritterSupply)
- JasperFx Core Team Member

---

*This document will be updated as we discover more information or find a solution.*
