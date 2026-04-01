# Critter Stack Event Sourcing Projections

**Purpose:** Building read models (projections) from event-sourced aggregates in the Critter Stack (Marten + Polecat) — transforming append-only event streams into queryable denormalized views.

**When to read this:** Before implementing any projection from event-sourced aggregates, especially when you need queryable read models for hot-path queries, reporting, or cross-stream aggregation.

**Scope:** This document covers projections specifically — the read/view model layer. For event-sourced aggregate design, command handlers, and event modeling, see `marten-event-sourcing.md`.

---

## Table of Contents

1. [Overview](#overview)
2. [When to Use Projections](#when-to-use-projections)
3. [Projection Types and Lifecycles](#projection-types-and-lifecycles)
4. [Single-Stream Projections](#single-stream-projections)
5. [Multi-Stream Projections](#multi-stream-projections)
6. [Live Aggregation and FetchForWriting](#live-aggregation-and-fetchforwriting)
7. [Snapshot Projections](#snapshot-projections)
8. [Projection Registration and Configuration](#projection-registration-and-configuration)
9. [Querying Projected Documents](#querying-projected-documents)
10. [Testing Projection Logic](#testing-projection-logic)
11. [Common Pitfalls and Warnings](#common-pitfalls-and-warnings)
12. [Production Lessons Learned](#production-lessons-learned)
13. [Decision Matrix: Which Projection Pattern?](#decision-matrix-which-projection-pattern)

---

## Overview

**What are projections?**

Projections are denormalized read models built from event-sourced aggregates. They transform append-only event streams (the write model) into queryable documents or relational tables (the read model). This is the **read side** of CQRS (Command Query Responsibility Segregation).

**Why projections matter:**

- **Performance:** Querying raw event streams is slow. Projections pre-compute views.
- **Denormalization:** Combine data from multiple event streams into a single queryable document.
- **Query optimization:** Enable indexes, full-text search, and LINQ queries.
- **Separation of concerns:** Write model (aggregates) optimized for business logic; read model (projections) optimized for queries.

**The Critter Stack:** This document covers both **Marten** (PostgreSQL) and **Polecat** (SQL Server). Both libraries share the same projection API surface — code is portable with minimal changes.

---

## When to Use Projections

✅ **Use projections when:**

1. **Hot-path queries need <100ms response times** — Example: `CurrentPriceView` (Pricing BC), `CouponLookupView` (Promotions BC)
2. **Querying raw event streams is too slow** — Example: `Checkout` snapshot (Orders BC) avoids replaying 5-10 events
3. **You need denormalized views across multiple streams** — Example: Pricing's `CurrentPriceView` (Guid streams → string-keyed documents)
4. **Business logic requires queryable aggregate state** — Example: `SetPrice` handler validates floor/ceiling constraints
5. **You're building a read-optimized view for a BFF** — Example: Storefront BFF queries `CurrentPriceView` to compose `CartView`
6. **BFF-owned projections for cross-BC aggregation** — Example: `AdminDailyMetrics` (Backoffice BC) aggregates events from Orders, Payments, Inventory, Fulfillment

❌ **Do NOT use projections when:**

1. **The event stream itself is the query model** — Example: audit logs
2. **Write-only aggregates** — If you only append events and never query
3. **Cross-BC HTTP queries are simpler** — BFFs often compose via HTTP instead of projecting locally

---

## Projection Types and Lifecycles

### The Three Projection Types

| Type | Use Case | Example from CritterSupply |
|------|----------|----------------------------|
| **Single-Stream** | Aggregate a single event stream into one document | `Checkout` snapshot (Orders BC) |
| **Multi-Stream** | Aggregate events across multiple streams by a property | `CurrentPriceView` (Pricing BC) — Guid streams → SKU-keyed documents |
| **Live Aggregation** | On-demand event replay (no persistent projection) | Command handlers using `FetchForWriting()` |

### The Three Projection Lifecycles

| Lifecycle | When Events Are Processed | Latency | Use Case |
|-----------|---------------------------|---------|----------|
| **Inline** | Same transaction as command | 0ms | Hot-path queries (price lookups, coupon validation) |
| **Async** | Background daemon (seconds) | 1-5s | Reporting views, analytics, less-critical reads |
| **Live** | On-demand query (event replay every time) | 50-500ms | Ad-hoc queries, admin views, rarely-used endpoints |

**Key insight:** Inline projections are **zero-lag** — the projection document is updated in the same transaction that appends the event.

---

## Single-Stream Projections

### Overview

**Single-stream projections** aggregate a single event stream into one document. One stream → One projection document.

**When to use:**
- Snapshot queryable aggregates (avoid event replay on every query)
- Denormalize aggregate state into a read-optimized shape
- Enable indexed queries on aggregate properties

### Anatomy: Create() and Apply() Methods

**Key conventions:**
- `Create()` — Optional; runs on first event
- `Apply()` — One overload per event type
- `Evolve(IEvent e)` — Alternative single-method style for explicit switch-based projection logic
- Method names are exact: `Create`, `Apply`, and `Evolve` (case-sensitive)
- Return type must match document type

```csharp
public sealed class MyProjection : SingleStreamProjection<MyDocument>
{
    public MyDocument Create(FirstEvent evt) =>
        new MyDocument { Id = evt.AggregateId, Property = evt.Value };

    public static MyDocument Apply(MyDocument current, SubsequentEvent evt) =>
        current with { Property = evt.NewValue };
}
```

**Third option — `Evolve(IEvent e)`:** Marten 8.27 / Polecat 1.5 also support defining single-stream projection logic with an `Evolve(IEvent e)` method on the aggregate or projection type instead of separate `Apply()` overloads. Use this when a switch expression is clearer for your team or when event subclass hierarchies make `Apply()` conventions ambiguous. Existing CritterSupply `Apply()` projections remain correct; Jeremy's guidance is to choose the style your team prefers rather than treating `Evolve()` as a mandatory replacement.

### Example: Checkout Snapshot (Orders BC)

```csharp
// Registration
opts.Projections.Snapshot<Checkout>(SnapshotLifecycle.Inline);

// Aggregate with Create/Apply methods
public sealed record Checkout
{
    public Guid Id { get; init; }
    public CheckoutStatus Status { get; init; }
    public IReadOnlyList<CheckoutLineItem> Items { get; init; } = [];

    public static Checkout Create(CheckoutInitiated evt) =>
        new Checkout { Id = evt.CheckoutId, Status = CheckoutStatus.InProgress, Items = evt.Items };

    public static Checkout Apply(Checkout current, CheckoutCompleted evt) =>
        current with { Status = CheckoutStatus.Completed };
}
```

**Why inline?** Hot-path queries (cart expiry, checkout status) require zero lag.

---

## Multi-Stream Projections

### Overview

**Multi-stream projections** aggregate events from multiple event streams into a single projection document. The document ID is derived from an event property, not the stream ID.

**Use cases:**
- **Cross-stream aggregation:** "All returns for a given SKU" (many return streams → one document per SKU)
- **Different stream ID vs. document ID:** Event streams use Guid IDs, but queries use string keys (e.g., SKU)
- **Grouping by property:** Multiple event streams contribute to one projection based on a shared property

### Anatomy: Identity<T>() Mapping

**Key concepts:**
- Call `Identity<TEvent>(x => x.Property)` in constructor for each event type
- The property value becomes the document ID (enables Guid streams → string documents)
- `Create()` determines which event type creates the document
- All events with matching identity values update the same document

```csharp
public sealed class MyProjection : MultiStreamProjection<MyDocument, string>
{
    public MyProjection()
    {
        Identity<EventA>(x => x.Sku);  // Map event property to document ID
        Identity<EventB>(x => x.Sku);
    }

    public MyDocument Create(EventA evt) =>
        new MyDocument { Id = evt.Sku };  // Document ID matches Identity

    public static MyDocument Apply(MyDocument current, EventB evt) =>
        current with { /* updates */ };
}
```

### Example: CurrentPriceView (Pricing BC)

```csharp
public sealed class CurrentPriceViewProjection : MultiStreamProjection<CurrentPriceView, string>
{
    public CurrentPriceViewProjection()
    {
        Identity<InitialPriceSet>(x => x.Sku);
        Identity<PriceChanged>(x => x.Sku);
        Identity<PriceChangeScheduled>(x => x.Sku);
    }

    public CurrentPriceView Create(InitialPriceSet evt) =>
        new CurrentPriceView
        {
            Id = evt.Sku,
            BasePrice = evt.Price.Amount,
            Status = PriceStatus.Published,
            LastUpdatedAt = evt.PricedAt
        };

    public static CurrentPriceView Apply(CurrentPriceView view, PriceChanged evt) =>
        view with
        {
            BasePrice = evt.NewPrice.Amount,
            PreviousBasePrice = evt.OldPrice.Amount,
            LastUpdatedAt = evt.ChangedAt
        };
}

// Registration
opts.Projections.Add<CurrentPriceViewProjection>(ProjectionLifecycle.Inline);
```

**Why this pattern?** `ProductPrice` streams use Guid IDs but queries use SKU strings. Multi-stream projection bridges: Guid streams → string-keyed documents via `Identity<>(x => x.Sku)`.

### Discriminated Unions for Projection Views (JSON Polymorphism)

For projections that produce different event types for SignalR or real-time updates, use C# discriminated unions via JSON polymorphism:

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "eventType")]
[JsonDerivedType(typeof(LiveMetricUpdated), typeDiscriminator: "live-metric-updated")]
[JsonDerivedType(typeof(AlertCreated), typeDiscriminator: "alert-created")]
public abstract record BackofficeEvent(DateTimeOffset OccurredAt);

public sealed record LiveMetricUpdated(
    int OrderCount,
    decimal Revenue,
    decimal PaymentFailureRate,
    DateTimeOffset OccurredAt) : BackofficeEvent(OccurredAt);
```

**Why this pattern?** Type safety, JSON serialization, extensibility, SignalR compatibility.

**When to use:**
- ✅ Projection produces multiple event types for real-time updates
- ✅ Events share common properties (e.g., `OccurredAt`, `TenantId`)
- ✅ SignalR or WebSocket transport is involved

---

## Live Aggregation and FetchForWriting

### Why FetchForWriting() Is Preferred

```csharp
// ✅ Use FetchForWriting() for command handlers
public static async Task<IEnumerable<object>> Handle(
    ChangePrice cmd,
    IDocumentSession session)
{
    var price = await session.Events.FetchForWriting<ProductPrice>(ProductPrice.StreamId(cmd.Sku));

    return [new PriceChanged(...)];  // Marten auto-appends with optimistic concurrency
}
```

**Marten 8.27 awareness:** `FetchForWriting()` now auto-discovers natural keys without requiring explicit projection registration for that natural-key support. If your aggregate already uses a natural key, you may be able to load it directly by that natural key instead of precomputing a stream id; keep the explicit `StreamId()` helper when that remains the clearest fit for the aggregate design.

**What FetchForWriting() does:**
1. Replays events to reconstitute aggregate
2. Tracks stream version for optimistic concurrency
3. Automatically appends events returned by handler
4. Throws `ConcurrencyException` if stream modified concurrently
5. Uses inline snapshot if available (performance boost)

### When to Use Each Pattern

| Pattern | Use Case | Performance | Queryable? |
|---------|----------|-------------|------------|
| **Inline Snapshot** | Queryable aggregate (queries + commands) | ⚡ Fastest (no replay) | ✅ Yes |
| **FetchForWriting()** | Command-only (no queries) | 🟡 Medium (replay + concurrency) | ❌ No |
| **AggregateStreamAsync()** | Ad-hoc/debugging | 🔴 Slow (full replay) | ❌ No |

**Recommendation:** Use inline snapshots for queried aggregates; FetchForWriting() for write-only aggregates.

---

## Snapshot Projections

### Overview

**Snapshot projections** capture current aggregate state in a queryable document. The aggregate type itself is the projection document.

**Key differences:**
- Aggregate type **is** the projection document (no separate class)
- `Create()`/`Apply()` methods live on aggregate
- Configuration: `.Snapshot<T>()` (not `.Add<TProjection>()`)

✅ **Use snapshots when:**
- Aggregate is queried and modified
- Event replay is too slow (100+ events)
- Hot-path commands need fast aggregate loading

### Snapshot Lifecycles

| Lifecycle | When Updated | Use Case |
|-----------|-------------|----------|
| **Inline** | Same transaction | Hot-path queries (zero lag) |
| **Async** | Background daemon | Reporting, analytics |

**Inline is almost always correct** — if you need a snapshot, you likely need zero-lag queries.

### Example: Promotion and Coupon (Promotions BC)

```csharp
// Registration
opts.Projections.Snapshot<Promotion>(SnapshotLifecycle.Inline);
opts.Projections.Snapshot<Coupon>(SnapshotLifecycle.Inline);
```

**Why snapshots?** Even small streams (5-10 events) benefit from snapshots on hot paths. A 5-event replay (~10ms) × 1,000 queries/hour = 10 seconds cumulative overhead.

---

## Projection Registration and Configuration

### Registration in Program.cs

All projections are registered in the API's `Program.cs` inside the `AddMarten()` configuration block:

```csharp
builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = Constants.MyBc.ToLowerInvariant();

    // Inline projections (zero lag)
    opts.Projections.Add<MyInlineProjection>(ProjectionLifecycle.Inline);

    // Async projections (background daemon)
    opts.Projections.Add<MyAsyncProjection>(ProjectionLifecycle.Async);

    // Snapshot projections
    opts.Projections.Snapshot<MyAggregate>(SnapshotLifecycle.Inline);
})
.AddAsyncDaemon(DaemonMode.Solo);  // Required for async projections
```

### The Async Daemon

**What is the async daemon?** A background process that consumes events from `mt_events` and updates async projections.

**When is it required?** Only for `ProjectionLifecycle.Async` projections.

**DaemonMode options:**

| Mode | Use Case | Behavior |
|------|----------|----------|
| **Solo** | Single API instance | All projections run in this process |
| **HotCold** | Multiple API instances | Leader election — one instance runs projections |
| **Disabled** | No async projections | Daemon doesn't start |

### ⚠️ Critical Warning: Missing Projection Registration

**The silent query failure problem:**

```csharp
// ❌ ANTI-PATTERN: Projection class exists but is never registered
public sealed class MyProjection : SingleStreamProjection<MyDocument>
{
    // ... Create() and Apply() methods
}

// Program.cs — MISSING REGISTRATION
// No opts.Projections.Add<MyProjection>() call!

// Result: Queries return null (no error thrown)
var doc = await session.LoadAsync<MyDocument>(id);  // Always null!
```

**What happens:**
1. Marten creates table but projection logic **never runs**
2. Events are appended successfully (no errors)
3. Queries return `null` or empty results
4. **No exception thrown** — silent failure

**How to prevent:**
1. Always register projections immediately after creating them
2. Integration tests must query projected documents
3. Code review checklist: Every new projection class → verify `opts.Projections.Add<>()`

---

## Querying Projected Documents

### Querying by ID

```csharp
// Single document load
var priceView = await session.LoadAsync<CurrentPriceView>("DOG-FOOD-5LB");
if (priceView == null) { /* handle not found */ }
```

### Bulk Lookups with LoadManyAsync

```csharp
// Bulk load (single SQL query with WHERE IN)
var skus = new[] { "DOG-FOOD-5LB", "CAT-FOOD-3LB", "FISH-TANK-10G" };
var prices = await session.LoadManyAsync<CurrentPriceView>(skus);

// Returns Dictionary<string, CurrentPriceView>
foreach (var (sku, priceView) in prices)
{
    Console.WriteLine($"{sku}: ${priceView.BasePrice}");
}
```

**Performance:** `LoadManyAsync()` issues a single SQL query. Much faster than N individual `LoadAsync()` calls.

### LINQ Queries with Query<T>()

```csharp
// Query all published prices below $20
var cheapProducts = await session.Query<CurrentPriceView>()
    .Where(p => p.Status == PriceStatus.Published && p.BasePrice < 20m)
    .OrderBy(p => p.BasePrice)
    .Take(10)
    .ToListAsync();
```

### Cross-Document Queries

Projections live in separate tables. Joins require explicit queries:

```csharp
// ✅ CORRECT: Load separately, join in memory
var prices = await session.LoadManyAsync<CurrentPriceView>(skus);
var products = await session.LoadManyAsync<ProductInfo>(skus);
var joined = skus.Select(sku => new { Price = prices[sku], Product = products[sku] });
```

**Alternative:** Use `EfCoreMultiStreamProjection` (see `efcore-marten-projections.md`) if you need true relational joins.

---

## Testing Projection Logic

### Unit Testing Apply() Methods

Projection logic is **pure functions** — easy to unit test without infrastructure:

```csharp
public class CurrentPriceViewProjectionTests
{
    [Fact]
    public void Apply_PriceChanged_UpdatesPriceAndPreviousPrice()
    {
        // Arrange
        var current = new CurrentPriceView
        {
            Id = "DOG-FOOD-5LB",
            BasePrice = 24.99m,
            LastUpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        var evt = new PriceChanged(...);

        // Act
        var result = CurrentPriceViewProjection.Apply(current, evt);

        // Assert
        result.BasePrice.ShouldBe(19.99m);
        result.PreviousBasePrice.ShouldBe(24.99m);
    }
}
```

### Integration Testing with Alba

```csharp
[Fact]
public async Task SetPrice_UpdatesCurrentPriceView()
{
    // Arrange
    var sku = "TEST-SKU-" + Guid.NewGuid().ToString("N")[..8];
    var cmd = new SetPrice(sku, 24.99m, Guid.NewGuid());

    // Act
    var result = await _fixture.Host.PostJson($"/api/pricing/products/{sku}/price", cmd)
        .Receive<SetPriceResult>();

    // Assert — Check projection
    var view = await _fixture.LoadAsync<CurrentPriceView>(sku);
    view.ShouldNotBeNull();  // ⚠️ Catches missing projection registration
    view.BasePrice.ShouldBe(24.99m);
}
```

**Key assertion:** `view.ShouldNotBeNull()` — this fails if projection isn't registered. Event-only tests don't catch this bug.

---

## Common Pitfalls and Warnings

### ⚠️ Anti-Pattern #1: Missing Projection Registration

**Problem:** Projection class exists but never registered in `Program.cs`.
**Result:** Silent query failure. Queries return `null`, no exception thrown.
**Fix:** Always register projections immediately after creating them.

### ⚠️ Anti-Pattern #2: Using Async Lifecycle for Hot-Path Queries

**Problem:** Hot-path queries use async projections (eventual consistency lag).
**Fix:** Use inline lifecycle for hot-path queries.

### ⚠️ Anti-Pattern #3: Mutable Apply() Methods

**Problem:** `Apply()` methods mutate state instead of returning new state.
**Fix:** Use immutable records with `with` expressions.

### ⚠️ Anti-Pattern #4: Forgetting AddAsyncDaemon for Async Projections

**Problem:** Async projection registered but daemon not started.
**Fix:** Always call `.AddAsyncDaemon()` when using async projections.

---

## Production Lessons Learned

### Lesson 0: Inline Projections Require Explicit SaveChanges Before Querying

**From:** Backoffice BC (M32.0 Session 8)

**Problem:** Handler queried inline projection immediately after `Events.Append()` without calling `SaveChangesAsync()`.

**Root Cause:** Marten inline projections update **during** `SaveChangesAsync()`, not during `Events.Append()`.

**Fix:** Call `await session.SaveChangesAsync()` before querying the projection:

```csharp
// ✅ GOOD: Explicit SaveChanges before projection query
public static async Task<LiveMetricUpdated> Handle(
    OrderPlaced message,
    IDocumentSession session)
{
    // 1. Append event
    session.Events.Append(Guid.NewGuid(), message);

    // 2. Commit transaction (inline projection updates here)
    await session.SaveChangesAsync();

    // 3. Now query the updated projection
    var metrics = await session.LoadAsync<AdminDailyMetrics>(today);

    return new LiveMetricUpdated(...);
}
```

**When this pattern is needed:**
- Integration message handlers that append events **and** query inline projections
- Handlers returning SignalR events with projection data

### Lesson 1: Always Test Projected Documents, Not Just Events

**From:** Pricing BC (Cycle 29)

**Problem:** `CurrentPriceViewProjection` was implemented but not registered. Integration tests only checked event persistence.

**Fix:** Tests now query projected documents to catch missing registration.

**Takeaway:** Event-only tests are insufficient. Always query projected documents in integration tests.

### Lesson 2: Inline Projections for Hot-Path Queries Are Non-Negotiable

**From:** Pricing BC and Promotions BC (Cycle 29-30)

**Reality:** Hot-path queries cannot tolerate lag. Shopping BC's `AddItemToCart` validates price existence synchronously — 5 seconds of lag causes "price not found" errors.

**Decision:** Inline projections for all hot-path queries.

**Takeaway:** "Eventual consistency is fine" is rarely true for hot-path queries. Default to inline; use async only for reporting/analytics.

### Lesson 3: MultiStreamProjection Identity Mapping Is Subtle

**From:** Pricing BC `CurrentPriceView` (Cycle 29)

**Problem:** Forgot `Identity<>()` mapping for one event type. Result: Silent failure.

**Fix:** Every event type that updates a multi-stream projection MUST have `Identity<>()` mapping.

**Takeaway:** Integration tests should append all event types and verify projection updates.

### Lesson 4: Snapshot Projections Are Cheaper Than You Think

**From:** Promotions BC (Cycle 29-30)

**Reality:** A 5-event replay is ~10ms. For 1,000 queries/hour, that's 10 seconds of cumulative latency overhead. Snapshot eliminates all replay — worth it even for small streams.

**Takeaway:** Snapshot overhead (write-time projection update) is negligible. Benefits pay off even for small streams on hot paths.

### Lesson 5: FetchForWriting() vs. Snapshot — Know the Difference

**From:** Pricing BC `ProductPrice` (Cycle 29)

**Answer:** It depends on **query patterns**:
- **Snapshot:** Aggregate is queried directly
- **FetchForWriting():** Aggregate is write-only; separate projection handles queries

**Takeaway:** Don't snapshot aggregates that are never queried. Use `FetchForWriting()` for write-only workflows.

### Lesson 6: BFF-Owned Projections Avoid Need for Separate Analytics BC

**From:** Backoffice BC (M32.0 Sessions 6-7)

**Observation:** Backoffice BFF owns two Marten projections aggregating events from 7 domain BCs:
- **AdminDailyMetrics** — sourced from Orders, Payments, Inventory, Fulfillment
- **AlertFeedView** — sourced from Payments, Inventory, Fulfillment, Returns

**Decision:** BFF-owned projections are sufficient for Phase 1-2 operational dashboards. Defer Analytics BC until business analytics requirements mature.

**Why BFF-owned projections?**
1. **Lower infrastructure cost:** No separate Analytics BC API/database
2. **Faster delivery:** Projections added in 2 sessions vs. 5+ sessions for new BC
3. **Sufficient for Phase 1:** Operational dashboards don't need complex analytics
4. **Easy migration path:** Projections can be moved without changing domain BCs

**When to use BFF-owned projections:**
- ✅ Operational dashboards (real-time KPIs, alert feeds, executive summary)
- ✅ Cross-BC aggregation for UI composition
- ✅ Inline lifecycle required (zero-lag updates)

**When to create separate Analytics BC:**
- ❌ Complex analytics (time-series analysis, forecasting, ML models)
- ❌ Long-term data warehousing (years of historical data)
- ❌ Heavy BI tooling integration (Tableau, Power BI, etc.)

**Takeaway:** BFF-owned projections are a pragmatic alternative to creating a separate Analytics BC. Start with BFF projections; migrate to Analytics BC when requirements demand it.

### Lesson 7: Missing `AutoApplyTransactions()` Mimics Projection Timing Issues ⭐ *M36.0 Addition*

**From:** Product Catalog BC (M36.0)

**Problem:** 5 integration tests failed with stale projection data. Symptom was identical to an async projection race condition — projection state did not reflect events appended by the handler. The root cause was not projection timing but a missing `opts.Policies.AutoApplyTransactions()` in `Program.cs`.

**Why it was misclassified:** Without `AutoApplyTransactions()`, the Marten session is never committed. Events are appended to the in-memory session but never persisted to the database. Inline projections, which update during `SaveChangesAsync()`, never fire. The result — stale projection data — looks exactly like a projection lag issue.

**Diagnostic:** When projection tests fail with stale data:
1. First check `Program.cs` for `opts.Policies.AutoApplyTransactions()` — if missing, add it
2. Only investigate projection lifecycle or timing issues after confirming the policy is present

**Takeaway:** Always verify `AutoApplyTransactions()` before debugging projection timing. This policy is required in every Marten BC. See `marten-document-store.md` and `marten-event-sourcing.md` for full documentation.

---

## Decision Matrix: Which Projection Pattern?

Use this matrix to choose the correct projection pattern for your use case:

| Scenario | Pattern | Lifecycle | Example |
|----------|---------|-----------|---------|
| **Aggregate is queried and modified** | Snapshot | Inline | `Checkout` (Orders BC) |
| **Hot-path denormalized view** | Multi-Stream | Inline | `CurrentPriceView` (Pricing BC) |
| **Reporting / analytics view** | Single or Multi-Stream | Async | Returns analytics by SKU |
| **Write-only aggregate (commands only)** | FetchForWriting() | N/A | `ProductPrice` (Pricing BC) |
| **Admin/audit views (rarely queried)** | Single-Stream | Async | Price history audit log |
| **Cross-stream aggregation** | Multi-Stream | Inline or Async | Coupon usage by promotion ID |
| **Ad-hoc queries (debugging)** | Live Aggregation | Live | `AggregateStreamAsync()` in admin tools |

**Key decision points:**
1. **Is the aggregate queried?** → Yes: Snapshot. No: FetchForWriting().
2. **Is it a hot path?** → Yes: Inline. No: Async.
3. **Do you need cross-stream aggregation?** → Yes: Multi-Stream. No: Single-Stream.
4. **Is eventual consistency acceptable?** → Yes: Async. No: Inline.

---

**Next steps:**
- For aggregate design patterns, read `marten-event-sourcing.md`
- For EF Core as projection target, read `efcore-marten-projections.md`
- For Wolverine command handler integration, read `wolverine-message-handlers.md`
- For testing patterns, read `critterstack-testing-patterns.md`
