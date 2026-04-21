# Marten Document Store

Practical patterns for using Marten as a document database (not event sourcing) in CritterSupply.

**Document store vs event store:** Marten supports both paradigms in the same database. This document covers document storage only — read models, projections, and reference data that doesn't benefit from an event history. For event-sourced aggregates, see `marten-event-sourcing.md`.

---

## Table of Contents

1. [When to Use the Document Store (vs. the Event Store)](#1-when-to-use-the-document-store-vs-the-event-store)
2. [Document Identity](#2-document-identity)
3. [Session Usage: IDocumentSession vs IQuerySession](#3-session-usage-idocumentsession-vs-iquerysession)
4. [Basic CRUD Patterns](#4-basic-crud-patterns)
5. [Querying with LINQ](#5-querying-with-linq)
   - [5.5 Streaming JSON Responses (Wolverine 5.32+)](#55-streaming-json-responses-wolverine-532)
6. [Compiled Queries](#6-compiled-queries)
7. [Batched Queries](#7-batched-queries)
8. [Projections as Documents](#8-projections-as-documents)
9. [Indexing](#9-indexing)
10. [Schema Management](#10-schema-management)
11. [Multi-Tenancy](#11-multi-tenancy)
12. [Anti-Patterns to Avoid](#12-anti-patterns-to-avoid)
13. [File Organization and Conventions](#13-file-organization-and-conventions)

---

## 1. When to Use the Document Store (vs. the Event Store)

Marten stores documents as JSONB in PostgreSQL. Use document storage when you need **current state only** and event history provides no value.

**Use Document Store for:**
- Master data with infrequent changes (Product Catalog)
- Read models and projections (built from events, but stored as documents)
- Reference data (categories, configuration, lookup tables)
- State machines where another BC owns the audit trail
- Read-heavy workloads (90%+ reads, few writes)

**Use Event Store for:**
- Transaction data with frequent state changes (Orders, Payments, Inventory)
- Historical changes are valuable (audit trail, temporal queries, "what happened when?")
- Complex business logic benefits from event sourcing patterns
- This BC is the authoritative owner of the lifecycle

**CritterSupply examples:**
- **Document Store:** Product Catalog BC (master product data), projection read models (OrderSummary, CartView)
- **Event Store:** Orders, Payments, Inventory, Fulfillment, Shopping, Returns

**Key insight:** In CritterSupply, event sourcing is the dominant pattern. Document storage is used for:
1. Product Catalog BC (the only pure document store BC)
2. **Projection read models** — stored as JSONB documents even when sourced from event-sourced BCs

Everything in this skill applies to both.

---

## 2. Document Identity

Marten requires documents to have an identity field named `Id` with one of these types:
- `Guid` (default, auto-assigned if not provided)
- `string` (application-assigned)
- `int` or `long` (database-assigned via sequence)

**CritterSupply convention:** Prefer `Guid` with application-assigned IDs to avoid database round trips.

**Example: Product Catalog uses string identity (SKU):**

```csharp
public sealed record Product
{
    // Marten identity field - must be named "Id"
    public string Id { get; init; } = null!;

    // Domain identity as value object - implicit string conversion
    public Sku Sku { get; init; } = null!;

    public string Name { get; init; } = null!;
    public string Category { get; init; } = null!;
    // ...
}

// Value object with implicit conversion to string
[JsonConverter(typeof(SkuJsonConverter))]
public sealed record Sku
{
    public string Value { get; init; } = null!;

    public static Sku From(string value)
    {
        // Validation logic
        return new Sku { Value = value };
    }

    public static implicit operator string(Sku sku) => sku.Value;
}

// Factory method assigns Id from SKU
public static Product Create(string sku, string name, string category)
{
    return new Product
    {
        Id = sku,                    // String identity for Marten
        Sku = Sku.From(sku),         // Value object for domain
        Name = name,
        Category = category,
        AddedAt = DateTimeOffset.UtcNow
    };
}
```

**Why this pattern works:**
- `Id = sku` allows direct document lookup: `session.LoadAsync<Product>("DOG-BOWL-001")`
- `Sku` value object provides domain validation and type safety
- Implicit conversion means `Id = sku` just works without ceremony

**Alternative: Guid identity with value object**

```csharp
public sealed record Order
{
    public Guid Id { get; init; }          // Marten identity
    public OrderId OrderId { get; init; }   // Domain value object

    public static Order Create()
    {
        var id = Guid.NewGuid();
        return new Order
        {
            Id = id,
            OrderId = OrderId.From(id)
        };
    }
}
```

**Configuration:**

```csharp
builder.Services.AddMarten(opts =>
{
    // Guid identity (default - no configuration needed)
    opts.Schema.For<Order>();

    // String identity (explicit)
    opts.Schema.For<Product>()
        .Identity(x => x.Id);  // Not strictly needed if property is named "Id"
});
```

**Key takeaway:** Application-assigned IDs (Guid or string) avoid extra database round trips. CritterSupply uses Guid for most BCs, string for Product Catalog (natural key).

### Natural Key as Document Identity ⭐ *M36.1 Addition*

Configuration entities with a natural, stable business identifier can use that identifier directly as the Marten document `Id`. This eliminates lookup-by-property queries.

```csharp
// Marketplace document uses ChannelCode as the Id
public sealed class Marketplace
{
    public string Id { get; init; } = null!; // = ChannelCode (e.g., "AMAZON_US")
    public string DisplayName { get; set; } = null!;
    public bool IsActive { get; set; }
}

// Registration:
opts.Schema.For<Marketplace>().Identity(x => x.Id);

// Lookup is direct:
var marketplace = await session.LoadAsync<Marketplace>("AMAZON_US", ct);
```

**When to use natural keys:** Configuration entities, reference data, or entities where the business identifier is immutable and unique (e.g., channel codes, ISO country codes, tenant slugs).

### Composite Key Identity ⭐ *M36.1 Addition*

Documents keyed by two or more fields can use a composite string key. This enables single `LoadAsync` calls without multi-field queries.

```csharp
public sealed class CategoryMapping
{
    public string Id { get; init; } = null!; // "{ChannelCode}:{InternalCategory}"
    public string ChannelCode { get; init; } = null!;
    public string InternalCategory { get; init; } = null!;
    public string ExternalCategory { get; set; } = null!;

    public static CategoryMapping Create(string channelCode, string internalCategory, string externalCategory)
        => new()
        {
            Id = $"{channelCode}:{internalCategory}",
            ChannelCode = channelCode,
            InternalCategory = internalCategory,
            ExternalCategory = externalCategory
        };
}

// Registration:
opts.Schema.For<CategoryMapping>().Identity(x => x.Id);

// Direct lookup by composite key:
var mapping = await session.LoadAsync<CategoryMapping>("AMAZON_US:Dogs", ct);
```

**Why:** Avoids `session.Query<CategoryMapping>().Where(x => x.ChannelCode == code && x.InternalCategory == cat)` — a single `LoadAsync` is faster and simpler. See ADR 0049 for the design rationale.

> **Reference:** [Marten Document Identity](https://martendb.io/documents/identity.html)

---

## 3. Session Usage: IDocumentSession vs IQuerySession

Marten provides two session types:

**IQuerySession** — Read-only queries
```csharp
public static async Task<Product?> Handle(
    string sku,
    IQuerySession session,  // Read-only
    CancellationToken ct)
{
    return await session.LoadAsync<Product>(sku, ct);
}
```

**IDocumentSession** — Reads + writes
```csharp
public static async Task Handle(
    AddProduct command,
    IDocumentSession session,  // Read + write
    CancellationToken ct)
{
    var product = Product.Create(command.Sku, command.Name, command.Category);
    session.Store(product);
    await session.SaveChangesAsync(ct);
}
```

**When to use which:**
- Use `IQuerySession` for read-only endpoints (GET requests) — lighter weight, no transaction overhead
- Use `IDocumentSession` for mutations (POST/PUT/PATCH/DELETE)

**Wolverine integration:** When you inject `IDocumentSession` into a Wolverine handler, Wolverine's transactional middleware manages the session lifecycle:
- Opens the session at handler start
- Calls `SaveChangesAsync()` after handler completes successfully
- Rolls back on exceptions

**⚠️ CRITICAL (M36.0): `AutoApplyTransactions()` is mandatory.** This middleware only activates when `opts.Policies.AutoApplyTransactions()` is present in the Wolverine configuration. Without it, `IDocumentSession` changes (including `session.Events.Append()`) are silently discarded — handlers return HTTP 200 but no data is persisted.

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.Policies.AutoApplyTransactions(); // ⭐ M36.0 Addition: MANDATORY — do not omit
    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
});
```

This was the most impactful correctness fix in M36.0. Product Catalog had 5 integration test failures misclassified as async projection timing issues — the actual root cause was a missing `AutoApplyTransactions()` in `Program.cs`. Every other BC (13 total) already had this policy; Product Catalog was the sole outlier after its M35.0 ES migration.

**❌ DO NOT call `SaveChangesAsync()` manually inside Wolverine handlers** — the middleware handles it:

```csharp
// ❌ WRONG - SaveChangesAsync called manually
public static async Task Handle(AddProduct cmd, IDocumentSession session)
{
    var product = Product.Create(cmd.Sku, cmd.Name, cmd.Category);
    session.Store(product);
    await session.SaveChangesAsync();  // ❌ Wolverine will call this again
}

// ✅ CORRECT - Let Wolverine call SaveChangesAsync
public static async Task Handle(AddProduct cmd, IDocumentSession session)
{
    var product = Product.Create(cmd.Sku, cmd.Name, cmd.Category);
    session.Store(product);
    // Wolverine calls SaveChangesAsync after handler returns
}
```

**Exception:** Call `SaveChangesAsync()` manually only when:
1. Not using Wolverine (raw Marten usage)
2. Explicitly testing with `GetDocumentSession()` in test fixtures
3. Seed data initialization (outside handler context)

> **Reference:** [Marten Sessions](https://martendb.io/documents/sessions.html)

---

## 4. Basic CRUD Patterns

Marten's document session provides a unit-of-work pattern: batch writes and commit with `SaveChangesAsync()`.

### Store (Upsert)

`session.Store()` performs insert-or-update:

```csharp
public static async Task Handle(
    AddProduct command,
    IDocumentSession session,
    CancellationToken ct)
{
    // Check for duplicate (prevents overwrite)
    var existing = await session.LoadAsync<Product>(command.Sku, ct);
    if (existing is not null)
        return Results.Conflict(new { Message = "Product already exists" });

    var product = Product.Create(
        command.Sku,
        command.Name,
        command.Description,
        command.Category);

    session.Store(product);  // Batches write operation
    // Wolverine calls SaveChangesAsync() after handler completes

    return Results.Created($"/api/products/{command.Sku}", new { Sku = command.Sku });
}
```

### Load (Read)

`session.LoadAsync<T>(id)` loads a single document by ID:

```csharp
public static Task<Product?> Load(
    string sku,
    IDocumentSession session,
    CancellationToken ct)
{
    return session.LoadAsync<Product>(sku, ct);
}
```

Returns `null` if document doesn't exist.

### Update (Immutable Pattern)

Use `with` expressions for immutable updates:

```csharp
public static async Task Handle(
    UpdateProduct command,
    Product product,  // Loaded via Wolverine compound handler
    IDocumentSession session)
{
    var updated = product.Update(
        name: command.Name,
        description: command.Description,
        category: command.Category);

    session.Store(updated);  // Store updated document
    // Wolverine calls SaveChangesAsync()
}

// In Product.cs
public Product Update(string? name = null, string? description = null, string? category = null)
{
    return this with
    {
        Name = name ?? Name,
        Description = description ?? Description,
        Category = category ?? Category,
        UpdatedAt = DateTimeOffset.UtcNow
    };
}
```

### Delete vs HardDelete

**Soft delete** (recommended):

```csharp
// Configuration
opts.Schema.For<Product>().SoftDeleted();

// Handler
public static async Task Handle(
    DeleteProduct command,
    Product product,
    IDocumentSession session)
{
    session.Delete(product);  // Marks IsDeleted = true
    // Queries automatically filter out soft-deleted documents
}
```

**Hard delete** (permanent removal):

```csharp
public static async Task Handle(
    HardDeleteProduct command,
    Product product,
    IDocumentSession session)
{
    session.HardDelete(product);  // Removes from database
}
```

CritterSupply convention: Use soft delete for all business data. Hard delete only for test cleanup.

### Batch Operations

Multiple stores in single transaction:

```csharp
public static async Task Handle(
    BulkAddProducts command,
    IDocumentSession session)
{
    foreach (var item in command.Products)
    {
        var product = Product.Create(item.Sku, item.Name, item.Category);
        session.Store(product);  // Batches all writes
    }

    // Single SaveChangesAsync commits all products atomically
}
```

**Key insight:** Marten batches all `Store()` calls within a session. One `SaveChangesAsync()` commits everything in a single database transaction.

> **Reference:** [Marten Document Operations](https://martendb.io/documents/)

---

## 5. Querying with LINQ

Marten translates LINQ expressions to PostgreSQL JSONB queries.

### Basic Query

```csharp
public static async Task<IReadOnlyList<Product>> Handle(
    IDocumentSession session,
    CancellationToken ct)
{
    return await session.Query<Product>()
        .Where(p => !p.IsDeleted)  // Automatic with .SoftDeleted() config
        .OrderBy(p => p.AddedAt)
        .ToListAsync(ct);
}
```

### Filtering

```csharp
public static async Task<IReadOnlyList<Product>> Handle(
    string category,
    IDocumentSession session,
    CancellationToken ct)
{
    return await session.Query<Product>()
        .Where(p => p.Category == category)
        .Where(p => p.Status == ProductStatus.Active)
        .ToListAsync(ct);
}
```

Marten generates: `WHERE data->>'Category' = 'Dogs' AND data->>'Status' = 'Active'`

### Pagination

```csharp
public static async Task<ProductListResult> Handle(
    int page,
    int pageSize,
    IDocumentSession session,
    CancellationToken ct)
{
    var query = session.Query<Product>()
        .Where(p => !p.IsDeleted);

    // Get total count (for pagination metadata)
    var totalCount = await query.CountAsync(ct);

    // Get page of results
    var products = await query
        .OrderBy(p => p.Name)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync(ct);

    return new ProductListResult(products, page, pageSize, totalCount);
}
```

### Async Enumerable (Streaming Large Datasets)

For large result sets, `ToAsyncEnumerable()` streams results without loading the entire dataset into memory:

```csharp
public static async Task ProcessAllProducts(
    IDocumentSession session,
    CancellationToken ct)
{
    var products = session.Query<Product>()
        .Where(p => !p.IsDeleted)
        .ToAsyncEnumerable();  // Streams results

    await foreach (var product in products.WithCancellation(ct))
    {
        // Process one product at a time
        await DoSomethingWith(product);
    }
}
```

**When to use:** Bulk operations, exports, reports where result set could be thousands of documents. Avoids loading everything into memory at once.

**Warning:** Keep the session alive while enumerating. Don't dispose the session before iteration completes.

### LINQ Limitations

**❌ Enum arrays cannot be parameterized:**

```csharp
// ❌ FAILS - Npgsql cannot serialize enum arrays
var activeStatuses = new[] { ProductStatus.Active, ProductStatus.ComingSoon };
query.Where(p => activeStatuses.Contains(p.Status));
// InvalidCastException: Writing values of 'ProductStatus[]' is not supported

// ✅ Use explicit OR conditions
query.Where(p => p.Status == ProductStatus.Active ||
                 p.Status == ProductStatus.ComingSoon);
```

**❌ Nullable `.Value` cannot be evaluated in LINQ:**

```csharp
// ❌ Throws if 'since' is null
query.Where(p => p.AddedAt > since.Value);

// ✅ Build query conditionally
var baseQuery = session.Query<Product>();
var filtered = since.HasValue
    ? baseQuery.Where(p => p.AddedAt > since.Value)
    : baseQuery;
```

**❌ Value objects on queryable fields:**

```csharp
// ❌ Marten cannot translate nested property access
query.Where(p => p.Category.Value == "Dogs");

// ✅ Use primitives for queryable fields (see ADR 0003)
query.Where(p => p.Category == "Dogs");
```

**Lesson from Cycle 22 (ChangeRequest queries):** Static enum arrays are fine for documentation and in-memory checks. Use explicit OR conditions in LINQ queries.

> **Reference:** [Marten LINQ Querying](https://martendb.io/documents/querying/linq/)

---

## 5.5 Streaming JSON Responses (Wolverine 5.32+)

`Marten.AspNetCore` ships three `IResult` implementations that stream raw Marten JSON directly from Postgres to the HTTP response body, bypassing the .NET deserialize/serialize round-trip. They implement `IEndpointMetadataProvider` so OpenAPI metadata is generated correctly. The only required addition is `using Marten.AspNetCore;` on the endpoint file — no Wolverine configuration changes needed.

### Type Overview

| Type | Query Source | 200 Response | Not Found | Empty Collection |
|------|-------------|-------------|-----------|-----------------|
| `StreamOne<T>` | `IQueryable<T>` (single doc) | Document JSON | 404 | 404 |
| `StreamMany<T>` | `IQueryable<T>` (multiple docs) | JSON array | — | 200 with `[]` |
| `StreamAggregate<T>` | Event stream via `FetchLatest` | Aggregate JSON | 404 | 404 |

`StreamOne<T>` and `StreamMany<T>` are for document store queries — the patterns in this skill. `StreamAggregate<T>` is for event-sourced aggregates; see `marten-event-sourcing.md` §8 for the full treatment.

**Key behavioral difference:** `StreamMany<T>` returns `[]` (HTTP 200) for an empty result set — it never returns 404. `StreamOne<T>` returns 404 when no document matches.

### `StreamOne<T>` — Single Document

```csharp
using Marten.AspNetCore;

[WolverineGet("/api/products/{sku}")]
public static StreamOne<Product> Handle(string sku, IQuerySession session)
    => new(session.Query<Product>().Where(p => p.Id == sku));
```

Returns 200 + JSON on hit, 404 on miss. Marten pipes the raw JSONB bytes from Postgres directly to the HTTP response stream — no C# object allocation for the document.

### `StreamMany<T>` — Document List

```csharp
[WolverineGet("/api/products")]
public static StreamMany<Product> Handle(string category, IQuerySession session)
    => new(session.Query<Product>()
               .Where(p => p.Category == category && p.Status == ProductStatus.Active));
```

Always returns HTTP 200. An empty result produces `[]`, not a 404.

### Overriding Status Code and Content Type

Both types expose init-only `OnFoundStatus` and `ContentType` properties:

```csharp
// Non-standard 2xx or explicit content-type negotiation
new StreamOne<CurrentPriceView>(query) { OnFoundStatus = 200, ContentType = "application/json" }
```

### When to Prefer Streaming over Returning `T`

- Response documents are large (projection read models, full catalog pages)
- List endpoints returning `IReadOnlyList<T>` with many items — eliminates the `.ToListAsync()` allocation and the `System.Text.Json` serialize pass
- Tight latency budgets where the deserialize-then-reserialize hop is measurable
- Fine-grained control over status code or Content-Type is required

### When NOT to Use Streaming

- The endpoint transforms or enriches the document before returning it (you need the C# object)
- The endpoint composes data from multiple sources
- Alba integration tests using `ReadAsJson<T>()` — streamed responses require different assertion patterns

### Contrast with Compiled Queries (§6)

These two optimizations are orthogonal. Compiled queries (§6) cache LINQ-to-SQL translation, removing parse overhead on hot paths. Streaming removes the deserialize/serialize allocation for the HTTP response body. You can combine them: pass the `IQueryable<T>` result of a compiled query directly into `StreamMany<T>`. Streaming alone does not eliminate LINQ translation overhead — for truly hot-path list endpoints, use both.

**Natural fits in CritterSupply:** Pricing (`CurrentPriceView` by SKU), Orders (`OrderSummary` customer list), Product Catalog (`Product` detail and list endpoints).

---

## 6. Compiled Queries

**Why compiled queries matter:** Marten's LINQ provider translates C# expression trees to SQL on every query execution. This is computationally expensive. Compiled queries cache the SQL translation, making repeated query execution 5-10x cheaper.

**When to use:**
- Any query executed frequently on a hot path
- Queries with parameters that change but structure stays the same
- APIs with high read traffic (GET endpoints, dashboards, reports)

**Pattern:**

```csharp
public sealed class FindProductsByCategoryQuery : ICompiledQuery<Product, IReadOnlyList<Product>>
{
    public string Category { get; init; } = null!;

    public Expression<Func<IMartenQueryable<Product>, IReadOnlyList<Product>>> QueryIs()
    {
        return query => query
            .Where(p => p.Category == Category)
            .Where(p => p.Status == ProductStatus.Active)
            .OrderBy(p => p.Name)
            .ToList();
    }
}

// Usage in handler
public static async Task<IReadOnlyList<Product>> Handle(
    string category,
    IDocumentSession session,
    CancellationToken ct)
{
    return await session.QueryAsync(
        new FindProductsByCategoryQuery { Category = category },
        ct);
}
```

**Key points:**
- Implement `ICompiledQuery<TDoc, TResult>`
- `TDoc` = document type (Product)
- `TResult` = query return type (IReadOnlyList<Product>, single Product, int count, etc.)
- Properties on the query class become SQL parameters
- Marten caches the SQL translation per compiled query type

**Example: Single document query**

```csharp
public sealed class FindProductBySkuQuery : ICompiledQuery<Product, Product?>
{
    public string Sku { get; init; } = null!;

    public Expression<Func<IMartenQueryable<Product>, Product?>> QueryIs()
    {
        return query => query.FirstOrDefault(p => p.Id == Sku);
    }
}

// Usage
var product = await session.QueryAsync(
    new FindProductBySkuQuery { Sku = "DOG-BOWL-001" });
```

**Example: Count query**

```csharp
public sealed class CountProductsInCategoryQuery : ICompiledQuery<Product, int>
{
    public string Category { get; init; } = null!;

    public Expression<Func<IMartenQueryable<Product>, int>> QueryIs()
    {
        return query => query.Count(p => p.Category == Category);
    }
}
```

**When NOT to use:**
- One-off queries (overhead of creating query class not worth it)
- Queries with highly dynamic structure (compiled queries require fixed structure)
- Test-only queries

**CritterSupply status:** Product Catalog doesn't currently use compiled queries (read traffic not high enough yet). Pattern established here for future use in high-traffic BFF queries (Storefront, Vendor Portal dashboards).

> **Reference:** [Marten Compiled Queries](https://martendb.io/documents/querying/compiled-queries.html)

---

## 7. Batched Queries

**Problem:** N+1 query problem — multiple round trips to database kills performance.

**Solution:** `IBatchedQuery` combines multiple queries into a single database call.

**Pattern:**

```csharp
public static async Task<CartView> Handle(
    Guid cartId,
    IDocumentSession session,
    CancellationToken ct)
{
    var batch = session.CreateBatchQuery();

    // Queue up multiple queries
    var cartTask = batch.LoadAsync<Cart>(cartId, ct);
    var productsTask = batch.Query<Product>()
        .Where(p => p.Category == "Dogs")
        .Take(10)
        .ToList();
    var countTask = batch.Query<Product>().Count();

    // Execute all queries in single database call
    await batch.Execute(ct);

    // Results available from Tasks
    var cart = await cartTask;
    var products = await productsTask;
    var totalProducts = await countTask;

    return new CartView(cart, products, totalProducts);
}
```

**Example: BFF composition query**

```csharp
public static async Task<CheckoutView> Handle(
    Guid customerId,
    IDocumentSession session,
    CancellationToken ct)
{
    var batch = session.CreateBatchQuery();

    // Fetch customer, cart, and order history in one database call
    var customerTask = batch.LoadAsync<Customer>(customerId, ct);
    var cartTask = batch.Query<Cart>()
        .FirstOrDefault(c => c.CustomerId == customerId);
    var ordersTask = batch.Query<Order>()
        .Where(o => o.CustomerId == customerId)
        .OrderByDescending(o => o.PlacedAt)
        .Take(5)
        .ToList();

    await batch.Execute(ct);

    var customer = await customerTask;
    var cart = await cartTask;
    var recentOrders = await ordersTask;

    return new CheckoutView(customer, cart, recentOrders);
}
```

**Combining with compiled queries:**

```csharp
public static async Task<DashboardView> Handle(
    Guid vendorId,
    IDocumentSession session,
    CancellationToken ct)
{
    var batch = session.CreateBatchQuery();

    // Mix compiled queries and regular queries
    var productsTask = batch.Query(new FindProductsByVendorQuery { VendorId = vendorId });
    var ordersTask = batch.Query(new FindRecentOrdersQuery { VendorId = vendorId });
    var statsTask = batch.Query<Product>()
        .Where(p => p.VendorTenantId == vendorId)
        .Count();

    await batch.Execute(ct);

    return new DashboardView(
        await productsTask,
        await ordersTask,
        await statsTask);
}
```

**When to use:**
- BFF composition queries (fetching related data from multiple BCs)
- Dashboard queries (aggregating multiple metrics)
- Any time you need data from multiple document types or multiple filters
- API endpoints that would otherwise make 3+ separate queries

**Performance impact:** Reduces 5 database round trips to 1. On production systems, this can cut API response time from 200ms to 40ms.

**CritterSupply status:** Not yet used, but critical pattern for BFF layers (Storefront, Vendor Portal) when we optimize read performance.

> **Reference:** [Marten Batched Queries](https://martendb.io/documents/querying/batched-queries.html)

---

## 8. Projections as Documents

**Critical insight:** Read models generated from event projections are stored as JSONB documents in PostgreSQL. Every querying, indexing, and session pattern in this document applies to projection read models.

**Example: OrderSummary projection**

```csharp
// Event-sourced Order aggregate emits events
public sealed record OrderPlaced(Guid OrderId, Guid CustomerId, DateTimeOffset PlacedAt);
public sealed record OrderConfirmed(Guid OrderId, DateTimeOffset ConfirmedAt);

// Projection builds read model document
public sealed class OrderSummaryProjection : SingleStreamProjection<OrderSummary>
{
    public OrderSummary Create(OrderPlaced evt)
    {
        return new OrderSummary
        {
            Id = evt.OrderId,
            CustomerId = evt.CustomerId,
            PlacedAt = evt.PlacedAt,
            Status = "Placed"
        };
    }

    public OrderSummary Apply(OrderConfirmed evt, OrderSummary current)
    {
        return current with
        {
            Status = "Confirmed",
            ConfirmedAt = evt.ConfirmedAt
        };
    }
}

// Stored as JSONB document - same as Product Catalog documents
public sealed record OrderSummary
{
    public Guid Id { get; init; }
    public Guid CustomerId { get; init; }
    public string Status { get; init; } = null!;
    public DateTimeOffset PlacedAt { get; init; }
    public DateTimeOffset? ConfirmedAt { get; init; }
}

// Query projection documents with LINQ (same as Product)
public static async Task<IReadOnlyList<OrderSummary>> Handle(
    Guid customerId,
    IQuerySession session,
    CancellationToken ct)
{
    return await session.Query<OrderSummary>()
        .Where(o => o.CustomerId == customerId)
        .OrderByDescending(o => o.PlacedAt)
        .ToListAsync(ct);
}
```

**Key points:**
- Projections update documents via event handlers, not direct mutations
- Projection documents are queryable with same LINQ patterns as Product Catalog
- Indexing, compiled queries, batched queries — all work the same
- Soft delete configuration works the same

**BFF pattern: Projections + HTTP clients**

BFFs use projections for data owned by event-sourced BCs:

```csharp
// Storefront queries OrderSummary projection (document) from Orders BC
public static async Task<IReadOnlyList<OrderSummary>> Handle(
    Guid customerId,
    IOrdersClient client,  // HTTP client to Orders BC
    CancellationToken ct)
{
    return await client.GetCustomerOrders(customerId, ct);
}

// Orders BC exposes projection via HTTP endpoint (traditional pattern)
[WolverineGet("/api/orders/customer/{customerId}")]
public static async Task<IReadOnlyList<OrderSummary>> Handle(
    Guid customerId,
    IQuerySession session,
    CancellationToken ct)
{
    return await session.Query<OrderSummary>()
        .Where(o => o.CustomerId == customerId)
        .ToListAsync(ct);
}
```

When the endpoint does nothing but return the projection documents, the streaming pattern eliminates the `.ToListAsync()` allocation and the `System.Text.Json` serialize pass:

```csharp
using Marten.AspNetCore;

// Orders BC — streaming pattern (no deserialization, no re-serialization)
[WolverineGet("/api/orders/customer/{customerId}")]
public static StreamMany<OrderSummary> Handle(Guid customerId, IQuerySession session)
    => new(session.Query<OrderSummary>().Where(o => o.CustomerId == customerId));
```

**When to use each:** Use the streaming pattern when the endpoint is a direct read — no transformation, enrichment, or composition. Use the traditional `Task<IReadOnlyList<T>>` pattern when the handler needs to shape, filter, or merge the data before returning. See `marten-document-store.md §5.5` for the full `StreamMany<T>` / `StreamOne<T>` reference.

**This is the bridge:** Product Catalog is being migrated to event sourcing, but the patterns here — querying documents, indexing, compiled queries — outlive that migration because projections are documents.

> **Reference:** [Marten Projections](https://martendb.io/events/projections/)

---

## 9. Indexing

JSONB queries are slower than indexed column queries. Marten provides indexing strategies to improve performance.

### GIN Index (Default)

Marten automatically creates a GIN index on the entire JSONB column:

```csharp
opts.Schema.For<Product>()
    .GinIndexJsonData();  // Default - indexes entire JSONB document
```

**When to use:** General-purpose. Good for queries on any field, but not as fast as duplicated field indexes.

### Duplicated Field Index

For frequently queried fields, duplicate them as database columns:

```csharp
opts.Schema.For<Product>()
    .Index(x => x.Category)   // Creates separate 'category' column
    .Index(x => x.Status)     // Creates separate 'status' column
    .Index(x => x.Sku);       // Creates separate 'sku' column
```

**Generated SQL:**
```sql
CREATE INDEX idx_product_category ON productcatalog.mt_doc_product (category);
CREATE INDEX idx_product_status ON productcatalog.mt_doc_product (status);
```

Marten keeps the duplicated column in sync automatically.

**Performance difference:**
- JSONB path query: `WHERE data->>'Category' = 'Dogs'` (slower)
- Duplicated field query: `WHERE category = 'Dogs'` (5-10x faster)

**When to use:** Any field used in `WHERE`, `ORDER BY`, or `GROUP BY` clauses on hot paths.

### Unique Index

Enforce uniqueness at database level:

```csharp
opts.Schema.For<Product>()
    .UniqueIndex(UniqueIndexType.DuplicatedField, x => x.Sku);
```

**Result:** Database constraint prevents duplicate SKUs. `Store()` throws on violation.

**Alternative:** Check uniqueness in handler (CritterSupply pattern):

```csharp
public static async Task Handle(AddProduct cmd, IDocumentSession session)
{
    var existing = await session.LoadAsync<Product>(cmd.Sku);
    if (existing is not null)
        return Results.Conflict(new { Message = "Product already exists" });

    var product = Product.Create(cmd.Sku, cmd.Name, cmd.Category);
    session.Store(product);
}
```

**Trade-off:** Unique index is faster (database-level check), but throws exception. Handler check returns friendly HTTP 409.

### Soft Delete Index

When using soft deletes, index the `mt_deleted` column:

```csharp
opts.Schema.For<Product>()
    .SoftDeleted()           // Adds mt_deleted column
    .SoftDeletedWithIndex(); // Indexes mt_deleted for faster queries
```

Queries with `.Where(p => !p.IsDeleted)` become much faster.

### Composite Index

Index multiple fields together:

```csharp
opts.Schema.For<Product>()
    .Index(x => x.Category, x => x.Status);  // Composite index
```

**When to use:** Queries that filter on both fields simultaneously:

```csharp
query.Where(p => p.Category == "Dogs" && p.Status == ProductStatus.Active);
```

**CritterSupply Product Catalog indexes:**

```csharp
opts.Schema.For<Product>()
    .Index(x => x.Sku)      // Fast SKU lookups
    .Index(x => x.Category) // Category filtering
    .Index(x => x.Status)   // Status filtering
    .SoftDeleted();         // Soft delete support
```

**Configuration location:** All indexing configuration lives in `Program.cs` Marten setup, not scattered in handlers.

> **Reference:** [Marten Indexing](https://martendb.io/documents/indexing/)

---

## 10. Schema Management

### Lightweight Sessions

CritterSupply uses lightweight sessions for document store BCs:

```csharp
builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = "productcatalog";

    // Document configuration
    opts.Schema.For<Product>()
        .Index(x => x.Category)
        .SoftDeleted();
})
.UseLightweightSessions()  // Stateless, minimal overhead
.IntegrateWithWolverine();
```

**Lightweight vs. Default Sessions:**
- **Lightweight:** Stateless, no identity map, faster for read-heavy workloads
- **Default:** Tracks loaded documents, enforces single instance per ID, heavier

**CritterSupply convention:** Use lightweight sessions for document store BCs. Use default sessions for event-sourced BCs (identity map prevents duplicate aggregate instances).

### Schema Isolation

Each BC uses its own PostgreSQL schema within the shared `postgres` database:

```csharp
opts.DatabaseSchemaName = "productcatalog";  // Product Catalog BC
opts.DatabaseSchemaName = "orders";          // Orders BC
opts.DatabaseSchemaName = "shopping";        // Shopping BC
```

**Result:** Documents stored in separate schemas:
- `productcatalog.mt_doc_product`
- `orders.mt_doc_ordersummary`
- `shopping.mt_doc_cart`

**Why:** Logical isolation, easier backups, clearer ownership.

### Auto-Create Schema

**Development:**
```csharp
if (app.Environment.IsDevelopment())
{
    await app.Services.GetRequiredService<IDocumentStore>()
        .Storage.ApplyAllConfiguredChangesToDatabaseAsync();
}
```

**Production:** Never auto-create. Use Marten migrations or explicit schema scripts.

**CritterSupply convention:** Auto-create in development, explicit migrations in production (not yet implemented — future work).

### Schema Patching

Marten detects schema differences and generates patches:

```bash
# Generate schema patch SQL
dotnet run --project src/ProductCatalog.Api -- marten:patch schema-patch.sql

# Review and apply manually
psql -U postgres -d critter_supply -f schema-patch.sql
```

**Not yet used in CritterSupply** — schema changes are rare (only Product Catalog, which is stable).

> **Reference:** [Marten Schema Management](https://martendb.io/documents/storage.html)

---

## 11. Multi-Tenancy

Marten supports tenant-scoped sessions. When enabled, all queries automatically filter by tenant ID.

**Configuration:**

```csharp
opts.Schema.For<Product>()
    .MultiTenanted();  // Adds tenant_id column
```

**Usage:**

```csharp
public static async Task<IReadOnlyList<Product>> Handle(
    string category,
    IDocumentSession session,  // Wolverine injects tenant-scoped session
    CancellationToken ct)
{
    // Query automatically scoped to current tenant
    return await session.Query<Product>()
        .Where(p => p.Category == category)
        .ToListAsync(ct);
}
```

Wolverine extracts tenant ID from HTTP headers or JWT claims and injects a scoped session.

**CritterSupply usage:**
- **Product Catalog:** Not tenant-scoped (global product catalog)
- **Vendor Portal:** Tenant-scoped documents (ChangeRequest per vendor)
- **Returns BC:** Tenant-scoped documents (Return per vendor)

**Pattern from Vendor Portal ChangeRequest:**

```csharp
// Document with tenant ID
public sealed record ChangeRequest
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }  // Vendor tenant ID
    public ChangeRequestStatus Status { get; init; }
    // ...
}

// Marten configuration
opts.Schema.For<ChangeRequest>()
    .MultiTenanted()
    .Index(x => x.Status);

// Query (automatically scoped to tenant)
var requests = await session.Query<ChangeRequest>()
    .Where(r => r.Status == ChangeRequestStatus.Draft)
    .ToListAsync();
// SQL: WHERE tenant_id = 'current-tenant-id' AND data->>'Status' = 'Draft'
```

**Key insight:** Don't bypass tenancy on documents that belong to a tenant. Follow Wolverine's tenant ID conventions upstream.

> **Reference:** [Marten Multi-Tenancy](https://martendb.io/documents/multi-tenancy.html)

---

## 12. Anti-Patterns to Avoid

### ❌ Calling SaveChangesAsync Manually in Wolverine Handlers

**Wrong:**
```csharp
public static async Task Handle(AddProduct cmd, IDocumentSession session)
{
    var product = Product.Create(cmd.Sku, cmd.Name, cmd.Category);
    session.Store(product);
    await session.SaveChangesAsync();  // ❌ Wolverine will call this again
}
```

**Right:**
```csharp
public static async Task Handle(AddProduct cmd, IDocumentSession session)
{
    var product = Product.Create(cmd.Sku, cmd.Name, cmd.Category);
    session.Store(product);
    // Wolverine calls SaveChangesAsync after handler returns
}
```

**Why:** Wolverine's transactional middleware manages session lifecycle. Manual `SaveChangesAsync()` bypasses transaction management and can cause double-commits.

---

### ❌ Opening IDocumentSession Manually

**Wrong:**
```csharp
public static async Task Handle(AddProduct cmd, IDocumentStore store)
{
    using var session = store.LightweightSession();  // ❌ Manual session
    var product = Product.Create(cmd.Sku, cmd.Name, cmd.Category);
    session.Store(product);
    await session.SaveChangesAsync();
}
```

**Right:**
```csharp
public static async Task Handle(AddProduct cmd, IDocumentSession session)
{
    // Wolverine injects session
    var product = Product.Create(cmd.Sku, cmd.Name, cmd.Category);
    session.Store(product);
}
```

**Why:** Wolverine manages session scope, transaction boundaries, and disposal. Manual sessions bypass middleware.

---

### ❌ Using Document Storage for Data with Meaningful History

**Wrong:**
```csharp
// Order lifecycle stored as document (no event history)
public sealed record Order
{
    public Guid Id { get; init; }
    public OrderStatus Status { get; init; }  // Placed → Confirmed → Shipped
}

// Update replaces previous state (history lost)
public static async Task Handle(ConfirmOrder cmd, Order order, IDocumentSession session)
{
    var updated = order with { Status = OrderStatus.Confirmed };
    session.Store(updated);  // ❌ Previous status lost forever
}
```

**Right:** Use event sourcing for data with meaningful history:

```csharp
// Event-sourced Order aggregate
public sealed record OrderPlaced(Guid OrderId, DateTimeOffset PlacedAt);
public sealed record OrderConfirmed(Guid OrderId, DateTimeOffset ConfirmedAt);

public static (Order, OrderPlaced) Handle(PlaceOrder cmd)
{
    var order = Order.Create(cmd.CustomerId, cmd.Items);
    return (order, new OrderPlaced(order.Id, DateTimeOffset.UtcNow));
}
```

**Why:** Document storage is for current state only. If you need "what happened when?" use event sourcing.

---

### ❌ Querying JSONB Without Indexes on Hot Paths

**Wrong:**
```csharp
// High-traffic query with no index on Category
public static async Task<IReadOnlyList<Product>> Handle(
    string category,
    IDocumentSession session)
{
    return await session.Query<Product>()
        .Where(p => p.Category == category)  // Full JSONB scan
        .ToListAsync();
}
```

**Right:**
```csharp
// Add index in Program.cs
opts.Schema.For<Product>()
    .Index(x => x.Category);  // Duplicated field index

// Query runs 5-10x faster
```

**Why:** JSONB queries without indexes require full table scans. On high-traffic endpoints, this kills performance.

---

### ❌ Skipping Compiled Queries on Frequently Executed LINQ Queries

**Wrong:**
```csharp
// Dashboard endpoint called 1000x/day - LINQ translation overhead every time
public static async Task<IReadOnlyList<Product>> Handle(
    string category,
    IDocumentSession session)
{
    return await session.Query<Product>()
        .Where(p => p.Category == category)  // Translation overhead on every call
        .ToListAsync();
}
```

**Right:**
```csharp
// Compiled query - SQL cached after first execution
public sealed class FindProductsByCategoryQuery : ICompiledQuery<Product, IReadOnlyList<Product>>
{
    public string Category { get; init; } = null!;

    public Expression<Func<IMartenQueryable<Product>, IReadOnlyList<Product>>> QueryIs()
    {
        return q => q.Where(p => p.Category == Category).ToList();
    }
}

public static async Task<IReadOnlyList<Product>> Handle(
    string category,
    IDocumentSession session)
{
    return await session.QueryAsync(new FindProductsByCategoryQuery { Category = category });
}
```

**Why:** LINQ translation is expensive. On hot paths, compiled queries pay for themselves immediately.

---

### ❌ Treating Projection Documents Differently from Other Documents

**Wrong assumption:**
```csharp
// "Projection documents are special, I can't query them with LINQ"
```

**Right:**
```csharp
// Projection documents ARE documents - same LINQ queries work
public static async Task<IReadOnlyList<OrderSummary>> Handle(
    Guid customerId,
    IQuerySession session,
    CancellationToken ct)
{
    return await session.Query<OrderSummary>()
        .Where(o => o.CustomerId == customerId)
        .ToListAsync(ct);
}
```

**Why:** Projection documents are stored as JSONB, just like Product documents. All querying, indexing, and optimization patterns apply equally.

---

### ❌ Seed Data in Program.cs During Test Runs

**Wrong:**
```csharp
// Program.cs
if (app.Environment.IsDevelopment())
{
    await SeedData.SeedProductsAsync(store);  // ❌ Runs during test runs
}
```

**Why it breaks tests:**
- Alba creates test host in "Development" environment
- Seed data runs once when TestFixture initializes
- Tests call `CleanAllDocumentsAsync()` which deletes seed data
- Subsequent tests have inconsistent state

**Right:**
```csharp
// Skip seed data when running in test context
if (app.Environment.IsDevelopment() && !IsRunningInTests())
{
    await SeedData.SeedProductsAsync(store);
}

static bool IsRunningInTests()
{
    return AppDomain.CurrentDomain.GetAssemblies()
        .Any(a => a.FullName?.StartsWith("xunit") == true);
}
```

**Alternative:** Explicit seed endpoint (`POST /api/_seed`) only called during manual testing.

**Lesson from Cycle 19:** Seed data in `Program.cs` breaks test isolation. Tests must seed their own data.

---

## 13. File Organization and Conventions

### Document Type Location

**Pattern:** Document types live in the domain project, colocated with their feature:

```
src/Product Catalog/
├── ProductCatalog/                      # Domain project
│   └── Products/
│       ├── Product.cs                   # Document type
│       ├── Sku.cs                       # Value object (identity)
│       ├── ProductImage.cs              # Value object (nested)
│       ├── ProductDimensions.cs         # Value object (nested)
│       └── ProductStatus.cs             # Enum
└── ProductCatalog.Api/                  # API project
    ├── Program.cs                       # Marten configuration
    └── Products/
        ├── AddProduct.cs                # Command + handler
        ├── GetProduct.cs                # Query handler
        ├── UpdateProduct.cs             # Update handler
        └── ListProducts.cs              # List query handler
```

**Why:** Document types are domain concepts, not infrastructure. API project references domain project.

---

### Marten Configuration Location

**Pattern:** All Marten configuration in `Program.cs`, not scattered:

```csharp
// Program.cs
builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = "productcatalog";

    // All Product document configuration in one place
    opts.Schema.For<Product>()
        .Identity(x => x.Id)
        .Index(x => x.Sku)
        .Index(x => x.Category)
        .Index(x => x.Status)
        .SoftDeleted();
})
.UseLightweightSessions()
.IntegrateWithWolverine();
```

**Don't:** Scatter index configuration across multiple files or add indexes in migration scripts without updating `Program.cs`.

---

### Projection Document Naming

**Pattern:** Projection documents named `{Aggregate}Summary` or `{Aggregate}View`:

```csharp
// Projection document (read model)
public sealed record OrderSummary  // Not "Order" (that's the aggregate)
{
    public Guid Id { get; init; }
    public Guid CustomerId { get; init; }
    public string Status { get; init; } = null!;
}

// Projection class
public sealed class OrderSummaryProjection : SingleStreamProjection<OrderSummary>
{
    // ...
}
```

**Why:** Clear distinction between aggregate (Order) and read model (OrderSummary).

---

### Value Objects vs Primitives for Queryable Fields

**Rule (from ADR 0003):** Use primitives for any field queried in LINQ.

```csharp
// ✅ Queryable fields = primitives
public sealed record Product
{
    public string Id { get; init; }        // String identity (queryable)
    public string Category { get; init; }  // Queryable field
    public ProductStatus Status { get; init; }  // Enum (queryable)

    // ✅ Non-queryable fields = value objects
    public Sku Sku { get; init; }                        // Value object (identity, not queried)
    public IReadOnlyList<ProductImage> Images { get; init; }  // Nested objects (not queried)
    public ProductDimensions? Dimensions { get; init; }       // Nested object (not queried)
}
```

**Validation at boundary:**
```csharp
public class AddProductValidator : AbstractValidator<AddProduct>
{
    public AddProductValidator()
    {
        RuleFor(x => x.Category)
            .NotEmpty()
            .MaximumLength(50);
    }
}
```

---

### Test Data Setup

**Pattern:** Tests seed their own data via `InitializeAsync()`:

```csharp
[Collection(IntegrationTestCollection.Name)]
public sealed class ListProductsTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ListProductsTests(TestFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.CleanAllDocumentsAsync();

        // Seed test data
        var products = new[]
        {
            Product.Create("DOG-BOWL-001", "Dog Bowl", "Dogs"),
            Product.Create("CAT-TOY-001", "Cat Toy", "Cats")
        };

        using var session = _fixture.GetDocumentSession();
        foreach (var product in products)
            session.Store(product);
        await session.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CanListProducts() { /* ... */ }
}
```

**Why:** Test isolation. Each test controls its own data setup.

---

## Key Takeaways

1. **Document store is for current state** — Product Catalog (master data) and projections (read models)
2. **Event store is for history** — Orders, Payments, Inventory (transactional data)
3. **Projections are documents** — All patterns here apply to event-sourced projection read models
4. **Primitives for queryable fields** — Value objects break Marten LINQ translation (ADR 0003)
5. **Wolverine manages sessions** — Don't call `SaveChangesAsync()` manually in handlers
6. **Index hot paths** — Duplicated field indexes for frequently queried fields
7. **Compiled queries for repeated execution** — Cache SQL translation on high-traffic endpoints
8. **Batched queries reduce round trips** — BFF composition queries benefit most
9. **Seed data breaks tests** — Tests seed their own data; never seed in `Program.cs` during test runs
10. **Async enumerable for large datasets** — Stream results without loading everything into memory
11. **Stream large responses directly** — `StreamOne<T>` / `StreamMany<T>` from `Marten.AspNetCore` eliminate the deserialize/serialize round-trip for pass-through HTTP endpoints (Wolverine 5.32+)

---

**CritterSupply's document store journey:** Product Catalog is the reference implementation, but it's being migrated to event sourcing. The patterns here outlive that migration because **projection read models are documents** — queried, indexed, and optimized with the same tools.

This document is the bridge between pure document storage (Product Catalog today) and projection document usage (everywhere, forever).
