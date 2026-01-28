# Wolverine Message Handlers

Patterns and practices for building message handlers and HTTP endpoints with Wolverine in CritterSupply.

## Core Principle: Pure Functions for Business Logic

Wolverine's compound handler feature separates infrastructure concerns (validation, data loading) from business logic. The goal is a pure `Handle()` method focused entirely on domain decisions.

This approach is inspired by the [A-Frame Architecture](https://jeremydmiller.com/2023/07/19/a-frame-architecture-with-wolverine/) — infrastructure at the edges, pure logic in the middle.

## Compound Handler Lifecycle

Wolverine invokes methods in this order:

| Lifecycle | Method Names | Purpose |
|-----------|--------------|---------|
| Before Handler | `Before`, `BeforeAsync`, `Load`, `LoadAsync`, `Validate`, `ValidateAsync` | Load data, check preconditions |
| Handler | `Handle`, `HandleAsync` | Business logic (pure function) |
| After Handler | `After`, `AfterAsync`, `PostProcess`, `PostProcessAsync` | Side effects, notifications |
| Finally | `Finally`, `FinallyAsync` | Cleanup (runs even on failure) |

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

For HTTP endpoints, use `MartenOps.StartStream()` which returns `IStartStream`:

```csharp
public static class InitializeCartHandler
{
    [WolverinePost("/api/carts")]
    public static (IStartStream, CreationResponse) Handle(InitializeCart command)
    {
        var cartId = Guid.CreateVersion7();
        var @event = new CartInitialized(command.CustomerId, DateTimeOffset.UtcNow);

        var stream = MartenOps.StartStream<Cart>(cartId, @event);

        return (stream, new CreationResponse($"/api/carts/{cartId}"));
    }
}
```

> **Reference:** [Wolverine HTTP + Marten](https://wolverinefx.net/guide/http/marten.html)

### Summary Table

| Scenario | Return Type | Stream Creation |
|----------|-------------|-----------------|
| Update existing aggregate | `(Events, OutgoingMessages)` | N/A — uses `[WriteAggregate]` |
| Start new stream (message handler) | `OutgoingMessages` | `session.Events.StartStream<T>()` |
| Start new stream (HTTP endpoint) | `(IStartStream, HttpResponse)` | `MartenOps.StartStream<T>()` |

## `[WriteAggregate]` vs `Load()` Pattern

**Always prefer `[WriteAggregate]`** — it's the cleanest pattern. Only fall back to `Load()` when Wolverine cannot auto-resolve the aggregate ID.

### Use `[WriteAggregate]` When:

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

### Use `Load()` When:

The aggregate ID must be computed or discovered:

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

### Key Differences

| Aspect | `[WriteAggregate]` | `Load()` Pattern |
|--------|-------------------|------------------|
| Aggregate Loading | Automatic | Manual via `Load()` |
| ID Resolution | Direct command property | Computed or queried |
| Event Persistence | Automatic via return | Manual via `session.Events.Append()` |
| Return Type | `(Events, OutgoingMessages)` | `OutgoingMessages` only |

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

## File Organization

Commands, validators, and handlers are colocated in a single file:

```
Features/
  Payments/
    ProcessPayment.cs      # Command + Validator + Handler
    CapturePayment.cs      # Command + Validator + Handler
    PaymentProcessed.cs    # Domain event (separate file)
```

See `skills/vertical-slice-organization.md` for complete file organization patterns.
