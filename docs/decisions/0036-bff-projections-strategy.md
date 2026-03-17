# ADR 0036: BFF-Owned Projections Strategy

**Status:** ✅ Accepted

**Date:** 2026-03-17

**Context:**

The Backoffice BFF needs to display real-time dashboard metrics for the Executive persona:

- **Daily Order Count** — Total orders placed today
- **Daily Revenue** — Total payment amounts captured today
- **Payment Failure Rate** — Percentage of failed payment attempts
- **Average Order Value** — Revenue / order count
- **Fulfillment Pipeline Count** — Orders in "awaiting shipment" state

These metrics aggregate events from 3 domain BCs (Orders, Payments, Fulfillment) and must be queryable with **zero lag** (inline projection lifecycle, not async daemon).

**Architectural Decision Points:**

1. **Who owns the projection?** Backoffice BFF vs. separate Analytics BC
2. **Where does source data come from?** Domain BC events (RabbitMQ) vs. domain BC query endpoints
3. **What projection lifecycle?** Inline (zero lag) vs. async (eventually consistent)
4. **What storage mechanism?** Marten document projections vs. EF Core relational projections

**Decision Trigger:**

M32.0 (Backoffice Phase 1) implemented executive dashboard with 5 KPIs. This ADR documents the **BFF-owned projections strategy** validated during implementation: Marten inline projections consuming integration messages from domain BCs.

---

## Decision

Backoffice BFF **owns dashboard projections** using **Marten inline projections** consuming **integration messages from domain BCs via RabbitMQ**.

### 1. Projection Ownership: BFF (Not Analytics BC)

**Pattern: BFF owns projections for operational dashboards.**

**Rationale:**

- **Localized Queries:** Dashboard metrics are queried exclusively by Backoffice BFF. No other BC needs AdminDailyMetrics.
- **Composition:** Metrics aggregate events from Orders + Payments (2+ domain BCs). BFF is the natural composition point.
- **Simplicity:** Avoids creating a separate Analytics BC for 5 dashboard KPIs (over-engineering for Phase 1).

**Alternative Rejected: Separate Analytics BC**

**Rejected because:**
- Analytics BC would need to subscribe to same 14+ domain BC queues as Backoffice
- Backoffice would query Analytics BC for dashboard data (adds HTTP hop, latency)
- Over-engineering for Phase 1 (single dashboard with 5 KPIs)
- Phase 3+ can introduce Analytics BC if reporting requirements grow significantly (multi-tenant analytics, historical trends, data warehouse integration)

### 2. Source Data: Integration Messages (Not Query Endpoints)

**Pattern: BFF subscribes to domain BC events via RabbitMQ.**

**RabbitMQ Queue Wiring:**

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.UseRabbitMq(rabbit =>
    {
        // Subscribe to Orders BC events
        rabbit.DeclareQueue("backoffice-orders-events", q => q.IsDurable = true);
        rabbit.BindQueue("backoffice-orders-events", "orders-events");

        // Subscribe to Payments BC events
        rabbit.DeclareQueue("backoffice-payments-events", q => q.IsDurable = true);
        rabbit.BindQueue("backoffice-payments-events", "payments-events");
    });

    opts.ListenToRabbitQueue("backoffice-orders-events");
    opts.ListenToRabbitQueue("backoffice-payments-events");
});
```

**Integration Message Handlers:**

```csharp
// Append events to Backoffice stream (triggers AdminDailyMetricsProjection)
public static class OrderPlacedHandler
{
    public static async Task Handle(OrderPlaced evt, IDocumentSession session)
    {
        session.Events.Append(Guid.NewGuid(), evt);
        await session.SaveChangesAsync();
    }
}

