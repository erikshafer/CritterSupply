# Event-Sourced Aggregate Design with Marten + Wolverine

Patterns for building event-sourced aggregates with Marten in CritterSupply, following **Wolverine's flavor of the Decider pattern**.

## Core Principles

1. **Aggregates are immutable records** — No mutable state, use `with` expressions
2. **Pure functions for Apply methods** — Events transform state without side effects
3. **No behavior in aggregates** — Only data + Apply methods. Business logic lives in handlers or Decider classes.
4. **Decider pattern via Wolverine** — Business decisions live in handlers, not aggregates
5. **No base classes** — No `Aggregate` base class or `IEntity` interface
6. **Functional programming mindset** — Aggregates return new instances (like functional programming)

---

## CritterSupply Conventions

### 1. Parameter Naming: Always `@event`

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

---

### 2. Expression Body Syntax for Apply Methods

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

---

### 3. No Behavior in Aggregates

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

---

### 4. Static vs Instance Methods

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

**When to use static methods:**
- If you have a strong functional programming background and prefer explicit state passing
- When the aggregate has no state dependencies (rare)

**Important:** Be consistent within a bounded context. Do not mix instance and static Apply methods in the same aggregate.

---

### 5. Functional Programming Mindset

**Event-sourced aggregates are functional data structures:**

```csharp
// Aggregate is a pure function: (CurrentState, Event) → NewState
var cart = /* current cart */;
var @event = new ItemAdded(sku, quantity, unitPrice, DateTimeOffset.UtcNow);
var updatedCart = cart.Apply(@event);  // Returns NEW cart, doesn't mutate
```

**This is why:**
- Aggregates are `sealed record` (immutable by default)
- Apply methods return `this with { ... }` (new instance)
- No side effects (no HTTP calls, no logging, no database writes)
- Pure functions (same inputs → same outputs, every time)

**Functional programming benefits for event sourcing:**
- Testability (no mocks needed — just input → output)
- Replayability (events can be replayed to rebuild state)
- Time-travel debugging (rebuild state at any point in time)
- Parallel event processing (no shared mutable state)

---

## Event-Sourced Aggregate Structure

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

