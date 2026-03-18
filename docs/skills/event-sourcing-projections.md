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
   - [Discriminated Unions for Projection Views (JSON Polymorphism)](#discriminated-unions-for-projection-views-json-polymorphism)
6. [Live Aggregation and FetchForWriting](#live-aggregation-and-fetchforwriting)
7. [Snapshot Projections](#snapshot-projections)
8. [Projection Registration and Configuration](#projection-registration-and-configuration)
9. [Event Metadata in Projections](#event-metadata-in-projections)
10. [Querying Projected Documents](#querying-projected-documents)
11. [Testing Projection Logic](#testing-projection-logic)
12. [Polecat Compatibility](#polecat-compatibility)
13. [Common Pitfalls and Warnings](#common-pitfalls-and-warnings)
14. [Production Lessons Learned](#production-lessons-learned)
    - [Lesson 0: Inline Projections Require Explicit SaveChanges Before Querying](#lesson-0-inline-projections-require-explicit-savechanges-before-querying) ⭐ *M32 Addition*
    - [Lesson 1: Always Test Projected Documents, Not Just Events](#lesson-1-always-test-projected-documents-not-just-events)
    - [Lesson 2: Inline Projections for Hot-Path Queries Are Non-Negotiable](#lesson-2-inline-projections-for-hot-path-queries-are-non-negotiable)
    - [Lesson 3: MultiStreamProjection Identity Mapping Is Subtle](#lesson-3-multiststreamprojection-identity-mapping-is-subtle)
    - [Lesson 4: Snapshot Projections Are Cheaper Than You Think](#lesson-4-snapshot-projections-are-cheaper-than-you-think)
    - [Lesson 5: FetchForWriting() vs. Snapshot — Know the Difference](#lesson-5-fetchforwriting-vs-snapshot--know-the-difference)
    - [Lesson 6: BFF-Owned Projections Avoid Need for Separate Analytics BC](#lesson-6-bff-owned-projections-avoid-need-for-separate-analytics-bc) ⭐ *M32 Addition*
15. [Decision Matrix: Which Projection Pattern?](#decision-matrix-which-projection-pattern)
16. [Appendix: How Projections Work Under the Hood](#appendix-how-projections-work-under-the-hood)

---

## Overview

**What are projections?**

Projections are denormalized read models built from event-sourced aggregates. They transform append-only event streams (the write model) into queryable documents or relational tables (the read model). This is the **read side** of CQRS (Command Query Responsibility Segregation).

**Why projections matter:**

- **Performance:** Querying raw event streams is slow (replaying hundreds of events per query). Projections pre-compute views.
- **Denormalization:** Projections can combine data from multiple event streams into a single queryable document.
- **Query optimization:** Projections enable indexes, full-text search, and LINQ queries that would be impossible on raw events.
- **Separation of concerns:** Write model (aggregates) optimized for business logic; read model (projections) optimized for queries.

**The Critter Stack:** This document covers both **Marten** (PostgreSQL) and **Polecat** (SQL Server). Both libraries share the same projection API surface — code is portable with minimal changes.

---

## When to Use Projections

✅ **Use projections when:**

1. **Hot-path queries need <100ms response times** — Projections pre-compute views so queries are simple lookups
   - Example: Product price lookup in `CurrentPriceView` (Pricing BC) — inline projection ensures zero lag
   - Example: Coupon code lookup in `CouponLookupView` (Promotions BC) — inline projection for redemption workflow

2. **Querying raw event streams is too slow** — Aggregate snapshots or inline projections avoid event replay overhead
   - Example: `Checkout` snapshot (Orders BC) — queries load single document instead of replaying 5-10 events
   - Example: `Promotion` snapshot (Promotions BC) — status checks don't require event replay

3. **You need denormalized views across multiple streams** — Multi-stream projections aggregate events by a non-stream property
   - Example: Pricing's `CurrentPriceView` — Guid streams → string-keyed documents (SKU as ID)
   - Example: Returns analytics — aggregate return items by product SKU across many return streams

4. **Business logic requires queryable aggregate state** — Command handlers need to check aggregate state before appending events
   - Example: `SetPrice` handler (Pricing BC) loads `ProductPrice` snapshot to validate floor/ceiling constraints
   - Example: Coupon redemption checks `Coupon` snapshot status before appending `CouponRedeemed`

5. **You're building a read-optimized view for a BFF** — Projections create tailored views for UI composition
   - Example: Storefront BFF queries `CurrentPriceView` to compose `CartView` with live pricing
   - Example: Backoffice BFF owns `AdminDailyMetrics` and `AlertFeedView` projections aggregating events from 7 domain BCs

6. **BFF-owned projections for cross-BC aggregation** — BFF projections aggregate events from multiple domain BCs into unified views
   - Example: `AdminDailyMetrics` (Backoffice BC) — sources events from Orders, Payments, Inventory, Fulfillment
   - Example: `AlertFeedView` (Backoffice BC) — sources events from Payments, Inventory, Fulfillment, Returns
   - Pattern: BFF subscribes to integration messages from domain BCs, appends to BFF's event store, inline projections aggregate into queryable views

❌ **Do NOT use projections when:**

1. **The event stream itself is the query model** — If consumers query raw events (e.g., audit logs), no projection needed
2. **Write-only aggregates** — If you only append events and never query, skip projections (e.g., audit trail aggregates)
3. **Cross-BC HTTP queries are simpler** — BFFs often compose via HTTP instead of projecting locally

---

## Projection Types and Lifecycles

### The Three Projection Types

| Type | Use Case | Example from CritterSupply |
|------|----------|----------------------------|
| **Single-Stream** | Aggregate a single event stream into one document | `Checkout` snapshot (Orders BC) |
| **Multi-Stream** | Aggregate events across multiple streams by a property | `CurrentPriceView` (Pricing BC) — Guid streams → SKU-keyed documents |
| **Live Aggregation** | On-demand event replay (no persistent projection) | Command handlers using `FetchForWriting()` (preferred) |

### The Three Projection Lifecycles

| Lifecycle | When Events Are Processed | Latency | Use Case |
|-----------|---------------------------|---------|----------|
| **Inline** | Same transaction as command | 0ms | Hot-path queries (price lookups, coupon validation) |
| **Async** | Background daemon (seconds) | 1-5s | Reporting views, analytics, less-critical reads |
| **Live** | On-demand query (event replay every time) | 50-500ms | Ad-hoc queries, admin views, rarely-used endpoints |

**Key insight:** Inline projections are **zero-lag** — the projection document is updated in the same transaction that appends the event. This eliminates eventual consistency lag for hot-path queries.

---

## Single-Stream Projections

### Overview

**Single-stream projections** aggregate a single event stream into one document. One stream → One projection document. The document ID typically matches the stream ID.

**When to use:**
- Snapshot queryable aggregates (avoid event replay on every query)
- Denormalize aggregate state into a read-optimized shape
- Enable indexed queries on aggregate properties

### Anatomy: Create() and Apply() Methods

**Key conventions:**
- `Create()` — Optional; runs on first event; if omitted, uses parameterless constructor
- `Apply()` — One overload per event type; can be instance or static (CritterSupply uses instance methods for consistency)
- Method names are exact: `Create` and `Apply` (case-sensitive)
- Return type must match document type
- Parameter order: `(document, event)`

```csharp
public sealed class MyProjection : SingleStreamProjection<MyDocument>
{
    public MyDocument Create(FirstEvent evt) =>
        new MyDocument { Id = evt.AggregateId, Property = evt.Value };

    public static MyDocument Apply(MyDocument current, SubsequentEvent evt) =>
        current with { Property = evt.NewValue };
}
```

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

    public static Checkout Apply(Checkout current, ShippingAddressSet evt) =>
        current with { ShippingAddress = evt.Address };

    public static Checkout Apply(Checkout current, CheckoutCompleted evt) =>
        current with { Status = CheckoutStatus.Completed };
}
```

**Why inline?** Hot-path queries (cart expiry, checkout status) require zero lag.

**Key pattern:** Marten discovers `Create()`/`Apply()` on the aggregate type via `.Snapshot<Checkout>()`. No separate projection class needed.

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
        // Map all price events to SKU-keyed documents
        Identity<InitialPriceSet>(x => x.Sku);
        Identity<PriceChanged>(x => x.Sku);
        Identity<PriceChangeScheduled>(x => x.Sku);
        // ... other price events
    }

    public CurrentPriceView Create(InitialPriceSet evt) =>
        new CurrentPriceView
        {
            Id = evt.Sku,  // Document ID is SKU string
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

**Why this pattern?**
- **ProductPrice** streams use Guid IDs (UUID v5 from SKU — see ADR 0016)
- **CurrentPriceView** queries use SKU strings (`session.LoadAsync<CurrentPriceView>("DOG-FOOD-5LB")`)
- Multi-stream projection bridges: Guid streams → string-keyed documents via `Identity<>(x => x.Sku)`
- Inline lifecycle ensures zero-lag for Shopping BC price queries (hot path)

### Discriminated Unions for Projection Views (JSON Polymorphism)

**From:** Backoffice BC (M32.0 Session 8)

For projections that produce different event types for SignalR or real-time updates, use C# discriminated unions via JSON polymorphism:

**Pattern:**

```csharp
using System.Text.Json.Serialization;

/// <summary>
/// Base class for all real-time events from a projection.
/// Uses discriminated union pattern with JSON polymorphism for type-safe deserialization.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "eventType")]
[JsonDerivedType(typeof(LiveMetricUpdated), typeDiscriminator: "live-metric-updated")]
[JsonDerivedType(typeof(AlertCreated), typeDiscriminator: "alert-created")]
public abstract record BackofficeEvent(DateTimeOffset OccurredAt);

/// <summary>
/// Real-time metric update for executive dashboard.
/// </summary>
public sealed record LiveMetricUpdated(
    int OrderCount,
    decimal Revenue,
    decimal PaymentFailureRate,
    DateTimeOffset OccurredAt) : BackofficeEvent(OccurredAt);

/// <summary>
/// Alert notification for operations team.
/// </summary>
public sealed record AlertCreated(
    string AlertType,
    string Severity,
    string Message,
    DateTimeOffset OccurredAt) : BackofficeEvent(OccurredAt);
```

**Why this pattern?**

1. **Type safety:** Clients can deserialize the base type and switch on derived types
2. **JSON serialization:** `[JsonPolymorphic]` adds `eventType` discriminator field automatically
3. **Extensibility:** Add new event types by adding new `[JsonDerivedType]` attributes
4. **SignalR compatibility:** Works seamlessly with Wolverine SignalR transport

**Example JSON output:**

```json
{
  "eventType": "live-metric-updated",
  "orderCount": 42,
  "revenue": 1234.56,
  "paymentFailureRate": 0.02,
  "occurredAt": "2026-03-16T12:00:00Z"
}

{
  "eventType": "alert-created",
  "alertType": "PaymentFailed",
  "severity": "Critical",
  "message": "Payment failed for order #12345",
  "occurredAt": "2026-03-16T12:01:00Z"
}
```

**Client-side deserialization (C#):**

```csharp
var baseEvent = JsonSerializer.Deserialize<BackofficeEvent>(json);
switch (baseEvent)
{
    case LiveMetricUpdated metric:
        // Handle metric update
        break;
    case AlertCreated alert:
        // Handle alert
        break;
}
```

**When to use discriminated unions in projections:**

✅ **Use when:**
- Projection produces multiple event types for real-time updates
- Events share common properties (e.g., `OccurredAt`, `TenantId`)
- Clients need type-safe deserialization
- SignalR or WebSocket transport is involved

❌ **Don't use when:**
- Projection produces a single document type
- Events are completely unrelated (no shared properties)
- Simple DTO mapping is sufficient

**Related patterns:**
- See `wolverine-signalr.md` for SignalR hub integration
- See `bff-realtime-patterns.md` for BFF-owned projection patterns
- See M32.0 retrospective (Session 8) for Backoffice example

**Key insight:** Discriminated unions via `[JsonPolymorphic]` provide type-safe, extensible event modeling for projection outputs without hand-rolling type discriminators.

---

### Example: CouponLookupView (Promotions BC)

```csharp
public sealed class CouponLookupViewProjection : MultiStreamProjection<CouponLookupView, string>
{
    public CouponLookupViewProjection()
    {
        Identity<CouponIssued>(x => x.CouponCode);
        Identity<CouponRedeemed>(x => x.CouponCode);
        Identity<CouponRevoked>(x => x.CouponCode);
    }

    public CouponLookupView Create(CouponIssued evt) =>
        new CouponLookupView
        {
            Id = evt.CouponCode.ToUpperInvariant(),  // Case-insensitive
            Code = evt.CouponCode.ToUpperInvariant(),
            PromotionId = evt.PromotionId,
            Status = CouponStatus.Issued
        };
}
```

**Why inline?** Hot-path coupon redemption during checkout requires zero-lag validation.

**Case normalization:** `ToUpperInvariant()` ensures case-insensitive queries.

---

## Live Aggregation and FetchForWriting

### Why FetchForWriting() Is Preferred

**Pattern:**

```csharp
// ✅ Use FetchForWriting() for command handlers
public static async Task<IEnumerable<object>> Handle(
    ChangePrice cmd,
    IDocumentSession session)
{
    var streamId = ProductPrice.StreamId(cmd.Sku);
    var price = await session.Events.FetchForWriting<ProductPrice>(streamId);

    return [new PriceChanged(...)];  // Marten auto-appends with optimistic concurrency
}
```

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

### Example: ProductPrice vs. Checkout

**ProductPrice (write-only):**
```csharp
// FetchForWriting() — aggregate not queried
var price = await session.Events.FetchForWriting<ProductPrice>(streamId);
return [new PriceChanged(...)];
```
- CurrentPriceView projection handles queries
- Commands use FetchForWriting()

**Checkout (queried + modified):**
```csharp
// Snapshot projection — aggregate is queried
opts.Projections.Snapshot<Checkout>(SnapshotLifecycle.Inline);

// Command loads snapshot
public static ShippingAddressSet Handle(SetShippingAddress cmd, [ReadAggregate] Checkout checkout)
    => new ShippingAddressSet(checkout.Id, cmd.Address);
```
- Queries use snapshot (`GET /api/checkout/{id}`)
- Commands load snapshot (zero-cost)

**Decision rule:** Snapshot if queried; FetchForWriting() if write-only.

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

❌ **Do NOT use when:**
- Write-only aggregates (use FetchForWriting())
- Separate query model exists (projection is redundant)

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

**Why snapshots?**
- Promotion status checked by Shopping BC (hot path)
- Coupon status checked during redemption (hot path)
- 5-10 events per stream — snapshot eliminates replay overhead

**Key insight:** Even small streams benefit from snapshots on hot paths. A 5-event replay (~10ms) × 1,000 queries/hour = 10 seconds cumulative overhead.

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
    opts.Projections.Snapshot<AnotherAggregate>(SnapshotLifecycle.Async);
})
.AddAsyncDaemon(DaemonMode.Solo);  // Required for async projections
```

**Key configuration points:**

1. **Inline projections** use `ProjectionLifecycle.Inline` — updated in same transaction as events
2. **Async projections** use `ProjectionLifecycle.Async` — require `.AddAsyncDaemon()`
3. **Snapshot projections** use `.Snapshot<T>()` with `SnapshotLifecycle.Inline` or `SnapshotLifecycle.Async`
4. **DaemonMode.Solo** is correct for single-instance deployments; use `DaemonMode.HotCold` for multi-instance

### The Async Daemon

**What is the async daemon?**

The async daemon is a background process that consumes events from `mt_events` and updates async projections. It runs continuously, polling for new events.

**When is it required?**
- Only for `ProjectionLifecycle.Async` projections
- Not required for inline projections (they're updated synchronously)

**Configuration:**

```csharp
.AddAsyncDaemon(DaemonMode.Solo)
```

**DaemonMode options:**

| Mode | Use Case | Behavior |
|------|----------|----------|
| **Solo** | Single API instance | All projections run in this process |
| **HotCold** | Multiple API instances | Leader election — one instance runs projections |
| **Disabled** | No async projections | Daemon doesn't start |

**Production note:** If you deploy multiple instances of your API (Kubernetes, Azure App Service scale-out), use `DaemonMode.HotCold` to avoid duplicate projection processing.

### Example: Pricing BC Configuration

```csharp
builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = Constants.Pricing.ToLowerInvariant();

    // Event-sourced aggregates
    opts.Events.StreamIdentity = StreamIdentity.AsGuid;

    // Inline projection for hot-path queries (zero lag)
    opts.Projections.Add<CurrentPriceViewProjection>(ProjectionLifecycle.Inline);
})
.AddAsyncDaemon(DaemonMode.Solo)
.UseLightweightSessions();
```

**Why no snapshots here?** The `ProductPrice` aggregate is **not** directly queried — `CurrentPriceView` is the query model. Command handlers use `FetchForWriting()` for aggregate loading.

### Example: Promotions BC Configuration

```csharp
builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = Constants.Promotions.ToLowerInvariant();

    opts.Events.StreamIdentity = StreamIdentity.AsGuid;

    // Snapshot projections for queryable aggregates (inline for zero lag)
    opts.Projections.Snapshot<Promotion>(SnapshotLifecycle.Inline);
    opts.Projections.Snapshot<Coupon>(SnapshotLifecycle.Inline);

    // Inline projection for coupon code lookups (hot path)
    opts.Projections.Add<CouponLookupViewProjection>(ProjectionLifecycle.Inline);
})
.AddAsyncDaemon(DaemonMode.Solo)
.UseLightweightSessions();
```

**Why both snapshots and projections?**
- **Snapshots** (`Promotion`, `Coupon`) are for command handlers loading aggregate state
- **Projections** (`CouponLookupView`) are for queries with different identity (string-keyed by code)

### ⚠️ Critical Warning: Missing Projection Registration

**The silent query failure problem:**

```csharp
// ❌ ANTI-PATTERN: Projection class exists but is never registered
public sealed class MyProjection : SingleStreamProjection<MyDocument>
{
    // ... Create() and Apply() methods
}

// Program.cs — MISSING REGISTRATION
builder.Services.AddMarten(opts =>
{
    // No opts.Projections.Add<MyProjection>() call!
});

// Result: Queries return empty results (no error thrown)
var doc = await session.LoadAsync<MyDocument>(id);  // Always null!
```

**What happens:**
1. Marten creates `mt_doc_mydocument` table (document type is discoverable)
2. Events are appended successfully (no errors)
3. Projection logic **never runs** (not registered)
4. Queries return `null` or empty results (table exists but has no rows)
5. **No exception thrown** — this is a silent failure

**How to prevent:**

1. **Always register projections immediately after creating them**
2. **Integration tests must query projected documents** — if the test only checks event persistence, the missing projection won't be caught
3. **Code review checklist:** Every new projection class → verify `opts.Projections.Add<>()` exists

**Example from CritterSupply:**

The Pricing BC initially had `CurrentPriceViewProjection` implemented but not registered. Integration tests only checked event persistence:

```csharp
// ❌ BAD TEST: Only checks events, not projection
[Fact]
public async Task SetPrice_AppendsInitialPriceSetEvent()
{
    var streamId = ProductPrice.StreamId("DOG-FOOD-5LB");
    await _fixture.PublishAsync(new SetPrice("DOG-FOOD-5LB", 24.99m, Guid.NewGuid()));

    var events = await _fixture.LoadEventsAsync(streamId);
    events.ShouldContain(e => e is InitialPriceSet);  // ✅ Passes (events work)
}

// ❌ This query would return null, but test didn't check it!
// var view = await _fixture.LoadAsync<CurrentPriceView>("DOG-FOOD-5LB");
```

**Fixed test:**

```csharp
// ✅ GOOD TEST: Checks both events AND projection
[Fact]
public async Task SetPrice_UpdatesCurrentPriceView()
{
    var streamId = ProductPrice.StreamId("DOG-FOOD-5LB");
    await _fixture.PublishAsync(new SetPrice("DOG-FOOD-5LB", 24.99m, Guid.NewGuid()));

    // Check events
    var events = await _fixture.LoadEventsAsync(streamId);
    events.ShouldContain(e => e is InitialPriceSet);

    // ✅ Check projection (catches missing registration)
    var view = await _fixture.LoadAsync<CurrentPriceView>("DOG-FOOD-5LB");
    view.ShouldNotBeNull();
    view.BasePrice.ShouldBe(24.99m);
}
```

**Key takeaway:** **Every projection must have an integration test that queries the projected document.** Event-only tests are insufficient.

---

## Event Metadata in Projections

### Overview

Event metadata includes version numbers, timestamps, correlation IDs, causation IDs, and custom headers. Projections can access this metadata via `IEvent<T>` wrappers.

**When to use metadata in projections:**
- Track when events were recorded (`IEvent<T>.Timestamp`)
- Store version for optimistic concurrency checks (`IEvent<T>.Version`)
- Correlation/causation IDs for distributed tracing (`IEvent<T>.CausationId`, `IEvent<T>.CorrelationId`)

### Accessing Metadata in Apply() Methods

Use `IEvent<T>` instead of the raw event type:

```csharp
public static MyDocument Apply(MyDocument current, IEvent<MyEvent> evt)
{
    return current with
    {
        LastModified = evt.Timestamp,  // Event metadata
        Version = evt.Version,         // Stream version
        Data = evt.Data.Value          // Actual event payload
    };
}
```

**Key properties:**
- `evt.Data` — The actual event payload
- `evt.Timestamp` — When the event was recorded (UTC)
- `evt.Version` — Stream version (1, 2, 3, ...)
- `evt.Id` — Unique event ID (Guid)
- `evt.CausationId` — ID of the message that caused this event
- `evt.CorrelationId` — ID of the originating request
- `evt.Headers` — Custom metadata dictionary

### Example: Tracking Event Timestamps

```csharp
public sealed class AuditedProjection : SingleStreamProjection<AuditedDocument>
{
    public AuditedDocument Create(IEvent<FirstEvent> evt)
    {
        return new AuditedDocument
        {
            Id = evt.Data.AggregateId,
            CreatedAt = evt.Timestamp,  // Metadata
            Version = evt.Version,      // Metadata
        };
    }

    public static AuditedDocument Apply(AuditedDocument current, IEvent<UpdatedEvent> evt)
    {
        return current with
        {
            LastModified = evt.Timestamp,
            Version = evt.Version,
        };
    }
}
```

**Why this matters:** Projection documents can include temporal metadata for auditing, cache invalidation, or stale-read detection.

### Example: Correlation IDs for Tracing

```csharp
public static MyDocument Apply(MyDocument current, IEvent<MyEvent> evt)
{
    return current with
    {
        Data = evt.Data.Value,
        CorrelationId = evt.CorrelationId,  // For distributed tracing
        CausationId = evt.CausationId,      // Parent message ID
    };
}
```

**Use case:** Distributed tracing across BCs — correlation IDs link events back to originating HTTP requests or integration messages.

---

## Querying Projected Documents

### Querying by ID

The most common query pattern — load a single document by its ID:

```csharp
// Single document load
var priceView = await session.LoadAsync<CurrentPriceView>("DOG-FOOD-5LB");
if (priceView == null) { /* handle not found */ }
```

**Key points:**
- `LoadAsync<T>(id)` returns `null` if document doesn't exist
- For inline projections, this is always current (zero lag)
- For async projections, may be slightly stale (seconds)

### Bulk Lookups with LoadManyAsync

When you need multiple documents by ID, use `LoadManyAsync<T>()`:

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

**Performance:** `LoadManyAsync()` issues a single SQL query (`WHERE id = ANY(@ids)`). Much faster than N individual `LoadAsync()` calls.

**Use case:** Product listing pages in BFFs — load prices for 20-50 SKUs in one query.

### LINQ Queries with Query<T>()

For filtering, sorting, or complex queries:

```csharp
// Query all published prices below $20
var cheapProducts = await session.Query<CurrentPriceView>()
    .Where(p => p.Status == PriceStatus.Published && p.BasePrice < 20m)
    .OrderBy(p => p.BasePrice)
    .Take(10)
    .ToListAsync();
```

**LINQ support:**
- `.Where()`, `.OrderBy()`, `.Select()`, `.Take()`, `.Skip()`
- Marten translates to PostgreSQL JSON queries
- Indexes on projected documents improve query performance

**Index configuration:**

```csharp
// In AddMarten() configuration
opts.Schema.For<CurrentPriceView>()
    .Index(x => x.Status)
    .Index(x => x.BasePrice);
```

### Cross-Document Queries

Projections are **documents** — they live in separate tables. Joins require explicit queries:

```csharp
// ❌ ANTI-PATTERN: Can't join documents in Marten
var results = await session.Query<CurrentPriceView>()
    .Join(session.Query<ProductInfo>(), p => p.Sku, pi => pi.Sku, ...)  // Not supported

// ✅ CORRECT: Load separately, join in memory
var prices = await session.LoadManyAsync<CurrentPriceView>(skus);
var products = await session.LoadManyAsync<ProductInfo>(skus);
var joined = skus.Select(sku => new { Price = prices[sku], Product = products[sku] });
```

**Why?** Marten stores documents in separate tables (`mt_doc_currentpriceview`, `mt_doc_productinfo`). Cross-table joins aren't supported. Use in-memory joins after loading.

**Alternative:** Use `EfCoreMultiStreamProjection` (see `efcore-marten-projections.md`) if you need true relational joins with foreign keys.

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
        var evt = new PriceChanged(
            Guid.NewGuid(),
            "DOG-FOOD-5LB",
            Money.Of(24.99m),
            Money.Of(19.99m),
            current.LastUpdatedAt,
            "Price drop",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            null,
            null
        );

        // Act
        var result = CurrentPriceViewProjection.Apply(current, evt);

        // Assert
        result.BasePrice.ShouldBe(19.99m);
        result.PreviousBasePrice.ShouldBe(24.99m);
        result.PreviousPriceSetAt.ShouldBe(current.LastUpdatedAt);
    }
}
```

**Why this works:** `Apply()` methods are static pure functions (`(state, event) => newState`). No Marten, no database, no fixtures required.

**What to test:**
- Each `Apply()` method with each event type
- Edge cases (null properties, empty collections, boundary values)
- Immutability (original state is not mutated)

### Integration Testing with Alba

Integration tests verify:
1. Projection is registered (catches missing `opts.Projections.Add<>()`)
2. Events trigger projection updates (end-to-end workflow)
3. Queries return projected documents (not null)

```csharp
public class PricingProjectionTests : IClassFixture<PricingTestFixture>
{
    private readonly PricingTestFixture _fixture;

    public PricingProjectionTests(PricingTestFixture fixture) => _fixture = fixture;

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
        view.Status.ShouldBe(PriceStatus.Published);
    }
}
```

**Key assertion:** `view.ShouldNotBeNull()` — this fails if projection isn't registered. Event-only tests don't catch this bug.

### Test Fixture Pattern

```csharp
public class PricingTestFixture : IAsyncLifetime
{
    public IAlbaHost Host { get; private set; } = null!;
    private PostgreSqlContainer? _postgresContainer;

    public async Task InitializeAsync()
    {
        // Start TestContainer
        _postgresContainer = new PostgreSqlBuilder().Build();
        await _postgresContainer.StartAsync();

        // Build Alba host with overridden connection string
        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.UseSetting("ConnectionStrings:postgres", _postgresContainer.GetConnectionString());
        });
    }

    public async Task<T?> LoadAsync<T>(object id) where T : class
    {
        var store = Host.Services.GetRequiredService<IDocumentStore>();
        await using var session = store.LightweightSession();
        return await session.LoadAsync<T>(id);
    }

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
        if (_postgresContainer != null)
            await _postgresContainer.DisposeAsync();
    }
}
```

**Why TestContainers?** Real PostgreSQL instance ensures projection infrastructure (Weasel schema migration, JSONB queries) works correctly. Mocks can't catch schema issues.

---

## Polecat Compatibility

### Overview

**Polecat** is the SQL Server counterpart to Marten (PostgreSQL). It mirrors Marten's API surface for event sourcing and projections, enabling cross-database portability.

**Key differences:**

| Feature | Marten (PostgreSQL) | Polecat (SQL Server) |
|---------|---------------------|----------------------|
| JSON storage | `jsonb` type | `json` type (SQL Server 2025+) |
| Projection API | Identical | Identical |
| Configuration | `AddMarten()` | `AddPolecat()` |
| Connection string | Npgsql format | SQL Server format |

**Code portability:** Projection classes (`SingleStreamProjection<T>`, `MultiStreamProjection<T>`) work identically on both Marten and Polecat. Only configuration changes.

### Example: Migrating a Projection to Polecat

**Before (Marten / PostgreSQL):**

```csharp
// appsettings.json
{
  "ConnectionStrings": {
    "marten": "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres"
  }
}

// Program.cs
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("marten"));
    opts.DatabaseSchemaName = "pricing";
    opts.Projections.Add<CurrentPriceViewProjection>(ProjectionLifecycle.Inline);
})
.AddAsyncDaemon(DaemonMode.Solo);
```

**After (Polecat / SQL Server):**

```csharp
// appsettings.json
{
  "ConnectionStrings": {
    "sqlserver": "Server=localhost,1434;Database=pricing;User Id=sa;Password=CritterSupply2025!;TrustServerCertificate=true"
  }
}

// Program.cs
builder.Services.AddPolecat(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("sqlserver"));
    opts.DatabaseSchemaName = "pricing";
    opts.Projections.Add<CurrentPriceViewProjection>(ProjectionLifecycle.Inline);  // Identical!
})
.AddAsyncDaemon(DaemonMode.Solo);
```

**Projection class:** **No changes required.** `CurrentPriceViewProjection` works identically on both.

### Collation Considerations (Polecat)

**SQL Server string collation is case-insensitive by default.** This affects string-keyed projections:

```csharp
// Marten (case-sensitive)
await session.LoadAsync<CurrentPriceView>("DOG-FOOD-5LB");   // Different from
await session.LoadAsync<CurrentPriceView>("dog-food-5lb");   // ← Different document

// Polecat (case-insensitive by default)
await session.LoadAsync<CurrentPriceView>("DOG-FOOD-5LB");   // Same as
await session.LoadAsync<CurrentPriceView>("dog-food-5lb");   // ← SAME document!
```

**Mitigation:** Normalize string keys in projection `Create()` methods:

```csharp
public CurrentPriceView Create(InitialPriceSet evt)
{
    return new CurrentPriceView
    {
        Id = evt.Sku.ToUpperInvariant(),  // ✅ Case normalization
        Sku = evt.Sku.ToUpperInvariant(),
        // ...
    };
}
```

**See also:** ADR 0026 (Polecat migration) covers collation risks in depth.

---

## Common Pitfalls and Warnings

### ⚠️ Anti-Pattern #1: Missing Projection Registration

**Problem:** Projection class exists but never registered in `Program.cs`:

```csharp
// ❌ BAD: Projection class defined but not registered
public sealed class MyProjection : SingleStreamProjection<MyDocument>
{
    // ... Create() and Apply() methods
}

// Program.cs — MISSING opts.Projections.Add<MyProjection>()!
```

**Result:** Silent query failure. Queries return `null`, no exception thrown.

**Fix:** Always register projections immediately after creating them:

```csharp
// ✅ GOOD: Register projection
opts.Projections.Add<MyProjection>(ProjectionLifecycle.Inline);
```

**Prevention:** Integration tests MUST query projected documents (see Testing section).

---

### ⚠️ Anti-Pattern #2: Forgetting Snapshot Projections for Queryable Aggregates

**Problem:** Aggregate is both queried and modified, but no snapshot projection:

```csharp
// ❌ BAD: Checkout is queried but has no snapshot
// Every query replays events (slow)
var checkout = await session.Events.AggregateStreamAsync<Checkout>(checkoutId);  // Replay!
```

**Fix:** Add inline snapshot projection:

```csharp
// ✅ GOOD: Snapshot eliminates replay
opts.Projections.Snapshot<Checkout>(SnapshotLifecycle.Inline);

// Query loads snapshot (fast)
var checkout = await session.LoadAsync<Checkout>(checkoutId);
```

**See also:** `marten-event-sourcing.md` Anti-Pattern #8 covers this in detail.

---

### ⚠️ Anti-Pattern #3: Using Async Lifecycle for Hot-Path Queries

**Problem:** Hot-path queries use async projections (eventual consistency lag):

```csharp
// ❌ BAD: Price lookup is hot-path but projection is async
opts.Projections.Add<CurrentPriceView>(ProjectionLifecycle.Async);  // Seconds of lag!

// Shopping BC queries during AddItemToCart
var price = await session.LoadAsync<CurrentPriceView>(sku);  // May be stale!
```

**Fix:** Use inline lifecycle for hot-path queries:

```csharp
// ✅ GOOD: Inline projection ensures zero lag
opts.Projections.Add<CurrentPriceViewProjection>(ProjectionLifecycle.Inline);
```

**When async is OK:** Reporting views, analytics, admin dashboards (where seconds of lag is acceptable).

---

### ⚠️ Anti-Pattern #4: Mutable Apply() Methods

**Problem:** `Apply()` methods mutate state instead of returning new state:

```csharp
// ❌ BAD: Mutable Apply() method
public void Apply(MyDocument doc, MyEvent evt)
{
    doc.Property = evt.Value;  // Mutation!
}
```

**Fix:** Use immutable records with `with` expressions:

```csharp
// ✅ GOOD: Immutable Apply() method
public static MyDocument Apply(MyDocument doc, MyEvent evt)
{
    return doc with { Property = evt.Value };  // Immutable
}
```

**Why:** Inline projections run in Marten's async pipeline. Mutable state causes race conditions with concurrent commands.

---

### ⚠️ Anti-Pattern #5: Forgetting AddAsyncDaemon for Async Projections

**Problem:** Async projection registered but daemon not started:

```csharp
// ❌ BAD: Async projection but no daemon
opts.Projections.Add<MyProjection>(ProjectionLifecycle.Async);

// Daemon not added — projection never runs!
// .AddAsyncDaemon() is missing
```

**Fix:** Always call `.AddAsyncDaemon()` when using async projections:

```csharp
// ✅ GOOD: Daemon starts background processing
builder.Services.AddMarten(opts =>
{
    opts.Projections.Add<MyProjection>(ProjectionLifecycle.Async);
})
.AddAsyncDaemon(DaemonMode.Solo);
```

---

### ⚠️ Anti-Pattern #6: Case-Sensitive String Keys on Polecat

**Problem:** String-keyed projections on Polecat without case normalization:

```csharp
// ❌ BAD: No normalization (case mismatch on SQL Server)
public MyDocument Create(MyEvent evt)
{
    return new MyDocument { Id = evt.Sku };  // "DOG-FOOD-5LB" vs "dog-food-5lb"
}
```

**Fix:** Normalize keys in `Create()`:

```csharp
// ✅ GOOD: Case normalization
public MyDocument Create(MyEvent evt)
{
    return new MyDocument { Id = evt.Sku.ToUpperInvariant() };
}
```

**See also:** ADR 0026 for Polecat collation considerations.

---

## Production Lessons Learned

### Lesson 0: Inline Projections Require Explicit SaveChanges Before Querying

**From:** Backoffice BC (M32.0 Session 8)

**Problem:** Handler queried inline projection immediately after `Events.Append()` without calling `SaveChangesAsync()`:

```csharp
// ❌ BAD: Query returns null because projection hasn't updated yet
public static async Task<LiveMetricUpdated> Handle(
    OrderPlaced message,
    IDocumentSession session)
{
    session.Events.Append(Guid.NewGuid(), message);

    // Projection hasn't updated yet!
    var metrics = await session.LoadAsync<AdminDailyMetrics>(today);
    // metrics is null or stale!

    return new LiveMetricUpdated(...);
}
```

**Root Cause:** Marten inline projections update **during** `SaveChangesAsync()`, not during `Events.Append()`. The projection document won't exist until the transaction commits.

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
    // metrics is current!

    return new LiveMetricUpdated(
        metrics.OrderCount,
        metrics.Revenue,
        metrics.PaymentFailureRate,
        DateTimeOffset.UtcNow
    );
}
```

**When this pattern is needed:**
- Integration message handlers that append events **and** query inline projections
- Handlers returning SignalR events with projection data
- Any workflow where the handler needs the updated projection state immediately

**When this pattern is NOT needed:**
- Handlers that only append events (Wolverine auto-commits via `AutoApplyTransactions()` policy)
- Handlers that don't query projections
- Async projections (daemon updates them later)

**Impact:** Handler becomes `async Task<T>` instead of synchronous `T` return. This is acceptable because the projection query requires async anyway.

**Discovered in:** M32.0 Session 8 (SignalR hub integration). OrderPlacedHandler needed to return `LiveMetricUpdated` with fresh metrics from `AdminDailyMetrics` projection.

**Takeaway:** When handlers need projection data immediately after appending events, always:
1. Append events
2. Call `SaveChangesAsync()` explicitly
3. Query projection
4. Return result

---

### Lesson 1: Always Test Projected Documents, Not Just Events

**From:** Pricing BC (Cycle 29)

**Problem:** `CurrentPriceViewProjection` was implemented but not registered. Integration tests only checked event persistence:

```csharp
// ❌ BAD TEST: Only checks events
[Fact]
public async Task SetPrice_AppendsInitialPriceSetEvent()
{
    var streamId = ProductPrice.StreamId("DOG-FOOD-5LB");
    await _fixture.PublishAsync(new SetPrice("DOG-FOOD-5LB", 24.99m, Guid.NewGuid()));

    var events = await _fixture.LoadEventsAsync(streamId);
    events.ShouldContain(e => e is InitialPriceSet);  // ✅ Passes
}

// But queries failed silently:
// var view = await session.LoadAsync<CurrentPriceView>("DOG-FOOD-5LB");  // null!
```

**Fix:** Tests now query projected documents:

```csharp
// ✅ GOOD TEST: Checks projection
[Fact]
public async Task SetPrice_UpdatesCurrentPriceView()
{
    var streamId = ProductPrice.StreamId("DOG-FOOD-5LB");
    await _fixture.PublishAsync(new SetPrice("DOG-FOOD-5LB", 24.99m, Guid.NewGuid()));

    // Check projection (catches missing registration)
    var view = await _fixture.LoadAsync<CurrentPriceView>("DOG-FOOD-5LB");
    view.ShouldNotBeNull();  // Fails if projection not registered!
    view.BasePrice.ShouldBe(24.99m);
}
```

**Takeaway:** Event-only tests are insufficient. Always query projected documents in integration tests.

---

### Lesson 2: Inline Projections for Hot-Path Queries Are Non-Negotiable

**From:** Pricing BC and Promotions BC (Cycle 29-30)

**Observation:** Both `CurrentPriceView` (Pricing) and `CouponLookupView` (Promotions) use inline projections. Initial designs considered async projections ("eventual consistency is fine, right?").

**Reality:** Hot-path queries cannot tolerate lag:
- **Pricing:** Shopping BC's `AddItemToCart` validates price existence synchronously — 5 seconds of lag causes "price not found" errors
- **Promotions:** Coupon redemption checks status synchronously — stale status allows double-redemption

**Decision:** Inline projections for all hot-path queries. Accept the slightly higher write latency (projection update in same transaction) for zero-lag reads.

**Takeaway:** "Eventual consistency is fine" is rarely true for hot-path queries. Default to inline; use async only for reporting/analytics.

---

### Lesson 3: MultiStreamProjection Identity Mapping Is Subtle

**From:** Pricing BC `CurrentPriceView` (Cycle 29)

**Problem:** Initial implementation forgot `Identity<>()` mapping for one event type (`PriceCorrected`). Result:

```csharp
// ❌ BAD: Missing Identity<PriceCorrected>()
public CurrentPriceViewProjection()
{
    Identity<InitialPriceSet>(x => x.Sku);
    Identity<PriceChanged>(x => x.Sku);
    // ... other events
    // Missing: Identity<PriceCorrected>(x => x.Sku);  ⚠️
}
```

**Result:** `PriceCorrected` events didn't update `CurrentPriceView`. No error thrown — silent failure.

**Fix:** Added missing identity mapping:

```csharp
// ✅ GOOD: All event types mapped
Identity<PriceCorrected>(x => x.Sku);
```

**Takeaway:** Every event type that updates a multi-stream projection MUST have `Identity<>()` mapping. Integration tests should append all event types and verify projection updates.

---

### Lesson 4: Snapshot Projections Are Cheaper Than You Think

**From:** Promotions BC (Cycle 29-30)

**Observation:** `Promotion` and `Coupon` aggregates have only 5-10 events per stream. Initial design skipped snapshots ("not worth the overhead for so few events").

**Reality:** A 5-event replay is ~10ms. For a query that runs 1,000 times/hour (cart operations), that's 10 seconds of cumulative latency overhead. Snapshot eliminates all replay — worth it even for small streams.

**Decision:** Inline snapshots for both `Promotion` and `Coupon`.

**Takeaway:** Snapshot overhead (write-time projection update) is negligible. Snapshot benefit (read-time replay elimination) pays off even for small streams on hot paths.

---

### Lesson 5: FetchForWriting() vs. Snapshot — Know the Difference

**From:** Pricing BC `ProductPrice` (Cycle 29)

**Confusion:** "Should I use `FetchForWriting()` or a snapshot for `ProductPrice`?"

**Answer:** It depends on **query patterns**:
- **Snapshot:** Aggregate is queried directly (`GET /api/products/{sku}/price` → load `ProductPrice` snapshot)
- **FetchForWriting():** Aggregate is write-only in commands; separate projection handles queries (`CurrentPriceView`)

**Pricing BC decision:** `ProductPrice` is **not** directly queried — `CurrentPriceView` is the query model. Commands use `FetchForWriting()` for aggregate loading.

**Takeaway:** Don't snapshot aggregates that are never queried. Use `FetchForWriting()` for write-only workflows.

---

### Lesson 6: BFF-Owned Projections Avoid Need for Separate Analytics BC

**From:** Backoffice BC (M32.0 Sessions 6-7)

**Observation:** Backoffice BFF owns two Marten projections aggregating events from 7 domain BCs:
- **AdminDailyMetrics** — sourced from Orders, Payments, Inventory, Fulfillment
- **AlertFeedView** — sourced from Payments, Inventory, Fulfillment, Returns

**Alternative considered:** Create separate Analytics BC to own these projections.

**Decision:** BFF-owned projections are sufficient for Phase 1-2 operational dashboards. Defer Analytics BC until business analytics requirements mature (Phase 3+).

**Pattern:**

```csharp
// BFF integration message handler (appends event to BFF's event store)
public static class OrderPlacedHandler
{
    public static async Task<LiveMetricUpdated> Handle(
        Orders.OrderPlaced message,
        IDocumentSession session)
    {
        // 1. Append integration message to BFF event store
        session.Events.Append(Guid.NewGuid(), message);

        // 2. Commit (inline projection updates AdminDailyMetrics)
        await session.SaveChangesAsync();

        // 3. Query updated projection
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var metrics = await session.LoadAsync<AdminDailyMetrics>(today);

        // 4. Return SignalR event with fresh metrics
        return new LiveMetricUpdated(
            metrics.OrderCount,
            metrics.Revenue,
            metrics.PaymentFailureRate,
            DateTimeOffset.UtcNow
        );
    }
}
```

**Why BFF-owned projections?**

1. **Lower infrastructure cost:** No separate Analytics BC API/database
2. **Faster delivery:** Projections added in 2 sessions vs. 5+ sessions for new BC
3. **Sufficient for Phase 1:** Operational dashboards don't need complex analytics (BI tools, machine learning, etc.)
4. **Easy migration path:** If Analytics BC becomes necessary, projections can be moved without changing domain BCs

**When to use BFF-owned projections:**
- ✅ Operational dashboards (real-time KPIs, alert feeds, executive summary)
- ✅ Cross-BC aggregation for UI composition
- ✅ Inline lifecycle required (zero-lag updates)
- ✅ Queries are simple (load by ID, basic filtering)

**When to create separate Analytics BC:**
- ❌ Complex analytics (time-series analysis, forecasting, ML models)
- ❌ Long-term data warehousing (years of historical data)
- ❌ Heavy BI tooling integration (Tableau, Power BI, etc.)
- ❌ Async/batch processing (nightly aggregations, reports)

**Takeaway:** BFF-owned projections are a pragmatic alternative to creating a separate Analytics BC. Start with BFF projections; migrate to Analytics BC when requirements demand it.

**ADR Reference:** ADR 0036 (BFF-Owned Projections Strategy) — rationale for deferring Analytics BC investment.

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

## Appendix: How Projections Work Under the Hood

### Inline Projections: Transaction Flow

```
1. HTTP Request arrives
   ↓
2. Wolverine handler executes
   ↓
3. Handler returns event(s)
   ↓
4. Marten appends event to mt_events
   ↓
5. ⚡ INLINE PROJECTION RUNS (same transaction)
   - Marten calls Apply() method
   - Updates mt_doc_{projection} table
   ↓
6. Wolverine commits transaction
   ↓
7. HTTP Response returns
```

**Key insight:** Inline projections are **synchronous** — the projection update happens before the HTTP response returns. This eliminates read-after-write consistency issues.

### Async Projections: Daemon Polling

```
1. Event appended to mt_events (inline projections update)
   ↓
2. HTTP Response returns
   ↓
   ... (seconds pass) ...
   ↓
3. Async Daemon wakes up (polls mt_events)
   ↓
4. Daemon reads new events since last checkpoint
   ↓
5. For each event:
   - Calls Apply() on async projections
   - Updates mt_doc_{projection} table
   ↓
6. Daemon updates checkpoint (mt_event_progression)
   ↓
7. Daemon sleeps, repeats
```

**Key insight:** Async projections have **lag** (1-5 seconds by default). Queries may see stale data until daemon catches up.

### Multi-Stream Projections: Identity Resolution

```
1. Event arrives: InitialPriceSet { ProductPriceId: guid-A, Sku: "DOG-FOOD-5LB" }
   Stream ID: guid-A (from ProductPrice.StreamId("DOG-FOOD-5LB"))
   ↓
2. Marten runs Identity<InitialPriceSet>(x => x.Sku) mapping
   → Document ID: "DOG-FOOD-5LB" (string)
   ↓
3. Marten checks if document exists: SELECT * FROM mt_doc_currentpriceview WHERE id = 'DOG-FOOD-5LB'
   - Not found → Call Create() method
   - Found → Call Apply() method
   ↓
4. Marten inserts/updates document
```

**Key insight:** Multi-stream projections decouple stream IDs (Guid) from document IDs (string). Multiple streams can update the same document via shared identity property.

### Snapshot Projections: Aggregate Loading

```
1. Command handler needs aggregate state
   ↓
2. Wolverine loads snapshot: SELECT * FROM mt_doc_checkout WHERE id = ?
   ↓
3. If snapshot exists:
   → Handler gets current aggregate state (no event replay)
   ↓
4. If snapshot doesn't exist:
   → Fall back to event replay (AggregateStreamAsync)
```

**Key insight:** Snapshots are **read optimizations**. They don't change write behavior (events still appended to `mt_events`).

---

**Next steps:**
- For aggregate design patterns, read `marten-event-sourcing.md`
- For EF Core as projection target, read `efcore-marten-projections.md`
- For Wolverine command handler integration, read `wolverine-message-handlers.md`
- For testing patterns, read `critterstack-testing-patterns.md`
