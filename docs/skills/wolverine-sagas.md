# Wolverine Sagas

Patterns and practices for building stateful orchestration sagas with Wolverine + Marten in CritterSupply.

---

## Table of Contents

1. [Core Principle: Sagas Are Mutable State Machines](#core-principle-sagas-are-mutable-state-machines)
2. [When to Use a Saga vs. Other Patterns](#when-to-use-a-saga-vs-other-patterns)
   - [Event-Sourced Aggregate vs. Document-Based Saga](#event-sourced-aggregate-vs-document-based-saga)
3. [The Wolverine Saga API](#the-wolverine-saga-api)
   - [The `Saga` Base Class](#the-saga-base-class)
   - [Saga Identity and Message Correlation](#saga-identity-and-message-correlation)
   - [`MarkCompleted()`](#markcompleted)
4. [Starting a Saga](#starting-a-saga)
5. [Handler Discovery: `IncludeAssembly` vs. `IncludeType`](#handler-discovery-includeassembly-vs-includetype)
6. [Marten Document Configuration for Sagas](#marten-document-configuration-for-sagas)
   - [Optimistic Concurrency with `ConcurrencyException`](#optimistic-concurrency-with-concurrencyexception)
7. [The Decider Pattern for Saga Business Logic](#the-decider-pattern-for-saga-business-logic)
8. [Multi-SKU Race Conditions](#multi-sku-race-conditions)
9. [At-Least-Once Delivery and Idempotency](#at-least-once-delivery-and-idempotency)
10. [Scheduling Delayed Messages](#scheduling-delayed-messages)
11. [Advanced Patterns for Ordered Workloads](#advanced-patterns-for-ordered-workloads)
12. [Saga Lifecycle Completion](#saga-lifecycle-completion)
13. [Return Processing — Active Return Tracking](#return-processing--active-return-tracking) ⭐ *M32-M34 Addition*
14. [Shared Guard: `CanBeCancelled()`](#shared-guard-canbecancelled)
15. [DOs and DO NOTs](#dos-and-do-nots) ⭐ *M32-M34 Addition*
16. [File Organization](#file-organization)
17. [Common Pitfalls](#common-pitfalls)
18. [Testing Sagas](#testing-sagas)
19. [Quick Reference](#quick-reference)

---

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
    public Guid? PaymentId { get; init; }
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
        if (decision.PaymentId.HasValue) PaymentId = decision.PaymentId.Value;

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

## Advanced Patterns for Ordered Workloads

These are awareness notes, not default guidance. Reach for them only when ordering guarantees become the actual problem.

### Re-Sequencer Saga

If related messages may arrive out of order, Wolverine's `ResequencerSaga<T>` can buffer gaps and replay messages in sequence once the missing order number arrives. Use it when message types in the workflow can implement `SequencedMessage` and ordering bugs would otherwise force custom buffering logic. Reference: <https://wolverinefx.io/guide/durability/sagas.html#resequencer-saga>.

### Global Partitioning

If a saga workload needs cluster-wide sequential processing within each message group, `UseInferredMessageGrouping()` plus `GlobalPartitioned()` spreads work across sharded queues while preserving ordering per group. This is Wolverine's advanced answer for scaling ordered saga traffic without falling back to pessimistic locking. Reference: <https://wolverinefx.io/guide/messaging/partitioning.html#global-partitioning>.

## Saga Lifecycle Completion

### Every Terminal Path Must Call `MarkCompleted()`

Wolverine deletes the saga document only when `MarkCompleted()` is explicitly called. Failing to call it leaves the saga in Marten forever — an orphaned document that wastes storage and confuses monitoring.

The Order saga has **six terminal paths** (increased from 4 with the addition of return processing):

| Terminal Path | Handler | Completion Logic |
|--------------|---------|-----------------|
| Return window expires (no active returns) | `Handle(ReturnWindowExpired)` | If `ActiveReturnIds.Count == 0` |
| Return window already expired + last return resolved | `Handle(ReturnCompleted/Denied/Rejected/Expired)` | If `ActiveReturnIds.Count == 0 && ReturnWindowFired` |
| Cancelled with no payment | `Handle(CancelOrder)` | Immediately if `!IsPaymentCaptured` |
| Cancelled with payment | `Handle(RefundCompleted)` | After refund confirmed |
| OutOfStock with payment | `Handle(RefundCompleted)` | After refund confirmed |
| PaymentFailed | `Handle(PaymentFailed)` | After compensation (release reservations) |

### Path 1: Return Window (Happy Path — No Active Returns)

```csharp
public void Handle(ReturnWindowExpired message)
{
    ReturnWindowFired = true;
    if (ActiveReturnIds.Count > 0) return; // Stay open; return completion will close the saga
    Status = OrderStatus.Closed;
    MarkCompleted();
}
```

**Key change from earlier implementation:** The return window handler no longer unconditionally closes the saga. If returns are in progress, it sets `ReturnWindowFired = true` and keeps the saga alive. Each return-terminal handler (`ReturnCompleted`, `ReturnDenied`, `ReturnRejected`, `ReturnExpired`) checks `ActiveReturnIds.Count == 0 && ReturnWindowFired` to close the saga when the last return resolves. See [Return Processing](#return-processing--active-return-tracking) for details.

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

## Return Processing — Active Return Tracking

⭐ *M32-M34 Addition*

The Order saga stays open after delivery to handle customer returns. This creates a coordination problem: `ReturnWindowExpired` fires on a timer (30 days after delivery), but a return may already be in progress. The saga must not close until all active returns have resolved.

### The Coordination Fields

```csharp
public sealed class Order : Saga
{
    // ... existing fields ...

    /// <summary>
    /// List of active return IDs currently in progress.
    /// Prevents premature saga closure when ReturnWindowExpired fires.
    /// Supports multiple concurrent returns from the same order.
    /// </summary>
    public IReadOnlyList<Guid> ActiveReturnIds { get; set; } = [];

    /// <summary>
    /// True if the ReturnWindowExpired message has already fired.
    /// Used to close the saga when all returns eventually complete after window expiry.
    /// </summary>
    public bool ReturnWindowFired { get; set; }

    /// <summary>
    /// The delivery timestamp from Fulfillment BC.
    /// Used by the BFF for "Return by {date}" display.
    /// </summary>
    public DateTimeOffset? DeliveredAt { get; set; }
}
```

### The Return Lifecycle Handlers

The saga handles 5 return messages from the Returns BC. All follow the same immutable-update pattern for `ActiveReturnIds`:

```csharp
// 1. ReturnRequested — add to active list
public void Handle(Messages.Contracts.Returns.ReturnRequested message)
{
    var activeReturns = ActiveReturnIds.ToList();
    if (!activeReturns.Contains(message.ReturnId))
    {
        activeReturns.Add(message.ReturnId);
        ActiveReturnIds = activeReturns.AsReadOnly();
    }
}

// 2. ReturnCompleted — remove from active list, request refund, maybe close
public OutgoingMessages Handle(Messages.Contracts.Returns.ReturnCompleted message)
{
    var activeReturns = ActiveReturnIds.ToList();
    activeReturns.Remove(message.ReturnId);
    ActiveReturnIds = activeReturns.AsReadOnly();

    var outgoing = new OutgoingMessages();

    if (message.FinalRefundAmount > 0m)
    {
        outgoing.Add(new RefundRequested(
            Id, message.FinalRefundAmount,
            "Customer return approved and completed",
            DateTimeOffset.UtcNow));
    }

    // Close saga if: no active returns remaining AND return window already expired
    if (ActiveReturnIds.Count == 0 && ReturnWindowFired)
    {
        Status = OrderStatus.Closed;
        MarkCompleted();
    }

    return outgoing;
}

// 3-5. ReturnDenied, ReturnRejected, ReturnExpired — remove from active list, maybe close
public void Handle(Messages.Contracts.Returns.ReturnDenied message)
{
    var activeReturns = ActiveReturnIds.ToList();
    activeReturns.Remove(message.ReturnId);
    ActiveReturnIds = activeReturns.AsReadOnly();

    if (ActiveReturnIds.Count == 0 && ReturnWindowFired)
    {
        Status = OrderStatus.Closed;
        MarkCompleted();
    }
}
// ReturnRejected and ReturnExpired follow the identical pattern
```

### The Closure Logic

Two conditions must both be true for the saga to close via the return path:

1. **`ActiveReturnIds.Count == 0`** — all returns have resolved (completed, denied, rejected, or expired)
2. **`ReturnWindowFired == true`** — the 30-day `ReturnWindowExpired` timer has already fired

**Why both conditions?** Consider these scenarios:

| Scenario | ReturnWindowFired | ActiveReturnIds | Saga Action |
|----------|-------------------|-----------------|-------------|
| Window expires, no returns ever filed | `true` | empty | ✅ Close immediately |
| Window expires while return in progress | `true` | non-empty | ⏳ Stay open |
| Return completes before window | `false` | empty | ⏳ Stay open (window hasn't fired yet) |
| Last return completes after window | `true` | empty | ✅ Close now |

### Immutable Update Pattern

`ActiveReturnIds` is `IReadOnlyList<Guid>`, not `List<Guid>`. The saga modifies it using `.ToList()` → mutate → `.AsReadOnly()`. This ensures Marten detects the change (a new reference is assigned) and serializes it correctly.

```csharp
// ✅ CORRECT — creates new list reference; Marten serializes the change
var activeReturns = ActiveReturnIds.ToList();
activeReturns.Remove(message.ReturnId);
ActiveReturnIds = activeReturns.AsReadOnly();

// ❌ WRONG — if ActiveReturnIds were List<Guid>, removing an element
// would not change the reference, and Marten might not detect the change
ActiveReturnIds.Remove(message.ReturnId); // Compiler error: IReadOnlyList has no Remove
```

## Shared Guard: `CanBeCancelled()`

The cancellation eligibility rule is shared between the HTTP endpoint (pre-flight validation) and the saga handler (idempotency/correctness). Keeping it in the Decider enforces consistency:

```csharp
// In OrderDecider — single source of truth
// M41.0 S4: Shipped removed from exclusion list.
// FulfillmentCancelled can arrive pre-handoff (order is Shipped but carrier hasn't
// taken possession yet). DeliveryFailed, Reshipping, and Backordered are also
// cancellable — they are non-terminal fulfillment states, not post-delivery.
public static bool CanBeCancelled(OrderStatus status) =>
    status is not (OrderStatus.Delivered or OrderStatus.Closed
        or OrderStatus.Cancelled or OrderStatus.OutOfStock
        or OrderStatus.PaymentFailed);
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

## New OrderStatus Values and Saga Properties (M41.0 S4)

⭐ *M41.0 S4 Addition*

The Fulfillment Remaster introduced new lifecycle states and saga properties when the Orders
saga was migrated to the new Fulfillment contract surface.

### New OrderStatus Values

```csharp
/// <summary>Carrier returned the shipment (failed delivery attempts exhausted)</summary>
DeliveryFailed,

/// <summary>Replacement shipment created; awaiting carrier handoff</summary>
Reshipping,

/// <summary>Item(s) backordered; fulfillment deferred until restock</summary>
Backordered,
```

### New Saga Properties

```csharp
/// <summary>Carrier tracking number (set by TrackingNumberAssigned)</summary>
public string? TrackingNumber { get; set; }

/// <summary>Number of shipments in a split order (default 1)</summary>
public int ShipmentCount { get; set; } = 1;

/// <summary>Shipment ID of the active reshipment (set by ReshipmentCreated)</summary>
public Guid? ActiveReshipmentShipmentId { get; set; }
```

### Pattern for Adding Nullable OrderDecision Fields

When new messages introduce new saga state, follow the consistent nullable field pattern:

**1. Add the field to `OrderDecision`:**
```csharp
public sealed record OrderDecision
{
    // ... existing fields ...
    public string? TrackingNumber { get; init; }
    public Guid? ActiveReshipmentShipmentId { get; init; }
    public int? ShipmentCount { get; init; }
}
```

**2. Return the new state from the Decider pure function:**
```csharp
public static OrderDecision HandleTrackingNumberAssigned(
    Order current,
    FulfillmentMessages.TrackingNumberAssigned message)
{
    return new OrderDecision { TrackingNumber = message.TrackingNumber };
}
```

**3. Apply in the saga handler with a null check:**
```csharp
public void Handle(FulfillmentMessages.TrackingNumberAssigned message)
{
    var decision = OrderDecider.HandleTrackingNumberAssigned(this, message);
    if (decision.TrackingNumber != null) TrackingNumber = decision.TrackingNumber;
}
```

The null check (`if (decision.Field != null) Field = decision.Field`) preserves the existing
value when the Decider signals no change. This pattern is consistent throughout the entire
Order saga.

---

## ⚠️ Non-Terminal Mid-Lifecycle States and Idempotency Guards

⭐ *M41.0 S4 Critical Discovery*

When a saga can cycle through states (e.g., `DeliveryFailed` → `Reshipping` → `Shipped` →
`Delivered` for a reshipment scenario), be careful about which statuses appear in idempotency
guards for the events that drive those transitions.

**The Reshipping trap:** `ShipmentHandedToCarrier` transitions the Order saga to `Shipped`.
A naive idempotency guard might exclude `Reshipping`:

```csharp
// ❌ WRONG — this permanently traps the saga in Reshipping
public static OrderDecision HandleShipmentHandedToCarrier(
    Order current,
    FulfillmentMessages.ShipmentHandedToCarrier message)
{
    if (current.Status is OrderStatus.Shipped or OrderStatus.Delivered
        or OrderStatus.Closed or OrderStatus.Reshipping) // ← DO NOT include Reshipping
        return new OrderDecision();

    return new OrderDecision { Status = OrderStatus.Shipped };
}
```

**Why this is wrong:** The reshipment lifecycle requires:
1. `ReturnToSenderInitiated` → `DeliveryFailed`
2. `ReshipmentCreated` → `Reshipping`
3. `ShipmentHandedToCarrier` → **must transition to `Shipped`**

If `Reshipping` is in the guard for step 3, the saga can never escape that state.

**Rule:** Only include truly terminal states (or states where the event is genuinely a
duplicate) in idempotency guards. `Reshipping` is a non-terminal mid-lifecycle state that
**must** be able to receive `ShipmentHandedToCarrier` to make forward progress.

**Verify with the full lifecycle test:**

```csharp
[Fact]
public async Task Full_Lifecycle_With_Reshipment()
{
    // Place → inventory → payment → FulfillmentRequested
    // → ShipmentHandedToCarrier (Shipped)
    // → ReturnToSenderInitiated (DeliveryFailed)
    // → ReshipmentCreated (Reshipping)
    // → ShipmentHandedToCarrier again (Shipped) ← the critical transition
    // → ShipmentDelivered (Delivered)
}
```

This test only passes if `Reshipping` is NOT in the `ShipmentHandedToCarrier` guard.

## DOs and DO NOTs

⭐ *M32-M34 Addition*

### ✅ DOs

| # | Rule | Rationale |
|---|------|-----------|
| 1 | **DO** inherit from `Wolverine.Saga` with `public Guid Id { get; set; }` | Wolverine correlation requires this exact property |
| 2 | **DO** name the correlation property `{SagaTypeName}Id` on all messages | Convention-based routing — no configuration needed |
| 3 | **DO** start sagas via a separate handler class returning `(SagaType, ...)` | Separates construction from state evolution |
| 4 | **DO** use `IncludeAssembly(typeof(Order).Assembly)` for handler discovery | Discovers all handlers, not just saga methods |
| 5 | **DO** configure `UseNumericRevisions(true)` for saga documents | Enables optimistic concurrency |
| 6 | **DO** configure `ConcurrencyException` retry policy | Prevents silent data loss on concurrent updates |
| 7 | **DO** extract business logic to a static Decider class with pure functions | Enables unit testing without infrastructure |
| 8 | **DO** call `MarkCompleted()` on every terminal path | Prevents orphaned saga documents |
| 9 | **DO** add idempotency guards on all handlers (check for duplicate message IDs) | At-least-once delivery means redelivery happens |
| 10 | **DO** add terminal-state guards at the top of handlers that issue compensation | Late-arriving messages must not trigger spurious commands |
| 11 | **DO** use `IReadOnlyList<Guid>` with immutable update pattern for tracking lists | Ensures Marten detects reference changes and serializes correctly |
| 12 | **DO** derive counts from authoritative collections (`CommittedReservationIds.Count`) | Prevents drift between stored count and actual set size |
| 13 | **DO** share cancellation eligibility rules between HTTP endpoint and saga handler | Both paths need the same business rule |
| 14 | **DO** use `OutgoingMessages.Delay()` for scheduled messages (not in-memory timers) | Survives restarts, deployments, and scaling |
| 15 | **DO** track `ReturnWindowFired` + `ActiveReturnIds` for post-delivery return coordination | Prevents premature saga closure while returns are in progress |
| 16 | **DO** be careful about non-terminal mid-lifecycle states in idempotency guards | States like `Reshipping` must be able to receive the event that advances them (e.g., `ShipmentHandedToCarrier`); including them in guards permanently traps the saga |

### ❌ DO NOTs

| # | Rule | Consequence |
|---|------|-------------|
| 1 | **DO NOT** use `IStartStream` / `MartenOps.StartStream<T>()` for sagas | Creates an event-sourced stream, not a saga document |
| 2 | **DO NOT** use `IncludeType<T>()` for handler discovery | Misses `PlaceOrderHandler` and other handler classes |
| 3 | **DO NOT** put FluentValidation on internally-constructed commands | Validators only fire for bus-dispatched messages; dead code |
| 4 | **DO NOT** store derived counts as separate properties | They drift from the authoritative collection |
| 5 | **DO NOT** throw exceptions for late-arriving messages in terminal states | Causes infinite retry; silently return instead |
| 6 | **DO NOT** forget `OutOfStock` in `HandleRefundCompleted` | OutOfStock orders with payment also receive `RefundCompleted` |
| 7 | **DO NOT** unconditionally close the saga in `Handle(ReturnWindowExpired)` | Returns may be in progress; check `ActiveReturnIds.Count` first |
| 8 | **DO NOT** use `List<Guid>` for saga tracking fields | Use `IReadOnlyList<Guid>` + immutable update pattern for Marten change detection |
| 9 | **DO NOT** mix `IMessageBus.InvokeAsync()` with manual `session.Events.Append()` | Two competing persistence strategies — one silently loses |
| 10 | **DO NOT** put initialization logic in the saga class | Use a separate `PlaceOrderHandler` class |
| 11 | **DO NOT** include non-terminal mid-lifecycle states (like `Reshipping`) in idempotency guards for the event that advances them | The saga will be permanently stuck; the full reshipment lifecycle test will fail |

## File Organization

Saga files should be colocated in a single feature folder:

```
src/Orders/Orders/Placement/
  Order.cs               # Saga class — state properties + thin Handle() adapters
  OrderDecider.cs        # Pure business logic + OrderDecision record
  OrderStatus.cs         # Enum — all lifecycle states
  OrderLineItem.cs       # Value object
  ShippingAddress.cs     # Value object (snapshot)
  AppliedDiscount.cs     # Value object
  CheckoutLineItem.cs    # Value object (input from Shopping BC)
  PlaceOrder.cs          # Initialization command + PlaceOrderValidator (NOTE: see pitfalls)
  PlaceOrderHandler.cs   # Saga start handler (returns (Order, OrderPlaced))
  CancelOrder.cs         # Cancellation command + validator
  ReturnWindowExpired.cs # Scheduled message (saga-internal)
  OrderPlaced.cs         # Integration event published to other BCs
  OrderResponse.cs       # API response DTO
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

### ❌ Pitfall 7: Unconditional Close in `ReturnWindowExpired` ⭐ *M32-M34 Addition*

```csharp
// ❌ WRONG — closes the saga even when a return is in progress
public void Handle(ReturnWindowExpired message)
{
    Status = OrderStatus.Closed;
    MarkCompleted(); // Customer's active return is now orphaned!
}

// ✅ CORRECT — check for active returns before closing
public void Handle(ReturnWindowExpired message)
{
    ReturnWindowFired = true;
    if (ActiveReturnIds.Count > 0) return; // Stay open
    Status = OrderStatus.Closed;
    MarkCompleted();
}
```

Premature saga closure means the `ReturnCompleted` message from Returns BC finds no saga to route to — the refund is never requested.

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

**Infrastructure:**
- [ ] Inherits from `Saga` with `public Guid Id { get; set; }`
- [ ] Integration messages have `{SagaName}Id` property for correlation
- [ ] Saga started via separate handler returning `(SagaType, ...)`
- [ ] `IncludeAssembly(typeof(Order).Assembly)` in Program.cs (not `IncludeType<T>()`)
- [ ] `[assembly: WolverineModule]` in domain assembly's `AssemblyAttributes.cs`
- [ ] Marten configured with `.Identity(x => x.Id).UseNumericRevisions(true)`
- [ ] `ConcurrencyException` retry policy configured

**Business Logic:**
- [ ] Business logic in static `Decider` class (pure functions)
- [ ] `OrderDecision` record carries nullable state changes + messages
- [ ] `CanBeCancelled()` shared between HTTP endpoint and saga handler
- [ ] Derived counts computed from authoritative collections, not stored separately

**Idempotency & Safety:**
- [ ] Idempotency guards on all handlers (HashSet for committed IDs, ContainsKey for reservations)
- [ ] Terminal-state guard at top of handlers that issue compensation messages
- [ ] `ShipmentDelivered` handler checks status before scheduling `ReturnWindowExpired`
- [ ] `HandleRefundCompleted` handles BOTH `Cancelled` AND `OutOfStock`

**Lifecycle Completion:**
- [ ] Every terminal path calls `MarkCompleted()`
- [ ] `CancelOrder` calls `MarkCompleted()` immediately when `!IsPaymentCaptured`
- [ ] `ReturnWindowExpired` checks `ActiveReturnIds.Count` before closing
- [ ] Return-terminal handlers check `ActiveReturnIds.Count == 0 && ReturnWindowFired`
- [ ] `ActiveReturnIds` uses `IReadOnlyList<Guid>` with immutable update pattern

### Handler Method Summary (Current Implementation)

| Handler | Message Source | Returns | Key Behavior |
|---------|--------------|---------|-------------|
| `Handle(CancelOrder)` | Orders API | `OutgoingMessages` | Compensation + conditional `MarkCompleted()` |
| `Handle(PaymentCaptured)` | Payments BC | `OutgoingMessages` | Tracks `PaymentId`; commits if inventory ready |
| `Handle(PaymentFailed)` | Payments BC | `OutgoingMessages` | Releases reservations |
| `Handle(PaymentAuthorized)` | Payments BC | `void` | Sets `PendingPayment` status |
| `Handle(RefundCompleted)` | Payments BC | `void` | Closes `Cancelled` / `OutOfStock` sagas |
| `Handle(RefundFailed)` | Payments BC | `void` | Logs; no status change |
| `Handle(ReservationConfirmed)` | Inventory BC | `OutgoingMessages` | Multi-SKU tracking; race-condition fix |
| `Handle(ReservationFailed)` | Inventory BC | `OutgoingMessages` | `OutOfStock` compensation |
| `Handle(ReservationCommitted)` | Inventory BC | `OutgoingMessages` | Fulfillment request when all committed |
| `Handle(ReservationReleased)` | Inventory BC | `void` | Compensation acknowledgement |
| `Handle(ShipmentHandedToCarrier)` | Fulfillment BC | `void` | `Shipped` status *(replaces ShipmentDispatched — M41.0 S4)* |
| `Handle(TrackingNumberAssigned)` | Fulfillment BC | `void` | Stores tracking number; no status change |
| `Handle(ShipmentDelivered)` | Fulfillment BC | `OutgoingMessages` | `Delivered` + schedules return window |
| `Handle(ReturnToSenderInitiated)` | Fulfillment BC | `void` | `DeliveryFailed` status *(replaces ShipmentDeliveryFailed — M41.0 S4)* |
| `Handle(ReshipmentCreated)` | Fulfillment BC | `void` | `Reshipping` status; stores `ActiveReshipmentShipmentId` |
| `Handle(BackorderCreated)` | Fulfillment BC | `void` | `Backordered` status |
| `Handle(FulfillmentCancelled)` | Fulfillment BC | `OutgoingMessages` | Refund + release inventory + `Cancelled` |
| `Handle(OrderSplitIntoShipments)` | Fulfillment BC | `void` | Stores `ShipmentCount`; no status change |
| `Handle(ReturnWindowExpired)` | Scheduled | `void` | Closes if no active returns |
| `Handle(ReturnRequested)` | Returns BC | `void` | Adds to `ActiveReturnIds` |
| `Handle(ReturnCompleted)` | Returns BC | `OutgoingMessages` | Refund request + conditional close |
| `Handle(ReturnDenied)` | Returns BC | `void` | Removes from active + conditional close |
| `Handle(ReturnRejected)` | Returns BC | `void` | Removes from active + conditional close |
| `Handle(ReturnExpired)` | Returns BC | `void` | Removes from active + conditional close |
