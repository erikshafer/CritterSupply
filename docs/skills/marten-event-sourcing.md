# Marten Event Sourcing

Patterns for building event-sourced aggregates with Marten in CritterSupply.

## Core Principles

1. **Aggregates are immutable records** — No mutable state, use `with` expressions
2. **Pure functions for Apply methods** — Events transform state without side effects
3. **Decider pattern via Wolverine** — Business decisions live in handlers, not aggregates
4. **No base classes** — No `Aggregate` base class or `IEntity` interface

## Event-Sourced Aggregate Structure

```csharp
public sealed record Payment(
    Guid Id,
    Guid OrderId,
    decimal Amount,
    PaymentStatus Status,
    DateTimeOffset? ProcessedAt,
    string? FailureReason)
{
    // Factory method — creates aggregate from first event
    public static Payment Create(IEvent<PaymentInitiated> @event) =>
        new(@event.StreamId,
            @event.Data.OrderId,
            @event.Data.Amount,
            PaymentStatus.Pending,
            null,
            null);

    // Apply methods — pure functions that transform state
    public Payment Apply(PaymentSucceeded @event) =>
        this with
        {
            Status = PaymentStatus.Succeeded,
            ProcessedAt = @event.ProcessedAt
        };

    public Payment Apply(PaymentFailed @event) =>
        this with
        {
            Status = PaymentStatus.Failed,
            ProcessedAt = @event.ProcessedAt,
            FailureReason = @event.Reason
        };
}
```

> **Reference:** [Marten Event Sourcing](https://martendb.io/events/)

## Domain Event Structure

**Always include the aggregate ID as the first parameter**, even though it's also the stream ID:

```csharp
// GOOD — includes aggregate ID
public sealed record PaymentInitiated(
    Guid PaymentId,      // Always first — matches stream ID
    Guid OrderId,
    decimal Amount,
    DateTimeOffset InitiatedAt);

public sealed record PaymentSucceeded(
    Guid PaymentId,      // Always first
    string TransactionId,
    DateTimeOffset ProcessedAt);

// BAD — omitting aggregate ID breaks Marten projections
public sealed record PaymentInitiated(
    Guid OrderId,        // Missing PaymentId!
    decimal Amount,
    DateTimeOffset InitiatedAt);
```

**Why this matters:**
- Marten's inline projections expect the ID in event data
- Events are self-documenting when viewed in isolation
- Enables correlation in queries and diagnostics

> **Reference:** [Marten Projections](https://martendb.io/events/projections/)

## Status Enum Pattern

Use a `Status` enum instead of multiple boolean flags:

```csharp
// GOOD — single source of truth
public sealed record Cart(
    Guid Id,
    Guid? CustomerId,
    Dictionary<string, CartLineItem> Items,
    CartStatus Status)
{
    public bool IsTerminal => Status != CartStatus.Active;

    public Cart Apply(CartCleared @event) =>
        this with { Status = CartStatus.Cleared };

    public Cart Apply(CheckoutInitiated @event) =>
        this with { Status = CartStatus.CheckedOut };
}

public enum CartStatus
{
    Active,      // Can be modified
    Abandoned,   // Terminal
    Cleared,     // Terminal
    CheckedOut   // Terminal
}

// BAD — multiple booleans create ambiguity
public sealed record Cart(
    Guid Id,
    bool IsAbandoned,
    bool IsCleared,
    bool CheckoutInitiated)  // What if multiple are true?
```

## Marten Configuration

```csharp
builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);

    // Inline projections for immediate consistency
    opts.Projections.Snapshot<Payment>(SnapshotLifecycle.Inline);
    opts.Projections.Snapshot<Cart>(SnapshotLifecycle.Inline);
    opts.Projections.Snapshot<Order>(SnapshotLifecycle.Inline);
});
```

> **Reference:** [Marten Snapshot Projections](https://martendb.io/events/projections/aggregate-projections.html)

## Decider Pattern with Wolverine

Business decisions live in handlers, not aggregates. The aggregate only applies events:

```csharp
// Handler makes decisions
public static class ProcessPaymentHandler
{
    public static ProblemDetails Before(ProcessPayment command, Payment? payment)
    {
        if (payment is null)
            return new ProblemDetails { Detail = "Not found", Status = 404 };

        if (payment.Status != PaymentStatus.Pending)
            return new ProblemDetails { Detail = "Already processed", Status = 400 };

        return WolverineContinue.NoProblems;
    }

    public static (Events, OutgoingMessages) Handle(
        ProcessPayment command,
        [WriteAggregate] Payment payment)
    {
        // Decision logic here — returns events
        var events = new Events();
        events.Add(new PaymentSucceeded(payment.Id, "txn_123", DateTimeOffset.UtcNow));
        return (events, new OutgoingMessages());
    }
}

// Aggregate only applies events — no decision logic
public sealed record Payment(/* ... */)
{
    public Payment Apply(PaymentSucceeded @event) =>
        this with { Status = PaymentStatus.Succeeded };
}
```

> **Reference:** [Decider Pattern](https://thinkbeforecoding.com/post/2021/12/17/functional-event-sourcing-decider)

## Starting Event Streams

### From HTTP Endpoint

```csharp
[WolverinePost("/api/payments")]
public static (IStartStream, CreationResponse) Handle(InitiatePayment command)
{
    var paymentId = Guid.CreateVersion7();
    var @event = new PaymentInitiated(
        paymentId,
        command.OrderId,
        command.Amount,
        DateTimeOffset.UtcNow);

    var stream = MartenOps.StartStream<Payment>(paymentId, @event);
    return (stream, new CreationResponse($"/api/payments/{paymentId}"));
}
```

### From Message Handler

```csharp
public static OutgoingMessages Handle(
    StartPaymentProcessing message,
    IDocumentSession session)
{
    var paymentId = Guid.CreateVersion7();
    var @event = new PaymentInitiated(paymentId, message.OrderId, message.Amount, DateTimeOffset.UtcNow);

    session.Events.StartStream<Payment>(paymentId, @event);

    return new OutgoingMessages();
}
```

## Loading Aggregates

```csharp
// In a handler — let Wolverine load it
public static (Events, OutgoingMessages) Handle(
    ProcessPayment command,
    [WriteAggregate] Payment payment)  // Wolverine loads by PaymentId
{ }

// Manual loading when needed
var payment = await session.Events.AggregateStreamAsync<Payment>(paymentId, ct);
```

## When to Use Event Sourcing

**Use event sourcing for:**
- Transactional data with frequent state changes (Orders, Payments, Inventory)
- When historical changes are valuable (audit, replay, temporal queries)
- Saga/orchestration patterns
- Complex business logic benefiting from event-driven design

**Use document store instead for:**
- Master data with infrequent changes (Product Catalog)
- Read-heavy workloads
- When current state is all that matters

See `docs/skills/marten-document-store.md` for document store patterns.