public static class PaymentCapturedHandler
{
    public static async Task Handle(PaymentCaptured evt, IDocumentSession session)
    {
        session.Events.Append(Guid.NewGuid(), evt);
        await session.SaveChangesAsync();
    }
}
```

**Rationale:**

- **Event-Driven Architecture:** BFF reacts autonomously to domain BC events (choreography pattern, see ADR 0034)
- **Zero Coupling:** Domain BCs don't know about Backoffice projections
- **Resilience:** Backoffice downtime doesn't block domain BC workflows
- **Scalability:** Adding new metrics requires subscribing to more events (no domain BC changes)

**Alternative Rejected: Query Endpoints**

**Rejected because:**
- Requires domain BCs to expose admin-specific query endpoints (`GET /api/orders/metrics`)
- Adds tight coupling (domain BCs must know about Backoffice metric requirements)
- No event stream for projections (must poll domain BCs, higher latency)
- Domain BCs would duplicate projection logic (metrics calculation in Orders BC + Backoffice)

### 3. Projection Lifecycle: Inline (Zero Lag)

**Pattern: Inline projections update in the same transaction as message handling.**

**Marten Configuration:**

```csharp
builder.Services.AddMarten(opts =>
{
    opts.DatabaseSchemaName = "backoffice";

    // Inline lifecycle: zero lag, same transaction
    opts.Projections.Add<AdminDailyMetricsProjection>(ProjectionLifecycle.Inline);
    opts.Projections.Add<AlertFeedViewProjection>(ProjectionLifecycle.Inline);
})
.UseLightweightSessions();  // No dirty checking (projections are append-only)
```

**Rationale:**

- **Real-Time Dashboard:** Executive dashboard queries must return current-second metrics (not stale)
- **Transactional Consistency:** Projection updates atomically with event append (no lag)
- **Simplicity:** No async daemon process to manage

**Alternative Rejected: Async Lifecycle**

**Rejected because:**
- Async projections have lag (eventual consistency) — dashboard would show stale metrics
- Async daemon adds complexity (process management, retry logic, health checks)
- Executive dashboard is read frequently (every page refresh) — inline lifecycle is acceptable performance trade-off

**When to Use Async Lifecycle:**

- **Historical Trends:** Multi-month trend reports (not time-sensitive)
- **Heavy Computation:** Projections with complex joins or aggregations (would slow down message handling)
- **Analytics BC (Phase 3+):** Separate BC for historical reporting

### 4. Storage Mechanism: Marten Documents (Not EF Core)

**Pattern: Marten document projections with string-keyed IDs.**

**AdminDailyMetrics Document:**

```csharp
public sealed record AdminDailyMetrics
{
    public string Id { get; init; } = default!;  // Date key: "2026-03-17"
    public DateTimeOffset Date { get; init; }
    public int OrderCount { get; init; }
    public int CancelledOrderCount { get; init; }
    public decimal TotalRevenue { get; init; }
    public int PaymentFailureCount { get; init; }
    public DateTimeOffset LastUpdatedAt { get; init; }
}
```

**Marten MultiStreamProjection (Date-Keyed):**

```csharp
public sealed class AdminDailyMetricsProjection : MultiStreamProjection<AdminDailyMetrics, string>
{
    public AdminDailyMetricsProjection()
    {
        // Map events to date-keyed documents (YYYY-MM-DD)
        Identity<OrderPlaced>(x => ToDateKey(x.PlacedAt));
        Identity<PaymentCaptured>(x => ToDateKey(x.CapturedAt));
        Identity<PaymentFailed>(x => ToDateKey(x.FailedAt));
    }

    private static string ToDateKey(DateTimeOffset timestamp)
        => timestamp.UtcDateTime.Date.ToString("yyyy-MM-dd");

    public AdminDailyMetrics Create(OrderPlaced evt)
    {
        return new AdminDailyMetrics
        {
            Id = ToDateKey(evt.PlacedAt),
            Date = new DateTimeOffset(evt.PlacedAt.UtcDateTime.Date, TimeSpan.Zero),
            OrderCount = 1,
            TotalRevenue = 0m,
            PaymentFailureCount = 0,
            LastUpdatedAt = evt.PlacedAt
        };
    }

    public static AdminDailyMetrics Apply(AdminDailyMetrics current, OrderPlaced evt)
    {
        return current with
        {
            OrderCount = current.OrderCount + 1,
            LastUpdatedAt = evt.PlacedAt
        };
    }

