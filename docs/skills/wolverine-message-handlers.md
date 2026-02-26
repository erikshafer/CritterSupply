# Wolverine Message Handlers

Patterns and practices for building message handlers and HTTP endpoints with Wolverine in CritterSupply.

## Core Principle: Pure Functions for Business Logic

Wolverine's compound handler feature separates infrastructure concerns (validation, data loading) from business logic. The goal is a pure `Handle()` method focused entirely on domain decisions.

This approach is inspired by the [A-Frame Architecture](https://jeremydmiller.com/2023/07/19/a-frame-architecture-with-wolverine/) — infrastructure at the edges, pure logic in the middle.

## Compound Handler Lifecycle

Wolverine invokes methods in this order:

| Lifecycle      | Method Names                                                              | Purpose                        |
|----------------|---------------------------------------------------------------------------|--------------------------------|
| Before Handler | `Before`, `BeforeAsync`, `Load`, `LoadAsync`, `Validate`, `ValidateAsync` | Load data, check preconditions |
| Handler        | `Handle`, `HandleAsync`                                                   | Business logic (pure function) |
| After Handler  | `After`, `AfterAsync`, `PostProcess`, `PostProcessAsync`                  | Side effects, notifications    |
| Finally        | `Finally`, `FinallyAsync`                                                 | Cleanup (runs even on failure) |

> **Reference:** [Wolverine Compound Handlers](https://wolverinefx.net/guide/handlers/middleware.html)

## Standard Handler Pattern

```csharp
public sealed record ProcessPayment(Guid PaymentId, decimal Amount)
{
    public class ProcessPaymentValidator : AbstractValidator<ProcessPayment>
    {
        public ProcessPaymentValidator()
        {
            RuleFor(x => x.PaymentId).NotEmpty();
            RuleFor(x => x.Amount).GreaterThan(0);
        }
    }
}

public static class ProcessPaymentHandler
{
    // Precondition check - runs before Handle()
    public static ProblemDetails Before(ProcessPayment command, Payment? payment)
    {
        if (payment is null)
            return new ProblemDetails { Detail = "Payment not found", Status = 404 };

        if (payment.Status != PaymentStatus.Pending)
            return new ProblemDetails { Detail = "Payment already processed", Status = 400 };

        return WolverineContinue.NoProblems;
    }

    // Pure function - business logic only
    public static (Events, OutgoingMessages) Handle(
        ProcessPayment command,
        [WriteAggregate] Payment payment)
    {
        var events = new Events();
        var outgoing = new OutgoingMessages();

        events.Add(new PaymentProcessed(payment.Id, command.Amount, DateTimeOffset.UtcNow));
        outgoing.Add(new IntegrationMessages.PaymentCompleted(payment.Id, payment.OrderId));

        return (events, outgoing);
    }
}
```

> **Reference:** [Wolverine Message Handlers](https://wolverinefx.net/guide/handlers/)

## Handler Return Patterns

### Pattern 1: Existing Aggregate with `[WriteAggregate]`

For handlers operating on existing aggregates, return `(Events, OutgoingMessages)`:

```csharp
public static (Events, OutgoingMessages) Handle(
    CapturePayment command,
    [WriteAggregate] Payment payment)
{
    var events = new Events();
    events.Add(new PaymentCaptured(payment.Id, DateTimeOffset.UtcNow));

    var outgoing = new OutgoingMessages();
    outgoing.Add(new IntegrationMessages.PaymentCaptured(payment.Id, payment.OrderId));

    return (events, outgoing);
}
```

- `Events` — Domain events appended to the aggregate's stream
- `OutgoingMessages` — Integration messages published to other bounded contexts
- Wolverine handles persistence automatically

### Pattern 2: Starting New Streams (Message Handlers)

For message handlers that create new aggregates, use `IDocumentSession` directly:

```csharp
public static async Task<OutgoingMessages> Handle(
    StartPayment command,
    IDocumentSession session,
    CancellationToken ct)
{
    var paymentId = Guid.CreateVersion7();
    var initiated = new PaymentInitiated(paymentId, command.OrderId, command.Amount);

    session.Events.StartStream<Payment>(paymentId, initiated);

    var outgoing = new OutgoingMessages();
    outgoing.Add(new IntegrationMessages.PaymentStarted(paymentId, command.OrderId));

    return outgoing;
}
```

- Inject `IDocumentSession` to access event store
- Use `session.Events.StartStream<T>()` to create new stream
- Return only `OutgoingMessages` (events persisted via session)

### Pattern 3: Starting New Streams (HTTP Endpoints)

For HTTP endpoints that create new event-sourced aggregates, use `MartenOps.StartStream()` which returns `IStartStream`:

```csharp
public static class InitializeCartHandler
{
    [WolverinePost("/api/carts")]
    public static (CreationResponse<Guid>, IStartStream) Handle(InitializeCart command)
    {
        var cartId = Guid.CreateVersion7();
        var @event = new CartInitialized(command.CustomerId, command.SessionId, DateTimeOffset.UtcNow);

        var stream = MartenOps.StartStream<Cart>(cartId, @event);

        // CRITICAL: Response MUST come first in the tuple!
        var response = new CreationResponse<Guid>($"/api/carts/{cartId}", cartId);

        return (response, stream);
    }
}
```

**⚠️ CRITICAL: Tuple Order Matters!**

In Wolverine, **the first item in a return tuple is ALWAYS treated as the HTTP response**.

- ✅ **Correct:** `(CreationResponse<Guid>, IStartStream)` → Returns 201 Created with JSON body
- ❌ **Wrong:** `(IStartStream, CreationResponse)` → Returns 204 No Content (no body!)

**Using `CreationResponse<T>`:**

- `CreationResponse<T>` (generic) includes the value `T` in the response body
- `CreationResponse` (non-generic) only sets the Location header (no body)
- Returns HTTP 201 Created with Location header + JSON body

**Example response:**
```http
HTTP/1.1 201 Created
Location: /api/carts/019c49bf-9852-73c1-bb67-da545727eca4
Content-Type: application/json

{
  "value": "019c49bf-9852-73c1-bb67-da545727eca4",
  "url": "/api/carts/019c49bf-9852-73c1-bb67-da545727eca4"
}
```

> **References:**
> - [Wolverine HTTP + Marten](https://wolverinefx.net/guide/http/marten.html)
> - [Wolverine HTTP Return Types](https://wolverinefx.net/guide/http/endpoints.html)
> - [CQRS with Marten Tutorial](https://wolverinefx.net/tutorials/cqrs-with-marten.html#start-a-new-stream)

### Pattern 4: Returning Updated Aggregate State

Use `UpdatedAggregate<T>` to return the updated state of a projected aggregate as the HTTP response:

```csharp
[AggregateHandler]
[WolverinePost("/orders/{id}/confirm")]
public static (UpdatedAggregate, Events) Handle(ConfirmOrder command, Order order)
{
    return (
        new UpdatedAggregate(),
        [new OrderConfirmed()]
    );
}
```

**When multiple event streams are involved**, use the generic `UpdatedAggregate<T>` to specify which aggregate to return:

```csharp
public static class MakePurchaseHandler
{
    // Use UpdatedAggregate<T> to tell Wolverine we want *only* the XAccount as the response
    public static UpdatedAggregate<XAccount> Handle(
        MakePurchase command,
        [WriteAggregate] IEventStream<XAccount> account,
        [WriteAggregate] IEventStream<Inventory> inventory)
    {
        if (command.Number > inventory.Aggregate.Quantity ||
            (command.Number * inventory.Aggregate.UnitPrice) > account.Aggregate.Balance)
        {
            // Do nothing if validation fails
            return new UpdatedAggregate<XAccount>();
        }

        account.AppendOne(new ItemPurchased(command.InventoryId, command.Number, inventory.Aggregate.UnitPrice));
        inventory.AppendOne(new Drawdown(command.Number));

        return new UpdatedAggregate<XAccount>();
    }
}
```

**Key points:**
- `UpdatedAggregate` (non-generic) returns the single aggregate from the handler
- `UpdatedAggregate<T>` (generic) specifies which aggregate to return when using multiple `[WriteAggregate]` streams
- The aggregate must have a Marten projection configured to compute its state
- Wolverine automatically queries the projection and returns the updated aggregate as the HTTP response body

> **Reference:** [Wolverine UpdatedAggregate Documentation](https://wolverine.netlify.app/guide/http/marten.html#responding-with-the-updated-aggregate)

### Summary Table

| Scenario                           | Return Type                                 | Stream Creation                   |
|------------------------------------|---------------------------------------------|-----------------------------------|
| Update existing aggregate          | `(Events, OutgoingMessages)`                | N/A — uses `[WriteAggregate]`     |
| Return updated aggregate state     | `UpdatedAggregate` or `UpdatedAggregate<T>` | N/A — uses `[WriteAggregate]`     |
| Start new stream (message handler) | `OutgoingMessages`                          | `session.Events.StartStream<T>()` |
| Start new stream (HTTP endpoint)   | `(IStartStream, HttpResponse)`              | `MartenOps.StartStream<T>()`      |

## Aggregate Loading Patterns

Wolverine provides three approaches for loading event-sourced aggregates: `[ReadAggregate]` for read-only access, `[WriteAggregate]` for modifications, and `Load()` for complex scenarios.

### `[ReadAggregate]` — Read-Only Access

Use `[ReadAggregate]` when you need to query the current state of an aggregate without modifying it. This is ideal for HTTP GET endpoints:

```csharp
[WolverineGet("/orders/{id}")]
public static Order? GetOrder(Guid id, [ReadAggregate] Order? order) => order;

[WolverineGet("/orders/{id}/status")]
public static OrderStatusResponse GetStatus(Guid id, [ReadAggregate] Order? order)
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

**Key points:**
- No events are persisted — read-only operation
- Wolverine loads the aggregate by projecting all events in the stream
- Returns the aggregate (or DTO derived from it) directly
- Ideal for query endpoints that don't modify state
- Aggregate can be nullable (`Order?`) for 404 handling

> **Reference:** [Wolverine ReadAggregate Documentation](https://wolverine.netlify.app/guide/http/marten.html#reading-the-latest-version-of-an-aggregate)

### `[WriteAggregate]` — Modifying Aggregates

**Always prefer `[WriteAggregate]`** — it's the cleanest pattern for commands. Only fall back to `Load()` when Wolverine cannot auto-resolve the aggregate ID.

**Use when:**

The command has the aggregate ID as a direct property:

```csharp
public sealed record CapturePayment(Guid PaymentId, decimal Amount);

public static class CapturePaymentHandler
{
    public static ProblemDetails Before(CapturePayment command, Payment? payment)
    {
        if (payment is null)
            return new ProblemDetails { Detail = "Not found", Status = 404 };
        return WolverineContinue.NoProblems;
    }

    public static (Events, OutgoingMessages) Handle(
        CapturePayment command,
        [WriteAggregate] Payment payment)  // Wolverine loads by PaymentId
    {
        // Return events — Wolverine persists automatically
    }
}
```

### `Load()` — Complex ID Resolution

Use `Load()` when the aggregate ID must be computed or discovered from the command:

**Use when:**

```csharp
public sealed record ReserveStock(Guid OrderId, string Sku, string WarehouseId, int Quantity)
{
    // Computed — Wolverine can't auto-resolve this
    public Guid InventoryId => ProductInventory.CombinedGuid(Sku, WarehouseId);
}

public static class ReserveStockHandler
{
    public static async Task<ProductInventory?> Load(
        ReserveStock command,
        IDocumentSession session,
        CancellationToken ct)
    {
        return await session.LoadAsync<ProductInventory>(command.InventoryId, ct);
    }

    public static ProblemDetails Before(ReserveStock command, ProductInventory? inventory)
    {
        if (inventory is null)
            return new ProblemDetails { Detail = "Inventory not found", Status = 404 };
        return WolverineContinue.NoProblems;
    }

    // NO [WriteAggregate] — we loaded manually
    public static OutgoingMessages Handle(
        ReserveStock command,
        ProductInventory inventory,
        IDocumentSession session)
    {
        var @event = new StockReserved(command.OrderId, command.Quantity);
        session.Events.Append(inventory.Id, @event);  // Manual persistence

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.StockReserved(/* ... */));
        return outgoing;  // Return ONLY OutgoingMessages
    }
}
```

> **Reference:** [Wolverine Aggregate Handler Workflow](https://wolverinefx.net/guide/durability/marten/event-sourcing.html#aggregate-handlers-and-event-sourcing)

### Summary: Choosing the Right Pattern

| Aspect | `[ReadAggregate]` | `[WriteAggregate]` | `Load()` Pattern |
|--------|------------------|-------------------|------------------|
| **Use Case** | Query aggregate state | Modify aggregate | Complex ID resolution |
| **Aggregate Loading** | Automatic | Automatic | Manual via `Load()` |
| **ID Resolution** | Direct route parameter | Direct command property | Computed or queried |
| **Event Persistence** | None (read-only) | Automatic via return | Manual via `session.Events.Append()` |
| **Return Type** | Aggregate or DTO | `(Events, OutgoingMessages)` | `OutgoingMessages` only |
| **Typical Endpoints** | GET | POST, PUT, DELETE | Any (when needed) |

### Common Pitfall: Double Persistence

Never use both `session.Events.Append()` AND return in `Events` collection:

```csharp
// BAD — events persisted twice!
public static (Events, OutgoingMessages) Handle(...)
{
    session.Events.Append(id, domainEvent);  // Persisted here
    var events = new Events();
    events.Add(domainEvent);  // Also persisted here — WRONG
    return (events, outgoing);
}

// GOOD — single persistence with Load() pattern
public static OutgoingMessages Handle(...)
{
    session.Events.Append(id, domainEvent);  // Persisted only here
    return outgoing;  // No Events collection
}
```

## HTTP Endpoint Attributes

```csharp
[WolverineGet("/api/orders/{orderId}")]
[WolverinePost("/api/orders")]
[WolverinePut("/api/orders/{orderId}")]
[WolverineDelete("/api/orders/{orderId}")]
```

Use `CreationResponse` for POST endpoints:

```csharp
public sealed record PlaceOrderResponse(Guid Id) : CreationResponse($"/api/orders/{Id}");
```

## HTTP Endpoint Conventions

CritterSupply uses **flat, resource-centric** HTTP endpoints:

```
/api/carts/{cartId}
/api/orders/{orderId}
/api/payments/{paymentId}
/api/products/{sku}
```

**Key principles:**
- **Resources are top-level** — Not nested under BC names (e.g., `/api/orders`, not `/api/order-management/orders`)
- **Resource names are plural nouns** — `/api/products`, not `/api/product`
- **BC ownership is internal** — URL structure doesn't expose bounded context boundaries
- **Avoid deep nesting** — Prefer `/api/order-items?orderId={orderId}` over `/api/orders/{orderId}/items`

**Why flat structure?**
- Simpler client routing and API discovery
- Easier to refactor bounded context boundaries without breaking URLs
- Clearer resource ownership (each resource has one canonical URL)
- Aligns with REST best practices for resource addressability

**Examples:**

```csharp
// GOOD: Flat, resource-centric
[WolverineGet("/api/orders/{orderId}")]
public static Task<Order?> GetOrder(Guid orderId, IDocumentSession session)
    => session.LoadAsync<Order>(orderId);

[WolverinePost("/api/carts")]
public static (IStartStream, CreatedAtRoute<AddCartResponse>) AddCart(AddCart command)
{
    var cartId = Guid.CreateVersion7();
    var @event = new CartCreated(cartId, command.CustomerId, DateTimeOffset.UtcNow);

    return (
        MartenOps.StartStream<Cart>(cartId, @event),
        new CreatedAtRoute<AddCartResponse>("GetCart", new { cartId }, new AddCartResponse(cartId))
    );
}

// AVOID: Nested resources (harder to maintain)
[WolverineGet("/api/orders/{orderId}/items")]
public static Task<List<OrderItem>> GetOrderItems(Guid orderId) { /* ... */ }

// PREFER: Query parameter for filtering
[WolverineGet("/api/order-items")]
public static Task<List<OrderItem>> GetOrderItems([FromQuery] Guid orderId) { /* ... */ }
```

## File Organization

Commands, validators, and handlers are colocated in a single file:

```
Features/
  Payments/
    ProcessPayment.cs      # Command + Validator + Handler
    CapturePayment.cs      # Command + Validator + Handler
    PaymentProcessed.cs    # Domain event (separate file)
```

See `docs/skills/vertical-slice-organization.md` for complete file organization patterns.
