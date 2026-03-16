# Event Sourcing with Marten and Wolverine

Comprehensive patterns for event-sourced systems in CritterSupply using Marten (event store + document database) and Wolverine (command handling + message bus).

**Scope:** This document covers the full lifecycle of event sourcing: aggregate design, stream management, projections, testing, Wolverine integration, and lessons learned from production implementation.

---

## Table of Contents

1. [Overview](#overview)
2. [When to Use Event Sourcing](#when-to-use-event-sourcing)
3. [Event Stream Conventions](#event-stream-conventions)
4. [Event-Sourced Aggregate Design](#event-sourced-aggregate-design)
5. [Domain Event Structure](#domain-event-structure)
6. [Projections](#projections)
7. [Snapshot Strategies](#snapshot-strategies)
8. [Wolverine Integration Patterns](#wolverine-integration-patterns)
9. [Testing Event-Sourced Systems](#testing-event-sourced-systems)
10. [Event Versioning and Schema Evolution](#event-versioning-and-schema-evolution)
11. [Marten Configuration](#marten-configuration)
12. [Anti-Patterns to Avoid](#anti-patterns-to-avoid)
13. [Lessons Learned from Production](#lessons-learned-from-production)
14. [Related Documentation](#related-documentation)

---

## Overview

**What is Event Sourcing?**

Event sourcing stores state changes as an immutable sequence of events. Instead of updating a record in place, each state change is appended to an event stream. The current state is derived by replaying all events from the beginning.

**Marten's Role:**

- **Event Store:** Persists events in PostgreSQL's `mt_events` table
- **Stream Management:** Organizes events into streams (one per aggregate)
- **Projections:** Builds read models from events (inline, async, or live)
- **Document Store:** Stores projections and snapshots as JSON documents

**Wolverine's Role:**

- **Command Handling:** Routes commands to handlers that produce events
- **Aggregate Loading:** Automatically loads/saves aggregates via attributes
- **Message Bus:** Publishes integration events to other bounded contexts
- **Saga Orchestration:** Coordinates long-running workflows (see `wolverine-sagas.md`)

**CritterSupply's Approach:**

- **Pure functions for business logic** — Handlers and `Apply` methods are pure
- **Immutable aggregates** — Use `sealed record` with `with` expressions
- **Decider pattern** — Separate decision logic from state transformation
- **Inline projections** — Zero-lag read models for hot-path queries
- **A-Frame architecture** — Infrastructure at edges, pure logic in the middle

---

## When to Use Event Sourcing

### ✅ Use Event Sourcing For:

| Use Case | Examples in CritterSupply |
|----------|---------------------------|
| **Transactional data with frequent state changes** | Orders, Carts, Payments, Returns |
| **Audit trail is valuable** | Order lifecycle, Return inspection results, Pricing history |
| **Complex business logic** | Order saga orchestration, Multi-SKU inventory coordination |
| **Temporal queries needed** | "What was the cart state 10 minutes ago?" |
| **Event-driven integrations** | Order events → Fulfillment, Return events → Inventory |
| **Replay/rebuild capability** | Recreate projections from events after schema changes |

**BCs using event sourcing in CritterSupply:**
- **Orders** — Event-sourced `Checkout`, document-backed `Order` saga
- **Shopping** — Event-sourced `Cart`
- **Returns** — Event-sourced `Return`
- **Pricing** — Event-sourced `ProductPrice`
- **Inventory** — Event-sourced `InventoryReservation` (future)
- **Payments** — Event-sourced `Payment` (future)

### ❌ Use Document Store Instead For:

| Use Case | Examples in CritterSupply |
|----------|---------------------------|
| **Master data with infrequent changes** | Product Catalog, Customer Identity |
| **Read-heavy workloads** | Product listings, Category hierarchies |
| **Current state is all that matters** | Customer addresses, Vendor profiles |
| **Simple CRUD operations** | Category management, Tag management |

See `docs/skills/marten-document-store.md` for document store patterns.

---

## Event Stream Conventions

### Stream Identity: UUID v7 vs UUID v5

**CritterSupply uses two stream identity patterns:**

| Pattern | When to Use | Example BCs |
|---------|-------------|-------------|
| **UUID v7 (time-ordered, random)** | Stream ID generated at entity creation | Orders, Shopping, Returns, Fulfillment |
| **UUID v5 (deterministic, SHA-1)** | Stream ID derived from natural key | Pricing (SKU → stream ID) |

**UUID v7 Pattern (Most Aggregates):**

```csharp
// Stream ID generated when entity is created
public static (IStartStream, CreationResponse) Handle(InitializeCart command)
{
    var cartId = Guid.CreateVersion7(); // Time-ordered, random
    var @event = new CartInitialized(
        cartId,
        command.CustomerId,
        command.SessionId,
        DateTimeOffset.UtcNow);

    var stream = MartenOps.StartStream<Cart>(cartId, @event);
    return (stream, new CreationResponse($"/api/carts/{cartId}"));
}
```

**UUID v5 Pattern (Natural Key Aggregates):**

Used when multiple handlers need to resolve the same stream without a database lookup.

```csharp
public sealed record ProductPrice(
    Guid Id,
    string Sku,
    /* ... */)
{
    /// <summary>
    /// Generates a deterministic UUID v5 stream ID from SKU string.
    /// WHY: Multiple handlers can resolve the same stream ID without lookup.
    /// ALGORITHM: SHA-1(RFC4122_URL_NAMESPACE + "pricing:{SKU}")
    /// </summary>
    public static Guid StreamId(string sku)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku, nameof(sku));

        // RFC 4122 URL namespace UUID
        var namespaceBytes = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8").ToByteArray();

        // Normalize SKU to uppercase for case-insensitive determinism
        var nameBytes = Encoding.UTF8.GetBytes($"pricing:{sku.ToUpperInvariant()}");

        // Compute SHA-1 hash (UUID v5 uses SHA-1 per RFC 4122 §4.3)
        var hash = SHA1.HashData([.. namespaceBytes, .. nameBytes]);

        // Set version (4 bits): 0101 (5) at offset 48-51
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);

        // Set variant (2 bits): 10 at offset 64-65 (RFC 4122)
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        // Take first 16 bytes for UUID
        return new Guid(hash[..16]);
    }

    public static ProductPrice Create(string sku, DateTimeOffset registeredAt) =>
        new ProductPrice
        {
            Id = StreamId(sku), // Deterministic stream ID
            Sku = sku.ToUpperInvariant(),
            Status = PriceStatus.Unpriced,
            RegisteredAt = registeredAt
        };
}
```

**When to use each:**

- **UUID v7:** Most aggregates (Cart, Order, Return, Checkout, Payment)
- **UUID v5:** Natural-key aggregates where multiple handlers need the same stream ID (ProductPrice → SKU)

> **Reference:** [ADR 0016: UUID v5 for Natural-Key Stream IDs](../decisions/0016-uuid-v5-for-natural-key-stream-ids.md)

### Stream Naming per Bounded Context

**Convention:** One event stream per aggregate instance. Stream ID = Aggregate ID.

| BC | Aggregate | Stream ID Type | Schema Name |
|----|-----------|----------------|-------------|
| Orders | `Checkout` | UUID v7 | `orders` |
| Orders | `Order` (saga, not event-sourced) | UUID v7 (document) | `orders` |
| Shopping | `Cart` | UUID v7 | `shopping` |
| Returns | `Return` | UUID v7 | `returns` |
| Pricing | `ProductPrice` | UUID v5 (from SKU) | `pricing` |

**Schema isolation:** Each BC has its own PostgreSQL schema (`opts.DatabaseSchemaName = Constants.<BcName>.ToLowerInvariant()`). This isolates event streams and projections per BC while sharing the same database.

---

## Event-Sourced Aggregate Design

### Core Principles

1. **Aggregates are immutable records** — No mutable state, use `with` expressions
2. **Pure functions for Apply methods** — Events transform state without side effects
3. **No behavior in aggregates** — Only data + Apply methods. Business logic lives in handlers or Decider classes.
4. **Decider pattern via Wolverine** — Business decisions live in handlers, not aggregates
5. **No base classes** — No `Aggregate` base class or `IEntity` interface
6. **Functional programming mindset** — Aggregates return new instances (like FP)

### Aggregate Structure

**Anatomy of a CritterSupply event-sourced aggregate:**

```csharp
public sealed record Cart(
    Guid Id,
    Guid? CustomerId,
    string? SessionId,
    DateTimeOffset InitializedAt,
    Dictionary<string, CartLineItem> Items,
    CartStatus Status)
{
    // Derived property (computed from state, no side effects)
    public bool IsTerminal => Status != CartStatus.Active;

    // Factory method — creates aggregate from first event
    // Marten calls this when starting a new stream
    public static Cart Create(IEvent<CartInitialized> @event) =>
        new(@event.StreamId,
            @event.Data.CustomerId,
            @event.Data.SessionId,
            @event.Data.InitializedAt,
            new Dictionary<string, CartLineItem>(),
            CartStatus.Active);

    // Apply methods — pure functions that transform state
    // Marten calls these when replaying events to rebuild aggregate state
    public Cart Apply(ItemAdded @event)
    {
        var updatedItems = new Dictionary<string, CartLineItem>(Items);

        if (updatedItems.TryGetValue(@event.Sku, out var existingItem))
        {
            updatedItems[@event.Sku] = existingItem with
            {
                Quantity = existingItem.Quantity + @event.Quantity
            };
        }
        else
        {
            updatedItems[@event.Sku] = new CartLineItem(
                @event.Sku,
                @event.Quantity,
                @event.UnitPrice);
        }

        return this with { Items = updatedItems };
    }

    public Cart Apply(CartCleared @event) =>
        this with
        {
            Items = new Dictionary<string, CartLineItem>(),
            Status = CartStatus.Cleared
        };

    public Cart Apply(CheckoutInitiated @event) =>
        this with { Status = CartStatus.CheckedOut };
}
```

> **Reference:** `src/Shopping/Shopping/Cart/Cart.cs`

### CritterSupply Conventions

#### 1. Parameter Naming: Always `@event`

**✅ GOOD — Use `@event` for event parameters:**
```csharp
public ProductPrice Apply(InitialPriceSet @event) =>
    this with
    {
        Status = PriceStatus.Published,
        BasePrice = @event.Price
    };
```

**❌ BAD — Do not use `evt`, `e`, or other abbreviations:**
```csharp
public ProductPrice Apply(InitialPriceSet evt) =>  // ❌ Wrong!
    this with { BasePrice = evt.Price };
```

**Why `@event`?**
- `@` prefix escapes the C# keyword `event`
- Consistent across the entire codebase
- Self-documenting (clearly an event parameter)
- Matches Marten and Wolverine documentation conventions

#### 2. Expression Body Syntax for Apply Methods

**✅ GOOD — Use expression bodies (`=>`):**
```csharp
public Checkout Apply(ShippingAddressProvided @event) =>
    this with
    {
        ShippingAddress = new ShippingAddress(
            @event.AddressLine1,
            @event.AddressLine2,
            @event.City,
            @event.StateOrProvince,
            @event.PostalCode,
            @event.Country)
    };
```

**❌ BAD — Do not use block bodies when expression bodies work:**
```csharp
public Checkout Apply(ShippingAddressProvided @event)
{
    return this with
    {
        ShippingAddress = new ShippingAddress(/* ... */)
    };
}
```

**Why expression bodies?**
- More concise (less ceremony)
- Emphasizes the pure function nature of Apply methods
- Consistent with functional programming style
- Used throughout the codebase (Cart, Checkout, ProductPrice, etc.)

**Exception:** Use block bodies when you need temporary variables (e.g., `Cart.Apply(ItemAdded)` with dictionary manipulation).

#### 3. No Behavior in Aggregates

**Aggregates only contain:**
- Immutable state (properties)
- `Create()` factory method (for Marten stream initialization)
- `Apply()` methods (pure state transformations)
- Derived properties (computed from state, no side effects)

**Aggregates do NOT contain:**
- ❌ Business logic / decision logic
- ❌ Side effects (HTTP calls, database writes, logging)
- ❌ Validation logic
- ❌ Command handling logic

**✅ GOOD — Aggregate only applies events:**
```csharp
public sealed record ProductPrice(
    Guid Id,
    string Sku,
    PriceStatus Status,
    Money? BasePrice,
    DateTimeOffset RegisteredAt)
{
    public static ProductPrice Create(string sku, DateTimeOffset registeredAt) =>
        new ProductPrice
        {
            Id = StreamId(sku),
            Sku = sku.ToUpperInvariant(),
            Status = PriceStatus.Unpriced,
            RegisteredAt = registeredAt
        };

    public ProductPrice Apply(InitialPriceSet @event) =>
        this with
        {
            Status = PriceStatus.Published,
            BasePrice = @event.Price
        };
}
```

**❌ BAD — Do not put business logic in aggregates:**
```csharp
public sealed record ProductPrice(/* ... */)
{
    // ❌ WRONG — validation belongs in handlers or validators
    public ProductPrice SetPrice(Money newPrice)
    {
        if (newPrice.Amount <= 0)
            throw new InvalidOperationException("Price must be positive");

        if (newPrice.Amount < FloorPrice?.Amount)
            throw new InvalidOperationException("Price below floor");

        return this with { BasePrice = newPrice };
    }
}
```

#### 4. Static vs Instance Methods

**Current convention: Prefer instance methods for consistency.**

Both patterns are valid in C#:

```csharp
// ✅ Instance method (current CritterSupply convention)
public ProductPrice Apply(PriceChanged @event) =>
    this with { BasePrice = @event.NewPrice };

// ✅ Static method (valid alternative, used in some codebases)
public static ProductPrice Apply(ProductPrice state, PriceChanged @event) =>
    state with { BasePrice = @event.NewPrice };
```

**Why instance methods?**
- Consistent across the entire CritterSupply codebase
- Less verbose (`this with` vs `state with`)
- Matches Marten's default expectations
- Easier to read for developers coming from OOP backgrounds

**Important:** Be consistent within a bounded context. Do not mix instance and static Apply methods in the same aggregate.

#### 5. Status Enum Pattern

Use a `Status` enum instead of multiple boolean flags:

```csharp
// ✅ GOOD — single source of truth
public sealed record Cart(
    Guid Id,
    Guid? CustomerId,
    Dictionary<string, CartLineItem> Items,
    CartStatus Status)
{
    public bool IsTerminal => Status != CartStatus.Active;
}

public enum CartStatus
{
    Active,      // Can be modified
    Abandoned,   // Terminal
    Cleared,     // Terminal
    CheckedOut   // Terminal
}

// ❌ BAD — multiple booleans create ambiguity
public sealed record Cart(
    Guid Id,
    bool IsAbandoned,
    bool IsCleared,
    bool CheckoutInitiated)  // What if multiple are true?
```

**Why enums?**
- Single source of truth (one field, not three)
- Impossible states are impossible (can't be both Abandoned AND CheckedOut)
- Easy to add new statuses (just add enum value)
- Easy to query (`Status == CartStatus.Active`)

### Decider Pattern with Wolverine

**The Decider pattern separates decision logic from state transformation.**

CritterSupply uses two flavors of the Decider pattern:

#### Flavor 1: Inline Handler Logic (Simple Cases)

For simple aggregates, business logic lives directly in the handler:

```csharp
// Handler makes decisions and returns events
public static class SetInitialPriceHandler
{
    public static ProblemDetails Before(SetInitialPrice command, ProductPrice? productPrice)
    {
        if (productPrice is null)
            return new ProblemDetails { Detail = "Product not registered", Status = 404 };

        if (productPrice.Status != PriceStatus.Unpriced)
            return new ProblemDetails { Detail = "Price already set", Status = 400 };

        return WolverineContinue.NoProblems;
    }

    public static Events Handle(
        SetInitialPrice command,
        [WriteAggregate] ProductPrice productPrice)
    {
        // Business decision: create InitialPriceSet event
        var @event = new InitialPriceSet(
            productPrice.Id,
            command.Price,
            command.FloorPrice,
            command.CeilingPrice,
            DateTimeOffset.UtcNow);

        return [@event];
    }
}

// Aggregate only applies events — no decision logic
public sealed record ProductPrice(/* ... */)
{
    public ProductPrice Apply(InitialPriceSet @event) =>
        this with
        {
            Status = PriceStatus.Published,
            BasePrice = @event.Price,
            FloorPrice = @event.FloorPrice,
            CeilingPrice = @event.CeilingPrice
        };
}
```

> **Reference:** `src/Pricing/Pricing/Products/SetInitialPriceHandler.cs`, `src/Pricing/Pricing/Products/ProductPrice.cs`

#### Flavor 2: Separate Decider Class (Complex Cases)

For complex aggregates with many handlers, extract decision logic to a separate `Decider` class:

```csharp
// Pure functions in a separate Decider class
public static class OrderDecider
{
    /// <summary>
    /// Pure function - accepts time as parameter for testability.
    /// Returns a Decision record with status changes and messages.
    /// </summary>
    public static OrderDecision HandlePaymentCaptured(
        Order order,
        PaymentCaptured message,
        DateTimeOffset timestamp)
    {
        // Decision logic here (pure function)
        if (order.Status != OrderStatus.AwaitingPayment)
            return OrderDecision.NoChange;

        var decision = new OrderDecision
        {
            Status = OrderStatus.PaymentConfirmed,
            IsPaymentCaptured = true
        };

        // If inventory is already reserved, commit it
        if (order.IsInventoryReserved)
        {
            foreach (var (reservationId, sku) in order.ReservationIds)
            {
                decision.Messages.Add(new CommitReservation(
                    order.Id,
                    reservationId,
                    sku,
                    timestamp));
            }
        }

        return decision;
    }
}

// Saga handler delegates to Decider
public sealed class Order : Saga
{
    public OutgoingMessages Handle(PaymentCaptured message)
    {
        // Delegate decision logic to Decider
        var decision = OrderDecider.HandlePaymentCaptured(this, message, DateTimeOffset.UtcNow);

        // Apply state changes from decision
        if (decision.Status.HasValue) Status = decision.Status.Value;
        if (decision.IsPaymentCaptured.HasValue) IsPaymentCaptured = decision.IsPaymentCaptured.Value;

        // Return outgoing messages
        var outgoing = new OutgoingMessages();
        foreach (var msg in decision.Messages) outgoing.Add(msg);
        return outgoing;
    }
}
```

**When to use a separate Decider class:**
- ✅ Complex business logic with multiple decision paths
- ✅ Many handlers for the same aggregate (Order has 10+ handlers)
- ✅ Logic that benefits from unit testing in isolation
- ✅ When you want to accept `DateTimeOffset` as a parameter (pure functions)

**When to use inline handler logic:**
- ✅ Simple aggregates with 1-3 handlers
- ✅ Straightforward decision logic (no complex branching)
- ✅ When the handler is the only place the logic is used

> **Reference:** `src/Orders/Orders/Placement/OrderDecider.cs`, `src/Orders/Orders/Placement/Order.cs`, [ADR 0029: Order Saga Design Decisions](../decisions/0029-order-saga-design-decisions.md)

---

## Domain Event Structure

**Always include the aggregate ID as the first parameter**, even though it's also the stream ID:

```csharp
// ✅ GOOD — includes aggregate ID
public sealed record InitialPriceSet(
    Guid ProductPriceId,    // Always first — matches stream ID
    Money Price,
    Money? FloorPrice,
    Money? CeilingPrice,
    DateTimeOffset PricedAt);

public sealed record PriceChanged(
    Guid ProductPriceId,    // Always first
    Money OldPrice,
    Money NewPrice,
    DateTimeOffset PreviousPriceSetAt,
    DateTimeOffset ChangedAt);

// ❌ BAD — omitting aggregate ID breaks Marten projections
public sealed record InitialPriceSet(
    Money Price,           // Missing ProductPriceId!
    DateTimeOffset PricedAt);
```

**Why this matters:**
- Marten's inline projections expect the ID in event data
- Events are self-documenting when viewed in isolation
- Enables correlation in queries and diagnostics
- Avoids "magic" — the stream ID is explicit in the event

**Event Naming Conventions:**
- Use past tense (happened): `ItemAdded`, `PriceChanged`, `OrderPlaced`
- Not present tense: ~~`AddItem`~~, ~~`ChangePrice`~~, ~~`PlaceOrder`~~
- Events are facts — something that already happened

> **Reference:** [Marten Projections](https://martendb.io/events/projections/)

---

## Projections

**Projections build read models from events.** Marten supports three types:

### 1. Inline Snapshots (Most Common in CritterSupply)

**What:** Aggregate state persisted as a JSON document after every event append.

**When:** Zero-lag read models for hot-path queries.

**Configuration:**

```csharp
builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);

    // Inline snapshot — aggregate state updated in same transaction as event append
    opts.Projections.Snapshot<Cart>(SnapshotLifecycle.Inline);
    opts.Projections.Snapshot<Checkout>(SnapshotLifecycle.Inline);
    opts.Projections.Snapshot<Return>(SnapshotLifecycle.Inline);
});
```

**How it works:**

1. Command handler appends events to stream
2. Marten replays all events through `Apply()` methods
3. Resulting aggregate state saved as document in same transaction
4. Queries read the document, not the events

**Query Pattern:**

```csharp
// Query the snapshot document (zero lag)
var cart = await session.LoadAsync<Cart>(cartId);

// Or query multiple snapshots
var activeCarts = await session.Query<Cart>()
    .Where(c => c.Status == CartStatus.Active && c.CustomerId == customerId)
    .ToListAsync();
```

**Pros:**
- ✅ Zero lag — document updated in same transaction as events
- ✅ Strong consistency — queries always see committed state
- ✅ Simple — no async daemon required
- ✅ Perfect for hot-path queries

**Cons:**
- ❌ Write latency — snapshot update adds to transaction time
- ❌ Not suitable for complex read models (use async projections)

**Used in:** Orders (Checkout), Shopping (Cart), Returns (Return), Pricing (ProductPrice snapshot)

### 2. Multi-Stream Projections (Async)

**What:** Read models derived from events across multiple streams, keyed differently than source streams.

**When:** Complex queries requiring data from multiple aggregates.

**Example: CurrentPriceView (Pricing BC)**

Maps Guid-keyed event streams → string-keyed documents (SKU as document ID):

```csharp
/// <summary>
/// Inline Marten projection for CurrentPriceView using MultiStreamProjection.
/// Maps: Guid event streams → string-keyed documents (SKU as document ID).
/// </summary>
public sealed class CurrentPriceViewProjection : MultiStreamProjection<CurrentPriceView, string>
{
    public CurrentPriceViewProjection()
    {
        // Tell Marten which property to use as the document ID for each event type
        Identity<InitialPriceSet>(x => x.Sku);
        Identity<PriceChanged>(x => x.Sku);
        Identity<PriceChangeScheduled>(x => x.Sku);
        Identity<ScheduledPriceChangeCancelled>(x => x.Sku);
        // ... more event types
    }

    // Create method for InitialPriceSet (first event creates the document)
    public CurrentPriceView Create(InitialPriceSet evt)
    {
        return new CurrentPriceView
        {
            Id = evt.Sku, // String ID, not Guid!
            Sku = evt.Sku,
            BasePrice = evt.Price.Amount,
            Currency = evt.Price.Currency,
            FloorPrice = evt.FloorPrice?.Amount,
            CeilingPrice = evt.CeilingPrice?.Amount,
            Status = PriceStatus.Published,
            LastUpdatedAt = evt.PricedAt
        };
    }

    public static CurrentPriceView Apply(CurrentPriceView view, PriceChanged evt)
    {
        return view with
        {
            BasePrice = evt.NewPrice.Amount,
            Currency = evt.NewPrice.Currency,
            PreviousBasePrice = evt.OldPrice.Amount,
            PreviousPriceSetAt = evt.PreviousPriceSetAt,
            LastUpdatedAt = evt.ChangedAt
        };
    }

    // More Apply methods for other event types...
}
```

**Configuration:**

```csharp
builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);

    // Multi-stream projection (inline for zero lag)
    opts.Projections.Add<CurrentPriceViewProjection>(ProjectionLifecycle.Inline);
});
```

**Why Multi-Stream?**
- ProductPrice streams are keyed by Guid (UUID v5 from SKU)
- Storefront queries need SKU-keyed documents for fast lookup
- Multi-stream projection maps Guid streams → string-keyed read model

**Query Pattern:**

```csharp
// Query by SKU (string ID)
var priceView = await session.LoadAsync<CurrentPriceView>("CHWY-001");

// Or query multiple prices
var prices = await session.Query<CurrentPriceView>()
    .Where(p => p.Status == PriceStatus.Published)
    .ToListAsync();
```

> **Reference:** `src/Pricing/Pricing/Products/CurrentPriceViewProjection.cs`

### 3. Async Projections (Background Processing)

**What:** Projections processed by background daemon, not in the write transaction.

**When:** Complex read models, denormalized views, analytics, high write throughput requirements.

**Configuration:**

```csharp
builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);

    // Async projection — processed by background daemon
    opts.Projections.Add<OrderHistoryProjection>(ProjectionLifecycle.Async);
})
.AddAsyncDaemon(DaemonMode.Solo); // Solo for single-instance, HotCold for multi-instance
```

**Daemon Modes:**

| Mode | Use Case | Behavior |
|------|----------|----------|
| `Solo` | Single-instance APIs | One daemon processes all projections |
| `HotCold` | Multi-instance APIs (production) | Leader election via database lock |
| `Disabled` | Development/testing | No background processing |

**Pros:**
- ✅ Low write latency — projection not in write transaction
- ✅ High throughput — write path not blocked by projection
- ✅ Complex read models — can aggregate across many streams

**Cons:**
- ❌ Eventual consistency — read lag between write and projection update
- ❌ Requires daemon — additional infrastructure to monitor
- ❌ More complex — need to handle projection rebuild, failures

**Not yet used in CritterSupply** — all current projections are inline for strong consistency. Async projections planned for analytics (Cycle 29+).

### 4. Live Aggregation (On-Demand)

**What:** Aggregate state computed by replaying events at query time.

**When:** Ad-hoc queries, temporal queries ("state at time T"), no persistence.

**Usage:**

```csharp
// Current state (replay all events)
var cart = await session.Events.AggregateStreamAsync<Cart>(cartId);

// State at specific version
var cartV3 = await session.Events.AggregateStreamAsync<Cart>(cartId, version: 3);

// State at specific time (temporal query)
var cartYesterday = await session.Events.AggregateStreamAsync<Cart>(
    cartId,
    timestamp: DateTime.UtcNow.AddDays(-1));
```

**Pros:**
- ✅ No projection setup required
- ✅ Temporal queries — "time travel" to any point in history
- ✅ Always consistent — reads from event stream

**Cons:**
- ❌ Slow for large streams — replays all events
- ❌ No indexing — can't efficiently query across aggregates
- ❌ Not cached — replay happens on every query

**Used in:** Testing, diagnostics, admin tools. Not for hot-path queries.

### Projection Lifecycle Decision Matrix

| Requirement | Recommended Lifecycle | Example |
|-------------|----------------------|---------|
| Zero-lag queries for aggregate state | `Inline` snapshot | Cart, Checkout, Return |
| Read model keyed differently than source stream | `Inline` multi-stream | CurrentPriceView (SKU-keyed) |
| Complex denormalized view | `Async` | OrderHistory (future) |
| High write throughput | `Async` | Analytics projections (future) |
| Temporal queries | `Live` | Admin audit tools |
| Testing | `Inline` or `Live` | Integration tests |

---

## Snapshot Strategies

**Snapshots cache aggregate state to avoid replaying large event streams.**

### When to Use Snapshots

| Scenario | Snapshot? | Rationale |
|----------|-----------|-----------|
| Aggregate with 10-50 events | ✅ Yes (inline) | Negligible overhead, simplifies queries |
| Aggregate with 100+ events | ✅ Yes (inline or async) | Avoids replay latency |
| High read:write ratio (10:1+) | ✅ Yes (inline) | Amortize snapshot cost over many reads |
| High write:read ratio | ❌ No | Snapshot overhead not justified |
| Write latency critical | ❌ No | Use async snapshot or no snapshot |

**CritterSupply uses inline snapshots for all event-sourced aggregates:**
- Cart (Shopping BC) — typically 5-20 events
- Checkout (Orders BC) — typically 5-10 events
- Return (Returns BC) — typically 10-20 events
- ProductPrice (Pricing BC) — typically 5-15 events

**Configuration:**

```csharp
// Inline snapshot (updated in write transaction)
opts.Projections.Snapshot<Cart>(SnapshotLifecycle.Inline);

// Async snapshot (updated by daemon)
opts.Projections.Snapshot<Order>(SnapshotLifecycle.Async);
```

**How Snapshots Work:**

1. Events appended to `mt_events` table
2. Marten replays events through `Apply()` methods
3. Resulting aggregate persisted as document in `mt_doc_<aggregate>` table
4. Next load reads snapshot + events since snapshot version (incremental replay)

**Snapshot Metadata:**

Snapshots store version number for incremental replay:

```csharp
var cart = await session.LoadAsync<Cart>(cartId);
// Marten loaded snapshot at version 8, then replayed events 9-12
```

**When Not to Snapshot:**
- Short-lived aggregates (deleted after use)
- Aggregates never queried directly (only via projections)
- Extremely high write volume where snapshot update is bottleneck

---

## Wolverine Integration Patterns

### Handler Return Types for Event-Sourced Aggregates

Wolverine recognizes specific return patterns for appending events:

#### 1. `Events` Collection (Simplest)

```csharp
public static Events Handle(
    ChangePrice command,
    [WriteAggregate] ProductPrice productPrice)
{
    var @event = new PriceChanged(
        productPrice.Id,
        productPrice.BasePrice!,
        command.NewPrice,
        productPrice.LastUpdatedAt,
        DateTimeOffset.UtcNow);

    return [@event]; // Wolverine appends to stream
}
```

#### 2. `IEnumerable<object>` (Multiple Events)

```csharp
public static IEnumerable<object> Handle(
    MarkItemReady command,
    [WriteAggregate] Order order)
{
    if (order.Items.TryGetValue(command.ItemName, out var item))
    {
        item.Ready = true;
        yield return new ItemReady(command.ItemName);
    }

    if (order.IsReadyToShip())
    {
        yield return new OrderReady(); // Conditional event
    }
}
```

#### 3. `(Events, OutgoingMessages)` Tuple (Events + Integration Messages)

```csharp
public static (Events, OutgoingMessages) Handle(
    CompleteReturn command,
    [WriteAggregate] Return @return)
{
    var events = new Events();
    var messages = new OutgoingMessages();

    // Domain event (appended to stream)
    events.Add(new ReturnCompleted(
        @return.Id,
        @return.OrderId,
        @return.CustomerId,
        @return.CalculateFinalRefund(),
        DateTimeOffset.UtcNow));

    // Integration event (published to message bus)
    messages.Add(new ReturnCompletedIntegration(
        @return.Id,
        @return.OrderId,
        @return.CustomerId,
        @return.Items.ToList(), // Per-item disposition data
        @return.TotalRefundAmount));

    return (events, messages);
}
```

#### 4. `UpdatedAggregate` (Return Updated State)

Used for HTTP endpoints that return the updated aggregate as JSON:

```csharp
[WolverinePost("/api/orders/{orderId}/items/{itemName}/mark-ready")]
public static (UpdatedAggregate, Events) Handle(
    MarkItemReady command,
    [WriteAggregate] Order order)
{
    var events = new Events();

    if (order.Items.TryGetValue(command.ItemName, out var item))
    {
        item.Ready = true;
        events.Add(new ItemReady(command.ItemName));
    }

    if (order.IsReadyToShip())
    {
        events.Add(new OrderReady());
    }

    return (new UpdatedAggregate(), events);
    // Wolverine fetches latest aggregate state and returns as HTTP response
}
```

**Generic version for multiple streams:**

```csharp
public static UpdatedAggregate<XAccount> Handle(
    MakePurchase command,
    [WriteAggregate] IEventStream<XAccount> account,
    [WriteAggregate] IEventStream<Inventory> inventory)
{
    // Append events to both streams
    account.AppendOne(new ItemPurchased(/* ... */));
    inventory.AppendOne(new Drawdown(command.Number));

    // Return only XAccount as HTTP response
    return new UpdatedAggregate<XAccount>();
}
```

#### 5. `IStartStream` (New Aggregate) — ⚠️ **CRITICAL: Required for Stream Creation**

**IMPORTANT:** Handlers that create new event streams MUST return `IStartStream` from `MartenOps.StartStream()`. Direct `session.Events.StartStream()` usage DOES NOT enroll in Wolverine's transactional middleware and events will not be persisted.

**✅ CORRECT — Return IStartStream:**
```csharp
[WolverinePost("/api/carts")]
public static (IStartStream, CreationResponse) Handle(InitializeCart command)
{
    var cartId = Guid.CreateVersion7();
    var @event = new CartInitialized(
        cartId,
        command.CustomerId,
        command.SessionId,
        DateTimeOffset.UtcNow);

    // ✅ CORRECT: Return IStartStream from MartenOps.StartStream()
    var stream = MartenOps.StartStream<Cart>(cartId, @event);
    return (stream, new CreationResponse($"/api/carts/{cartId}"));
}
```

**❌ ANTI-PATTERN — Direct session usage (does NOT persist):**
```csharp
[WolverinePost("/api/carts")]
public static CreationResponse Handle(InitializeCart command, IDocumentSession session)
{
    var cartId = Guid.CreateVersion7();
    var @event = new CartInitialized(/* ... */);

    // ❌ WRONG: Direct session usage does NOT enroll in transactional middleware
    session.Events.StartStream<Cart>(cartId, @event);

    // Events are NOT persisted — no transaction enrolled!
    return new CreationResponse($"/api/carts/{cartId}");
}
```

**Tuple return order matters:**
- First element: Response object (e.g., `CreationResponse`, `SetPriceResult`)
- Second element: `IStartStream` (Wolverine uses this to enroll transaction)

**Example with multiple return values:**
```csharp
public static (SetPriceResult, IStartStream) Handle(SetPrice cmd)
{
    var streamId = ProductPrice.StreamId(cmd.Sku);
    var @event = new InitialPriceSet(/* ... */);

    var stream = MartenOps.StartStream<ProductPrice>(streamId, @event);
    var result = new SetPriceResult(streamId);

    // Response first, IStartStream second
    return (result, stream);
}
```

### Aggregate Loading Attributes

| Attribute | Purpose | When to Use |
|-----------|---------|-------------|
| `[WriteAggregate]` | Load aggregate, append events, save | Command handlers that modify aggregate |
| `[ReadAggregate]` | Load aggregate read-only (no save) | Queries, read-only operations |
| `[AggregateHandler]` | Class-level attribute for aggregate workflow | Single-stream handlers (less common than `[WriteAggregate]`) |

**Examples:**

```csharp
// Load aggregate by ID from command property
public static Events Handle(
    ChangePrice command, // command.ProductPriceId
    [WriteAggregate] ProductPrice productPrice) // Loaded by command.ProductPriceId
{
    // Wolverine convention: command.{AggregateType}Id or command.Id
    return [new PriceChanged(/* ... */)];
}

// Explicit ID mapping
public static Events Handle(
    ApproveReturn command, // command.ReturnId (not command.Id)
    [WriteAggregate(nameof(ApproveReturn.ReturnId))] Return @return)
{
    return [new ReturnApproved(/* ... */)];
}

// Multiple aggregates
public static void Handle(
    TransferFunds command,
    [WriteAggregate(nameof(TransferFunds.FromAccountId))] Account fromAccount,
    [WriteAggregate(nameof(TransferFunds.ToAccountId))] Account toAccount)
{
    // Append events to both streams
}
```

### IEventStream\<T\> (Advanced)

For fine-grained control over event appending:

```csharp
[AggregateHandler]
public static void Handle(
    MarkItemReady command,
    IEventStream<Order> stream)
{
    var order = stream.Aggregate; // Current state

    if (order.Items.TryGetValue(command.ItemName, out var item))
    {
        item.Ready = true;
        stream.AppendOne(new ItemReady(command.ItemName));
    }

    if (order.IsReadyToShip())
    {
        stream.AppendOne(new OrderReady());
    }
}
```

**Use `IEventStream<T>` when:**
- You need to inspect aggregate state before appending
- Conditional event appending based on current state
- Multiple events appended in sequence

**Prefer `[WriteAggregate]` + return `Events` when:**
- Simple one-to-one command → event mapping
- Clearer intent (pure function style)
- Easier to test (no Marten dependency in test)

---

## Testing Event-Sourced Systems

### The Race Condition Problem (L5 from Cycle 26)

**Issue:** HTTP-based integration tests for event-sourced aggregates can fail due to race conditions.

**What happens:**

1. Test POSTs command via HTTP → handler appends events
2. Wolverine's `AutoApplyTransactions()` commits **asynchronously**
3. HTTP 200 response sent **before** transaction commits
4. Test GETs aggregate → reads **stale state** (snapshot not updated yet)
5. Test fails with 409 Conflict or stale data

**Example of the Problem:**

```csharp
// ❌ BAD: HTTP-based test with race condition
[Fact]
public async Task POST_approve_transitions_return_to_Approved()
{
    var returnId = Guid.CreateVersion7();

    // Create return (works fine)
    await CreateReturnUnderReview(returnId);

    // Approve return via HTTP POST
    await _fixture.Host.Scenario(s =>
    {
        s.Post.Json(new ApproveReturn(returnId))
            .ToUrl($"/api/returns/{returnId}/approve");
        s.StatusCodeShouldBe(200); // ✅ HTTP response sent
    });

    // RACE CONDITION: Transaction may not be committed yet!

    // Immediate GET to verify state
    await _fixture.Host.Scenario(s =>
    {
        s.Get.Url($"/api/returns/{returnId}");
        s.StatusCodeShouldBe(200); // ❌ May fail with 409 or stale data
    });
}
```

**Why This Happens:**
- Wolverine's `AutoApplyTransactions()` policy commits transactions **asynchronously**
- HTTP response returns **before** transaction completes
- Marten inline snapshot projections update **after** transaction commits
- Subsequent reads see stale data

**❌ Why Delays Don't Work:**

```csharp
await _fixture.Host.Scenario(s => { /* POST */ });
await Task.Delay(500); // Still fails sometimes!
await _fixture.Host.Scenario(s => { /* GET */ });
```

Timing-based solutions are inherently fragile — they depend on system load, CI environment, and other unpredictable factors.

### ✅ The Solution: Direct Command Invocation

**Instead of testing via HTTP, invoke commands directly through Wolverine's message bus:**

```csharp
// ✅ GOOD: Direct command invocation with no race condition
[Fact]
public async Task POST_approve_transitions_return_to_Approved()
{
    var orderId = Guid.CreateVersion7();
    var customerId = Guid.CreateVersion7();
    await SeedEligibilityWindow(orderId, customerId);

    // Create return under review
    var createResponse = await CreateReturnUnderReview(orderId, customerId);
    var returnId = createResponse.ReturnId!.Value;

    // ✅ Invoke command directly through Wolverine (not HTTP)
    var command = new ApproveReturn(returnId);
    await _fixture.ExecuteAndWaitAsync(command);

    // ✅ Query event store directly (guaranteed to see committed events)
    await using var session = _fixture.GetDocumentSession();
    var aggregate = await session.Events.AggregateStreamAsync<Return>(returnId);

    aggregate.ShouldNotBeNull();
    aggregate.Status.ShouldBe(ReturnStatus.Approved);
    aggregate.ApprovedAt.ShouldNotBeNull();
    aggregate.ShipByDeadline.ShouldNotBeNull();

    // ✅ Still verify HTTP GET endpoint works (optional)
    var getResult = await _fixture.Host.Scenario(s =>
    {
        s.Get.Url($"/api/returns/{returnId}");
        s.StatusCodeShouldBe(200);
    });

    var summary = getResult.ReadAsJson<ReturnSummaryResponse>();
    summary.ShouldNotBeNull();
    summary.Status.ShouldBe("Approved");
}
```

**Why This is Better:**

1. **Tests the aggregate directly** — source of truth in event sourcing
2. **Eliminates race conditions** — `ExecuteAndWaitAsync` waits for all side effects to complete
3. **Separates concerns** — integration tests verify business logic (commands → events → aggregate state), HTTP endpoints verified separately
4. **Aligns with eventual consistency** — tests don't assume instant consistency

**TestFixture Helper Method:**

```csharp
public class TestFixture : IAsyncLifetime
{
    private IAlbaHost _host = null!;

    public async Task ExecuteAndWaitAsync(object command)
    {
        await _host.InvokeAsync(command);
        // Wolverine ensures all transactions complete before returning
    }

    public IDocumentSession GetDocumentSession()
    {
        return _host.Services.GetRequiredService<IDocumentStore>()
            .LightweightSession();
    }
}
```

**When to Use Each Approach:**

| Test Type | Approach | Rationale |
|-----------|----------|-----------|
| **Aggregate state transitions** | Direct command invocation | Avoids race conditions, tests business logic |
| **HTTP endpoint routing** | HTTP via Alba | Verifies URL routing, status codes, serialization |
| **Integration flows** | Direct command invocation | Ensures events published to message bus |
| **E2E tests** | HTTP or Playwright | Simulates real user interactions |

**Lesson Learned from Cycle 26:**

> "HTTP-based testing pattern (POST command → immediate GET verification) doesn't respect eventual consistency in event sourcing. Inline projections update asynchronously after transaction commit. Tests should use direct command invocation for state-changing operations and query the event store directly for assertions." — Cycle 26 Retrospective L5

---

## Event Versioning and Schema Evolution

**CritterSupply's Approach:** Events are immutable sealed records. Schema evolution is handled at the application layer, not in the event store.

### Current Strategy: Additive Changes Only

All events use sealed records with immutable properties:

```csharp
// Version 1 (original)
public sealed record ItemAdded(
    Guid CartId,
    string Sku,
    int Quantity,
    decimal UnitPrice,
    DateTimeOffset AddedAt);

// Version 2 (added optional field)
public sealed record ItemAdded(
    Guid CartId,
    string Sku,
    int Quantity,
    decimal UnitPrice,
    DateTimeOffset AddedAt,
    string? PromoCode = null); // New optional field
```

**Why this works:**
- Old events deserialize to new type (PromoCode = null)
- No upcasting required
- Backward compatible

### Handling Breaking Changes (Future)

**Not yet implemented in CritterSupply.** When breaking changes are needed:

**Option 1: Multi-Version Apply Methods**

```csharp
public sealed record Cart(/* ... */)
{
    // Handle V1 events
    public Cart Apply(ItemAddedV1 @event) =>
        this with { /* transform V1 → current state */ };

    // Handle V2 events
    public Cart Apply(ItemAddedV2 @event) =>
        this with { /* transform V2 → current state */ };
}
```

**Option 2: Upcasters (Marten Built-In)**

```csharp
// Not yet used in CritterSupply
public class ItemAddedUpcaster : EventUpcaster<ItemAddedV1, ItemAddedV2>
{
    public override ItemAddedV2 Upcast(ItemAddedV1 oldEvent)
    {
        return new ItemAddedV2(
            oldEvent.CartId,
            oldEvent.Sku,
            oldEvent.Quantity,
            oldEvent.UnitPrice,
            oldEvent.AddedAt,
            PromoCode: null); // Default for new field
    }
}
```

**Option 3: Projection-Level Transformation**

```csharp
// Handle multiple event versions in projection
public static CurrentPriceView Apply(CurrentPriceView view, InitialPriceSetV1 evt) =>
    view with { BasePrice = evt.Price };

public static CurrentPriceView Apply(CurrentPriceView view, InitialPriceSetV2 evt) =>
    view with { BasePrice = evt.Price, FloorPrice = evt.FloorPrice };
```

### Best Practices (Learned from Marten Community)

1. **Additive changes are free** — Add optional fields with default values
2. **Avoid removing fields** — Mark as deprecated instead
3. **Version explicitly if needed** — Use event type names: `ItemAddedV1`, `ItemAddedV2`
4. **Test migration** — Seed old events, verify new code handles them
5. **Projections are rebuild-able** — If schema changes, rebuild projections from events

**Reference:** Marten has extensive upcasting support for complex scenarios. CritterSupply hasn't needed it yet due to additive-only changes.

---

## Marten Configuration

### Standard CritterSupply Pattern

All BCs follow this configuration:

```csharp
var connectionString = builder.Configuration.GetConnectionString("postgres")
    ?? throw new Exception("The connection string 'postgres' was not found");

builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);
    opts.AutoCreateSchemaObjects = AutoCreate.All;
    opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);

    // Schema isolation per BC
    opts.DatabaseSchemaName = Constants.<BcName>.ToLowerInvariant();
    opts.DisableNpgsqlLogging = true;

    // Event sourcing setup
    opts.Events.StreamIdentity = StreamIdentity.AsGuid;

    // Inline snapshot projections
    opts.Projections.Snapshot<Cart>(SnapshotLifecycle.Inline);
    opts.Projections.Snapshot<Checkout>(SnapshotLifecycle.Inline);

    // Multi-stream projections
    opts.Projections.Add<CurrentPriceViewProjection>(ProjectionLifecycle.Inline);

    // Document store indexes (for query performance)
    opts.Schema.For<Return>()
        .Identity(x => x.Id)
        .Index(x => x.OrderId); // Fast lookup by foreign key
})
.AddAsyncDaemon(DaemonMode.Solo) // Solo for single-instance, HotCold for multi-instance
.UseLightweightSessions() // No identity map (better performance for most cases)
.IntegrateWithWolverine(config =>
{
    config.UseWolverineManagedEventSubscriptionDistribution = true;
});
```

### Configuration Options Explained

| Option | Purpose | CritterSupply Usage |
|--------|---------|---------------------|
| `AutoCreate.All` | Auto-create tables/schemas on startup | Development + CI (change to `None` in production) |
| `EnumStorage.AsString` | Store enums as strings in JSON | All BCs (readable JSON, safe schema evolution) |
| `DatabaseSchemaName` | Isolate BC data per schema | `orders`, `shopping`, `returns`, `pricing`, etc. |
| `StreamIdentity.AsGuid` | Use Guid for stream IDs | All event-sourced BCs |
| `DaemonMode.Solo` | Single daemon instance | Development + small APIs |
| `DaemonMode.HotCold` | Leader election for multi-instance | Production (future) |
| `UseLightweightSessions` | No identity map tracking | Default (better performance) |

### ⚠️ CRITICAL: AutoApplyTransactions() Policy is REQUIRED

**IMPORTANT:** The `AutoApplyTransactions()` Wolverine policy is **REQUIRED** for Marten integration, not optional.

**Why this matters:**
- Without `AutoApplyTransactions()`, Wolverine does NOT wrap handlers in transactional middleware
- Marten changes (events, documents) are NOT automatically committed
- Handlers complete successfully but no data is persisted to the database
- **Silent failure** — no exceptions thrown, handler returns success, but database remains unchanged

**✅ CORRECT — AutoApplyTransactions() configured:**
```csharp
builder.Host.UseWolverine(opts =>
{
    // ✅ REQUIRED: Enroll Marten handlers in transactional middleware
    opts.Policies.AutoApplyTransactions();

    // Also configure durable messaging
    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
});
```

**❌ ANTI-PATTERN — Missing AutoApplyTransactions() (silent failure):**
```csharp
builder.Host.UseWolverine(opts =>
{
    // ❌ WRONG: Missing AutoApplyTransactions()
    // Handlers will NOT automatically commit Marten changes
    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
});

// Result: Handler executes, returns success, but no database changes persist!
```

**Where to find this in your codebase:**
Every BC's `Program.cs` file MUST include `opts.Policies.AutoApplyTransactions()` in the Wolverine configuration.

**Example: Orders BC Program.cs:**
```csharp
builder.Host.UseWolverine(opts =>
{
    opts.Policies.AutoApplyTransactions();  // ✅ Present in all working BCs
    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

    opts.Discovery.IncludeAssembly(typeof(Program).Assembly);
    opts.Discovery.IncludeAssembly(typeof(Orders.Order.Order).Assembly);
});
```

**How to diagnose missing policy:**
1. Handler returns success but data not in database
2. No exceptions thrown in handler or middleware
3. Events not persisted to `mt_events` table
4. Projections not updated (queries return stale/empty results)
5. Check Program.cs for `opts.Policies.AutoApplyTransactions()` — if missing, add it

**Reference:** All working BCs in CritterSupply (Orders, Shopping, Returns, Pricing, Inventory, Fulfillment, Payments, Promotions, Correspondence, Backoffice, Vendor Identity) include this policy.

### Session Types

| Session Type | When to Use | Example |
|--------------|-------------|---------|
| `LightweightSession()` | Most handlers (no change tracking) | Command handlers, queries |
| `DirtyTrackedSession()` | Change tracking needed (rare) | Complex updates with conditional saves |
| `QuerySession()` | Read-only queries | HTTP GET endpoints |

**CritterSupply uses lightweight sessions by default** — change tracking not needed when Wolverine manages transactions.

---

## Anti-Patterns to Avoid

### ❌ 1. Mutable State

**BAD:**
```csharp
public sealed record Cart(
    Guid Id,
    List<CartLineItem> Items)  // ❌ Mutable list!
{
    public void Apply(ItemAdded @event)
    {
        Items.Add(new CartLineItem(@event.Sku, @event.Quantity));  // ❌ Mutation!
    }
}
```

**GOOD:**
```csharp
public sealed record Cart(
    Guid Id,
    IReadOnlyList<CartLineItem> Items)  // ✅ Immutable list
{
    public Cart Apply(ItemAdded @event) =>
        this with { Items = Items.Append(new CartLineItem(@event.Sku, @event.Quantity)).ToList() };
}
```

### ❌ 2. Business Logic in Apply Methods

**BAD:**
```csharp
public Cart Apply(ItemAdded @event)
{
    if (@event.Quantity <= 0)  // ❌ Validation in Apply!
        throw new InvalidOperationException("Quantity must be positive");

    // ... rest of Apply logic
}
```

**GOOD:**
```csharp
// Validation in handler or validator
public static ProblemDetails Before(AddItemToCart command)
{
    if (command.Quantity <= 0)
        return new ProblemDetails { Detail = "Quantity must be positive", Status = 400 };

    return WolverineContinue.NoProblems;
}

// Apply method only transforms state
public Cart Apply(ItemAdded @event)
{
    var updatedItems = new Dictionary<string, CartLineItem>(Items);
    updatedItems[@event.Sku] = new CartLineItem(@event.Sku, @event.Quantity, @event.UnitPrice);
    return this with { Items = updatedItems };
}
```

### ❌ 3. Side Effects in Apply Methods

**BAD:**
```csharp
public Cart Apply(ItemAdded @event)
{
    Console.WriteLine($"Item added: {@event.Sku}");  // ❌ Side effect (logging)!

    var updatedItems = new Dictionary<string, CartLineItem>(Items);
    updatedItems[@event.Sku] = new CartLineItem(@event.Sku, @event.Quantity, @event.UnitPrice);
    return this with { Items = updatedItems };
}
```

**GOOD:**
```csharp
// Apply method is pure (no side effects)
public Cart Apply(ItemAdded @event)
{
    var updatedItems = new Dictionary<string, CartLineItem>(Items);
    updatedItems[@event.Sku] = new CartLineItem(@event.Sku, @event.Quantity, @event.UnitPrice);
    return this with { Items = updatedItems };
}

// Logging happens in handler, not Apply
public static (Events, OutgoingMessages) Handle(
    AddItemToCart command,
    ILogger<AddItemToCartHandler> logger,
    [WriteAggregate] Cart cart)
{
    var @event = new ItemAdded(command.Sku, command.Quantity, command.UnitPrice, DateTimeOffset.UtcNow);
    logger.LogInformation("Adding item {Sku} to cart {CartId}", command.Sku, cart.Id);
    return ([@event], new OutgoingMessages());
}
```

### ❌ 4. Inconsistent Parameter Naming

**BAD:**
```csharp
public Cart Apply(ItemAdded evt) =>   // ❌ Use @event, not evt
    this with { /* ... */ };

public Cart Apply(ItemRemoved e) =>   // ❌ Use @event, not e
    this with { /* ... */ };
```

**GOOD:**
```csharp
public Cart Apply(ItemAdded @event) =>   // ✅ Always @event
    this with { /* ... */ };

public Cart Apply(ItemRemoved @event) =>  // ✅ Always @event
    this with { /* ... */ };
```

### ❌ 5. Block Bodies When Expression Bodies Work

**BAD:**
```csharp
public Cart Apply(CartCleared @event)
{
    return this with { Status = CartStatus.Cleared };  // ❌ Unnecessary block body
}
```

**GOOD:**
```csharp
public Cart Apply(CartCleared @event) =>
    this with { Status = CartStatus.Cleared };  // ✅ Expression body
```

**Exception:** Use block bodies when you need temporary variables (e.g., `Cart.Apply(ItemAdded)` with dictionary manipulation).

### ❌ 6. Missing Aggregate ID in Events

**BAD:**
```csharp
public sealed record ItemAdded(
    string Sku,           // ❌ Missing CartId!
    int Quantity,
    decimal UnitPrice,
    DateTimeOffset AddedAt);
```

**GOOD:**
```csharp
public sealed record ItemAdded(
    Guid CartId,          // ✅ Always include aggregate ID first
    string Sku,
    int Quantity,
    decimal UnitPrice,
    DateTimeOffset AddedAt);
```

### ❌ 7. HTTP-Based Testing of Event-Sourced Aggregates

**BAD:**
```csharp
// ❌ Race condition — transaction may not be committed
await _fixture.Host.Scenario(s => s.Post.Json(command).ToUrl(url));
await _fixture.Host.Scenario(s => s.Get.Url(url)); // Stale data!
```

**GOOD:**
```csharp
// ✅ Direct command invocation — waits for transaction
await _fixture.ExecuteAndWaitAsync(command);
var aggregate = await session.Events.AggregateStreamAsync<Return>(returnId);
```

See [Testing Event-Sourced Systems](#testing-event-sourced-systems) for details.

### ❌ 8. Forgetting Snapshot Projections for Queryable Aggregates

**Problem:** `session.Query<T>()` returns empty even after successfully executing commands that create or modify the aggregate.

**Symptom:**
```csharp
// After successful command execution:
await _fixture.ExecuteAndWaitAsync(new CreatePromotion(...));

// Query returns empty/null:
var promotions = await session.Query<Promotion>().ToListAsync();
// promotions.Count == 0 ❌

// BUT aggregate exists in event stream:
var promotion = await session.Events.AggregateStreamAsync<Promotion>(promotionId);
// promotion != null ✅
```

**Root Cause:** Marten doesn't automatically make event-sourced aggregates queryable via LINQ. Without a snapshot projection configuration, the aggregate only exists as an event stream in `mt_events` table, not as a queryable document.

**BAD:**
```csharp
// Missing snapshot configuration
builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);
    // ❌ No projection configured for Promotion aggregate
});

// Tests fail with "Sequence contains no elements"
var promotions = await session.Query<Promotion>().ToListAsync();
var promotion = promotions.Single(); // ❌ Throws!
```

**GOOD:**
```csharp
// Configure snapshots for queryable aggregates
builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);

    // ✅ Inline snapshots make aggregates queryable via LINQ
    opts.Projections.Snapshot<Promotion>(SnapshotLifecycle.Inline);
    opts.Projections.Snapshot<Coupon>(SnapshotLifecycle.Inline);
});

// Now queries work as expected
var promotions = await session.Query<Promotion>().ToListAsync();
var promotion = promotions.Single(); // ✅ Works!
```

**Why This Works:**
- Snapshot projections create `mt_doc_<aggregate>` tables
- `SnapshotLifecycle.Inline` = zero-lag updates in same transaction as event append
- Makes aggregates queryable via `session.Query<T>()` and LINQ
- Standard pattern across all CritterSupply BCs: Shopping (Cart), Orders (Checkout), Returns (Return), Pricing (ProductPrice), Promotions (Promotion, Coupon)

**When to Use Snapshots:**
- ✅ **Always** for aggregates you need to query via LINQ (e.g., `session.Query<Cart>().Where(...)`)
- ✅ **Always** for aggregates with 10+ events (avoids replay latency)
- ❌ **Never** for aggregates only loaded by ID (snapshot overhead not justified)
- ❌ **Never** for short-lived aggregates deleted after use

**Diagnosis Steps:**
1. Verify events are being stored: Check `mt_events` table or Wolverine tracking output
2. Try loading via event stream: `await session.Events.AggregateStreamAsync<T>(id)` — if this works, events exist ✅
3. Try LINQ query: `await session.Query<T>().ToListAsync()` — if this returns empty, missing snapshot ❌
4. Solution: Add `opts.Projections.Snapshot<T>(SnapshotLifecycle.Inline)` to Program.cs

**Real-World Example:**
- **Issue:** Promotions BC integration tests (Cycle 29 Phase 2) failing with "Sequence contains no elements"
- **Fix:** Added snapshot projections for Promotion and Coupon aggregates
- **Reference:** `docs/planning/cycles/cycle-29-phase-2-retrospective-notes.md` (Session 2, Lesson #3)
- **Commit:** Promotions BC Program.cs configuration

---

## Lessons Learned from Production

### L1: Integration Queue Wiring Must Be Verified End-to-End (Cycle 26)

**What happened:** Fulfillment BC configured to publish `ShipmentDelivered` but didn't wire it to the Returns queue. Tests passed because they seeded `ReturnEligibilityWindow` directly. In production, no return eligibility windows would have been created.

**Root cause:** No cross-BC integration test verifying publish → consume pipeline.

**Fix:** Added cross-BC smoke tests for all RabbitMQ queues.

**Takeaway:** Unit tests + handler tests aren't enough. Verify the full integration pipeline end-to-end.

### L2: Contract Expansion Must Include All Downstream Consumers (Cycle 26)

**What happened:** `ReturnCompleted` only carried `FinalRefundAmount`. Inventory BC needs per-item disposition; Customer Experience BC needs per-item refund breakdown.

**Root cause:** Contract design focused on immediate consumer (Orders saga) without considering future consumers.

**Fix:** Expanded `ReturnCompleted` to carry `IReadOnlyList<ReturnedItem>` with per-item data.

**Takeaway:** When designing integration events, document **all known consumers** and their data requirements.

### L3: Event Sourcing Race Conditions in HTTP-Based Tests (Cycle 26 L5)

**What happened:** Integration tests failed with 409 (Conflict) status codes when expecting 200. Command handlers with `[WriteAggregate]` returned domain events, but Wolverine's transaction middleware committed asynchronously AFTER the HTTP response was sent.

**Root cause:** HTTP-based testing pattern (POST command → immediate GET verification) doesn't respect eventual consistency in event sourcing.

**Fix:** Refactored tests to use direct command invocation (`ExecuteAndWaitAsync`) and query event store directly.

**Takeaway:** Use direct command invocation for event-sourced aggregate tests. HTTP-based testing better suited for E2E tests where eventual consistency delays are expected.

**Reference:** [Testing Event-Sourced Systems](#testing-event-sourced-systems), Cycle 26 Retrospective

### L4: Saga Terminal State Handlers Must Cover All Terminal Events (Cycle 26)

**What happened:** Orders saga only handled 3 of 6 return-related messages. `ReturnRejected` and `ReturnExpired` left the saga in a dangling state (`IsReturnInProgress = true` permanently).

**Root cause:** Phase 1 only implemented the events it published; downstream handlers weren't verified.

**Fix:** Added handlers for all terminal return events.

**Takeaway:** When adding integration events, always verify **ALL consumers handle ALL terminal states**.

### L5: Document-Based Saga vs Event-Sourced Aggregate (Cycle 20, ADR 0029)

**What happened:** Initial Order implementation used event sourcing for saga state. Performance degraded as saga processed more messages (event replay on every handler invocation).

**Root cause:** Sagas are write-heavy, read-light. Event sourcing optimizes for history, not coordination velocity.

**Fix:** Migrated Order saga to document-based storage with numeric revisions.

**Takeaway:** Use event sourcing for domain aggregates where history matters. Use document store for sagas where coordination velocity matters.

**Reference:** [ADR 0029: Order Saga Design Decisions](../decisions/0029-order-saga-design-decisions.md)

---

## Related Documentation

### CritterSupply Skills

- [Wolverine Message Handlers](./wolverine-message-handlers.md) — Handler patterns, compound handlers, return types
- [Wolverine Sagas](./wolverine-sagas.md) — Stateful orchestration patterns (Order lifecycle)
- [Marten Document Store](./marten-document-store.md) — Non-event-sourced persistence patterns
- [CritterStack Testing Patterns](./critterstack-testing-patterns.md) — Event sourcing race conditions, direct command invocation
- [TestContainers Integration Tests](./testcontainers-integration-tests.md) — TestFixture patterns for Marten

### Architectural Decision Records

- [ADR 0016: UUID v5 for Natural-Key Stream IDs](../decisions/0016-uuid-v5-for-natural-key-stream-ids.md)
- [ADR 0029: Order Saga Design Decisions](../decisions/0029-order-saga-design-decisions.md)

### Cycle Retrospectives

- [Cycle 20 Retrospective](../planning/cycles/cycle-20-retrospective.md) — Order saga document vs event-sourced decision
- [Cycle 21 Retrospective](../planning/cycles/cycle-21-retrospective.md) — Pricing BC multi-stream projection
- [Cycle 26 Retrospective](../planning/cycles/cycle-26-returns-bc-phase-2-retrospective.md) — Event sourcing race conditions (L5)

### Official Documentation

- [Marten Event Sourcing](https://martendb.io/events/)
- [Marten Projections](https://martendb.io/events/projections/)
- [Wolverine Marten Integration](https://wolverinefx.net/guide/durability/marten/)
- [Decider Pattern (Jérémie Chassaing)](https://thinkbeforecoding.com/post/2021/12/17/functional-event-sourcing-decider)

---

## Real Codebase Examples

| Pattern | Example File | Lines | Notes |
|---------|--------------|-------|-------|
| **Simple event-sourced aggregate** | `src/Shopping/Shopping/Cart/Cart.cs` | 75 | Dictionary state, inline Apply methods |
| **Event-sourced aggregate with expression bodies** | `src/Orders/Orders/Checkout/Checkout.cs` | 90 | Clean example of expression body syntax |
| **UUID v5 deterministic stream ID** | `src/Pricing/Pricing/Products/ProductPrice.cs` | 119 | StreamId() method, RFC 4122 compliant |
| **Decider pattern (separate class)** | `src/Orders/Orders/Placement/OrderDecider.cs` | 250+ | Pure functions, testable in isolation |
| **Document-backed saga** | `src/Orders/Orders/Placement/Order.cs` | 300+ | NOT event-sourced, numeric revisions |
| **Saga initialization handler** | `src/Orders/Orders/Placement/PlaceOrderHandler.cs` | 50 | Returns (Order, OrderPlaced) tuple |
| **Multi-stream projection** | `src/Pricing/Pricing/Products/CurrentPriceViewProjection.cs` | 134 | Guid streams → string-keyed documents |
| **Direct command invocation tests** | `tests/Returns/Returns.Api.IntegrationTests/ReturnLifecycleEndpointTests.cs` | 250+ | ExecuteAndWaitAsync pattern |
| **Marten configuration** | `src/Orders/Orders.Api/Program.cs` | 60 | Snapshot projections, schema setup |
| **Marten configuration** | `src/Pricing/Pricing.Api/Program.cs` | 60 | Multi-stream projection registration |

---

*Last updated: 2026-03-13*
*Reflects lessons learned through Cycle 27 (Returns BC Phase 3)*