    public static AdminDailyMetrics Apply(AdminDailyMetrics current, PaymentCaptured evt)
    {
        return current with
        {
            TotalRevenue = current.TotalRevenue + evt.Amount,
            LastUpdatedAt = evt.CapturedAt
        };
    }
}
```

**Rationale:**

- **Simple Schema:** Document projections are schema-less JSON (no EF Core migrations)
- **Date-Keyed Documents:** One document per day (e.g., `"2026-03-17"`) — efficient daily metric queries
- **Immutable Updates:** `Apply` methods return new instances (`with` expressions)
- **Consistency with BC:** Backoffice BC uses Marten for event sourcing (OrderNote aggregate) and projections (AdminDailyMetrics, AlertFeedView)

**Alternative Rejected: EF Core Relational Projections**

**Rejected because:**
- Requires `Marten.EntityFrameworkCore` package + EF Core DbContext (added complexity)
- Requires schema migrations (`.Migrations/` folder, `dotnet ef migrations add`)
- No performance benefit for simple aggregations (Marten document queries are fast enough)
- EF Core projections are better for complex joins or relational queries (not needed for dashboard KPIs)

**When to Use EF Core Projections:**

- **Complex Joins:** Multi-table queries with foreign keys (e.g., Order + OrderLineItems + Product)
- **Relational Integrity:** Referential integrity constraints (foreign keys, cascading deletes)
- **Analytics BC (Phase 3+):** Warehouse-style fact tables with dimension tables

### 5. Projection Query Pattern

**Pattern: Query projections by ID (date key) or filter by timestamp.**

**Dashboard Query (Today's Metrics):**

```csharp
public static class GetDashboardMetrics
{
    [WolverineGet("/api/backoffice/dashboard/metrics")]
    [Authorize(Policy = "Executive")]
    public static async Task<DashboardMetricsView> Handle(
        IDocumentSession session)
    {
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");

        // Query projection by date key
        var metrics = await session.LoadAsync<AdminDailyMetrics>(today)
            ?? new AdminDailyMetrics
            {
                Id = today,
                Date = DateTime.UtcNow.Date,
                OrderCount = 0,
                TotalRevenue = 0m,
                PaymentFailureCount = 0,
                LastUpdatedAt = DateTime.UtcNow
            };

        return new DashboardMetricsView(
            OrderCount: metrics.OrderCount,
            Revenue: metrics.TotalRevenue,
            AverageOrderValue: metrics.OrderCount > 0
                ? metrics.TotalRevenue / metrics.OrderCount
                : 0m,
            PaymentFailureRate: metrics.PaymentFailureCount > 0
                ? (decimal)metrics.PaymentFailureCount / metrics.OrderCount
                : 0m,
            FulfillmentPipelineCount: 0  // TODO: Add FulfillmentRequested event handler
        );
    }
}
```

**Rationale:**

- **Single Query:** Load projection by ID (no joins, no aggregations at query time)
- **Fallback for New Days:** Return empty metrics if document doesn't exist yet (first event of the day creates document)
- **Computed Metrics:** Average Order Value, Payment Failure Rate calculated in-memory (not stored in projection)

---

## Rationale

**Why BFF Owns Projections (Not Analytics BC):**

1. **Localized Queries:** Dashboard metrics are queried exclusively by Backoffice BFF
2. **Composition:** Metrics aggregate events from Orders + Payments (BFF is the natural composition point)
3. **Simplicity:** Avoids creating a separate Analytics BC for 5 dashboard KPIs (over-engineering for Phase 1)
4. **Consistency:** Storefront BFF owns CartView projection, Vendor Portal BFF owns VendorAccount projection

**Why Integration Messages (Not Query Endpoints):**

1. **Event-Driven Architecture:** BFF reacts autonomously to domain BC events (choreography pattern)
2. **Zero Coupling:** Domain BCs don't know about Backoffice projections
3. **Resilience:** Backoffice downtime doesn't block domain BC workflows
4. **Scalability:** Adding new metrics requires subscribing to more events (no domain BC changes)

**Why Inline Lifecycle (Not Async):**

1. **Real-Time Dashboard:** Executive dashboard queries must return current-second metrics
2. **Transactional Consistency:** Projection updates atomically with event append
3. **Simplicity:** No async daemon process to manage

**Why Marten Documents (Not EF Core):**

1. **Simple Schema:** Document projections are schema-less JSON (no migrations)
2. **Date-Keyed Documents:** One document per day — efficient daily metric queries
3. **Consistency:** Backoffice BC uses Marten for event sourcing (OrderNote) and projections (AdminDailyMetrics, AlertFeedView)

---

## Consequences

**Positive:**

- ✅ **Zero-lag dashboards** — Inline projections update in the same transaction as message handling
- ✅ **Simple schema** — Document projections are schema-less JSON (no EF Core migrations)
- ✅ **Event-driven architecture** — BFF reacts autonomously to domain BC events
- ✅ **Scalable** — Adding new metrics requires subscribing to more events (no domain BC changes)
- ✅ **Testable** — Integration message handlers are pure functions (append event → projection updates)

**Negative:**

- ⚠️ **Inline projection performance** — High-throughput events (1000+ orders/minute) may slow down message handling
- ⚠️ **No historical trends** — Projections optimized for "today's metrics" (not multi-month trends)
- ⚠️ **RabbitMQ queue proliferation** — BFF subscribes to 14+ domain BC queues (1 per integration)

**Mitigation:**

- **Inline projection performance:** Dashboard metrics are low-volume (Orders + Payments events, not Shopping cart events). If throughput becomes a bottleneck, move to async lifecycle.
- **No historical trends:** Phase 3+ can introduce Analytics BC with async projections for historical reporting (multi-month trends, data warehouse integration).
- **Queue proliferation:** RabbitMQ handles 1000s of queues; 14 queues is not a scaling concern.

---

## Alternatives Considered

### Alternative A: Separate Analytics BC

**Pattern:** Create a dedicated Analytics BC that owns dashboard projections.

**Rejected because:**
- Over-engineering for Phase 1 (single dashboard with 5 KPIs)
- Analytics BC would subscribe to same 14+ domain BC queues as Backoffice
- Backoffice would query Analytics BC for dashboard data (adds HTTP hop, latency)
- Phase 3+ can introduce Analytics BC if reporting requirements grow significantly

---

### Alternative B: Domain BCs Expose Query Endpoints

**Pattern:** Domain BCs expose admin-specific query endpoints (e.g., `GET /api/orders/metrics`).

**Rejected because:**
- Adds tight coupling (domain BCs must know about Backoffice metric requirements)
- No event stream for projections (must poll domain BCs, higher latency)
- Domain BCs duplicate projection logic (metrics calculation in Orders BC + Backoffice)
- Violates BC boundaries (domain BCs should not expose admin-specific views)

---

### Alternative C: EF Core Relational Projections

**Pattern:** Use `Marten.EntityFrameworkCore` to project events to relational tables.

**Rejected because:**
- Requires EF Core DbContext + migrations (added complexity)
- No performance benefit for simple aggregations (Marten document queries are fast enough)
- Better for complex joins or relational queries (not needed for dashboard KPIs)
- Phase 3+ can introduce EF Core projections if Analytics BC needs warehouse-style fact tables

---

## References

- **BFF Pattern:** [ADR 0034: Backoffice BFF Architecture](./0034-backoffice-bff-architecture.md)
- **OrderNote Aggregate:** [ADR 0037: OrderNote Aggregate Ownership](./0037-ordernote-aggregate-ownership.md) (why OrderNote lives in Backoffice BC)
- **SignalR Hub:** [ADR 0035: Backoffice SignalR Hub Design](./0035-backoffice-signalr-hub-design.md) (real-time dashboard updates)
- **Skills:**
  - [Event Sourcing Projections](../skills/event-sourcing-projections.md) — MultiStreamProjection, inline lifecycle
  - [Integration Messaging](../skills/integration-messaging.md) — RabbitMQ queue wiring
  - [BFF Real-Time Patterns](../skills/bff-realtime-patterns.md) — Integration message handlers

---

**Implementation Milestone:**

- **M32.0 (Backoffice Phase 1):** BFF projections validated — AdminDailyMetrics + AlertFeedView (inline lifecycle, Marten documents, 14+ RabbitMQ queue subscriptions)

---

**Status:** ✅ **Accepted** — 2026-03-17

*This ADR documents the BFF-owned projections strategy validated during M32.0 implementation.*
