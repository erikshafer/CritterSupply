# Wolverine Sagas

Patterns and practices for building stateful orchestration sagas with Wolverine + Marten in CritterSupply.

## Core Principle: Sagas Are Mutable State Machines

A Wolverine saga is the right tool when business logic must coordinate **multiple bounded contexts over time**, maintaining mutable state that drives orchestration decisions. Unlike event-sourced aggregates, which append immutable events, a saga is a **living document** that mutates as the process progresses.

The canonical example in CritterSupply is the `Order` saga, which orchestrates Inventory, Payments, and Fulfillment across potentially minutes or hours — handling confirmations, failures, cancellations, and delayed events throughout the order lifecycle.

This approach mirrors the [Process Manager pattern](https://www.enterpriseintegrationpatterns.com/patterns/messaging/ProcessManager.html) from EIP, and Wolverine's `Saga` base class provides the infrastructure scaffolding. The Order saga pairs Wolverine's saga runtime with the [Decider pattern](https://thinkbeforecoding.com/post/2021/12/17/functional-event-sourcing-decider) for purely functional business logic.

## When to Use a Saga vs. Other Patterns

| Scenario | Best Pattern | Why |
|----------|-------------|-----|
| Coordinate 2+ BCs over time (minutes/hours) | **Saga** | Mutable state, correlation, compensation |
| Record what happened to a single aggregate | **Event-sourced aggregate** | Immutable history, time-travel queries |
| Simple fire-and-forget message routing | **Message handler** | No state needed |
| Stateless transformation of one message into others | **Message handler** | Functional, no persistence |
| Complex conditional flow with rollback | **Saga** | Compensation chains, state guards |
| Long-running workflow with timeouts | **Saga** | Scheduled messages, durable state |

**Rule of thumb:** If you find yourself asking "what state is the workflow in right now?" — reach for a saga.

### Event-Sourced Aggregate vs. Document-Based Saga

Marten supports both. Choosing wrong is a common mistake:

| Dimension | Event-Sourced Aggregate | Document-Based Saga |
|-----------|------------------------|---------------------|
| Persistence | Append-only event stream | Mutable JSON document |
| History | Full audit trail — replay-able | Current state only |
| State access | Rebuild from events (`IReadOnlyList<T> Apply`) | Direct property read |
| Concurrency | Optimistic via stream version | Optimistic via numeric revision |
| Best for | Domain objects with history requirements | Orchestration processes |
| CritterSupply usage | `Checkout` aggregate | `Order` saga |

**Use a document-based saga for stateful orchestration.** The saga's job is coordination, not audit — the individual BCs (Inventory, Payments, Fulfillment) each own their own event streams with full history. The saga just needs to know "where are we now."

> **Reference:** [Wolverine Saga Documentation](https://wolverinefx.net/guide/durability/marten/sagas.html)

## The Wolverine Saga API

### The `Saga` Base Class

All Wolverine sagas inherit from `Wolverine.Saga`. The minimum contract is:

```csharp
public sealed class Order : Saga
{
    // REQUIRED: Wolverine uses this as the correlation key for all related messages.
    // Must be named "Id" — the Wolverine/Marten convention.
    public Guid Id { get; set; }

    // Any other properties you need — all serialized as a JSON document in Marten.
    public OrderStatus Status { get; set; }
    public bool IsPaymentCaptured { get; set; }
    // ...

    // Handler methods — Wolverine discovers these by naming convention.
    public OutgoingMessages Handle(SomeIntegrationMessage message) { ... }
    public void Handle(AnotherMessage message) { ... }
}
```

### Saga Identity and Message Correlation

Wolverine correlates incoming messages to a saga instance by convention: it looks for a property on the incoming message named `{SagaTypeName}Id`. For an `Order` saga, it looks for `OrderId`.

```csharp
// Wolverine finds the Order saga whose Id == message.OrderId
// The property must be named exactly "OrderId" (PascalCase, saga name + "Id")
public sealed record PaymentCaptured(
    Guid PaymentId,
    Guid OrderId,   // <-- Wolverine uses this to find/load the Order saga
    decimal Amount,
    DateTimeOffset CapturedAt);
```

This is compile-time magic — **no attribute, no configuration** — just the naming convention. If the property name doesn't match, the saga won't be found, and Wolverine will throw at startup.

```csharp
// ✅ CORRECT — Wolverine finds Order saga by OrderId
public sealed record ReservationConfirmed(Guid ReservationId, Guid OrderId, string Sku);

// ❌ WRONG — "Id" alone won't correlate; must be "{SagaName}Id"
public sealed record ReservationConfirmed(Guid Id, Guid OrderId, string Sku);

// ❌ WRONG — Wrong name; Wolverine won't find the Order saga
public sealed record ReservationConfirmed(Guid ReservationId, Guid SagaCorrelationId, string Sku);
```

### `MarkCompleted()`

Call `MarkCompleted()` when the saga reaches a terminal state. Wolverine will delete the saga document from Marten after the current handler completes. This is not just good hygiene — it prevents orphaned saga documents from accumulating in your database indefinitely.

```csharp
public void Handle(ReturnWindowExpired message)
{
    Status = OrderStatus.Closed;
    MarkCompleted(); // Wolverine deletes the document after this handler
}
```

**⚠️ Every terminal state must call `MarkCompleted()`.** See [Saga Lifecycle Completion](#saga-lifecycle-completion) for a full analysis.

> **Reference:** [Wolverine Saga Completion](https://wolverinefx.net/guide/durability/marten/sagas.html#completing-a-saga)

## Starting a Saga

### The `PlaceOrderHandler` Pattern (Recommended)

The cleanest way to start a saga is via a **separate static handler class** that returns a tuple of `(SagaType, ...)`. Wolverine recognizes this return type as a saga start:

```csharp
// Separate handler class — NOT on the saga itself
public static class PlaceOrderHandler
{
    // Return type (Order, ...) signals Wolverine to persist the Order saga
    // and cascade the remaining messages
    public static (Order, IntegrationMessages.OrderPlaced) Handle(
        Messages.Contracts.Shopping.CheckoutCompleted message)
    {
        // Map integration message to domain command
        var command = new PlaceOrder(
            message.OrderId,
            // ... map other fields
        );

        // Delegate to pure Decider — no side effects
        return OrderDecider.Start(command, DateTimeOffset.UtcNow);
    }
}
```

**Why a separate handler?** Keeping initialization logic outside the `Order` class respects single responsibility: the saga class handles state transitions; the handler handles construction. The saga's `Handle()` methods can then focus purely on evolving state.

### What NOT to Do: `IStartStream` Is for Event-Sourced Aggregates

`MartenOps.StartStream<T>()` / `IStartStream` applies to event-sourced aggregates, not document-based sagas. Don't use it to start a saga:

```csharp
// ❌ WRONG — IStartStream is for event-sourced aggregates (like Checkout)
public static (IStartStream, OrderPlaced) Handle(CheckoutCompleted message)
{
    var stream = MartenOps.StartStream<Order>(message.OrderId, new OrderCreated(...));
    return (stream, new OrderPlaced(...));
}

// ✅ CORRECT — Return the saga instance; Wolverine persists it as a document
public static (Order, OrderPlaced) Handle(CheckoutCompleted message)
{
    var saga = new Order { Id = message.OrderId, Status = OrderStatus.Placed, ... };
    return (saga, new OrderPlaced(...));
}
```

> **Reference:** [Starting a Marten-backed Saga](https://wolverinefx.net/guide/durability/marten/sagas.html#starting-a-saga)

## Handler Discovery: `IncludeAssembly` vs. `IncludeType`

### The Canonical Pattern: `[assembly: WolverineModule]` + `IncludeAssembly`

Every CritterSupply domain assembly declares itself with the `[WolverineModule]` attribute, and the host (`.Api`) project includes it via `IncludeAssembly`:

```csharp
// Orders/AssemblyAttributes.cs
[assembly: WolverineModule]
```

```csharp
// Orders.Api/Program.cs — include the entire domain assembly
opts.Discovery.IncludeAssembly(typeof(Order).Assembly);
```

This discovers **all handlers in the assembly** in one shot: saga handlers, PlaceOrderHandler, checkout handlers, etc.

### Why Not `IncludeType<T>()`?

`IncludeType<Order>()` only discovers handler methods on the `Order` class itself. It misses `PlaceOrderHandler`, `CheckoutInitiatedHandler`, and any other handler classes in the assembly.

```csharp
// ❌ INCOMPLETE — misses PlaceOrderHandler and other handlers
opts.Discovery.IncludeType<Order>();

// ✅ CORRECT — discovers all handlers decorated with [WolverineModule]
opts.Discovery.IncludeAssembly(typeof(Order).Assembly);
```

If you only include the saga type, Wolverine will silently fail to route incoming messages to handlers that live in other classes — a frustrating debugging experience.

> **Reference:** [Wolverine Handler Discovery](https://wolverinefx.net/guide/handlers/discovery.html)

## Marten Document Configuration for Sagas

Configure the saga's Marten storage in `Program.cs` alongside your other Marten setup:

```csharp
builder.Services.AddMarten(opts =>
{
    opts.Connection(martenConnectionString);
    opts.DatabaseSchemaName = Constants.Orders.ToLowerInvariant();

    // Saga document store configuration
    opts.Schema.For<Order>()
        .Identity(x => x.Id)              // Explicit identity mapping
        .UseNumericRevisions(true)         // Enable optimistic concurrency
        .Index(x => x.CustomerId);         // Index for querying by customer
})
.UseLightweightSessions()
.IntegrateWithWolverine();              // Wire saga persistence into Wolverine
```

### Optimistic Concurrency with `ConcurrencyException`

`UseNumericRevisions(true)` enables Marten's optimistic concurrency. When two handlers process saga messages concurrently (e.g., `ReservationConfirmed` and `PaymentCaptured` arrive simultaneously), only one will win; the other gets a `ConcurrencyException`.

Configure Wolverine to retry on concurrency exceptions:

```csharp
opts.OnException<ConcurrencyException>()
    .RetryOnce()
    .Then.RetryWithCooldown(100.Milliseconds(), 250.Milliseconds())
    .Then.Discard();
```

**This is essential for correctness.** Without retry, a concurrent update silently discards one message. With retry, the losing handler reloads fresh state and re-applies its logic.

> **Reference:** [Marten Optimistic Concurrency](https://martendb.io/documents/concurrency.html)

## The Decider Pattern for Saga Business Logic

### Why Separate the Decider?

The naive approach puts all business logic directly in the saga's `Handle()` methods. This makes the logic hard to unit test (requires a full Marten/Wolverine stack) and blurs the line between coordination and business rules.

The Decider pattern extracts business logic into a **static class with pure functions**:

```
Order (saga)                    OrderDecider (static)
├── Handle(PaymentCaptured)  →  HandlePaymentCaptured(current, message, timestamp)
├── Handle(ReservationFailed) →  HandleReservationFailed(current, message, timestamp)
└── Handle(CancelOrder)      →  HandleCancelOrder(current, command, timestamp)
```

The saga methods become thin adapters: call the decider, apply the decision, return the messages.

### The `OrderDecision` Record

The decider returns a value object carrying state changes and outgoing messages:

```csharp
// Pure value object — no side effects
public sealed record OrderDecision
{
    public OrderStatus? Status { get; init; }
    public bool? IsPaymentCaptured { get; init; }
    public int? ConfirmedReservationCount { get; init; }
    public Dictionary<Guid, string>? ReservationIds { get; init; }
    public HashSet<Guid>? CommittedReservationIds { get; init; }
    public bool ShouldComplete { get; init; }
    public IReadOnlyList<object> Messages { get; init; } = [];
}
```

Nullable fields signal "no change needed" — the saga only applies changes that are explicitly set.

### The Static `OrderDecider` Class

```csharp
public static class OrderDecider
{
    // Pure function — takes current state + event, returns decision
    // No I/O, no side effects, fully testable without infrastructure
    public static OrderDecision HandlePaymentCaptured(
        Order current,
        PaymentCaptured message,
        DateTimeOffset timestamp)
    {
        var messages = new List<object>();

        // If ALL inventory is already reserved, commit all reservations now
        if (current.IsInventoryReserved)
        {
            foreach (var reservationId in current.ReservationIds.Keys)
            {
                messages.Add(new ReservationCommitRequested(
                    current.Id, reservationId, timestamp));
            }
        }

        return new OrderDecision
        {
            Status = OrderStatus.PaymentConfirmed,
            IsPaymentCaptured = true,
            Messages = messages
        };
    }
}
```

### The Saga as a Thin Adapter

```csharp
public sealed class Order : Saga
{
    // ...

    public OutgoingMessages Handle(PaymentCaptured message)
    {
        // 1. Call pure decider function
        var decision = OrderDecider.HandlePaymentCaptured(this, message, DateTimeOffset.UtcNow);

        // 2. Apply state changes from decision
        if (decision.Status.HasValue) Status = decision.Status.Value;
        if (decision.IsPaymentCaptured.HasValue) IsPaymentCaptured = decision.IsPaymentCaptured.Value;

        // 3. Return outgoing messages for Wolverine to dispatch
        var outgoing = new OutgoingMessages();
        foreach (var msg in decision.Messages) outgoing.Add(msg);
        return outgoing;
    }
}
```

**Benefits of this split:**
- `OrderDecider` methods are **unit-testable without any infrastructure** — just pass in an `Order` instance and a message
- Business rules are co-located in one file, easy to reason about
- The saga's `Handle()` methods are so thin they rarely need tests beyond integration tests
- `DateTimeOffset.UtcNow` is injected as a parameter, so tests can control time

> **Reference:** [Decider Pattern (Jérémie Chassaing)](https://thinkbeforecoding.com/post/2021/12/17/functional-event-sourcing-decider)

## Multi-SKU Race Conditions

### The Problem

When an order contains multiple SKUs, the Inventory BC confirms each SKU's reservation separately via individual `ReservationConfirmed` messages. Inventory processing in parallel means these messages arrive in an unpredictable order — and so does the `PaymentCaptured` message.

A naive implementation of "when all reservations confirmed, commit them all" breaks when payment arrives *between* reservation confirmations:

```
Timeline:
  T1: ReservationConfirmed (SKU-001) → payment not yet captured, commit not sent
  T2: PaymentCaptured              → IsInventoryReserved=false, no commits sent yet
  T3: ReservationConfirmed (SKU-002) → payment already captured, commits SKU-002 only
  
Result: SKU-001 never gets a commit request → order stuck in InventoryReserved
```

### The Solution: `ExpectedReservationCount` + `CommittedReservationIds`

Track the full multi-SKU lifecycle with three fields on the saga:

```csharp
public sealed class Order : Saga
{
    // Set at saga start: count of distinct SKUs
    public int ExpectedReservationCount { get; set; }

    // Incremented when each per-SKU reservation is confirmed
    public int ConfirmedReservationCount { get; set; }

    // Tracks which specific reservation IDs have been committed
    // Derived count prevents drift: use CommittedReservationIds.Count, not a separate int
    public HashSet<Guid> CommittedReservationIds { get; set; } = [];
    public int CommittedReservationCount => CommittedReservationIds.Count; // Derived!

    // Tracks ReservationId → Sku for orchestration
    public Dictionary<Guid, string> ReservationIds { get; set; } = new();

    public bool IsInventoryReserved => ExpectedReservationCount > 0
        && ConfirmedReservationCount >= ExpectedReservationCount;

    public bool IsAllInventoryCommitted => ExpectedReservationCount > 0
        && CommittedReservationCount >= ExpectedReservationCount;
}
```

Set `ExpectedReservationCount` at saga creation:

```csharp
// In OrderDecider.Start():
var expectedReservationCount = command.LineItems
    .Select(li => li.Sku)
    .Distinct()
    .Count();

var saga = new Order
{
    ExpectedReservationCount = expectedReservationCount,
    // ...
};
```

### The Race-Condition Fix in `HandleReservationConfirmed`

When a reservation confirmation arrives and payment is already captured, commit **all confirmations so far** — not just the new one. This ensures previously confirmed SKUs don't get stranded if payment arrived between confirmation messages:

```csharp
public static OrderDecision HandleReservationConfirmed(
    Order current,
    ReservationConfirmed message,
    DateTimeOffset timestamp)
{
    // ... idempotency and terminal-state guards (see below) ...

    var newReservations = new Dictionary<Guid, string>(current.ReservationIds)
    {
        [message.ReservationId] = message.Sku
    };

    var messages = new List<object>();

    // KEY INSIGHT: If payment is already captured, commit ALL confirmed reservations.
    // Not just this new one — all of them. This handles the payment-between-confirmations race.
    // Inventory BC must handle duplicate commit requests idempotently.
    if (current.IsPaymentCaptured)
    {
        foreach (var reservationId in newReservations.Keys) // ALL, not just message.ReservationId
        {
            messages.Add(new ReservationCommitRequested(current.Id, reservationId, timestamp));
        }
    }

    var newConfirmedCount = current.ConfirmedReservationCount + 1;
    var allReserved = newConfirmedCount >= current.ExpectedReservationCount;

    return new OrderDecision
    {
        Status = allReserved ? OrderStatus.InventoryReserved : current.Status,
        ConfirmedReservationCount = newConfirmedCount,
        ReservationIds = newReservations,
        Messages = messages
    };
}
```

Because this re-issues commit requests for already-confirmed reservations, the downstream `HandleReservationCommitted` must guard against double-counting via `CommittedReservationIds`.

> **Reference:** [EIP Aggregator pattern](https://www.enterpriseintegrationpatterns.com/patterns/messaging/Aggregator.html) — collecting related messages before proceeding

## At-Least-Once Delivery and Idempotency

Wolverine (and any message bus) delivers messages **at least once** — under transient failures or restarts, a message may be redelivered. Every saga handler must be idempotent: processing the same message twice must produce the same observable outcome as processing it once.

### Guard 1: Duplicate Reservation Confirmation

**Problem:** A redelivered `ReservationConfirmed` for an already-tracked reservation would increment `ConfirmedReservationCount` a second time, making it exceed `ExpectedReservationCount` and triggering premature state transitions.

```csharp
public static OrderDecision HandleReservationConfirmed(
    Order current,
    ReservationConfirmed message,
    DateTimeOffset timestamp)
{
    // Guard: already tracking this reservation — idempotent no-op
    if (current.ReservationIds.ContainsKey(message.ReservationId))
        return new OrderDecision();

    // ... normal processing ...
}
```

### Guard 2: Duplicate Reservation Commitment

**Problem:** `HandleReservationConfirmed` (with `IsPaymentCaptured=true`) re-issues commit requests for all prior reservations to handle the race condition. This means `ReservationCommitted` messages arrive more than once per reservation. Without a guard, the saga would double-count and trigger fulfillment prematurely.

```csharp
public static OrderDecision HandleReservationCommitted(
    Order current,
    ReservationCommitted message,
    DateTimeOffset timestamp)
{
    // Guard: already processed this reservation's commit — idempotent no-op
    if (current.CommittedReservationIds.Contains(message.ReservationId))
        return new OrderDecision();

    var newCommittedIds = new HashSet<Guid>(current.CommittedReservationIds)
        { message.ReservationId };

    // ... normal processing ...
}
```

`HashSet<Guid>` is the right data structure here: O(1) lookup, naturally prevents duplicates, and serializes cleanly to JSON as an array.

### Guard 3: Duplicate Delivery Notification

**Problem:** A redelivered `ShipmentDelivered` would schedule a *second* `ReturnWindowExpired` message, extending the return window and potentially calling `MarkCompleted()` twice.

```csharp
public OutgoingMessages Handle(FulfillmentMessages.ShipmentDelivered message)
{
    // Guard: already delivered — do not schedule another ReturnWindowExpired
    if (Status is OrderStatus.Delivered or OrderStatus.Closed)
        return new OutgoingMessages();

    var decision = OrderDecider.HandleShipmentDelivered(this, message);
    if (decision.Status.HasValue) Status = decision.Status.Value;

    var outgoing = new OutgoingMessages();
    foreach (var msg in decision.Messages) outgoing.Add(msg);
    outgoing.Delay(new ReturnWindowExpired(Id), OrderDecider.ReturnWindowDuration);
    return outgoing;
}
```

### Guard 4: Terminal-State Protection

**Problem:** A late-arriving `ReservationConfirmed` on a cancelled or failed order — combined with `IsPaymentCaptured=true` — would issue spurious `ReservationCommitRequested` messages, directing Inventory to hard-allocate stock for an order that was already cancelled.

```csharp
public static OrderDecision HandleReservationConfirmed(
    Order current,
    ReservationConfirmed message,
    DateTimeOffset timestamp)
{
    // Terminal-state guard: never process confirmations for self-compensated orders
    if (current.Status is OrderStatus.Cancelled or OrderStatus.OutOfStock
        or OrderStatus.PaymentFailed or OrderStatus.Closed)
        return new OrderDecision();

    // ... continue with idempotency guard and normal processing ...
}
```

**Rule:** Late-arriving messages for terminal sagas must be silently ignored, not rejected. Rejecting (throwing) would cause the message bus to retry indefinitely.

## Scheduling Delayed Messages

Use `OutgoingMessages.Delay()` to schedule a future message that the same saga will process. Wolverine + Marten persists the scheduled delivery durably — it survives application restarts:

```csharp
public OutgoingMessages Handle(FulfillmentMessages.ShipmentDelivered message)
{
    // ... update status to Delivered ...

    var outgoing = new OutgoingMessages();
    // Schedule ReturnWindowExpired to fire after 30 days
    // The saga remains open and will process this message when it arrives
    outgoing.Delay(new ReturnWindowExpired(Id), OrderDecider.ReturnWindowDuration);
    return outgoing;
}

// 30 days later, Wolverine delivers this to the same saga
public void Handle(ReturnWindowExpired message)
{
    Status = OrderStatus.Closed;
    MarkCompleted();
}
```

**Why this works:**
- `Delay()` registers the message in Wolverine's durable outbox backed by Marten/PostgreSQL
- No in-memory timer — survives restarts, deployments, and scaling
- The `ReturnWindowExpired.OrderId` correlation property routes it back to the correct saga instance

**Keep duration constants in the Decider, not the saga:**

```csharp
public static class OrderDecider
{
    // Injected as parameter so tests can verify behavior without waiting 30 days
    public static readonly TimeSpan ReturnWindowDuration = TimeSpan.FromDays(30);
}
```

> **Reference:** [Wolverine Scheduled Messages](https://wolverinefx.net/guide/messaging/scheduled.html)

## Saga Lifecycle Completion

### Every Terminal Path Must Call `MarkCompleted()`

Wolverine deletes the saga document only when `MarkCompleted()` is explicitly called. Failing to call it leaves the saga in Marten forever — an orphaned document that wastes storage and confuses monitoring.

The Order saga has **four terminal paths**:

| Terminal Path | Handler | Completion Logic |
|--------------|---------|-----------------|
| Return window expires | `Handle(ReturnWindowExpired)` | Always |
| Cancelled with no payment | `Handle(CancelOrder)` | Immediately if `!IsPaymentCaptured` |
| Cancelled with payment | `Handle(RefundCompleted)` | After refund confirmed |
| OutOfStock with payment | `Handle(RefundCompleted)` | After refund confirmed |

### Path 1: Return Window (Happy Path)

```csharp
public void Handle(ReturnWindowExpired message)
{
    Status = OrderStatus.Closed;
    MarkCompleted(); // Simple — always complete here
}
```

### Path 2: Cancel Without Payment

When no payment was captured, there's no refund to wait for. Complete immediately:

```csharp
public OutgoingMessages Handle(CancelOrder command)
{
    if (!OrderDecider.CanBeCancelled(Status))
        return new OutgoingMessages();

    var decision = OrderDecider.HandleCancelOrder(this, command, DateTimeOffset.UtcNow);
    if (decision.Status.HasValue) Status = decision.Status.Value;

    var outgoing = new OutgoingMessages();
    foreach (var msg in decision.Messages) outgoing.Add(msg);

    // KEY: If no payment was captured, no RefundCompleted will ever arrive.
    // Close the saga now. Any late ReservationReleased messages will be silently
    // discarded by Wolverine (they cannot start a new Order saga).
    if (!IsPaymentCaptured)
        MarkCompleted();

    return outgoing;
}
```

### Path 3 & 4: Cancelled OR OutOfStock with Payment

Both states emit `RefundRequested` when payment was captured. A completed refund closes **both** paths:

```csharp
public void Handle(RefundCompleted message)
{
    var decision = OrderDecider.HandleRefundCompleted(this, message);
    if (decision.Status.HasValue) Status = decision.Status.Value;
    if (decision.ShouldComplete) MarkCompleted();
}

// In OrderDecider:
public static OrderDecision HandleRefundCompleted(Order current, RefundCompleted message)
{
    // BOTH Cancelled and OutOfStock need this handler.
    // The OutOfStock path is easy to forget — it was a real bug before being fixed.
    if (current.Status is OrderStatus.Cancelled or OrderStatus.OutOfStock)
    {
        return new OrderDecision
        {
            Status = OrderStatus.Closed,
            ShouldComplete = true
        };
    }

    return new OrderDecision(); // No-op for other statuses
}
```

**The OutOfStock bug:** If `HandleRefundCompleted` only checked for `Cancelled`, `OutOfStock` orders with a captured payment would receive a `RefundCompleted` from Payments BC but the saga would never call `MarkCompleted()` — orphaned indefinitely.

> **Reference:** [Wolverine Saga Persistence](https://wolverinefx.net/guide/durability/marten/sagas.html)

## Shared Guard: `CanBeCancelled()`

The cancellation eligibility rule is shared between the HTTP endpoint (pre-flight validation) and the saga handler (idempotency/correctness). Keeping it in the Decider enforces consistency:

```csharp
// In OrderDecider — single source of truth
public static bool CanBeCancelled(OrderStatus status) =>
    status is not (OrderStatus.Shipped or OrderStatus.Delivered
        or OrderStatus.Closed or OrderStatus.Cancelled
        or OrderStatus.OutOfStock or OrderStatus.PaymentFailed);
```

```csharp
// HTTP endpoint — pre-flight check
public static async Task<IResult> Handle(Guid orderId, CancelOrderRequest request, ...)
{
    var order = await querySession.LoadAsync<Order>(orderId);

    if (!OrderDecider.CanBeCancelled(order.Status))
        return Results.Conflict(new ProblemDetails { Detail = "Cannot cancel at this stage", Status = 409 });

    await bus.PublishAsync(new CancelOrder(orderId, request.Reason));
    return Results.Accepted();
}

// Saga handler — idempotency guard (message bus may redeliver)
public OutgoingMessages Handle(CancelOrder command)
{
    if (!OrderDecider.CanBeCancelled(Status))
        return new OutgoingMessages(); // Silently ignore
    // ...
}
```

**Why the saga also checks:** The HTTP endpoint validates before publishing, but Wolverine's at-least-once delivery means the handler may process the same `CancelOrder` message twice. Both guards are necessary.

## File Organization

Saga files should be colocated in a single feature folder:

```
src/Orders/Orders/Placement/
  Order.cs             # Saga class — state properties + thin Handle() adapters
  OrderDecider.cs      # Pure business logic + OrderDecision record
  OrderStatus.cs       # Enum — all lifecycle states
  OrderLineItem.cs     # Value object
  PlaceOrder.cs        # Initialization command + PlaceOrderValidator (NOTE: see pitfalls)
  PlaceOrderHandler.cs # Saga start handler (returns (Order, OrderPlaced))
  CancelOrder.cs       # Cancellation command + validator
  ReturnWindowExpired.cs # Scheduled message
  OrderPlaced.cs       # Integration event published to other BCs
```

The handler (`PlaceOrderHandler`) lives in a separate file from the saga (`Order`) because they serve different purposes: the handler creates the saga, the saga evolves it.

## Common Pitfalls

### ❌ Pitfall 1: FluentValidation Dead Code on Internal Commands

```csharp
// ⚠️ This validator runs ONLY for messages dispatched through the Wolverine bus
// PlaceOrder is constructed internally in PlaceOrderHandler and passed directly to OrderDecider.Start()
// — it never goes through the bus, so this validator NEVER executes.
public sealed record PlaceOrder(...)
{
    public class PlaceOrderValidator : AbstractValidator<PlaceOrder>
    {
        public PlaceOrderValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty(); // Dead code!
        }
    }
}
```

**Fix:** Remove validators from internally-constructed commands. Validation should happen at the integration message boundary (i.e., on `CheckoutCompleted` in the upstream Shopping BC), not on internal domain commands.

### ❌ Pitfall 2: Derived Count as Stored Property

```csharp
// ⚠️ If CommittedReservationCount is a stored int and CommittedReservationIds is also a set,
// they can drift: a bug in one handler updates one but not the other.
public int CommittedReservationCount { get; set; }          // Stored — drifts
public HashSet<Guid> CommittedReservationIds { get; set; }  // Authoritative

// ✅ CORRECT: derive from the authoritative collection
public int CommittedReservationCount => CommittedReservationIds.Count; // Computed, never stored
```

The computed property pattern makes the single source of truth obvious and eliminates an entire class of consistency bugs.

### ❌ Pitfall 3: `IncludeType<T>()` Instead of `IncludeAssembly()`

```csharp
// ❌ WRONG — only discovers Order saga handlers, misses PlaceOrderHandler
opts.Discovery.IncludeType<Order>();

// ✅ CORRECT — discovers all handlers in the Orders domain assembly
opts.Discovery.IncludeAssembly(typeof(Order).Assembly);
```

Symptoms: `CheckoutCompleted` messages are consumed from the queue, but no Order saga is created; no error is logged.

### ❌ Pitfall 4: Orphaned Sagas on `OutOfStock` Path

The `OutOfStock` terminal state is easy to overlook because it was originally named `InventoryFailed` (before renaming) and doesn't obviously need a refund handler. But if payment was captured before inventory failed, the saga will receive `RefundCompleted` from Payments BC — and without a handler, it's stuck forever.

```csharp
// ❌ WRONG — handles only Cancelled, leaves OutOfStock sagas orphaned
if (current.Status is OrderStatus.Cancelled)
    return new OrderDecision { Status = OrderStatus.Closed, ShouldComplete = true };

// ✅ CORRECT — handles both terminal states that trigger RefundRequested
if (current.Status is OrderStatus.Cancelled or OrderStatus.OutOfStock)
    return new OrderDecision { Status = OrderStatus.Closed, ShouldComplete = true };
```

### ❌ Pitfall 5: `IStartStream` for Document-Based Sagas

```csharp
// ❌ WRONG — IStartStream creates an event-sourced stream, not a saga document
public static (IStartStream, OrderPlaced) Handle(CheckoutCompleted msg)
{
    return (MartenOps.StartStream<Order>(msg.OrderId, new OrderCreated()), new OrderPlaced(...));
}

// ✅ CORRECT — return the saga instance; Wolverine persists it as a Marten document
public static (Order, OrderPlaced) Handle(CheckoutCompleted msg)
{
    var order = new Order { Id = msg.OrderId, ... };
    return (order, new OrderPlaced(...));
}
```

### ❌ Pitfall 6: Processing Late Messages in Terminal States

```csharp
// ❌ WRONG — no terminal-state guard
// If a late ReservationConfirmed arrives after cancellation with IsPaymentCaptured=true,
// this issues a spurious ReservationCommitRequested for a cancelled order
public static OrderDecision HandleReservationConfirmed(Order current, ReservationConfirmed msg, ...)
{
    if (current.ReservationIds.ContainsKey(msg.ReservationId)) return new OrderDecision();
    // ... continues to check IsPaymentCaptured, issues commit requests ...
}

// ✅ CORRECT — check terminal states first
public static OrderDecision HandleReservationConfirmed(Order current, ReservationConfirmed msg, ...)
{
    if (current.Status is OrderStatus.Cancelled or OrderStatus.OutOfStock
        or OrderStatus.PaymentFailed or OrderStatus.Closed)
        return new OrderDecision(); // Silently ignore — don't throw

    if (current.ReservationIds.ContainsKey(msg.ReservationId)) return new OrderDecision();
    // ...
}
```

## Testing Sagas

Test sagas at two levels:

**Unit tests (fast, no infrastructure):** Test `OrderDecider` pure functions directly. Given an `Order` state + an incoming message, assert the `OrderDecision` returned.

```csharp
[Fact]
public void HandlePaymentCaptured_WhenInventoryAlreadyReserved_IssuesCommitRequests()
{
    var order = new Order
    {
        Id = Guid.NewGuid(),
        Status = OrderStatus.InventoryReserved,
        ExpectedReservationCount = 2,
        ConfirmedReservationCount = 2,
        IsPaymentCaptured = false,
        ReservationIds = new Dictionary<Guid, string>
        {
            [Guid.NewGuid()] = "SKU-001",
            [Guid.NewGuid()] = "SKU-002"
        }
    };

    var decision = OrderDecider.HandlePaymentCaptured(order, new PaymentCaptured(...), DateTimeOffset.UtcNow);

    decision.Status.ShouldBe(OrderStatus.PaymentConfirmed);
    decision.IsPaymentCaptured.ShouldBe(true);
    decision.Messages.Count.ShouldBe(2); // One commit per reservation
}
```

**Integration tests (Alba + Wolverine + Marten):** Test the full saga lifecycle via message dispatch and state queries. See `docs/skills/critterstack-testing-patterns.md` for the `ExecuteAndWaitAsync` pattern.

```csharp
[Fact]
public async Task Order_ReachesPaymentConfirmed_WhenPaymentCapturedAfterInventoryReserved()
{
    // 1. Start the saga
    await _fixture.ExecuteAndWaitAsync(TestFixture.CreateCheckoutCompletedMessage(...));

    // 2. Simulate Inventory BC confirming all reservations
    var order = await _fixture.LoadOrderByCustomer(customerId);
    await _fixture.ExecuteAndWaitAsync(new ReservationConfirmed(Guid.NewGuid(), order.Id, "SKU-001"));

    // 3. Simulate Payments BC capturing payment
    await _fixture.ExecuteAndWaitAsync(new PaymentCaptured(Guid.NewGuid(), order.Id, 89.97m, ...));

    // 4. Assert saga reached expected state
    var updated = await _fixture.LoadOrder(order.Id);
    updated.Status.ShouldBe(OrderStatus.PaymentConfirmed);
}
```

> **References:**
> - [Wolverine Saga Testing](https://wolverinefx.net/guide/durability/marten/sagas.html)
> - [`docs/skills/critterstack-testing-patterns.md`](./critterstack-testing-patterns.md)

## Quick Reference

### Saga Checklist

- [ ] Inherits from `Saga` with `public Guid Id { get; set; }`
- [ ] Integration messages have `{SagaName}Id` property for correlation
- [ ] Saga started via separate `PlaceOrderHandler` returning `(Order, ...)`
- [ ] `IncludeAssembly(typeof(Order).Assembly)` in Program.cs (not `IncludeType<T>()`)
- [ ] `[assembly: WolverineModule]` in domain assembly's `AssemblyAttributes.cs`
- [ ] Marten configured with `.Identity(x => x.Id).UseNumericRevisions(true)`
- [ ] `ConcurrencyException` retry policy configured
- [ ] Business logic in static `Decider` class (pure functions)
- [ ] Every terminal path calls `MarkCompleted()`
- [ ] Idempotency guards on all handlers (HashSet for committed IDs, ContainsKey for reservations)
- [ ] Terminal-state guard at top of `HandleReservationConfirmed` (and any handler that issues compensation)
- [ ] `ShipmentDelivered` handler checks status before scheduling `ReturnWindowExpired`
- [ ] `HandleRefundCompleted` handles BOTH `Cancelled` AND `OutOfStock`
- [ ] `CancelOrder` calls `MarkCompleted()` immediately when `!IsPaymentCaptured`
- [ ] Derived counts (`CommittedReservationCount`) computed from authoritative collections, not stored separately
- [ ] `CanBeCancelled()` shared between HTTP endpoint and saga handler
