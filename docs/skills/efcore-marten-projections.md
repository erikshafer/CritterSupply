# EF Core Projections with Marten (and Polecat)

**Purpose:** Using Entity Framework Core as a projection target for Marten's event store — bridging event sourcing (Marten) with relational read models (EF Core).

**When to read this:** Before using EF Core entities as projections from Marten event streams, especially when you need relational query capabilities (joins, aggregations, filtering) that EF Core's LINQ provider handles better than Marten's native document projections.

---

## Table of Contents

1. [Overview](#overview)
2. [When to Use EF Core Projections](#when-to-use-ef-core-projections)
3. [When NOT to Use EF Core Projections](#when-not-to-use-ef-core-projections)
4. [Installation and Setup](#installation-and-setup)
5. [DbContext Configuration](#dbcontext-configuration)
6. [Single Stream Projections](#single-stream-projections)
7. [Multi-Stream Projections](#multi-stream-projections)
8. [Event Projections](#event-projections)
9. [Conjoined Multi-Tenancy](#conjoined-multi-tenancy)
10. [Composite Projections](#composite-projections)
11. [Polecat and SQL Server](#polecat-and-sql-server)
12. [Testing EF Core-Backed Projections](#testing-ef-core-backed-projections)
13. [Common Pitfalls and Warnings](#common-pitfalls-and-warnings)
14. [Production Lessons Learned](#production-lessons-learned)
15. [How It Works Under the Hood](#how-it-works-under-the-hood)
16. [Appendix](#appendix)

---

## Overview

**What this pattern enables:**

Marten's `Marten.EntityFrameworkCore` package provides three projection base classes that let you write event-sourced aggregates to relational tables via Entity Framework Core, while still benefiting from Marten's event store as the system of record.

**The three projection types:**

| Base Class | Use Case | Stream Model |
|------------|----------|--------------|
| `EfCoreSingleStreamProjection<TDoc, TId, TDbContext>` | Aggregate a single event stream into one EF Core entity | One stream → One entity |
| `EfCoreMultiStreamProjection<TDoc, TId, TDbContext>` | Aggregate events across multiple streams into one EF Core entity | Many streams → One entity |
| `EfCoreEventProjection<TDbContext>` | React to individual events, writing to both EF Core and Marten in the same transaction | Event-driven side effects |

**Key capabilities:**

- All three support **Inline**, **Async**, and **Live** projection lifecycles
- Full EF Core model configuration (`OnModelCreating`, navigation properties, indexes)
- Automatic schema migration via Weasel (no `dotnet ef database update` required)
- Transaction coordination — EF Core and Marten commit atomically
- EF Core change tracking handles insert vs. update detection

**Where this fits in CritterSupply's architecture:**

This is distinct from `efcore-wolverine-integration.md`, which covers using EF Core as the *primary* persistence layer for a bounded context. Here, EF Core is a *projection target* — events are the source of truth (stored in Marten's `mt_events` table), and EF Core tables are derived read models optimized for relational queries.

---

## When to Use EF Core Projections

✅ **Reach for EF Core projections when:**

1. **Relational queries are complex** — Joins, aggregations, and filtering are more ergonomic in EF Core's LINQ provider than Marten's JSONB queries
   - Example: "All open returns by customer, joined with order details and return reason codes"
   - Example: "Product performance summary aggregating sales by SKU × time bucket with vendor join"

2. **Your team's SQL/EF Core expertise outweighs JSONB query experience** — EF Core migrations, DbContext configuration, and SQL Server Management Studio are familiar territory for many .NET teams

3. **You need cross-database compatibility** — EF Core projections work with both Marten (PostgreSQL) and Polecat (SQL Server) with minimal code changes

4. **Reporting and analytics are primary concerns** — EF Core entities can be queried by BI tools (Power BI, Tableau) that speak SQL natively

5. **Migration from legacy relational schemas** — If you're event-sourcing a system that already has consumers expecting relational tables, EF Core projections let you maintain that schema shape

6. **Multi-stream aggregation with complex identity mapping** — `EfCoreMultiStreamProjection` shines when multiple event streams contribute to a single denormalized entity (e.g., customer order history across many order streams)

---

## When NOT to Use EF Core Projections

❌ **Do NOT use EF Core projections when:**

1. **Marten's document projections are sufficient** — If your queries are simple document lookups by ID or basic LINQ `.Where()` clauses, native Marten projections (`SingleStreamProjection<T>`, `MultiStreamProjection<T>`) are simpler and have fewer moving parts

2. **You're building a BFF that only composes HTTP calls** — BFFs like Storefront typically don't need rich projections at all; they query other BCs via HTTP and compose views in memory

3. **Performance is critical and you're on PostgreSQL** — Marten's native JSONB projections are highly optimized for Postgres; adding EF Core adds overhead (change tracking, additional ADO.NET round-trips)

4. **The event stream is the query model** — If consuming applications directly query the event store (e.g., "show me all OrderPlaced events for customer X"), projections are unnecessary

5. **You want to avoid dual persistence** — EF Core projections mean data lives in two places: Marten's event store (source of truth) and EF Core tables (derived read models). This adds operational complexity (schema migrations, projection rebuilds).

---

## Installation and Setup

### 1. Install the NuGet package

```bash
dotnet add package Marten.EntityFrameworkCore
```

**Current version:** Check `Directory.Packages.props` for the centrally managed version. As of Cycle 28, Marten is at 8.x.

### 2. Add EF Core provider

EF Core projections require an EF Core database provider. For CritterSupply:

- **PostgreSQL (Marten-backed BCs):** `Npgsql.EntityFrameworkCore.PostgreSQL`
- **SQL Server (Polecat-backed BCs):** `Microsoft.EntityFrameworkCore.SqlServer`

These should already be in `Directory.Packages.props` if you're adding projections to an existing BC.

### 3. Verify Weasel is configured

Marten's Weasel schema migration engine automatically migrates EF Core entity tables alongside Marten's own schema objects. No separate `dotnet ef database update` is required.

---

## DbContext Configuration

### Defining Your Projection DbContext

Create a `DbContext` with entity mappings for your projections. Use `OnModelCreating` to configure table names, column mappings, indexes, and constraints.

**Example:** Returns BC might define a `ReturnProjectionDbContext` for read models derived from `Return` event streams.

```csharp
using Microsoft.EntityFrameworkCore;

namespace Returns.Projections;

public class ReturnProjectionDbContext : DbContext
{
    public ReturnProjectionDbContext(DbContextOptions<ReturnProjectionDbContext> options)
        : base(options)
    {
    }

    public DbSet<ReturnSummary> ReturnSummaries => Set<ReturnSummary>();
    public DbSet<ReturnItemDetail> ReturnItemDetails => Set<ReturnItemDetail>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReturnSummary>(entity =>
        {
            entity.ToTable("return_summaries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.RequestedAt).HasColumnName("requested_at");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");

            // Index for common queries
            entity.HasIndex(e => new { e.CustomerId, e.Status });
            entity.HasIndex(e => e.OrderId);
        });

        modelBuilder.Entity<ReturnItemDetail>(entity =>
        {
            entity.ToTable("return_item_details");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ReturnId).HasColumnName("return_id");
            entity.Property(e => e.Sku).HasColumnName("sku");
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.Disposition).HasColumnName("disposition");

            // Navigation property to ReturnSummary
            entity.HasOne<ReturnSummary>()
                .WithMany()
                .HasForeignKey(e => e.ReturnId);
        });
    }
}
```

**Key conventions:**

- Use snake_case table and column names to match Marten's conventions
- Define all mappings explicitly (no conventions-based inference)
- Add indexes for queries you know will be common
- Navigation properties work as expected — EF Core handles foreign keys

### Registering the DbContext

In your API's `Program.cs`, register the `DbContext` with Marten's configuration:

```csharp
builder.Services.AddDbContext<ReturnProjectionDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("marten");
    options.UseNpgsql(connectionString); // Or UseSqlServer for Polecat BCs
});
```

**Important:** The `DbContext` is registered separately from your projection classes. Projection registration happens in Marten's `AddMarten()` configuration (see sections below).

---

## Single Stream Projections

Use `EfCoreSingleStreamProjection<TDoc, TId, TDbContext>` when **one event stream** maps to **one EF Core entity**.

**Example use case:** Each `Return` aggregate (one stream) projects to one `ReturnSummary` entity.

### Entity Model

```csharp
namespace Returns.Projections;

public sealed class ReturnSummary
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Reason { get; set; }
    public string? Disposition { get; set; }
}
```

### Projection Class

Override `ApplyEvent` to handle each domain event. The snapshot (current entity state), identity (stream ID), event, `DbContext`, and Marten query session are all available.

```csharp
using Marten;
using Marten.EntityFrameworkCore;
using Marten.Events;
using Returns.Events;

namespace Returns.Projections;

public sealed class ReturnSummaryProjection
    : EfCoreSingleStreamProjection<ReturnSummary, Guid, ReturnProjectionDbContext>
{
    public override ReturnSummary? ApplyEvent(
        ReturnSummary? snapshot,
        Guid identity,
        IEvent @event,
        ReturnProjectionDbContext dbContext,
        IQuerySession session)
    {
        switch (@event.Data)
        {
            case ReturnRequested requested:
                return new ReturnSummary
                {
                    Id = requested.ReturnId,
                    OrderId = requested.OrderId,
                    CustomerId = requested.CustomerId,
                    Status = "Requested",
                    RequestedAt = requested.RequestedAt,
                    Reason = requested.Reason
                };

            case ReturnApproved approved:
                if (snapshot != null)
                {
                    snapshot.Status = "Approved";
                }
                return snapshot;

            case ReturnReceived received:
                if (snapshot != null)
                {
                    snapshot.Status = "Received";
                }
                return snapshot;

            case ReturnCompleted completed:
                if (snapshot != null)
                {
                    snapshot.Status = "Completed";
                    snapshot.CompletedAt = completed.CompletedAt;
                    snapshot.Disposition = completed.Disposition.ToString();
                }
                return snapshot;

            case ReturnRejected rejected:
                if (snapshot != null)
                {
                    snapshot.Status = "Rejected";
                    snapshot.CompletedAt = rejected.RejectedAt;
                }
                return snapshot;
        }

        return snapshot;
    }
}
```

**Pattern notes:**

- First event (stream creation) returns a new entity
- Subsequent events mutate the snapshot and return it
- Returning `null` would delete the entity (rarely needed)
- EF Core change tracking determines if this is an insert or update

### Registration

Use the `StoreOptions.Add()` extension method to register the projection and configure its lifecycle:

```csharp
var store = DocumentStore.For(opts =>
{
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = "returns";

    // Register single-stream projection
    opts.Add(new ReturnSummaryProjection(), ProjectionLifecycle.Inline);
    // Options: Inline (synchronous), Async (background daemon), Live (real-time daemon)
});
```

**Lifecycle options:**

| Lifecycle | When It Runs | Use Case |
|-----------|-------------|----------|
| `Inline` | Within the same transaction as event append | Strong consistency required; user waits for projection |
| `Async` | Background daemon processes events after commit | Eventual consistency acceptable; better write throughput |
| `Live` | Real-time daemon, near-zero latency | Best of both worlds; requires async daemon running |

---

## Multi-Stream Projections

Use `EfCoreMultiStreamProjection<TDoc, TId, TDbContext>` when **multiple event streams** contribute to **one EF Core entity**.

**Example use case:** A `CustomerOrderHistory` entity aggregates events from many `Order` streams (one per order) into a single customer-scoped summary.

### Entity Model

```csharp
namespace Orders.Projections;

public sealed class CustomerOrderHistory
{
    public Guid Id { get; set; } // Customer ID
    public int TotalOrders { get; set; }
    public decimal TotalSpent { get; set; }
    public DateTimeOffset? LastOrderPlacedAt { get; set; }
    public int CancelledOrders { get; set; }
}
```

### Projection Class

Use the constructor to configure event-to-aggregate identity mapping via the `Identity<TEvent>()` method. This tells Marten which field in each event maps to the aggregate's ID.

```csharp
using Marten;
using Marten.EntityFrameworkCore;
using Marten.Events;
using Orders.Events;

namespace Orders.Projections;

public sealed class CustomerOrderHistoryProjection
    : EfCoreMultiStreamProjection<CustomerOrderHistory, Guid, OrderProjectionDbContext>
{
    public CustomerOrderHistoryProjection()
    {
        // Map events to the customer ID (the aggregate identity)
        Identity<OrderPlaced>(e => e.CustomerId);
        Identity<OrderCancelled>(e => e.CustomerId);
        // Add more event mappings as needed
    }

    public override CustomerOrderHistory? ApplyEvent(
        CustomerOrderHistory? snapshot,
        Guid identity, // This is the customer ID
        IEvent @event,
        OrderProjectionDbContext dbContext)
    {
        // Initialize snapshot if first event for this customer
        snapshot ??= new CustomerOrderHistory { Id = identity };

        switch (@event.Data)
        {
            case OrderPlaced placed:
                snapshot.TotalOrders++;
                snapshot.TotalSpent += placed.TotalAmount;
                snapshot.LastOrderPlacedAt = placed.PlacedAt;
                break;

            case OrderCancelled cancelled:
                snapshot.CancelledOrders++;
                // TotalOrders stays the same (they did place it, then cancelled)
                break;
        }

        return snapshot;
    }
}
```

**Key differences from single-stream projections:**

- `Identity<TEvent>()` calls in the constructor map events to aggregate ID
- Multiple streams (one per order) contribute to one entity (per customer)
- The `identity` parameter is the mapped ID (customer ID), not the stream ID (order ID)

### Registration

Same pattern as single-stream:

```csharp
opts.Add(new CustomerOrderHistoryProjection(), ProjectionLifecycle.Async);
```

**Why Async here?** Customer order history doesn't need to be updated synchronously on every order placement — eventual consistency (within seconds) is acceptable, and async projection improves write throughput.

---

## Event Projections

Use `EfCoreEventProjection<TDbContext>` when you need to react to individual events and write to **both** EF Core entities and Marten documents in the same transaction.

**Example use case:** Maintain both a relational `OrderSummary` table (for BI queries) and a Marten `OrderAnalytics` document (for internal aggregations) from the same event stream.

### Projection Class

Override `ProjectAsync` instead of `ApplyEvent`. You get the event, `DbContext`, Marten `IDocumentOperations`, and cancellation token.

```csharp
using Marten;
using Marten.EntityFrameworkCore;
using Marten.Events;
using Orders.Events;

namespace Orders.Projections;

public sealed class OrderDualStoreProjection : EfCoreEventProjection<OrderProjectionDbContext>
{
    protected override async Task ProjectAsync(
        IEvent @event,
        OrderProjectionDbContext dbContext,
        IDocumentOperations operations,
        CancellationToken token)
    {
        switch (@event.Data)
        {
            case OrderPlaced placed:
                // Write to EF Core (relational read model)
                dbContext.OrderSummaries.Add(new OrderSummary
                {
                    Id = placed.OrderId,
                    CustomerId = placed.CustomerId,
                    TotalAmount = placed.TotalAmount,
                    ItemCount = placed.Items.Count,
                    Status = "Placed",
                    PlacedAt = placed.PlacedAt
                });

                // Also write to Marten (document analytics model)
                operations.Store(new OrderAnalytics
                {
                    Id = placed.OrderId,
                    PlacedAt = placed.PlacedAt,
                    CustomerId = placed.CustomerId,
                    TotalAmount = placed.TotalAmount
                });
                break;

            case OrderShipped shipped:
                // Update EF Core entity
                var summary = await dbContext.OrderSummaries
                    .FindAsync(new object[] { shipped.OrderId }, token);
                if (summary != null)
                {
                    summary.Status = "Shipped";
                    summary.ShippedAt = shipped.ShippedAt;
                }

                // Update Marten document
                var analytics = await operations.LoadAsync<OrderAnalytics>(
                    shipped.OrderId, token);
                if (analytics != null)
                {
                    analytics.ShippedAt = shipped.ShippedAt;
                    operations.Store(analytics);
                }
                break;
        }
    }
}
```

**Pattern notes:**

- `ProjectAsync` is async (unlike `ApplyEvent`)
- `IDocumentOperations` lets you call `.Store()`, `.LoadAsync()`, `.Delete()` on Marten documents
- Both EF Core and Marten changes commit in the same transaction — atomicity guaranteed

### Registration

Event projections use a different registration pattern — `Projections.Add()` instead of `StoreOptions.Add()`, plus a call to register entity tables:

```csharp
var store = DocumentStore.For(opts =>
{
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = "orders";

    // Register event projection
    opts.Projections.Add(new OrderDualStoreProjection(), ProjectionLifecycle.Inline);

    // Register EF Core entity tables for schema migration
    opts.AddEntityTablesFromDbContext<OrderProjectionDbContext>();
});
```

**Why the different registration?** `EfCoreEventProjection` is a lower-level `IProjection` implementation that doesn't follow the aggregate-document model of single/multi-stream projections.

---

## Conjoined Multi-Tenancy

When Marten's event store uses `TenancyStyle.Conjoined` (tenant ID column in `mt_events`), EF Core projections automatically write the tenant ID to each projected entity — **if your entity implements `ITenanted`.**

### Requirements

#### 1. Entity must implement `ITenanted`

```csharp
using Marten.Metadata;

namespace Returns.Projections;

public sealed class TenantedReturnSummary : ITenanted
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }

    // Required by ITenanted
    public string? TenantId { get; set; }
}
```

#### 2. DbContext must map the TenantId column

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<TenantedReturnSummary>(entity =>
    {
        entity.ToTable("tenanted_return_summaries");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.TenantId).HasColumnName("tenant_id");
        // ... other mappings

        // Optional: Composite index for tenant + status queries
        entity.HasIndex(e => new { e.TenantId, e.Status });
    });
}
```

#### 3. Configure Marten for conjoined tenancy

```csharp
var store = DocumentStore.For(opts =>
{
    opts.Connection(connectionString);
    opts.Events.TenancyStyle = TenancyStyle.Conjoined;

    opts.Add(new TenantedReturnSummaryProjection(), ProjectionLifecycle.Inline);
});
```

### Projection Class (No Special Tenancy Logic)

The projection class itself does **not** need to set `TenantId` — the base infrastructure does it automatically:

```csharp
public sealed class TenantedReturnSummaryProjection
    : EfCoreSingleStreamProjection<TenantedReturnSummary, Guid, ReturnProjectionDbContext>
{
    public override TenantedReturnSummary? ApplyEvent(
        TenantedReturnSummary? snapshot,
        Guid identity,
        IEvent @event,
        ReturnProjectionDbContext dbContext,
        IQuerySession session)
    {
        switch (@event.Data)
        {
            case ReturnRequested requested:
                return new TenantedReturnSummary
                {
                    Id = requested.ReturnId,
                    OrderId = requested.OrderId,
                    CustomerId = requested.CustomerId,
                    Status = "Requested",
                    RequestedAt = requested.RequestedAt
                    // TenantId is set automatically by Marten
                };

            // ... other events
        }

        return snapshot;
    }
}
```

### Appending Events with Tenant Context

Use `ForTenant()` when opening a Marten session to associate events with a specific tenant:

```csharp
await using var session = store.LightweightSession("tenant-alpha");

var returnId = Guid.NewGuid();
session.Events.StartStream<Return>(
    returnId,
    new ReturnRequested(returnId, orderId, customerId, "Damaged", DateTimeOffset.UtcNow)
);

await session.SaveChangesAsync();
// The projected row in tenanted_return_summaries will have tenant_id = 'tenant-alpha'
```

### Startup Validation

Marten validates your configuration at startup. If:
- Event store uses `TenancyStyle.Conjoined`, AND
- Your aggregate entity does **not** implement `ITenanted`

Marten throws `InvalidProjectionException` with a descriptive error message.

**This is a safeguard** — it prevents silent data corruption where tenant IDs would be `NULL` in your projection tables.

### Limitations and Warnings

⚠️ **Critical Limitation 1: `EfCoreEventProjection` does NOT participate in tenancy validation**

The event projection base class (`EfCoreEventProjection<TDbContext>`) is a lower-level `IProjection` implementation that does not validate `ITenanted` at startup. If you're using event projections with conjoined tenancy, you are responsible for:
1. Reading the tenant ID from `@event.TenantId`
2. Writing it to your entity's `TenantId` property manually

**Example:**

```csharp
public sealed class TenantedOrderDualStoreProjection : EfCoreEventProjection<OrderProjectionDbContext>
{
    protected override async Task ProjectAsync(
        IEvent @event,
        OrderProjectionDbContext dbContext,
        IDocumentOperations operations,
        CancellationToken token)
    {
        switch (@event.Data)
        {
            case OrderPlaced placed:
                dbContext.OrderSummaries.Add(new OrderSummary
                {
                    Id = placed.OrderId,
                    TenantId = @event.TenantId, // Must set manually
                    CustomerId = placed.CustomerId,
                    // ... other properties
                });
                break;
        }
    }
}
```

⚠️ **Critical Limitation 2: `FindAsync` with composite keys and tenancy**

When using `EfCoreMultiStreamProjection` with conjoined tenancy, be aware that `DbContext.FindAsync()` looks up entities **by primary key only**, not by a composite of primary key + tenant ID.

If two tenants can produce the same aggregate key (e.g., a customer name, SKU, or sequential ID), you **must**:
- Use globally unique aggregate IDs (GUIDs), OR
- Configure a composite primary key in EF Core that includes the `TenantId` column

**Example of composite key configuration:**

```csharp
modelBuilder.Entity<TenantedCustomerOrderHistory>(entity =>
{
    entity.HasKey(e => new { e.TenantId, e.CustomerId });
    // Now lookups require both TenantId and CustomerId
});
```

**Why this matters:** Without composite keys, a multi-stream projection for customer "Alice" in tenant A could overwrite data for customer "Alice" in tenant B if both have the same aggregate ID.

---

## Composite Projections

EF Core projections can participate in Marten's [composite projections](https://martendb.io/events/projections/composite) for multi-stage processing. This is useful when one projection depends on another being completed first.

**Example:** A `ProductPerformanceSummary` projection (stage 2) reads from `OrderSummary` entities (stage 1) to calculate aggregated sales metrics.

### Registration

```csharp
var store = DocumentStore.For(opts =>
{
    opts.Connection(connectionString);

    opts.Projections.Composite(composite =>
    {
        // Stage 1: Order summaries
        composite.Add(opts, new OrderSummaryProjection(), stageNumber: 1);

        // Stage 2: Product performance (depends on stage 1)
        composite.Add(opts, new ProductPerformanceProjection(), stageNumber: 2);
    }, ProjectionLifecycle.Async);
});
```

**Key points:**

- Lower stage numbers run first
- Each stage completes before the next begins
- All stages share the same transaction (Inline) or high-water mark (Async/Live)

**When to use composite projections:**

- Derived aggregations (e.g., daily sales summaries derived from hourly summaries)
- Normalization across BCs (e.g., enriching order summaries with product catalog data)
- Fan-out notifications (e.g., stage 1 updates entities, stage 2 sends notifications)

---

## Polecat and SQL Server

[Polecat](https://github.com/JasperFx/polecat) is a port of Marten targeting SQL Server 2025 instead of PostgreSQL. It mirrors Marten's API surface, including EF Core projection support.

### What Changes for Polecat

| Aspect | Marten (PostgreSQL) | Polecat (SQL Server) |
|--------|---------------------|----------------------|
| NuGet package | `Marten.EntityFrameworkCore` | `Polecat.EntityFrameworkCore` |
| EF Core provider | `Npgsql.EntityFrameworkCore.PostgreSQL` | `Microsoft.EntityFrameworkCore.SqlServer` |
| JSON storage | `jsonb` (binary) | `json` (text, SQL Server 2025+) |
| Default schema | `public` | `dbo` |
| String collation | Case-sensitive (default) | Case-insensitive (default `SQL_Latin1_General_CP1_CI_AS`) |
| Change notification | `LISTEN/NOTIFY` (push) | Polling (configurable interval) |

### Registration Pattern (Polecat)

The registration API is identical to Marten:

```csharp
builder.Services.AddDbContext<ReturnProjectionDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("sqlserver");
    options.UseSqlServer(connectionString); // Instead of UseNpgsql
});

var store = DocumentStore.For(opts =>
{
    opts.ConnectionString = connectionString; // Polecat uses property, not method
    opts.DatabaseSchemaName = "returns"; // Explicit — Polecat defaults to "dbo"

    opts.Add(new ReturnSummaryProjection(), ProjectionLifecycle.Inline);
});
```

### String Collation Gotchas (SQL Server)

SQL Server's default collation (`SQL_Latin1_General_CP1_CI_AS`) is **case-insensitive**, while PostgreSQL defaults to case-sensitive. This affects:

- Document ID lookups (e.g., SKU-based IDs: `"DOG-001"` and `"dog-001"` are the same key on SQL Server)
- LINQ `.Where()` string comparisons
- Unique indexes on string columns

**Mitigation options:**

1. **Use case-sensitive collation:** Create SQL Server databases with `COLLATE Latin1_General_CS_AS` (case-sensitive). This must be set at database creation time.
2. **Use GUIDs for aggregate IDs:** Eliminates case sensitivity as a concern.
3. **Normalize string keys:** Always `.ToUpperInvariant()` or `.ToLowerInvariant()` before storing/querying.

**CritterSupply's approach (from ADR 0026):**

For Polecat-backed BCs (if implemented), composite string IDs (like `VendorProductCatalogEntry` using SKU as ID) will be tested with mixed-case lookups in Phase 0 to determine if explicit collation configuration is needed.

### Polecat Projection Example

This code would work identically on Marten (Postgres) or Polecat (SQL Server):

```csharp
public sealed class VendorProductPerformanceProjection
    : EfCoreMultiStreamProjection<VendorProductPerformance, string, VendorProjectionDbContext>
{
    public VendorProductPerformanceProjection()
    {
        Identity<OrderPlaced>(e => e.Items.Select(i => i.Sku));
    }

    public override VendorProductPerformance? ApplyEvent(
        VendorProductPerformance? snapshot,
        string identity, // SKU
        IEvent @event,
        VendorProjectionDbContext dbContext)
    {
        snapshot ??= new VendorProductPerformance { Sku = identity };

        if (@event.Data is OrderPlaced placed)
        {
            var item = placed.Items.FirstOrDefault(i => i.Sku == identity);
            if (item != null)
            {
                snapshot.TotalUnitsSold += item.Quantity;
                snapshot.TotalRevenue += item.Quantity * item.Price;
            }
        }

        return snapshot;
    }
}
```

The only difference would be in `Program.cs`:

```csharp
// Marten (Postgres)
builder.Services.AddDbContext<VendorProjectionDbContext>(opts =>
    opts.UseNpgsql(connectionString));

// Polecat (SQL Server)
builder.Services.AddDbContext<VendorProjectionDbContext>(opts =>
    opts.UseSqlServer(connectionString));
```

### When to Consider Polecat

✅ **Use Polecat (SQL Server) when:**

- Your organization mandates SQL Server for compliance/licensing reasons
- Your team's SQL Server expertise outweighs PostgreSQL knowledge
- You need Azure SQL Managed Instance integration
- BI tools in your org are optimized for SQL Server (SSMS, SQL Profiler, Power BI)
- You're migrating a legacy SQL Server-based system to event sourcing

❌ **Stick with Marten (PostgreSQL) when:**

- You have PostgreSQL expertise in-house
- Performance is critical (Postgres's JSONB is faster than SQL Server's JSON type)
- You're already running Postgres for other BCs (dual-database overhead)
- Polecat is pre-1.0 and your risk tolerance is low

**CritterSupply's plan (ADR 0026):** Returns BC and Vendor Portal are candidates for Polecat migration to validate SQL Server patterns in a reference architecture context. See `docs/decisions/0026-polecat-sql-server-migration.md` for the full migration plan.

---

## Testing EF Core-Backed Projections

### Integration Test Pattern

EF Core projections require a test fixture that sets up both Marten (for event appending) and the `DbContext` (for querying projected entities).

**Fixture example:**

```csharp
using Alba;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Returns.Api.IntegrationTests;

public sealed class ReturnProjectionTestFixture : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private IAlbaHost _host = null!;

    public IAlbaHost Host => _host;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithDatabase("critter_supply_returns")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _container.StartAsync();

        _host = await AlbaHost.For<Program>(builder =>
        {
            builder.UseSetting("ConnectionStrings:marten", _container.GetConnectionString());
        });
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        await _container.DisposeAsync();
    }
}
```

**Test example:**

```csharp
using Alba;
using Microsoft.Extensions.DependencyInjection;
using Returns.Events;
using Returns.Projections;
using Shouldly;
using Xunit;

namespace Returns.Api.IntegrationTests;

public sealed class ReturnSummaryProjectionTests : IClassFixture<ReturnProjectionTestFixture>
{
    private readonly ReturnProjectionTestFixture _fixture;

    public ReturnSummaryProjectionTests(ReturnProjectionTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ReturnRequested_should_project_to_ReturnSummary()
    {
        // Arrange
        var returnId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        // Act - Append event via Marten
        await using (var scope = _fixture.Host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
            await using var session = store.LightweightSession();

            session.Events.StartStream<Return>(
                returnId,
                new ReturnRequested(returnId, orderId, customerId, "Damaged", DateTimeOffset.UtcNow)
            );

            await session.SaveChangesAsync();
        }

        // Assert - Query via DbContext
        await using (var scope = _fixture.Host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ReturnProjectionDbContext>();

            var summary = await dbContext.ReturnSummaries.FindAsync(returnId);

            summary.ShouldNotBeNull();
            summary.Id.ShouldBe(returnId);
            summary.OrderId.ShouldBe(orderId);
            summary.CustomerId.ShouldBe(customerId);
            summary.Status.ShouldBe("Requested");
            summary.Reason.ShouldBe("Damaged");
        }
    }

    [Fact]
    public async Task ReturnCompleted_should_update_existing_ReturnSummary()
    {
        // Arrange
        var returnId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await using (var scope = _fixture.Host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
            await using var session = store.LightweightSession();

            session.Events.StartStream<Return>(
                returnId,
                new ReturnRequested(returnId, orderId, customerId, "Damaged", DateTimeOffset.UtcNow),
                new ReturnApproved(returnId, DateTimeOffset.UtcNow),
                new ReturnReceived(returnId, DateTimeOffset.UtcNow)
            );

            await session.SaveChangesAsync();
        }

        // Act - Append completion event
        await using (var scope = _fixture.Host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
            await using var session = store.LightweightSession();

            session.Events.Append(
                returnId,
                new ReturnCompleted(returnId, "Restockable", DateTimeOffset.UtcNow)
            );

            await session.SaveChangesAsync();
        }

        // Assert
        await using (var scope = _fixture.Host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ReturnProjectionDbContext>();

            var summary = await dbContext.ReturnSummaries.FindAsync(returnId);

            summary.ShouldNotBeNull();
            summary.Status.ShouldBe("Completed");
            summary.Disposition.ShouldBe("Restockable");
            summary.CompletedAt.ShouldNotBeNull();
        }
    }
}
```

**Key testing patterns:**

- Use `IClassFixture` to share the test container across tests
- Create separate scopes for appending events vs. querying projections
- Use `IDocumentStore` to append events (source of truth)
- Use `DbContext` to query projected entities (derived read models)
- Inline projections complete within the same `SaveChangesAsync()` call
- Async projections may require `await Task.Delay()` or polling (see below)

### Testing Async Projections

Async projections run in a background daemon. Tests must wait for the projection to catch up after appending events.

**Polling pattern:**

```csharp
[Fact]
public async Task Async_projection_should_eventually_update()
{
    // Arrange
    var returnId = Guid.NewGuid();

    await using (var scope = _fixture.Host.Services.CreateAsyncScope())
    {
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        await using var session = store.LightweightSession();

        session.Events.StartStream<Return>(
            returnId,
            new ReturnRequested(returnId, orderId, customerId, "Damaged", DateTimeOffset.UtcNow)
        );

        await session.SaveChangesAsync();
    }

    // Act & Assert - Poll until projection completes
    ReturnSummary? summary = null;
    var timeout = TimeSpan.FromSeconds(10);
    var stopwatch = Stopwatch.StartNew();

    while (stopwatch.Elapsed < timeout)
    {
        await using var scope = _fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReturnProjectionDbContext>();

        summary = await dbContext.ReturnSummaries.FindAsync(returnId);
        if (summary != null)
            break;

        await Task.Delay(100); // Poll every 100ms
    }

    summary.ShouldNotBeNull("Projection did not complete within timeout");
    summary.Status.ShouldBe("Requested");
}
```

### Testing Multi-Tenancy

For tenanted projections, use `ForTenant()` when appending events and verify `TenantId` in assertions:

```csharp
[Fact]
public async Task Tenanted_projection_should_write_tenant_id()
{
    // Arrange
    var tenantId = "tenant-alpha";
    var returnId = Guid.NewGuid();

    // Act
    await using (var scope = _fixture.Host.Services.CreateAsyncScope())
    {
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        await using var session = store.LightweightSession(tenantId);

        session.Events.StartStream<Return>(
            returnId,
            new ReturnRequested(returnId, orderId, customerId, "Damaged", DateTimeOffset.UtcNow)
        );

        await session.SaveChangesAsync();
    }

    // Assert
    await using (var scope = _fixture.Host.Services.CreateAsyncScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ReturnProjectionDbContext>();

        var summary = await dbContext.TenantedReturnSummaries.FindAsync(returnId);

        summary.ShouldNotBeNull();
        summary.TenantId.ShouldBe(tenantId);
    }
}
```

---

## Common Pitfalls and Warnings

### 1. Forgetting to Register Entity Tables for Event Projections

**Symptom:** `InvalidOperationException` on startup: "No service for type 'DbContext' has been registered."

**Cause:** When using `EfCoreEventProjection<TDbContext>`, you must call `opts.AddEntityTablesFromDbContext<TDbContext>()` to register entity tables for Weasel migration.

**Fix:**

```csharp
// Wrong
opts.Projections.Add(new OrderDualStoreProjection(), ProjectionLifecycle.Inline);

// Correct
opts.Projections.Add(new OrderDualStoreProjection(), ProjectionLifecycle.Inline);
opts.AddEntityTablesFromDbContext<OrderProjectionDbContext>();
```

### 2. Using `FindAsync()` with Non-Unique IDs Across Tenants

**Symptom:** Data from one tenant overwrites another tenant's data in a multi-stream projection.

**Cause:** `DbContext.FindAsync(id)` looks up by primary key only. If two tenants have the same aggregate ID (e.g., customer name "Alice"), the projection sees them as the same entity.

**Fix:** Use composite primary keys that include `TenantId`:

```csharp
modelBuilder.Entity<CustomerOrderHistory>(entity =>
{
    entity.HasKey(e => new { e.TenantId, e.CustomerId });
});
```

### 3. Not Implementing `ITenanted` for Conjoined Tenancy

**Symptom:** `InvalidProjectionException` on startup: "Type 'MyEntity' does not implement ITenanted but event store uses conjoined tenancy."

**Cause:** Marten validates that entities in tenanted projections implement `ITenanted` to prevent `TenantId` being `NULL`.

**Fix:** Implement `ITenanted`:

```csharp
public sealed class MyEntity : ITenanted
{
    public Guid Id { get; set; }
    public string? TenantId { get; set; } // Required
}
```

### 4. Mutating Events in `ApplyEvent`

**Symptom:** Projection behaves correctly in tests but fails intermittently in production.

**Cause:** `ApplyEvent` receives the same event instance multiple times (when replaying streams). Mutating the event causes projections to see stale data.

**Fix:** Treat events as immutable. Read from them, but never modify them.

```csharp
// Wrong
public override ReturnSummary? ApplyEvent(...)
{
    switch (@event.Data)
    {
        case ReturnCompleted completed:
            snapshot.Status = completed.Status = "Completed"; // MUTATION
            return snapshot;
    }
}

// Correct
public override ReturnSummary? ApplyEvent(...)
{
    switch (@event.Data)
    {
        case ReturnCompleted completed:
            snapshot.Status = "Completed"; // Read-only
            return snapshot;
    }
}
```

### 5. Async Projections Not Running in Tests

**Symptom:** Async projection tests fail with `ShouldNotBeNull()` — entity is never projected.

**Cause:** The async daemon may not be started in the test host, or the test finishes before the projection completes.

**Fix:**
- Ensure `_host` is an `IAlbaHost` (which starts the async daemon)
- Use polling (see "Testing Async Projections" above)
- Or use Inline projections in tests for deterministic behavior

### 6. Missing `DbContext` Registration

**Symptom:** `InvalidOperationException` on startup: "No database provider has been configured for this DbContext."

**Cause:** The `DbContext` must be registered separately from the projection.

**Fix:**

```csharp
// Program.cs
builder.Services.AddDbContext<MyProjectionDbContext>(opts =>
{
    var connectionString = builder.Configuration.GetConnectionString("marten");
    opts.UseNpgsql(connectionString);
});
```

### 7. Projection Lifecycle Mismatch

**Symptom:** Projection doesn't update when events are appended.

**Cause:** Inline projections require synchronous session commit. Async projections require the async daemon to be running.

**Fix:** Verify projection lifecycle matches your deployment:
- **Inline:** Use when strong consistency is required (entity always up-to-date after `SaveChangesAsync()`)
- **Async:** Use for better write throughput (projection eventually consistent)
- **Live:** Requires async daemon with near-zero latency config

---

## Production Lessons Learned

### Lesson 1: Schema Migrations Are Transparent but Irreversible

**What we learned:** Weasel automatically migrates EF Core entity tables alongside Marten schema objects on startup. This is convenient but makes rollbacks harder.

**Recommendation:**
- Test schema migrations in staging before production deploys
- Use `opts.AutoCreateSchemaObjects = AutoCreate.None` in production, control migrations explicitly
- Version control your `DbContext` configuration (it's your schema definition)

### Lesson 2: Change Tracking Has Performance Overhead

**What we learned:** EF Core's change tracking (`DetectChanges()`) adds CPU overhead on every `SaveChangesAsync()`. For high-throughput projections, this can become a bottleneck.

**Optimization:**

```csharp
public override void ConfigureDbContext(
    DbContextOptionsBuilder<OrderProjectionDbContext> builder)
{
    // Disable change tracking for read-only queries
    builder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
}
```

For write operations, change tracking is necessary (it determines insert vs. update). But read-only queries (in handlers that query projections) don't need it.

### Lesson 3: Inline Projections Block Writes

**What we learned:** Inline projections run within the same transaction as event appends. If a projection takes 500ms to run (e.g., complex aggregation), every event append waits 500ms.

**Mitigation:**
- Use Async projections for complex aggregations (eventual consistency acceptable)
- Use Inline only for simple projections (< 50ms)
- Profile projection performance before choosing Inline

### Lesson 4: Async Projections Require Monitoring

**What we learned:** Async projections run in a background daemon. If the daemon crashes or falls behind, projections become stale — but writes succeed (events still append).

**Mitigation:**
- Monitor async daemon high-water mark (Marten exposes this via `IProjectionCoordinator`)
- Add health checks that fail if projections are > 10 seconds behind
- Log projection errors separately from application errors

### Lesson 5: Composite Projections Are Powerful but Complex

**What we learned:** Composite projections (stage 1 → stage 2) add dependencies that make debugging harder. If stage 1 fails, stage 2 silently doesn't run.

**Recommendation:**
- Use composite projections sparingly (only when one projection genuinely depends on another)
- Prefer independent projections when possible
- Add explicit logging to each stage's `ApplyEvent` method during development

---

## How It Works Under the Hood

Understanding the mechanics helps with troubleshooting.

### Per-Slice DbContext Instances

For each projection slice (batch of events processed together), Marten creates a new `DbContext` instance using the registered `DbContextOptions`. This means:

- Each projection batch is isolated (no shared state across batches)
- `DbContext` is short-lived (disposed after batch completes)
- Connection pooling (via Npgsql or SqlClient) handles connection reuse

### Transaction Coordination

Inline projections use Marten's session transaction:

1. Marten opens a PostgreSQL/SQL Server transaction
2. Events are appended to `mt_events`
3. For each projection:
   - A `DbContext` is created
   - The projection's `ApplyEvent` is called
   - EF Core tracks changes
   - `dbContext.SaveChangesAsync()` is called **within the same transaction**
4. Transaction commits atomically (both events and projected entities)

If any projection fails (exception thrown), the entire transaction rolls back — events are not appended, entities are not written.

### Change Tracking and Insert vs. Update

EF Core's change tracking determines whether an entity is new (insert) or existing (update):

- **First projection of a stream:** `ApplyEvent` returns a new entity → EF Core tracks it as `Added` → `INSERT`
- **Subsequent projections:** `ApplyEvent` receives the snapshot (existing entity) → modifies it → EF Core tracks it as `Modified` → `UPDATE`

Marten loads the snapshot from the EF Core table before calling `ApplyEvent`, so the entity is already attached to the `DbContext` with change tracking enabled.

### Weasel Schema Migration

On startup, Marten uses Weasel to:

1. Inspect the `DbContext`'s model (`OnModelCreating` configuration)
2. Generate `CREATE TABLE`, `CREATE INDEX`, and `ALTER TABLE` statements
3. Compare desired schema (from `DbContext`) to actual schema (database tables)
4. Execute migrations (if `AutoCreateSchemaObjects` is enabled)

This means:
- No `dotnet ef migrations add` needed
- No separate migration pipeline
- DbContext configuration **is** the schema definition

**Trade-off:** Less control over migration timing (runs on app startup). For production, consider setting `AutoCreateSchemaObjects = AutoCreate.None` and running migrations explicitly.

---

## Appendix

### Canonical Marten EF Core Projection Documentation

- [Marten EF Core Projections](https://martendb.io/events/projections/efcore.html) — Official documentation with API reference and examples

### Polecat Documentation

- [Polecat GitHub Repository](https://github.com/JasperFx/polecat) — SQL Server port of Marten
- [Polecat.EntityFrameworkCore NuGet](https://www.nuget.org/packages/Polecat.EntityFrameworkCore/) — EF Core integration package (v0.9.0)

### Related CritterSupply Documentation

- **[ADR 0026: Polecat SQL Server Migration](../decisions/0026-polecat-sql-server-migration.md)** — Plan for migrating 4 BCs to Polecat
- **[Polecat Migration Research](../research/polecat-migration-research.md)** — API compatibility analysis and infrastructure delta
- **[Polecat Candidate BCs](../planning/spikes/polecat-candidates.md)** — Evaluation of which BCs are best suited for Polecat
- **[CONTEXTS.md](../CONTEXTS.md)** — Bounded context specifications (includes Returns BC, Vendor Portal)
- **[marten-event-sourcing.md](./marten-event-sourcing.md)** — Event-sourced aggregate design patterns
- **[efcore-wolverine-integration.md](./efcore-wolverine-integration.md)** — Using EF Core as primary persistence (not as projection target)

### Existing EF Core Projection Implementations in CritterSupply

As of Cycle 28, CritterSupply **does not yet have production EF Core projections**. The following are planned:

- **Returns BC (Cycle 27+):** `ReturnSummary` projection (single-stream) — not yet implemented
- **Vendor Portal (Cycle 24+):** `ProductPerformanceSummary`, `InventorySnapshot` (multi-stream) — if Polecat migration proceeds per ADR 0026

This document was created ahead of implementation to guide future development.

### Example GitHub Repositories Using EF Core Projections

- [JasperFx/marten: samples/EFCoreProjections](https://github.com/JasperFx/marten/tree/master/src/samples/EFCoreProjections) — Official Marten sample showing all three projection types

### FAQ

**Q: Can I use EF Core projections with Wolverine handlers?**

A: Yes. Wolverine's `[ReadAggregate]` and `[WriteAggregate]` attributes work with Marten aggregates, and you can inject the projection `DbContext` separately to query projected entities:

```csharp
public static class GetReturnSummary
{
    public sealed record Query(Guid ReturnId);

    public static async Task<ReturnSummary?> Handle(
        Query query,
        ReturnProjectionDbContext dbContext,
        CancellationToken ct)
    {
        return await dbContext.ReturnSummaries.FindAsync(
            new object[] { query.ReturnId }, ct);
    }
}
```

**Q: Do EF Core projections support soft deletes?**

A: Not automatically. If you want soft deletes, model it as a status change event (e.g., `ReturnDeleted` sets `IsDeleted = true` instead of deleting the row).

**Q: Can I query projections from other BCs?**

A: No. EF Core projections are scoped to the BC that defines them. For cross-BC queries, use integration messages or a dedicated query API (BFF pattern).

**Q: What happens if I change `OnModelCreating` after projections have run?**

A: Weasel detects schema drift and migrates tables on next startup (if `AutoCreateSchemaObjects` is enabled). Existing data is preserved if the change is compatible (e.g., adding a nullable column). Breaking changes (e.g., dropping a required column) require manual migration.

**Q: How do I rebuild projections after fixing a bug?**

A: Truncate the projection tables and replay events:

```csharp
await dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE return_summaries");

var daemon = host.Services.GetRequiredService<IProjectionCoordinator>();
await daemon.RebuildAsync<ReturnSummaryProjection>(ct);
```

Marten's async daemon replays all events through the projection.

---

**Last Updated:** 2026-03-14

**Related Skills:**
- [Marten Event Sourcing](./marten-event-sourcing.md) — Event-sourced aggregate design
- [EF Core + Wolverine Integration](./efcore-wolverine-integration.md) — Using EF Core as primary persistence
- [Wolverine Message Handlers](./wolverine-message-handlers.md) — Command and query handlers
- [BFF Real-time Patterns](./bff-realtime-patterns.md) — Querying projections from BFFs