> **Reference:** [Marten Projections](https://martendb.io/events/projections/)

---

## Status Enum Pattern

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

---

## Decider Pattern with Wolverine

**The Decider pattern separates decision logic from state transformation.**

CritterSupply uses two flavors of the Decider pattern:

### Flavor 1: Inline Handler Logic (Simple Cases)

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

---

### Flavor 2: Separate Decider Class (Complex Cases)

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

> **Reference:** `src/Orders/Orders/Placement/OrderDecider.cs`, `src/Orders/Orders/Placement/Order.cs`

**Reference:** [Decider Pattern](https://thinkbeforecoding.com/post/2021/12/17/functional-event-sourcing-decider)

---

## Deterministic Stream IDs (UUID v5)

For aggregates where the stream ID is derived from a natural key (e.g., SKU), use **UUID v5** (SHA-1 + namespace):

```csharp
public sealed record ProductPrice(
    Guid Id,
    string Sku,
    /* ... */)
{
    /// <summary>
    /// Generates a deterministic UUID v5 stream ID from SKU string.
    /// WHY NOT UUID v7: v7 is timestamp-random, cannot produce same value twice from same input.
    /// WHY NOT MD5: not RFC 4122-compliant, no namespace isolation.
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
            Id = StreamId(sku),
            Sku = sku.ToUpperInvariant(),
            Status = PriceStatus.Unpriced,
            RegisteredAt = registeredAt
        };
}
```

**When to use UUID v5:**
- ✅ Stream ID is derived from a natural key (SKU, email, username, etc.)
- ✅ Multiple handlers must resolve the same stream ID without a lookup
- ✅ You need deterministic IDs for idempotency

**When to use UUID v7:**
- ✅ Stream ID is generated at creation time (most aggregates)
- ✅ No natural key exists (Cart, Order, Payment, Checkout, etc.)

> **Reference:** `src/Pricing/Pricing/Products/ProductPrice.cs`, [ADR 0016: UUID v5 for Natural-Key Stream IDs](../decisions/0016-uuid-v5-for-natural-key-stream-ids.md)

---

## Marten Configuration

```csharp
builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = Constants.BcName.ToLowerInvariant();

    // Inline snapshot projections for immediate consistency
    opts.Projections.Snapshot<Cart>(SnapshotLifecycle.Inline);
    opts.Projections.Snapshot<Checkout>(SnapshotLifecycle.Inline);
    opts.Projections.Snapshot<ProductPrice>(SnapshotLifecycle.Inline);
});
```

> **Reference:** [Marten Snapshot Projections](https://martendb.io/events/projections/aggregate-projections.html)

---

## Starting Event Streams

### From HTTP Endpoint

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

    var stream = MartenOps.StartStream<Cart>(cartId, @event);
    return (stream, new CreationResponse($"/api/carts/{cartId}"));
}
```

### From Message Handler

```csharp
public static OutgoingMessages Handle(
    ProductAdded message,
    IDocumentSession session)
{
    var productPrice = ProductPrice.Create(message.Sku, DateTimeOffset.UtcNow);
    var @event = new ProductRegistered(
        productPrice.Id,
        message.Sku,
        DateTimeOffset.UtcNow);

    session.Events.StartStream<ProductPrice>(productPrice.Id, @event);

    return new OutgoingMessages();
}
```

---

## Loading Aggregates

```csharp
// In a handler — let Wolverine load it
public static (Events, OutgoingMessages) Handle(
    ChangePrice command,
    [WriteAggregate] ProductPrice productPrice)  // Wolverine loads by Id
{
    var @event = new PriceChanged(/* ... */);
    return ([@event], new OutgoingMessages());
}

// Manual loading when needed
var productPrice = await session.Events.AggregateStreamAsync<ProductPrice>(productPriceId, ct);
```

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

---

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

---

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

---

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

---

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

---

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

---

## When to Use Event Sourcing

**Use event sourcing for:**
- ✅ Transactional data with frequent state changes (Orders, Carts, Payments, Inventory)
- ✅ When historical changes are valuable (audit, replay, temporal queries)
- ✅ Saga/orchestration patterns (Order lifecycle coordination)
- ✅ Complex business logic benefiting from event-driven design
- ✅ When you need to rebuild read models from events

**Use document store instead for:**
- ✅ Master data with infrequent changes (Product Catalog)
- ✅ Read-heavy workloads (product listings, category hierarchies)
- ✅ When current state is all that matters (no audit trail needed)
- ✅ Simple CRUD operations

See `docs/skills/marten-document-store.md` for document store patterns.

---

## Related Documentation

- [Wolverine Message Handlers](./wolverine-message-handlers.md) — Handler patterns, compound handlers, return types
- [Wolverine Sagas](./wolverine-sagas.md) — Stateful orchestration patterns (Order lifecycle)
- [Marten Document Store](./marten-document-store.md) — Non-event-sourced persistence patterns
- [ADR 0016: UUID v5 for Natural-Key Stream IDs](../decisions/0016-uuid-v5-for-natural-key-stream-ids.md)
- [Marten Event Sourcing Documentation](https://martendb.io/events/)
- [Decider Pattern](https://thinkbeforecoding.com/post/2021/12/17/functional-event-sourcing-decider)

---

## Real Codebase Examples

- `src/Shopping/Shopping/Cart/Cart.cs` — Simple aggregate with dictionary state
- `src/Orders/Orders/Checkout/Checkout.cs` — Simple aggregate with expression bodies
- `src/Pricing/Pricing/Products/ProductPrice.cs` — Deterministic stream ID (UUID v5)
- `src/Orders/Orders/Placement/Order.cs` — Saga (not pure event-sourced aggregate)
- `src/Orders/Orders/Placement/OrderDecider.cs` — Separate Decider class pattern
