# Wolverine Message Handlers

Patterns and practices for building message handlers and HTTP endpoints with Wolverine in CritterSupply.

---

## Table of Contents

1. [Core Principle: The Decider Pattern](#core-principle-the-decider-pattern)
2. [Compound Handler Lifecycle](#compound-handler-lifecycle)
3. [Standard Handler Pattern](#standard-handler-pattern)
4. [Aggregate Handler Workflow (Decider Pattern)](#aggregate-handler-workflow-decider-pattern)
5. [Entity and Document Loading](#entity-and-document-loading)
6. [Handler Return Patterns](#handler-return-patterns)
   - [Pattern 7: HTTP Endpoint with Integration Messages](#pattern-7-http-endpoint-with-integration-messages--m361-addition) ⭐ *M36.1 Addition*
   - [Pattern 8: Async vs Sync Return Types](#pattern-8-async-vs-sync-return-types-m320-lesson) ⭐ *M32 Addition*
7. [Railway Programming in Handlers](#railway-programming-in-handlers)
8. [HTTP Endpoints](#http-endpoints)
9. [Handler Discovery](#handler-discovery)
10. [Error Handling](#error-handling)
11. [Multi-Tenancy](#multi-tenancy)
12. [Anti-Patterns to Avoid](#anti-patterns-to-avoid)
13. [File Organization and Naming](#file-organization-and-naming)

---

## Core Principle: The Decider Pattern

Wolverine's aggregate handler workflow implements the **Decider pattern** — a functional approach to event sourcing credited to Jérémie Chassaing ([Functional Event Sourcing Decider](https://thinkbeforecoding.com/post/2021/12/17/functional-event-sourcing-decider)).

**The Decider pattern separates three concerns:**

1. **Load** — Fetch current aggregate state (handled by Wolverine)
2. **Decide** — Pure function: `(Command, State) → Events` (your `Handle()` method)
3. **Evolve** — Apply events to update state (handled by Marten via `Apply()` methods)

**CritterSupply treats aggregates as immutable write models:**
- Aggregates are "always valid" — validation happens in handlers, not aggregates
- Aggregates are not responsible for decisions — handlers decide what events to emit
- `Handle()` methods are pure functions focused solely on business logic

**This is inspired by A-Frame Architecture:**
- Infrastructure at the edges (data loading, validation, persistence)
- Pure logic in the middle (the `Handle()` method)

**Example from CritterSupply:**

```csharp
// Aggregate: "Always valid" — no decision logic
public class Payment
{
    public Guid Id { get; set; }
    public PaymentStatus Status { get; private set; }
    public decimal Amount { get; private set; }

    public void Apply(PaymentAuthorized e) => Status = PaymentStatus.Authorized;
    public void Apply(PaymentCaptured e) => Status = PaymentStatus.Captured;
}

// Handler: Pure decider function
public static class CapturePaymentHandler
{
    // Infrastructure: Validation at the edge
    public static ProblemDetails Before(CapturePayment cmd, Payment? payment)
    {
        if (payment is null)
            return new ProblemDetails { Detail = "Not found", Status = 404 };
        if (payment.Status != PaymentStatus.Authorized)
            return new ProblemDetails { Detail = "Not authorized", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    // Pure function: Business logic only (Command + State → Events)
    public static async Task<(Events, OutgoingMessages)> Handle(
        CapturePayment cmd,
        [WriteAggregate] Payment payment,
        IPaymentGateway gateway)
    {
        var result = await gateway.CaptureAsync(payment.AuthorizationId);

        var events = new Events();
        events.Add(new PaymentCaptured(payment.Id, result.TransactionId));

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.PaymentCaptured(payment.Id, payment.OrderId));

        return (events, outgoing);
    }
}
```

**Why this matters:**
- `Handle()` is easy to reason about — no infrastructure concerns
- Unit tests are trivial — pure function with no mocks
- Business logic is isolated and auditable

---

## Compound Handler Lifecycle

Wolverine executes handler methods in this order:

| Lifecycle      | Method Names                                                              | Purpose                            |
|----------------|---------------------------------------------------------------------------|-----------------------------------|
| Before Handler | `Before`, `BeforeAsync`, `Load`, `LoadAsync`, `Validate`, `ValidateAsync` | Load data, validate preconditions |
| Handler        | `Handle`, `HandleAsync`                                                   | Business logic (pure function)    |
| After Handler  | `After`, `AfterAsync`, `PostProcess`, `PostProcessAsync`                  | Side effects, notifications       |
| Finally        | `Finally`, `FinallyAsync`                                                 | Cleanup (runs even on failure)    |

**Key points:**

1. **Wolverine discovers these methods by convention** — No interfaces required
2. **Values returned from early methods become parameters for later ones** — "Tuple threading"
3. **Tuple order matters** — Wolverine wires dependencies by type and position
4. **Early methods can short-circuit** — Return `HandlerContinuation.Stop`, `ProblemDetails`, or `IResult`

**Example from Shopping BC:**

```csharp
public static class AddItemToCartHandler
{
    // BEFORE: Pre-validation before aggregate is loaded
    public static ProblemDetails Before(AddItemToCart command, Cart? cart)
    {
        if (cart is null)
            return new ProblemDetails { Detail = "Cart not found", Status = 404 };
        if (cart.IsTerminal)
            return new ProblemDetails { Detail = "Cannot modify completed cart", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    // HANDLE: Main business logic (pure function - no validation errors here)
    [WolverinePost("/api/carts/{cartId}/items")]
    public static async Task<(Events, OutgoingMessages)> Handle(
        AddItemToCart command,
        IPricingClient pricingClient,
        [WriteAggregate] Cart cart,
        CancellationToken ct)
    {
        // Fetch server-authoritative price from Pricing BC
        // Assumes price exists - validation would be in Before/Validate if needed
        var price = await pricingClient.GetPriceAsync(command.Sku, ct);

        var @event = new ItemAdded(command.Sku, command.Quantity, price.BasePrice);
        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Shopping.ItemAdded(
            cart.Id, cart.CustomerId ?? Guid.Empty, command.Sku, command.Quantity, price.BasePrice));

        return ([@event], outgoing);
    }
}
```

**Tuple Threading Example:**

```csharp
public static class ProcessOrderHandler
{
    // Load() returns (Order, Customer) tuple
    public static async Task<(Order?, Customer?)> Load(
        ProcessOrder cmd,
        IDocumentSession session)
    {
        var order = await session.LoadAsync<Order>(cmd.OrderId);
        var customer = await session.LoadAsync<Customer>(cmd.CustomerId);
        return (order, customer);
    }

    // Validate() receives both Order and Customer from Load()
    public static ProblemDetails Validate(ProcessOrder cmd, Order? order, Customer? customer)
    {
        if (order is null) return new ProblemDetails { Detail = "Order not found", Status = 404 };
        if (customer is null) return new ProblemDetails { Detail = "Customer not found", Status = 404 };
        return WolverineContinue.NoProblems;
    }

    // Handle() receives both Order and Customer from Load()
    public static Events Handle(ProcessOrder cmd, Order order, Customer customer)
    {
        // Business logic here
        return [new OrderProcessed(order.Id, customer.PreferredShippingMethod)];
    }
}
```

**⚠️ CRITICAL: Tuple Order Matters**

Wolverine wires dependencies **by position in the tuple**, not by parameter name. If `Load()` returns `(Customer, Order)` but `Handle()` expects `(Order order, Customer customer)`, Wolverine will pass `Customer` as the first parameter (which expects `Order`) — causing runtime errors.

**Always match tuple order to parameter order across methods.**

---

## Standard Handler Pattern

**CritterSupply conventions:**

1. **Commands are sealed records** — Immutable, no logic
2. **Handlers are static classes** — Suffix with `Handler`
3. **Handler methods are static** — Small performance win (no object allocation)
4. **Colocation** — Commands, validators, and handlers in one file

**Template:**

```csharp
// Command
public sealed record ProcessPayment(Guid PaymentId, decimal Amount);

// Validator (optional)
public sealed class ProcessPaymentValidator : AbstractValidator<ProcessPayment>
{
    public ProcessPaymentValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}

// Handler
public static class ProcessPaymentHandler
{
    // Validation filter (optional)
    public static ProblemDetails Before(ProcessPayment cmd, Payment? payment)
    {
        if (payment is null)
            return new ProblemDetails { Detail = "Payment not found", Status = 404 };
        return WolverineContinue.NoProblems;
    }

    // Main handler method
    public static (Events, OutgoingMessages) Handle(
        ProcessPayment cmd,
        [WriteAggregate] Payment payment)
    {
        var events = new Events();
        events.Add(new PaymentProcessed(payment.Id, cmd.Amount));

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.PaymentCompleted(payment.Id, payment.OrderId));

        return (events, outgoing);
    }
}
```

**When to use instance handlers:**

Only when you need constructor-injected dependencies that are too heavy for method injection. Static handlers are preferred for simplicity.

---

## Aggregate Handler Workflow (Decider Pattern)

The aggregate handler workflow is Wolverine's flavor of the Decider pattern. It automates:

1. Loading the aggregate from the event stream
2. Appending events returned by the handler
3. Saving changes and committing the transaction
4. Optimistic concurrency checks

**Three attributes for aggregate loading:**

| Attribute | Use Case | Persistence |
|-----------|----------|-------------|
| `[ReadAggregate]` | Query aggregate state (no modifications) | None — read-only |
| `[WriteAggregate]` | Modify aggregate (append events) | Automatic via return |
| `[AggregateHandler]` | Class-level attribute for single-stream handlers | Automatic via return |

**Prefer `[WriteAggregate]`** — It's parameter-level and supports multi-stream operations, making it more flexible for complex scenarios.

### `[WriteAggregate]` — Standard Pattern

```csharp
public static class CapturePaymentHandler
{
    public static ProblemDetails Before(CapturePayment cmd, Payment? payment)
    {
        if (payment is null)
            return new ProblemDetails { Detail = "Not found", Status = 404 };
        if (payment.Status != PaymentStatus.Authorized)
            return new ProblemDetails { Detail = "Not authorized", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    public static (Events, OutgoingMessages) Handle(
        CapturePayment cmd,
        [WriteAggregate] Payment payment)  // Wolverine loads by PaymentId
    {
        var events = new Events();
        events.Add(new PaymentCaptured(payment.Id, DateTimeOffset.UtcNow));

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.PaymentCaptured(payment.Id, payment.OrderId));

        return (events, outgoing);
    }
}
```

**How Wolverine resolves the aggregate ID:**

1. Look for a command property named `{AggregateName}Id` (e.g., `PaymentId` for `Payment`)
2. Look for a command property with `[Identity]` attribute
3. Look for an HTTP route parameter (e.g., `/payments/{paymentId}`)

**Example with explicit identity:**

```csharp
public sealed record CapturePayment
{
    [Identity] public Guid Id { get; init; }  // Explicitly marks the aggregate ID
    public decimal Amount { get; init; }
}
```

**Example with route parameter:**

```csharp
[WolverinePost("/orders/{orderId}/confirm")]
public static Events Handle(
    ConfirmOrder cmd,
    [WriteAggregate("orderId")] Order order)  // Resolves from route parameter
{
    return [new OrderConfirmed(order.Id)];
}
```

### `[ReadAggregate]` — Query Pattern

Use when you need the aggregate state but won't modify it:

```csharp
[WolverineGet("/orders/{orderId}")]
public static Order? GetOrder(
    Guid orderId,
    [ReadAggregate] Order? order)  // Read-only, no events appended
{
    return order;
}

[WolverineGet("/orders/{orderId}/status")]
public static OrderStatusResponse GetStatus(
    Guid orderId,
    [ReadAggregate] Order? order)
{
    if (order is null)
        return new OrderStatusResponse { Status = "NotFound" };

    return new OrderStatusResponse
    {
        Status = order.Status.ToString(),
        LastUpdated = order.UpdatedAt
    };
}
```

**Key differences from `[WriteAggregate]`:**
- No events are persisted
- No optimistic concurrency checks
- Lighter weight (no pessimistic locking)
- Ideal for GET endpoints

### `[AggregateHandler]` — Class-Level Pattern

`[AggregateHandler]` is a **class-level** attribute that applies the aggregate handler workflow to all methods in the handler class.

**When to use it:**
- Single-stream operations (one aggregate per handler)
- You prefer class-level attribute for handler organization
- Handler contains multiple related methods all working with the same aggregate

**Why `[WriteAggregate]` is often preferred:**
- More explicit (parameter-level vs class-level)
- Supports multi-stream operations (multiple aggregates in one handler)
- More flexible for complex scenarios

**Example with `[AggregateHandler]`:**

```csharp
[AggregateHandler]  // ✅ Class-level — applies to all methods
public static class MarkItemReadyHandler
{
    public static Events Handle(MarkItemReady cmd, Order order)
    {
        return [new ItemReady(cmd.ItemName)];
    }
}
```

**Equivalent with `[WriteAggregate]`:**

```csharp
public static class MarkItemReadyHandler
{
    public static Events Handle(
        MarkItemReady cmd,
        [WriteAggregate] Order order)  // ✅ Parameter-level — more explicit
    {
        return [new ItemReady(cmd.ItemName)];
    }
}
```

Both patterns are fully supported. Choose based on your team's preferences and the complexity of your handlers.

### Optimistic Concurrency

Wolverine uses the `Version` property on your command to enforce optimistic concurrency:

```csharp
public sealed record CapturePayment(Guid PaymentId, decimal Amount, int Version);

public static class CapturePaymentHandler
{
    public static Events Handle(
        CapturePayment cmd,
        [WriteAggregate] Payment payment)  // Version check happens automatically
    {
        return [new PaymentCaptured(payment.Id)];
    }
}
```

**How it works:**
1. Wolverine calls `session.Events.FetchForWriting<Payment>(cmd.PaymentId, cmd.Version)`
2. Marten verifies the stream version matches `cmd.Version`
3. If mismatch → `ConcurrencyException` (another process modified the aggregate)
4. If match → Events appended, stream version incremented

**Always include `Version` in commands** — Without it, concurrent writes can overwrite each other.

### When NOT to Use `[WriteAggregate]`

Use the `Load()` pattern when:

1. **Aggregate ID must be computed** (e.g., composite keys)
2. **Multiple aggregates needed** (load separately)
3. **Conditional loading** (e.g., try multiple IDs)

**Example: Composite Key**

```csharp
public sealed record ReserveStock(Guid OrderId, string Sku, string WarehouseId, int Quantity)
{
    // Wolverine can't auto-resolve this
    public Guid InventoryId => ProductInventory.CombinedGuid(Sku, WarehouseId);
}

public static class ReserveStockHandler
{
    // Load() manually fetches the aggregate
    public static async Task<ProductInventory?> Load(
        ReserveStock cmd,
        IDocumentSession session,
        CancellationToken ct)
    {
        return await session.LoadAsync<ProductInventory>(cmd.InventoryId, ct);
    }

    public static ProblemDetails Before(ReserveStock cmd, ProductInventory? inventory)
    {
        if (inventory is null)
            return new ProblemDetails { Detail = "Inventory not found", Status = 404 };
        return WolverineContinue.NoProblems;
    }

    // NO [WriteAggregate] — we loaded manually
    public static OutgoingMessages Handle(
        ReserveStock cmd,
        ProductInventory inventory,
        IDocumentSession session)
    {
        var @event = new StockReserved(cmd.OrderId, cmd.Quantity);
        session.Events.Append(inventory.Id, @event);  // Manual persistence

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.StockReserved(cmd.OrderId, cmd.Sku, cmd.Quantity));
        return outgoing;  // Return ONLY OutgoingMessages
    }
}
```

**⚠️ CRITICAL:** When using `Load()` + manual `session.Events.Append()`, do NOT return `Events` collection. Return only `OutgoingMessages`. Returning both will persist events twice.

---

## Entity and Document Loading

Wolverine provides two attributes for loading Marten documents (non-event-sourced):

| Attribute | Use Case | ID Resolution |
|-----------|----------|---------------|
| `[Entity]` | Load relational entity or document | Command property or route param |
| `[Document]` | Load document from document store | Command property or route param |

**In practice, both behave identically** — use `[Entity]` as the standard.

**Example:**

```csharp
public sealed record UpdateProduct(string Sku, string Name, decimal Price);

public static class UpdateProductHandler
{
    public static ProblemDetails Before(UpdateProduct cmd, [Entity] Product? product)
    {
        if (product is null)
            return new ProblemDetails { Detail = "Product not found", Status = 404 };
        return WolverineContinue.NoProblems;
    }

    public static void Handle(
        UpdateProduct cmd,
        [Entity] Product product,
        IDocumentSession session)
    {
        product.Name = cmd.Name;
        product.Price = cmd.Price;
        session.Store(product);
        // SaveChangesAsync() called automatically by Wolverine middleware
    }
}
```

**ID Resolution:**
- Command property named `{EntityName}Id` (e.g., `ProductId` for `Product`)
- Command property with `[Identity]` attribute
- HTTP route parameter (e.g., `/products/{sku}`)

**Common Mistake: Mismatched Property Names**

```csharp
// ❌ WRONG: Property is "Id", but Wolverine looks for "ProductId"
public sealed record UpdateProduct(string Id, string Name);

// ✅ CORRECT: Property name matches entity name
public sealed record UpdateProduct(string ProductId, string Name);

// ✅ ALSO CORRECT: Use [Identity] attribute
public sealed record UpdateProduct
{
    [Identity] public string Id { get; init; }
    public string Name { get; init; }
}
```

---

## Handler Return Patterns

Wolverine interprets return values from `Handle()` methods to determine what to persist and publish:

| Return Type | Wolverine Action |
|-------------|------------------|
| `Events` (single event) | Append to current event stream |
| `IEnumerable<object>` or `Events` | Append all events to stream |
| `OutgoingMessages` | Publish integration messages |
| `(Events, OutgoingMessages)` | Append events + publish messages |
| `IStartStream` | Start a new event stream |
| `(CreationResponse, IStartStream)` | HTTP 201 + start stream |
| `UpdatedAggregate` or `UpdatedAggregate<T>` | Return updated aggregate state |
| `void` | No events, no messages |

### Pattern 1: Events (Single or Multiple)

```csharp
// Single event
public static Events Handle(ConfirmOrder cmd, [WriteAggregate] Order order)
    => [new OrderConfirmed(order.Id, DateTimeOffset.UtcNow)];

// Multiple events
public static IEnumerable<object> Handle(CompleteCheckout cmd, [WriteAggregate] Checkout checkout)
{
    yield return new ShippingAddressProvided(cmd.Address);
    yield return new PaymentMethodProvided(cmd.PaymentToken);
    yield return new CheckoutCompleted(DateTimeOffset.UtcNow);
}
```

### Pattern 2: Events + Integration Messages

```csharp
public static (Events, OutgoingMessages) Handle(
    CapturePayment cmd,
    [WriteAggregate] Payment payment)
{
    var events = new Events();
    events.Add(new PaymentCaptured(payment.Id));

    var outgoing = new OutgoingMessages();
    outgoing.Add(new IntegrationMessages.PaymentCaptured(payment.Id, payment.OrderId));

    return (events, outgoing);
}
```

**Events appended to stream, integration message published to RabbitMQ.**

### Pattern 3: Start New Stream

**⚠️ CRITICAL:** Handlers that create new event streams **MUST return `IStartStream`** from `MartenOps.StartStream()`.

**Message Handler:**
```csharp
public static IStartStream Handle(StartPayment cmd)
{
    var paymentId = Guid.CreateVersion7();
    var initiated = new PaymentInitiated(paymentId, cmd.OrderId, cmd.Amount);
    return MartenOps.StartStream<Payment>(paymentId, initiated);
}
```

```csharp
[WolverinePost("/api/carts")]
public static (CreationResponse<Guid>, IStartStream) Handle(InitializeCart cmd)
{
    var cartId = Guid.CreateVersion7();
    var stream = MartenOps.StartStream<Cart>(cartId, new CartInitialized(cmd.CustomerId));
    return (new CreationResponse<Guid>($"/api/carts/{cartId}", cartId), stream);
}
```

**⚠️ Tuple Order:** HTTP response MUST be first: `(CreationResponse, IStartStream)` not `(IStartStream, CreationResponse)`

**Why:** Direct `session.Events.StartStream()` bypasses transactional middleware — events are silently discarded. See `docs/skills/marten-event-sourcing.md` for details.

```csharp
[WolverinePost("/orders/{orderId}/confirm")]
public static (UpdatedAggregate, Events) Handle(ConfirmOrder cmd, [WriteAggregate] Order order)
    => (new UpdatedAggregate(), [new OrderConfirmed()]);
```

**Use when:** GraphQL mutations, client needs immediate state, or to avoid extra round trip.

```csharp
public static IEnumerable<object> Handle(DescriptionChangeApproved @event)
{
    return
    [
        new ChangeRequestStatusUpdated(...),     // → SignalR
        new SomeRabbitMqMessage(...)             // → RabbitMQ
    ];
}
```

**Wolverine routes by runtime type.** Return `[]` to no-op.

### Pattern 6: Async vs Sync Return Types (M32.0 Lesson)

**Critical Rule:** Handlers querying projections after event appends MUST be `async Task<T>` to call `await session.SaveChangesAsync()`.

**✅ CORRECT:**
```csharp
public static async Task<LiveMetricUpdated> Handle(OrderPlaced message, IDocumentSession session)
{
    session.Events.Append(Guid.NewGuid(), message);
    await session.SaveChangesAsync();  // Inline projections update HERE

    var metrics = await session.LoadAsync<AdminDailyMetrics>(DateOnly.FromDateTime(DateTime.UtcNow));
    return new LiveMetricUpdated(metrics.TodaysOrders, metrics.TodaysRevenue, DateTimeOffset.UtcNow);
}
```

**❌ WRONG:** Querying before `SaveChangesAsync()` returns stale data.

**Decision Rule:** Use `async Task<T>` when calling `await` on anything; use synchronous `T` for pure functions.

### Pattern 7: HTTP Endpoint with Integration Messages ⭐ *M36.1 Addition*

HTTP endpoints that need to publish integration messages alongside an HTTP response use the `(IResult, OutgoingMessages)` tuple return. Wolverine sends the `IResult` as the HTTP response and publishes the `OutgoingMessages` via the transactional outbox.

```csharp
[WolverinePost("/api/marketplaces")]
public static async Task<(IResult, OutgoingMessages)> Handle(
    RegisterMarketplace cmd,
    IDocumentSession session,
    CancellationToken ct)
{
    var outgoing = new OutgoingMessages();

    var existing = await session.LoadAsync<Marketplace>(cmd.ChannelCode, ct);
    if (existing is not null)
        return (Results.Ok(existing), outgoing); // Idempotent — no event published

    var marketplace = Marketplace.Create(cmd.ChannelCode, cmd.DisplayName);
    session.Store(marketplace);

    outgoing.Add(new MarketplaceRegistered(marketplace.ChannelCode, marketplace.DisplayName));
    return (Results.Created($"/api/marketplaces/{marketplace.ChannelCode}", marketplace), outgoing);
}
```

**Idempotent guard:** If the aggregate already exists or is already in the target state, return the HTTP response but leave `OutgoingMessages` empty. This prevents duplicate integration events from idempotent HTTP calls.

**⚠️ CRITICAL:** Do NOT use `IMessageBus.PublishAsync()` for integration events in HTTP endpoints. See Anti-Pattern #11 below.

### Summary Table

| Scenario | Return Type | Stream Creation | Example |
|----------|-------------|-----------------|---------|
| Append events to existing stream | `(Events, OutgoingMessages)` | N/A — `[WriteAggregate]` | CapturePayment |
| Return updated aggregate state | `UpdatedAggregate<T>` | N/A — `[WriteAggregate]` | ConfirmOrder |
| Start new stream (message handler) | `IStartStream` or `OutgoingMessages` | `MartenOps.StartStream<T>()` or `session.Events.StartStream<T>()` | StartPayment |
| Start new stream (HTTP endpoint) | `(CreationResponse, IStartStream)` | `MartenOps.StartStream<T>()` | InitializeCart |
| HTTP response + integration messages | `(IResult, OutgoingMessages)` | N/A | RegisterMarketplace |
| Multi-transport dispatch | `IEnumerable<object>` | N/A | DescriptionChangeApproved |

---

## Railway Programming in Handlers

Wolverine supports a quasi-Railway Programming approach where `Before/Validate/Load` methods can short-circuit the pipeline, keeping the `Handle()` method on the happy path only.

**Three ways to stop processing:**

1. `HandlerContinuation.Stop` — Generic stop signal
2. `ProblemDetails` — HTTP 400 with structured error
3. `IResult` — Custom HTTP response

### HandlerContinuation.Stop

```csharp
public static async Task<(HandlerContinuation, Order?, Customer?)> Load(
    ShipOrder cmd,
    IDocumentSession session)
{
    var order = await session.LoadAsync<Order>(cmd.OrderId);
    if (order is null)
        return (HandlerContinuation.Stop, null, null);

    var customer = await session.LoadAsync<Customer>(cmd.CustomerId);
    return (HandlerContinuation.Continue, order, customer);
}

// Handle() only called if Load() returned Continue
public static IEnumerable<object> Handle(ShipOrder cmd, Order order, Customer customer)
{
    yield return new MailOvernight(order.Id);
}
```

**For message handlers:**
- `HandlerContinuation.Stop` → Message discarded (logged as skipped)
- `HandlerContinuation.Continue` → Proceed to `Handle()`

**For HTTP endpoints:**
- `HandlerContinuation.Stop` → Returns 204 No Content
- `HandlerContinuation.Continue` → Proceed to `Handle()`

### ProblemDetails — HTTP 400 with Structured Error

```csharp
public static ProblemDetails Validate(IncidentCategorise cmd, Incident incident)
{
    if (incident.Status == IncidentStatus.Closed)
        return new ProblemDetails { Detail = "Incident is already closed", Status = 400 };

    // All good, keep going!
    return WolverineContinue.NoProblems;
}

[WolverinePost("/api/incidents/{incidentId}/category")]
public static IncidentCategorised Handle(
    IncidentCategorise cmd,
    [Aggregate("incidentId")] Incident incident)
{
    return new IncidentCategorised(incident.Id, cmd.Category, cmd.CategorisedBy);
}
```

**For HTTP endpoints:**
- `WolverineContinue.NoProblems` → Proceed to `Handle()`
- Any other `ProblemDetails` → Returns 400 (or custom status) with JSON body

**Example response:**

```json
{
  "detail": "Incident is already closed",
  "status": 400,
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1"
}
```

**For message handlers:**
- `WolverineContinue.NoProblems` → Proceed to `Handle()`
- Any other `ProblemDetails` → Message discarded, error logged

### IResult — Custom HTTP Response

```csharp
public static IResult Before([Entity] Todo? todo)
{
    if (todo is null)
        return Results.NotFound();

    return WolverineContinue.Result();  // Special sentinel value
}

[WolverinePost("/api/todos")]
public static void Handle(ExamineTodo cmd)
{
    // Business logic here
}
```

**`WolverineContinue.Result()`** tells Wolverine to keep going. Any other `IResult` stops processing and executes that result.

### Validation vs Business Logic

**Use `Before/Validate` methods for:**
- Precondition checks (aggregate exists, not in terminal state)
- Authorization checks (user has permission)
- Cross-aggregate validation (enough inventory, payment authorized)

**Keep in `Handle()` method:**
- Business rules (when to emit events)
- State transitions (status changes)
- Domain decisions (which workflow to follow)

**Example: Good Separation**

```csharp
public static class ApproveExchangeHandler
{
    // VALIDATION: Preconditions + business rules that stop processing
    public static ProblemDetails Before(ApproveExchange cmd, Return? aggregate)
    {
        if (aggregate is null)
            return new ProblemDetails { Detail = "Return not found", Status = 404 };
        if (aggregate.Type != ReturnType.Exchange)
            return new ProblemDetails { Detail = "Not an exchange request", Status = 409 };
        if (aggregate.Status != ReturnStatus.Requested)
            return new ProblemDetails { Detail = $"Cannot approve from '{aggregate.Status}' state", Status = 409 };

        var originalTotal = aggregate.Items.Sum(i => i.LineTotal);
        var replacementTotal = aggregate.ExchangeRequest.ReplacementQuantity *
                                aggregate.ExchangeRequest.ReplacementUnitPrice;
        if (replacementTotal > originalTotal)
            return new ProblemDetails { Detail = "Replacement costs more than original", Status = 409 };

        return WolverineContinue.NoProblems;
    }

    // BUSINESS LOGIC: Calculate price difference, schedule expiration
    [WolverinePost("/api/returns/{returnId}/approve-exchange")]
    public static async Task<(ExchangeApproved, OutgoingMessages)> Handle(
        ApproveExchange cmd,
        [WriteAggregate] Return aggregate,
        IMessageBus bus)
    {
        var now = DateTimeOffset.UtcNow;
        var shipByDeadline = now.AddDays(ReturnEligibilityWindow.ReturnWindowDays);

        var originalTotal = aggregate.Items.Sum(i => i.LineTotal);
        var replacementTotal = aggregate.ExchangeRequest!.ReplacementQuantity *
                                aggregate.ExchangeRequest.ReplacementUnitPrice;
        var priceDifference = originalTotal - replacementTotal;

        var domainEvent = new ExchangeApproved(cmd.ReturnId, priceDifference, shipByDeadline, now);

        // Schedule expiration
        await bus.ScheduleAsync(new ExpireReturn(cmd.ReturnId), shipByDeadline);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Returns.ExchangeApproved(
            cmd.ReturnId, aggregate.OrderId, aggregate.CustomerId,
            aggregate.ExchangeRequest.ReplacementSku, priceDifference, shipByDeadline, now));

        return (domainEvent, outgoing);
    }
}
```

**Why this separation works:**
- `Before()` reads like a checklist — "Can we proceed?"
- `Handle()` reads like a workflow — "What happens next?"
- Unit tests for `Handle()` assume all preconditions are met (simpler test setup)

### Async Validation with External Services

**⚠️ CRITICAL PATTERN: ValidateAsync() for HTTP Endpoints with External Service Validation**

When you need to validate against external services (e.g., checking coupon validity with Promotions BC), you **MUST use separate handler classes** for command handling vs HTTP endpoint handling:

1. **Command Handler** — For internal use (tests, sagas, etc.) - assumes caller has already validated
2. **HTTP Endpoint Handler** — Uses `ValidateAsync()` method for async validation

**❌ INCORRECT Pattern (Does NOT Work):**

```csharp
// DON'T DO THIS - Handle() cannot return ProblemDetails for validation
public static class ApplyCouponToCartHandler
{
    [WolverinePost("/api/carts/{cartId}/apply-coupon")]
    public static async Task<(Events, OutgoingMessages, ProblemDetails)> Handle(
        ApplyCouponToCart command,
        IPromotionsClient promotionsClient,
        [WriteAggregate] Cart cart,
        CancellationToken ct)
    {
        // ❌ This pattern doesn't work - ProblemDetails in Handle is anti-pattern
        var validation = await promotionsClient.ValidateCouponAsync(command.CouponCode, ct);
        if (!validation.IsValid)
            return ([], new OutgoingMessages(), new ProblemDetails
                { Detail = validation.Reason, Status = 400 });

        // ... rest of logic
    }
}
```

**✅ CORRECT Pattern (Separate Handler Classes):**

```csharp
// Command handler for internal use (tests, sagas, etc.)
public static class ApplyCouponToCartHandler
{
    public static ProblemDetails Before(
        ApplyCouponToCart command,
        Cart? cart)
    {
        if (cart is null)
            return new ProblemDetails { Detail = "Cart not found", Status = 404 };
        if (cart.IsTerminal)
            return new ProblemDetails { Detail = "Cannot modify completed cart", Status = 400 };
        if (cart.Items.Count == 0)
            return new ProblemDetails { Detail = "Cannot apply coupon to empty cart", Status = 400 };

        return WolverineContinue.NoProblems;
    }

    // Command handler - assumes coupon is already validated by caller
    public static async Task<(Events, OutgoingMessages)> Handle(
        ApplyCouponToCart command,
        IPromotionsClient promotionsClient,
        [WriteAggregate] Cart cart,
        CancellationToken ct)
    {
        // Calculate discount (validation already done by caller)
        var cartItems = cart.Items.Values
            .Select(item => new CartItemDto(item.Sku, item.Quantity, item.UnitPrice))
            .ToList();

        var discount = await promotionsClient.CalculateDiscountAsync(
            cartItems,
            [command.CouponCode],
            ct);

        var @event = new CouponApplied(
            cart.Id,
            command.CouponCode,
            discount.TotalDiscount,
            DateTimeOffset.UtcNow);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Shopping.CouponApplied(
            cart.Id,
            cart.CustomerId ?? Guid.Empty,
            command.CouponCode,
            discount.TotalDiscount,
            DateTimeOffset.UtcNow));

        return ([@event], outgoing);
    }
}

// HTTP endpoint in SEPARATE class to enable ValidateAsync
public static class ApplyCouponToCartHttpEndpoint
{
    public static ProblemDetails Before(
        Guid cartId,
        Cart? cart)
    {
        if (cart is null)
            return new ProblemDetails { Detail = "Cart not found", Status = 404 };
        if (cart.IsTerminal)
            return new ProblemDetails { Detail = "Cannot modify completed cart", Status = 400 };
        if (cart.Items.Count == 0)
            return new ProblemDetails { Detail = "Cannot apply coupon to empty cart", Status = 400 };

        return WolverineContinue.NoProblems;
    }

    // ✅ ValidateAsync for async external service validation
    public static async Task<ProblemDetails> ValidateAsync(
        ApplyCouponToCart command,
        IPromotionsClient promotionsClient,
        CancellationToken ct)
    {
        var validation = await promotionsClient.ValidateCouponAsync(command.CouponCode, ct);

        if (!validation.IsValid)
        {
            return new ProblemDetails
            {
                Detail = validation.Reason ?? $"Coupon code '{command.CouponCode}' is invalid",
                Status = 400
            };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/carts/{cartId}/apply-coupon")]
    public static async Task<(Events, OutgoingMessages)> Handle(
        ApplyCouponToCart command,
        IPromotionsClient promotionsClient,
        [WriteAggregate] Cart cart,
        CancellationToken ct)
    {
        // Validation complete - just calculate discount and apply
        var cartItems = cart.Items.Values
            .Select(item => new CartItemDto(item.Sku, item.Quantity, item.UnitPrice))
            .ToList();

        var discount = await promotionsClient.CalculateDiscountAsync(
            cartItems,
            [command.CouponCode],
            ct);

        var @event = new CouponApplied(
            cart.Id,
            command.CouponCode,
            discount.TotalDiscount,
            DateTimeOffset.UtcNow);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Shopping.CouponApplied(
            cart.Id,
            cart.CustomerId ?? Guid.Empty,
            command.CouponCode,
            discount.TotalDiscount,
            DateTimeOffset.UtcNow));

        return ([@event], outgoing);
    }
}
```

**Key Learnings (M30.1 Shopping BC Coupon Integration):**

1. **Wolverine's Railway Programming pattern requires ProblemDetails in Before/Validate/ValidateAsync, NOT in Handle**
   - `Handle()` is the "happy path" - it should assume all validation passed
   - Returning ProblemDetails from `Handle()` breaks Wolverine's pipeline expectations

2. **When you need async validation with external services:**
   - Split into TWO handler classes (not one)
   - Command handler: accepts command record, assumes validation done by caller
   - HTTP endpoint handler: accepts route parameters, uses `ValidateAsync()` for async checks

3. **Why Context7 documentation was essential:**
   - The incorrect pattern (ProblemDetails in Handle) looked plausible but doesn't work
   - Official Wolverine docs clarified that Railway Programming stops at Before/Validate level
   - `ValidateAsync()` is the correct place for async external service validation

**Real-World Example:** `src/Shopping/Shopping/Cart/ApplyCouponToCart.cs`

**Reference:** [M30.1 Shopping BC Coupon Integration Retrospective](../../planning/cycles/m30-1-shopping-bc-coupon-retrospective.md)

---

## Mixed Parameter Sources (Route + JSON Body) ⚠️ **M32.3 Discovery**

**Critical Limitation:** Wolverine's compound handler pattern (Before/Validate/Load/Handle lifecycle) **does not work** when mixing route parameters with JSON body parameters in the same endpoint.

**Discovery:** This limitation was discovered in M32.3 Session 10 while implementing BackofficeIdentity password reset endpoint. All 6 integration tests initially failed with 500 Internal Server Error because Wolverine couldn't construct the command from mixed parameter sources.

### The Problem

**❌ FAILS — Compound Handler with Mixed Parameters:**

```csharp
// Route parameter: userId (from /api/users/{userId})
// JSON body: newPassword (from request body)
public sealed record ResetPassword(Guid UserId, string NewPassword);

public static class ResetPasswordHandler
{
    // ❌ Before() cannot construct ResetPassword command
    // Wolverine can't resolve UserId (route) + NewPassword (body)
    public static async Task<ResetPasswordResponse?> Before(
        ResetPassword cmd,  // Wolverine CANNOT construct this!
        BackofficeIdentityDbContext db,
        CancellationToken ct)
    {
        var user = await db.Users.FindAsync([cmd.UserId], ct);
        if (user is null) return null;

        // Hash password, invalidate refresh token...
        return new ResetPasswordResponse(user.Id, DateTimeOffset.UtcNow);
    }

    [WolverinePost("/api/users/{userId}/reset-password")]
    public static IResult Handle(
        Guid userId,        // Route parameter
        string newPassword, // Body parameter — HOW DOES WOLVERINE GET THIS?
        ResetPasswordResponse? response,
        ProblemDetails? problem)
    {
        if (problem is not null) return Results.Problem(problem);
        return Results.Ok(response);
    }
}
```

**Why this fails:**
1. `Before()` expects `ResetPassword cmd` as first parameter (compound handler convention)
2. Wolverine tries to construct `ResetPassword` from incoming HTTP request
3. `UserId` comes from route parameter `/api/users/{userId}`
4. `NewPassword` comes from JSON body `{ "newPassword": "..." }`
5. Wolverine's command construction can't handle **mixed sources** — it expects all parameters from same source
6. Result: 500 Internal Server Error, handler never executes

### The Solution: Direct Implementation Pattern

**✅ WORKS — Direct Implementation (No Compound Handler):**

```csharp
// Request DTO for JSON body only
public sealed record ResetPasswordRequest(string NewPassword);

// FluentValidation still works!
public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .WithMessage("Password must be at least 8 characters.");
    }
}

public static class ResetPasswordEndpoint
{
    [WolverinePost("/api/users/{userId}/reset-password")]
    public static async Task<IResult> Handle(
        Guid userId,                      // Route parameter (injected separately)
        ResetPasswordRequest request,     // JSON body (auto-deserialized)
        BackofficeIdentityDbContext db,
        CancellationToken ct)
    {
        // Direct implementation — no Before/Load/Validate lifecycle
        var user = await db.Users.FindAsync([userId], ct);

        if (user is null)
        {
            return Results.Problem(
                detail: $"User with ID '{userId}' not found.",
                statusCode: 404);
        }

        // Hash password using ASP.NET Core Identity PasswordHasher
        var passwordHasher = new PasswordHasher<BackofficeUser>();
        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);

        // Invalidate refresh token (force re-authentication)
        user.RefreshToken = null;
        user.RefreshTokenExpiresAt = null;

        await db.SaveChangesAsync(ct);

        var response = new ResetPasswordResponse(user.Id, DateTimeOffset.UtcNow);
        return Results.Ok(response);
    }
}
```

**Why this works:**
- No compound handler lifecycle (`Before/Validate/Load/Handle`) — direct implementation only
- Wolverine auto-deserializes JSON body to `ResetPasswordRequest` type
- Route parameter `userId` injected separately (standard ASP.NET Core routing)
- FluentValidation still applies via `ResetPasswordRequestValidator`
- All logic in `Handle()` method — straightforward to reason about

### When to Use Each Pattern

| Scenario | Pattern | Why |
|----------|---------|-----|
| **All parameters from same source** | Compound handler (Before/Load/Handle) | ✅ Wolverine can construct command from single source |
| **Mixing route + JSON body** | Direct implementation (no compound handler) | ✅ Wolverine auto-deserializes body, routes inject separately |
| **Complex validation + loading** | Compound handler | ✅ Separation of concerns (Before validates, Load fetches, Handle decides) |
| **Simple CRUD endpoint** | Direct implementation | ✅ Less ceremony, fewer moving parts |

### Real Examples in CritterSupply

**✅ Compound Handler Works (All Parameters from JSON Body):**
```csharp
// Example: Shopping BC - AddItemToCart
// Command has all fields from JSON body
public sealed record AddItemToCart(Guid CartId, string Sku, int Quantity);

public static class AddItemToCartHandler
{
    // ✅ Wolverine constructs AddItemToCart from JSON body
    public static ProblemDetails Before(AddItemToCart cmd, Cart? cart) { /* ... */ }

    [WolverinePost("/api/carts/{cartId}/items")]
    public static async Task<(Events, OutgoingMessages)> Handle(/* ... */) { /* ... */ }
}
```

**✅ Direct Implementation Works (Route + JSON Body):**
```csharp
// Example: Pricing BC - SetBasePriceEndpoint.cs (lines 31-121)
[WolverinePost("/api/pricing/products/{sku}/base-price")]
public static async Task<IResult> SetBasePrice(
    string sku,                  // Route parameter
    SetBasePriceRequest request, // JSON body
    IDocumentSession session,
    CancellationToken ct)
{
    // Direct implementation, no compound handler
    var streamId = ProductPrice.StreamId(sku);
    var aggregate = await session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: ct);

    // Business logic here...
    return Results.Ok(new { /* ... */ });
}
```

**✅ Direct Implementation Works (Route + JSON Body):**
```csharp
// Example: BackofficeIdentity BC - ResetBackofficeUserPasswordEndpoint.cs (M32.3 Session 10)
[WolverinePost("/api/backoffice-identity/users/{userId}/reset-password")]
public static async Task<IResult> Handle(
    Guid userId,                      // Route parameter
    ResetPasswordRequest request,     // JSON body
    BackofficeIdentityDbContext db,
    CancellationToken ct)
{
    // Direct implementation (password reset security-critical logic verified via tests)
}
```

### Key Lessons (M32.3 Session 10)

1. **Wolverine compound handler pattern has undocumented limitation** — cannot mix route + body parameters
2. **Direct implementation is a valid pattern** — not an anti-pattern when needed
3. **FluentValidation works independently of handler pattern** — validators apply regardless
4. **Integration tests caught this early** — all 6 tests failing with 500 errors led to root cause
5. **Pricing BC endpoints were the reference** — `SetBasePriceEndpoint.cs` showed proven direct implementation pattern

**Reference Files:**
- `src/Backoffice Identity/BackofficeIdentity.Api/UserManagement/ResetBackofficeUserPasswordEndpoint.cs`
- `src/Pricing/Pricing.Api/Pricing/SetBasePriceEndpoint.cs`
- `docs/planning/milestones/m32-3-session-10-retrospective.md` (lines 34-91)

**Reference:** [M32.3 Session 10 Retrospective — Integration Test Fix](../../planning/milestones/m32-3-session-10-retrospective.md)

---

## HTTP Endpoints

Wolverine.HTTP provides attributes for HTTP verbs:

```csharp
[WolverineGet("/api/resource/{id}")]
[WolverinePost("/api/resource")]
[WolverinePut("/api/resource/{id}")]
[WolverineDelete("/api/resource/{id}")]
[WolverinePatch("/api/resource/{id}")]
```

**Route Parameters:**

Route parameters are bound by name:

```csharp
[WolverineGet("/orders/{orderId}/items/{itemId}")]
public static OrderItem? GetItem(
    Guid orderId,      // Bound from route
    Guid itemId,       // Bound from route
    IDocumentSession session)
{
    // ...
}
```

**Query Parameters:**

Use `[FromQuery]` or `[AsParameters]`:

```csharp
[WolverineGet("/products")]
public static Task<List<Product>> Search(
    [FromQuery] string? category,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20)
{
    // ...
}

// OR group into a record with [AsParameters]
public sealed record SearchParams(string? Category, int Page = 1, int PageSize = 20);

[WolverineGet("/products")]
public static Task<List<Product>> Search([AsParameters] SearchParams query)
{
    // ...
}
```

**Request Body:**

Wolverine automatically deserializes JSON request bodies:

```csharp
[WolverinePost("/orders")]
public static (CreationResponse, OutgoingMessages) PlaceOrder(
    PlaceOrder cmd,  // Deserialized from request body
    IDocumentSession session)
{
    // ...
}
```

**Empty Responses:**

Use `[EmptyResponse]` for 204 No Content:

```csharp
[EmptyResponse]
[WolverinePost("/orders/{orderId}/confirm")]
public static Events Handle(ConfirmOrder cmd, [WriteAggregate] Order order)
{
    return [new OrderConfirmed(order.Id)];
}
```

**Without `[EmptyResponse]`, Wolverine returns 200 OK with the events array as JSON.**

**CreationResponse:**

Use `CreationResponse` or `CreationResponse<T>` for 201 Created:

```csharp
[WolverinePost("/carts")]
public static (CreationResponse<Guid>, IStartStream) InitializeCart(InitializeCart cmd)
{
    var cartId = Guid.CreateVersion7();
    var stream = MartenOps.StartStream<Cart>(cartId, new CartInitialized(cmd.CustomerId));

    var response = new CreationResponse<Guid>($"/api/carts/{cartId}", cartId);
    return (response, stream);
}
```

**Response:**

```http
HTTP/1.1 201 Created
Location: /api/carts/019c49bf-9852-73c1-bb67-da545727eca4
Content-Type: application/json

{
  "value": "019c49bf-9852-73c1-bb67-da545727eca4",
  "url": "/api/carts/019c49bf-9852-73c1-bb67-da545727eca4"
}
```

**CritterSupply URL Conventions:**

- **Flat, resource-centric** — `/api/orders`, not `/api/order-management/orders`
- **Plural nouns** — `/api/products`, not `/api/product`
- **BC ownership is internal** — URL structure doesn't expose bounded contexts
- **Avoid deep nesting** — Prefer `/api/order-items?orderId={id}` over `/api/orders/{id}/items`

**Examples:**

```csharp
// ✅ GOOD: Flat, resource-centric
[WolverineGet("/api/orders/{orderId}")]
[WolverinePost("/api/carts")]
[WolverineDelete("/api/carts/{cartId}/items/{sku}")]

// ❌ AVOID: Nested resources (harder to maintain)
[WolverineGet("/api/orders/{orderId}/items")]

// ✅ PREFER: Query parameter for filtering
[WolverineGet("/api/order-items")]
public static Task<List<OrderItem>> GetOrderItems([FromQuery] Guid orderId) { /* ... */ }
```

### HTTP Endpoint Validation (M32.1)

**Key Insight:** HTTP endpoints that bypass domain command handlers need their own FluentValidation validators.

**✅ CORRECT:**
```csharp
// HTTP DTO + validator
public sealed record SetBasePriceRequest(decimal BasePrice);

public sealed class SetBasePriceValidator : AbstractValidator<SetBasePriceRequest>
{
    public SetBasePriceValidator()
    {
        RuleFor(x => x.BasePrice).GreaterThan(0);
    }
}

// Endpoint (direct event construction)
[WolverinePost("/api/pricing/products/{sku}/base-price")]
public static BasePriceSet Handle(string sku, SetBasePriceRequest request)
    => new BasePriceSet(sku, request.BasePrice, DateTimeOffset.UtcNow);
```

**When to Use:**

| Scenario | Pattern |
|----------|---------|
| Endpoint bypasses commands | HTTP validator only (example above) |
| Endpoint invokes command | Domain command validator |
| HTTP-specific validation needed | Both validators |

**❌ ANTI-PATTERN:** Missing validator when bypassing commands — invalid data reaches event stream.

**Rule:** Validator type must match handler parameter type. If handler takes `SetBasePriceRequest`, validator must be `AbstractValidator<SetBasePriceRequest>`.

---

## Handler Discovery

Wolverine discovers handlers by convention:

**Handler class requirements:**
- Public class
- Public constructor
- Suffix with `Handler` or `Consumer`

**Handler method requirements:**
- Public method
- Named `Handle`, `HandleAsync`, `Consume`, or `ConsumeAsync`
- First parameter is the message type
- Can be static or instance methods

**Where to place handlers:**

Wolverine scans all assemblies registered via `opts.Discovery.IncludeAssembly()`:

```csharp
builder.Host.UseWolverine(opts =>
{
    // API assembly (HTTP endpoints, queries)
    opts.Discovery.IncludeAssembly(typeof(Program).Assembly);

    // Domain assembly (message handlers, command handlers)
    opts.Discovery.IncludeAssembly(typeof(SomeDomainType).Assembly);
});
```

**CritterSupply structure:**

```
src/Orders/
├── Orders/                      # Domain assembly
│   └── Order/
│       ├── PlaceOrder.cs        # Command + Handler
│       ├── ConfirmOrder.cs      # Command + Handler
│       └── OrderSaga.cs         # Saga
└── Orders.Api/                  # API assembly
    ├── Program.cs               # Wolverine configuration
    └── Queries/
        └── GetOrderDetails.cs   # HTTP endpoint
```

**Discovery configuration in `Program.cs`:**

```csharp
builder.Host.UseWolverine(opts =>
{
    // Discover handlers in API project (queries, HTTP endpoints)
    opts.Discovery.IncludeAssembly(typeof(Program).Assembly);

    // Discover handlers in domain project (commands, sagas, integration handlers)
    opts.Discovery.IncludeAssembly(typeof(Orders.Order).Assembly);

    // ⚠️ CRITICAL: AutoApplyTransactions() is REQUIRED for Marten integration
    opts.Policies.AutoApplyTransactions();
});
```

**⚠️ CRITICAL: AutoApplyTransactions() Policy**

The `AutoApplyTransactions()` policy is **REQUIRED** for Marten integration, not optional:

- Without this policy, Wolverine does NOT wrap handlers in transactional middleware
- Marten changes (events, documents) are NOT automatically committed
- Handlers complete successfully but no data is persisted to the database
- This causes **silent failures** — no exceptions thrown, but database remains unchanged

**See also:** `docs/skills/marten-event-sourcing.md` (lines 1417-1476) for complete explanation of this critical requirement.

**Testing handler discovery:**

Use Alba's code generation to verify handlers are wired:

```bash
dotnet run -- codegen preview
```

This shows generated handler code. Look for:
- Handler class names
- Method signatures
- Middleware applied (validation, transactions)

---

## Error Handling

Wolverine provides built-in retry and error handling policies:

**Default behavior:**
- Exceptions are logged and message is moved to error queue
- Dead letter queue behavior can be configured per endpoint

**Retry policies:**

```csharp
builder.Host.UseWolverine(opts =>
{
    // Retry all messages 3 times with exponential backoff
    opts.Policies.OnException<HttpRequestException>()
        .RetryWithCooldown(50.Milliseconds(), 100.Milliseconds(), 250.Milliseconds());

    // Retry specific message type
    opts.Policies.OnException<TimeoutException>()
        .AndInnerIs<HttpRequestException>()
        .RetryWithCooldown(1.Seconds(), 2.Seconds(), 5.Seconds());
});
```

**Message-specific error handling:**

```csharp
public static class ProcessPaymentHandler
{
    [MaximumAttempts(5)]
    [RetryOn(typeof(PaymentGatewayException))]
    public static async Task<Events> Handle(ProcessPayment cmd, IPaymentGateway gateway)
    {
        // Wolverine retries up to 5 times on PaymentGatewayException
        var result = await gateway.CaptureAsync(cmd.PaymentId);
        return [new PaymentCaptured(cmd.PaymentId, result.TransactionId)];
    }
}
```

**What NOT to swallow:**

Never catch and swallow exceptions inside handlers:

```csharp
// ❌ WRONG: Swallows error, Wolverine can't retry
public static Events Handle(ProcessPayment cmd)
{
    try
    {
        // ...
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);  // Swallowed!
        return [];
    }
}

// ✅ CORRECT: Let exception propagate to Wolverine
public static Events Handle(ProcessPayment cmd)
{
    // No try/catch — Wolverine handles retries
    var result = gateway.CaptureAsync(cmd.PaymentId);
    return [new PaymentCaptured(cmd.PaymentId, result.TransactionId)];
}
```

**Wolverine's error handling gives you:**
- Automatic retries with exponential backoff
- Dead letter queue for poison messages
- Observability (OpenTelemetry tracing)
- Inbox/outbox durability (messages never lost)

---

## Multi-Tenancy

Wolverine supports multi-tenancy via Marten's built-in multi-tenancy features.

**Tenant ID resolution:**

Wolverine resolves tenant ID from:
1. HTTP header (e.g., `X-Tenant-Id`)
2. Route parameter (e.g., `/api/{tenantId}/orders`)
3. `IMessageContext.TenantId` property

**Header-based multi-tenancy:**

```csharp
builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);
    opts.Policies.AllDocumentsAreMultiTenanted();  // All documents require tenant ID
})
.IntegrateWithWolverine();

builder.Host.UseWolverine(opts =>
{
    // Wolverine reads tenant ID from X-Tenant-Id header
    opts.Policies.UseTenantIdFromHeader("X-Tenant-Id");
});
```

**Handler access to tenant ID:**

```csharp
public static Events Handle(
    PlaceOrder cmd,
    IMessageContext context)  // Access tenant ID here
{
    var tenantId = context.TenantId;
    // ...
}
```

**Multi-tenant aggregate workflow:**

Wolverine automatically scopes Marten queries to the current tenant:

```csharp
[WolverinePost("/api/orders")]
public static (CreationResponse, Events) PlaceOrder(
    PlaceOrder cmd,
    IMessageContext context)  // Wolverine sets TenantId
{
    // Marten automatically scopes to context.TenantId
    var orderId = Guid.CreateVersion7();
    var @event = new OrderPlaced(orderId, cmd.CustomerId, cmd.Items);

    return (new CreationResponse($"/api/orders/{orderId}"), [@event]);
}
```

**Do NOT hardcode tenant resolution:**

```csharp
// ❌ WRONG: Hardcoded tenant extraction
public static Events Handle(PlaceOrder cmd, HttpContext httpContext)
{
    var tenantId = httpContext.Request.Headers["X-Tenant-Id"];  // Don't do this
    // ...
}

// ✅ CORRECT: Use Wolverine's convention
public static Events Handle(PlaceOrder cmd, IMessageContext context)
{
    var tenantId = context.TenantId;  // Wolverine resolved this
    // ...
}
```

---

## Anti-Patterns to Avoid

### 1. ❌ Putting Business Logic in `Before/Load` Methods

**Wrong:**

```csharp
public static ProblemDetails Before(ApproveReturn cmd, Return? aggregate)
{
    if (aggregate is null)
        return new ProblemDetails { Detail = "Not found", Status = 404 };

    // ❌ Business logic in validation filter
    if (aggregate.Status != ReturnStatus.Requested)
    {
        aggregate.Status = ReturnStatus.Approved;  // Mutating state!
        session.Store(aggregate);  // Side effect!
    }

    return WolverineContinue.NoProblems;
}
```

**Why wrong:** `Before()` methods are for precondition checks, not business logic. Mutating state here bypasses Wolverine's event sourcing workflow.

**Correct:**

```csharp
public static ProblemDetails Before(ApproveReturn cmd, Return? aggregate)
{
    if (aggregate is null)
        return new ProblemDetails { Detail = "Not found", Status = 404 };
    if (aggregate.Status != ReturnStatus.Requested)
        return new ProblemDetails { Detail = "Cannot approve from current state", Status = 409 };
    return WolverineContinue.NoProblems;
}

public static Events Handle(ApproveReturn cmd, [WriteAggregate] Return aggregate)
{
    // ✅ Business logic in Handle()
    return [new ReturnApproved(aggregate.Id, DateTimeOffset.UtcNow)];
}
```

### 2. ❌ Loading Aggregates Manually Inside `Handle()`

**Wrong:**

```csharp
public static async Task<Events> Handle(
    ConfirmOrder cmd,
    IDocumentSession session)
{
    // ❌ Manual loading — Wolverine can't apply optimistic concurrency
    var order = await session.Events.AggregateStreamAsync<Order>(cmd.OrderId);

    if (order.Status != OrderStatus.Pending)
        throw new InvalidOperationException("Cannot confirm");

    session.Events.Append(cmd.OrderId, new OrderConfirmed(cmd.OrderId));
    return [];
}
```

**Why wrong:** Wolverine's aggregate workflow handles loading, concurrency checks, and persistence. Manual loading bypasses all of this.

**Correct:**

```csharp
public static ProblemDetails Before(ConfirmOrder cmd, Order? order)
{
    if (order is null)
        return new ProblemDetails { Detail = "Not found", Status = 404 };
    if (order.Status != OrderStatus.Pending)
        return new ProblemDetails { Detail = "Cannot confirm", Status = 400 };
    return WolverineContinue.NoProblems;
}

public static Events Handle(
    ConfirmOrder cmd,
    [WriteAggregate] Order order)  // ✅ Wolverine loads automatically
{
    return [new OrderConfirmed(order.Id)];
}
```

### 3. ❌ Wrong Tuple Order in Return Values

**Wrong:**

```csharp
[WolverinePost("/carts")]
public static (IStartStream, CreationResponse) InitializeCart(InitializeCart cmd)
{
    var cartId = Guid.CreateVersion7();
    var stream = MartenOps.StartStream<Cart>(cartId, new CartInitialized(cmd.CustomerId));
    var response = new CreationResponse($"/api/carts/{cartId}");

    // ❌ WRONG: IStartStream is first, so it gets serialized as response body!
    return (stream, response);
}
```

**Result:** Returns 200 OK with `IStartStream` serialized as JSON (not what you want).

**Correct:**

```csharp
[WolverinePost("/carts")]
public static (CreationResponse, IStartStream) InitializeCart(InitializeCart cmd)
{
    var cartId = Guid.CreateVersion7();
    var stream = MartenOps.StartStream<Cart>(cartId, new CartInitialized(cmd.CustomerId));
    var response = new CreationResponse($"/api/carts/{cartId}");

    // ✅ CORRECT: CreationResponse is first
    return (response, stream);
}
```

### 4. ❌ Using `[Aggregate]` When Optimistic Concurrency Is Needed

**Wrong:**

```csharp
[Aggregate]  // ❌ Class-level attribute doesn't support concurrency control
public static class CapturePaymentHandler
{
    public static Events Handle(CapturePayment cmd, Payment payment)
    {
        return [new PaymentCaptured(payment.Id)];
    }
}
```

**Why wrong:** `[Aggregate]` is a legacy class-level attribute that doesn't opt into optimistic concurrency checks.

**Correct:**

```csharp
public static class CapturePaymentHandler
{
    public static Events Handle(
        CapturePayment cmd,
        [WriteAggregate] Payment payment)  // ✅ Parameter-level with concurrency
    {
        return [new PaymentCaptured(payment.Id)];
    }
}
```

### 5. ❌ Building Mediator-Style Chained Result Flows

**Wrong:**

```csharp
public static async Task<Result<OrderPlaced>> Handle(
    PlaceOrder cmd,
    IMessageBus bus)
{
    // ❌ Chaining handler calls via mediator pattern
    var cartResult = await bus.InvokeAsync<Result<Cart>>(new GetCart(cmd.CartId));
    if (!cartResult.IsSuccess)
        return Result.Failure<OrderPlaced>(cartResult.Error);

    var inventoryResult = await bus.InvokeAsync<Result<bool>>(new CheckStock(cmd.Items));
    if (!inventoryResult.IsSuccess)
        return Result.Failure<OrderPlaced>(inventoryResult.Error);

    var paymentResult = await bus.InvokeAsync<Result<PaymentId>>(new AuthorizePayment(cmd.Amount));
    if (!paymentResult.IsSuccess)
        return Result.Failure<OrderPlaced>(paymentResult.Error);

    return Result.Success(new OrderPlaced(Guid.CreateVersion7()));
}
```

**Why wrong:**
- Lots of noise code (Result wrapping/unwrapping)
- Hard to reason about database interactions
- Performance problems (chatty database calls)
- Obscures the relationship between inputs and database work

**Correct:**

Use sagas for orchestration or compound handlers for data loading:

```csharp
public static class PlaceOrderHandler
{
    // Load data upfront
    public static async Task<(Cart?, Customer?, bool)> Load(
        PlaceOrder cmd,
        IDocumentSession session,
        IInventoryClient inventory)
    {
        var cart = await session.LoadAsync<Cart>(cmd.CartId);
        var customer = await session.LoadAsync<Customer>(cmd.CustomerId);
        var stockAvailable = await inventory.CheckStockAsync(cmd.Items);
        return (cart, customer, stockAvailable);
    }

    // Validate preconditions
    public static ProblemDetails Validate(
        PlaceOrder cmd,
        Cart? cart,
        Customer? customer,
        bool stockAvailable)
    {
        if (cart is null)
            return new ProblemDetails { Detail = "Cart not found", Status = 404 };
        if (customer is null)
            return new ProblemDetails { Detail = "Customer not found", Status = 404 };
        if (!stockAvailable)
            return new ProblemDetails { Detail = "Insufficient stock", Status = 409 };
        return WolverineContinue.NoProblems;
    }

    // Pure business logic
    public static (Events, OutgoingMessages) Handle(
        PlaceOrder cmd,
        Cart cart,
        Customer customer)
    {
        var orderId = Guid.CreateVersion7();
        var @event = new OrderPlaced(orderId, customer.Id, cart.Items);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.OrderPlaced(orderId, cart.Items));

        return ([@event], outgoing);
    }
}
```

### 6. ❌ Injecting `IDocumentSession` for Write Operations in Aggregate Handlers

**Wrong:**

```csharp
public static Events Handle(
    ConfirmOrder cmd,
    [WriteAggregate] Order order,
    IDocumentSession session)  // ❌ Don't inject session for writes
{
    session.Events.Append(order.Id, new OrderConfirmed(order.Id));  // ❌ Manual append
    return [];
}
```

**Why wrong:** Wolverine's aggregate workflow automatically appends events returned from `Handle()`. Manual appending bypasses this and can lead to double persistence.

**Correct:**

```csharp
public static Events Handle(
    ConfirmOrder cmd,
    [WriteAggregate] Order order)  // ✅ No session injection needed
{
    return [new OrderConfirmed(order.Id)];  // ✅ Wolverine appends automatically
}
```

**When to inject `IDocumentSession`:**
- Loading non-aggregate documents
- Querying projections
- Manual stream operations (when not using `[WriteAggregate]`)

### 7. ❌ Fat Handlers — Infrastructure Work in `Handle()`

**Wrong:**

```csharp
public static async Task<Events> Handle(
    PlaceOrder cmd,
    IDocumentSession session,
    IInventoryClient inventory,
    IPaymentGateway gateway)
{
    // ❌ Mixing infrastructure and business logic
    var cart = await session.LoadAsync<Cart>(cmd.CartId);
    if (cart is null)
        throw new InvalidOperationException("Cart not found");

    var stockAvailable = await inventory.CheckStockAsync(cart.Items);
    if (!stockAvailable)
        throw new InvalidOperationException("Insufficient stock");

    var paymentResult = await gateway.AuthorizeAsync(cmd.PaymentToken, cmd.Amount);
    if (!paymentResult.Success)
        throw new InvalidOperationException("Payment failed");

    // Business logic buried at the bottom
    return [new OrderPlaced(Guid.CreateVersion7(), cmd.CustomerId, cart.Items)];
}
```

**Why wrong:** Infrastructure concerns (loading, validation, external calls) pollute the business logic. Hard to test, hard to reason about.

**Correct:**

```csharp
public static class PlaceOrderHandler
{
    // Infrastructure: Load data
    public static async Task<(Cart?, bool, PaymentResult)> Load(
        PlaceOrder cmd,
        IDocumentSession session,
        IInventoryClient inventory,
        IPaymentGateway gateway)
    {
        var cart = await session.LoadAsync<Cart>(cmd.CartId);
        var stockAvailable = cart != null && await inventory.CheckStockAsync(cart.Items);
        var paymentResult = await gateway.AuthorizeAsync(cmd.PaymentToken, cmd.Amount);
        return (cart, stockAvailable, paymentResult);
    }

    // Infrastructure: Validate
    public static ProblemDetails Validate(
        PlaceOrder cmd,
        Cart? cart,
        bool stockAvailable,
        PaymentResult paymentResult)
    {
        if (cart is null)
            return new ProblemDetails { Detail = "Cart not found", Status = 404 };
        if (!stockAvailable)
            return new ProblemDetails { Detail = "Insufficient stock", Status = 409 };
        if (!paymentResult.Success)
            return new ProblemDetails { Detail = "Payment failed", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    // Pure: Business logic only
    public static (Events, OutgoingMessages) Handle(
        PlaceOrder cmd,
        Cart cart,
        PaymentResult paymentResult)
    {
        var orderId = Guid.CreateVersion7();
        var @event = new OrderPlaced(orderId, cmd.CustomerId, cart.Items, paymentResult.AuthorizationId);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.OrderPlaced(orderId, cart.Items));

        return ([@event], outgoing);
    }
}
```

### 8. ❌ Returning Tuples When Manually Loading Aggregates ⚠️ **CRITICAL**

**Problem:** When you manually load aggregates using `session.Events.AggregateStreamAsync<T>()` instead of using `[WriteAggregate]`, returning event tuples doesn't tell Wolverine to persist the events.

**Wrong:**

```csharp
public static async Task<(Coupon, CouponRedeemed)> Handle(
    RedeemCoupon cmd,
    IDocumentSession session,
    CancellationToken ct)
{
    // ❌ Manually loading aggregate
    var streamId = Coupon.StreamId(cmd.CouponCode);
    var coupon = await session.Events.AggregateStreamAsync<Coupon>(streamId, token: ct);

    if (coupon is null)
        throw new InvalidOperationException("Coupon not found");
    if (coupon.Status != CouponStatus.Issued)
        throw new InvalidOperationException("Coupon already redeemed");

    var evt = new CouponRedeemed(cmd.CouponCode, cmd.OrderId, cmd.CustomerId, DateTimeOffset.UtcNow);

    // ❌ Returning tuple - event will NOT be persisted!
    return (coupon, evt);
}
```

**Why wrong:** The tuple return pattern `(Aggregate, Event)` only works with `[WriteAggregate]`. When you manually load aggregates, Wolverine doesn't know to persist the returned event. The event gets discarded silently.

**Correct:**

```csharp
public static async Task Handle(
    RedeemCoupon cmd,
    IDocumentSession session,
    CancellationToken ct)
{
    // 1. Manually load aggregate
    var streamId = Coupon.StreamId(cmd.CouponCode);
    var coupon = await session.Events.AggregateStreamAsync<Coupon>(streamId, token: ct);

    // 2. Validate
    if (coupon is null)
        throw new InvalidOperationException("Coupon not found");
    if (coupon.Status != CouponStatus.Issued)
        throw new InvalidOperationException("Coupon already redeemed");

    // 3. Create event
    var evt = new CouponRedeemed(cmd.CouponCode, cmd.OrderId, cmd.CustomerId, DateTimeOffset.UtcNow);

    // 4. ✅ Manually append to stream (Wolverine persists via transactional outbox)
    session.Events.Append(streamId, evt);
}
```

**When to manually load aggregates:**
- Loading by deterministic ID (UUID v5 from code/string)
- Need full control over stream ID generation
- Cannot use `[WriteAggregate]` for some reason

**Pattern comparison:**

| Pattern | When to Use | Event Persistence |
|---------|-------------|-------------------|
| `[WriteAggregate]` + tuple return | Standard aggregate handlers | ✅ Automatic - Wolverine appends events |
| Manual loading + tuple return | ❌ **Never use this combination** | ❌ Events discarded silently |
| Manual loading + `session.Events.Append()` | Deterministic IDs, custom loading logic | ✅ Explicit - you control appending |

**Real examples from CritterSupply:**

```csharp
// ✅ Promotions BC: RedeemCouponHandler.cs (M30.0)
public static async Task Handle(
    RedeemCoupon cmd,
    IDocumentSession session,
    CancellationToken ct)
{
    var streamId = Coupon.StreamId(cmd.CouponCode); // UUID v5
    var coupon = await session.Events.AggregateStreamAsync<Coupon>(streamId, token: ct);

    // Validation...

    var evt = new CouponRedeemed(/* ... */);
    session.Events.Append(streamId, evt); // ✅ Manual append
}

// ✅ Returns BC: RequestReturnHandler.cs, ExpireReturnHandler.cs
// ✅ Inventory BC: Multiple handlers
// ✅ Correspondence BC: Message handlers
```

**Key lesson from M30.0:** This anti-pattern caused ~30 minutes of debugging when events weren't persisting. The tuple return looked correct but silently failed. Always use `session.Events.Append()` when manually loading aggregates.

**Reference:** [M30.0 Retrospective - D1: Manual Event Appending Pattern](../../planning/milestones/m30-0-retrospective.md#d1-manual-event-appending-pattern--critical-discovery)

### 9. ❌ Using Direct `session.Events.StartStream()` Without Returning `IStartStream` ⚠️ **CRITICAL**

**Problem:** Creating new event streams with direct `session.Events.StartStream()` calls bypasses Wolverine's transactional middleware. Events appear to succeed but are **silently discarded** — no exceptions thrown, handler completes successfully, but database remains unchanged.

**❌ ANTI-PATTERN — Events Not Persisted:**

```csharp
[WolverinePost("/api/carts")]
public static CreationResponse Handle(InitializeCart cmd, IDocumentSession session)
{
    var cartId = Guid.CreateVersion7();
    var @event = new CartInitialized(cmd.CustomerId, cmd.SessionId);

    // ❌ WRONG: Direct session usage does NOT enroll in transactional middleware
    session.Events.StartStream<Cart>(cartId, @event);

    // Handler completes successfully but events are NOT persisted!
    return new CreationResponse($"/api/carts/{cartId}");
}
```

**✅ CORRECT — Return IStartStream:**

```csharp
[WolverinePost("/api/carts")]
public static (CreationResponse, IStartStream) Handle(InitializeCart cmd)
{
    var cartId = Guid.CreateVersion7();
    var @event = new CartInitialized(cmd.CustomerId, cmd.SessionId);

    // ✅ CORRECT: Return IStartStream from MartenOps.StartStream()
    var stream = MartenOps.StartStream<Cart>(cartId, @event);

    var response = new CreationResponse($"/api/carts/{cartId}");
    return (response, stream);  // Response first, IStartStream second
}
```

**Why this matters:**
- `IStartStream` is a special return type Wolverine recognizes as a persistence side effect
- Wolverine's `AutoApplyTransactions()` policy only wraps handlers that return recognized types
- Direct `session.Events.StartStream()` calls don't produce return values Wolverine can intercept
- This is a **silent failure** pattern — no errors, but data never reaches the database
- **CRITICAL:** This pattern was discovered in M32.0 retrospectives after 30+ minutes of debugging

**See also:** `docs/skills/marten-event-sourcing.md` (lines 1029-1084) for detailed explanation and `AutoApplyTransactions()` requirements.

**Reference:** [M32.0 Session 5 Retrospective - Investigation Findings](../../retrospectives/m32.0-session5-retrospective.md)

### 10. ❌ Using Compound Handler Pattern with Mixed Parameter Sources ⚠️ **IMPORTANT**

**Problem:** Wolverine's compound handler pattern (with `Before()`, `Validate()`, `Load()` lifecycle) cannot handle mixed parameter sources where some parameters come from route and others from JSON body. The `Before()` method cannot construct the command because it doesn't have access to the deserialized body.

**When this occurs:**
- HTTP endpoints with route parameters (e.g., `{userId}`) AND a JSON body
- Compound handler expects to construct command in `Before()` method
- Result: 500 Internal Server Error during request handling

**❌ ANTI-PATTERN — Mixed Parameters in Compound Handler:**

```csharp
// ❌ This pattern fails
[WolverinePost("/api/users/{userId}/reset-password")]
public static class ResetPasswordHandler
{
    // Before() cannot be called - newPassword is from JSON body, not available at this stage
    public static Before(Guid userId, string newPassword)
    {
        return new ResetPassword(userId, newPassword);
    }

    public static async Task<IResult> Handle(
        ResetPasswordResponse? response,
        ProblemDetails? problem)
    {
        // Never reached - Before() construction fails
        return response is not null
            ? Results.Ok(response)
            : Results.Problem(problem);
    }
}
```

**✅ CORRECT — Use Direct Implementation Pattern:**

```csharp
// ✅ This pattern works
public sealed record ResetPasswordRequest(string NewPassword);

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
    }
}

[WolverinePost("/api/users/{userId}/reset-password")]
public static async Task<IResult> Handle(
    Guid userId,                        // Route parameter - injected by Wolverine
    ResetPasswordRequest request,       // JSON body - auto-deserialized by Wolverine
    BackofficeIdentityDbContext db,     // DI service
    CancellationToken ct)
{
    // Direct implementation - no compound handler lifecycle
    var user = await db.AdminUsers.FindAsync([userId], ct);
    if (user is null)
        return Results.NotFound();

    user.PasswordHash = HashPassword(request.NewPassword);
    user.RefreshToken = null; // Security: invalidate existing sessions
    await db.SaveChangesAsync(ct);

    return Results.Ok(new { Success = true });
}
```

**Why Direct Implementation Works:**
- Wolverine auto-deserializes JSON body to the `ResetPasswordRequest` type
- Route parameters are injected separately (standard ASP.NET Core behavior)
- No compound handler lifecycle - Wolverine treats this as a simple endpoint
- FluentValidation still works (via `ResetPasswordRequestValidator`)
- Less magic, more explicit - easier to understand and debug

**When to Use Each Pattern:**

| Scenario | Pattern | Rationale |
|----------|---------|-----------|
| All parameters from JSON body | ✅ Compound Handler | Can use `Before()` to construct command |
| Route parameters only (no body) | ✅ Compound Handler | Route params available to `Before()` |
| Mixed route + JSON body | ❌ Compound Handler, ✅ Direct | `Before()` can't access deserialized body |
| Complex validation/loading logic | ✅ Compound Handler | Benefit from lifecycle stages |
| Simple CRUD endpoint | ✅ Direct Implementation | Less ceremony, more direct |

**Real Example from CritterSupply:**

```csharp
// ✅ BackofficeIdentity BC: ResetBackofficeUserPasswordEndpoint.cs (M32.3 Session 10)
// Direct implementation after compound handler failed with 500 errors
[WolverinePost("/api/backoffice-identity/users/{userId}/reset-password")]
public static async Task<IResult> Handle(
    Guid userId,
    ResetPasswordRequest request,
    BackofficeIdentityDbContext db,
    CancellationToken ct)
{
    var user = await db.AdminUsers.FindAsync([userId], ct);
    // ... implementation
}

// ✅ Pricing BC: SetBasePriceEndpoint.cs (established pattern)
// Direct implementation for route + body scenario
[WolverinePost("/api/pricing/{sku}/base-price")]
public static async Task<IResult> Handle(
    string sku,
    SetBasePriceRequest request,
    IDocumentSession session,
    CancellationToken ct)
{
    // ... implementation
}
```

**Key Lessons (M32.3 Session 10):**
1. This anti-pattern caused 6/6 integration tests to fail with 500 errors
2. Error was silent during compilation - only failed at runtime
3. Direct implementation pattern is a **valid alternative**, not a workaround
4. When in doubt, check Pricing BC or BackofficeIdentity BC for reference patterns

**When Compound Handler Is Still Preferred:**
- All parameters from JSON body: `CreateUser(email, firstName, lastName, role)`
- Complex validation logic that benefits from separate `Validate()` stage
- Multi-step loading: `Load()` fetches aggregate, `Handle()` applies business logic
- Railway programming with async validation (see anti-pattern #4)

**Reference:** [M32.3 Session 10 Retrospective - Wolverine Pattern Limitations](../../planning/milestones/m32-3-session-10-retrospective.md)

### 11. ❌ Using `bus.PublishAsync()` for Integration Events in HTTP Endpoints ⭐ *M36.0 Addition*

**Problem:** `IMessageBus.PublishAsync()` in HTTP endpoints bypasses Wolverine's transactional outbox. The message is published immediately — even if the database transaction rolls back. This was the most pervasive Critter Stack idiom violation found in M36.0 (fixed across 4 BCs).

**❌ WRONG:**

```csharp
[WolverinePost("/api/marketplaces/{channelCode}/deactivate")]
public static async Task<IResult> Handle(
    string channelCode,
    IDocumentSession session,
    IMessageBus bus, // ❌ Injecting IMessageBus
    CancellationToken ct)
{
    var marketplace = await session.LoadAsync<Marketplace>(channelCode, ct);
    marketplace.IsActive = false;
    session.Store(marketplace);

    await bus.PublishAsync(new MarketplaceDeactivated(channelCode)); // ❌ Outside outbox
    return Results.Ok();
}
```

**✅ CORRECT:**

```csharp
[WolverinePost("/api/marketplaces/{channelCode}/deactivate")]
public static async Task<(IResult, OutgoingMessages)> Handle(
    string channelCode,
    IDocumentSession session,
    CancellationToken ct)
{
    var outgoing = new OutgoingMessages();
    var marketplace = await session.LoadAsync<Marketplace>(channelCode, ct);

    if (marketplace.IsActive)
    {
        marketplace.IsActive = false;
        session.Store(marketplace);
        outgoing.Add(new MarketplaceDeactivated(channelCode)); // ✅ Inside outbox
    }
    // Idempotent: already inactive → no event published

    return (Results.Ok(), outgoing);
}
```

**Why:** `OutgoingMessages` is processed within the same Wolverine middleware pipeline that commits the Marten session. If the session commit fails, messages are not published. `bus.PublishAsync()` sends immediately, outside this transaction boundary.

**Exception:** `bus.ScheduleAsync()` remains a valid use of `IMessageBus` — delayed message delivery cannot be expressed via `OutgoingMessages`.

---

## File Organization and Naming

**CritterSupply uses vertical slice organization:**

Commands, validators, and handlers are colocated in a single file:

```
Features/
  Payments/
    ProcessPayment.cs      # Command + Validator + Handler
    CapturePayment.cs      # Command + Validator + Handler
    PaymentProcessed.cs    # Domain event (separate file)
```

**File naming conventions:**

| Type | Naming | Example |
|------|--------|---------|
| Command | `{Verb}{Noun}.cs` | `PlaceOrder.cs` |
| Event | `{Noun}{PastTenseVerb}.cs` | `OrderPlaced.cs` |
| Handler | Static class suffixed with `Handler` | `PlaceOrderHandler` |
| Validator | Class suffixed with `Validator` | `PlaceOrderValidator` |

**Example file structure:**

```csharp
// File: PlaceOrder.cs

public sealed record PlaceOrder(Guid CustomerId, IReadOnlyList<OrderLineItem> Items);

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrder>
{
    public PlaceOrderValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Items).NotEmpty();
    }
}

public static class PlaceOrderHandler
{
    public static (Events, OutgoingMessages) Handle(PlaceOrder cmd)
    {
        var orderId = Guid.CreateVersion7();
        var @event = new OrderPlaced(orderId, cmd.CustomerId, cmd.Items);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.OrderPlaced(orderId, cmd.Items));

        return ([@event], outgoing);
    }
}
```

**Events in separate files:**

```csharp
// File: OrderPlaced.cs

public sealed record OrderPlaced(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<OrderLineItem> Items,
    DateTimeOffset PlacedAt);
```

**See `docs/skills/vertical-slice-organization.md` for complete file organization patterns.**

---

## References

- [Wolverine Message Handlers Guide](https://wolverinefx.net/guide/handlers/)
- [Wolverine Aggregate Handler Workflow](https://wolverinefx.net/guide/durability/marten/event-sourcing.html)
- [Railway Programming with Wolverine](https://wolverinefx.net/tutorials/railway-programming.html)
- [CQRS with Marten Tutorial](https://wolverinefx.net/tutorials/cqrs-with-marten.html)
- [A-Frame Architecture with Wolverine](https://jeremydmiller.com/2023/07/19/a-frame-architecture-with-wolverine/)
- [Functional Event Sourcing Decider](https://thinkbeforecoding.com/post/2021/12/17/functional-event-sourcing-decider)
