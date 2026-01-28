# Marten Document Store

Patterns for using Marten as a document database (not event sourcing) in CritterSupply.

## When to Use Document Store vs Event Sourcing

**Use Marten Document Store when:**
- Master data with infrequent changes (Product Catalog)
- Current state is all that matters (no need for historical replay)
- Read-heavy workload (90%+ reads, few writes)
- Document model fits naturally (flexible schema, nested objects)

**Use Marten Event Store when:**
- Transaction data with frequent state changes (Orders, Payments, Inventory)
- Historical changes are valuable (audit trail, temporal queries)
- Complex business logic benefits from event sourcing patterns

**CritterSupply examples:**
- **Document Store:** Product Catalog (products are master data)
- **Event Store:** Orders, Payments, Inventory, Fulfillment

## Document Model Design

Use immutable records with factory methods, same as event-sourced aggregates:

```csharp
public sealed record Product
{
    public Sku Sku { get; init; } = null!;           // Value object as ID
    public ProductName Name { get; init; } = null!;  // Value object
    public string Description { get; init; } = null!;
    public CategoryName Category { get; init; } = null!;
    public IReadOnlyList<ProductImage> Images { get; init; } = [];
    public ProductStatus Status { get; init; }
    public DateTimeOffset AddedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    private Product() { }  // Required by Marten

    public static Product Create(
        string sku,
        string name,
        string description,
        string category,
        IReadOnlyList<ProductImage> images)
    {
        return new Product
        {
            Sku = Sku.From(sku),
            Name = ProductName.From(name),
            Description = description,
            Category = CategoryName.From(category),
            Images = images,
            Status = ProductStatus.Active,
            AddedAt = DateTimeOffset.UtcNow
        };
    }

    public Product Update(string? name = null, string? description = null)
    {
        return this with
        {
            Name = name is not null ? ProductName.From(name) : Name,
            Description = description ?? Description,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public Product ChangeStatus(ProductStatus newStatus) =>
        this with { Status = newStatus, UpdatedAt = DateTimeOffset.UtcNow };
}
```

> **Reference:** [Marten Document Store](https://martendb.io/documents/)

## Marten Configuration

```csharp
builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);

    opts.Schema.For<Product>()
        .Identity(x => x.Sku)      // Use SKU as identifier (not Guid)
        .UniqueIndex(x => x.Sku)   // Enforce uniqueness
        .Index(x => x.Category)    // Index for queries
        .Index(x => x.Status)      // Index for filtering
        .SoftDeleted();            // Built-in soft delete
});
```

> **Reference:** [Marten Document Configuration](https://martendb.io/documents/configuration/)

## CRUD Handler Patterns

### Create

```csharp
public sealed record AddProduct(string Sku, string Name, string Description, string Category)
{
    public class AddProductValidator : AbstractValidator<AddProduct>
    {
        public AddProductValidator()
        {
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(24);
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        }
    }
}

public static class AddProductHandler
{
    [WolverinePost("/api/products")]
    public static async Task<CreationResponse> Handle(
        AddProduct command,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Check for duplicate
        var existing = await session.Query<Product>()
            .FirstOrDefaultAsync(p => p.Sku == Sku.From(command.Sku), ct);

        if (existing is not null)
            throw new InvalidOperationException($"Product {command.Sku} already exists");

        var product = Product.Create(
            command.Sku,
            command.Name,
            command.Description,
            command.Category,
            []);

        session.Store(product);  // Direct document insert
        await session.SaveChangesAsync(ct);

        return new CreationResponse($"/api/products/{command.Sku}");
    }
}
```

### Read

```csharp
public static class GetProductHandler
{
    [WolverineGet("/api/products/{sku}")]
    public static async Task<Product?> Handle(
        string sku,
        IDocumentSession session,
        CancellationToken ct)
    {
        return await session.LoadAsync<Product>(Sku.From(sku), ct);
    }
}
```

### Update

```csharp
public static class UpdateProductHandler
{
    [WolverinePut("/api/products/{sku}")]
    public static async Task Handle(
        string sku,
        UpdateProduct command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var product = await session.LoadAsync<Product>(Sku.From(sku), ct);

        if (product is null)
            return;  // 404

        var updated = product.Update(
            name: command.Name,
            description: command.Description);

        session.Store(updated);  // Marten detects changes
        await session.SaveChangesAsync(ct);
    }
}
```

### Query with Filtering

```csharp
public static class GetProductListingHandler
{
    [WolverineGet("/api/products")]
    public static async Task<PagedResult<ProductSummary>> Handle(
        CategoryName? category,
        ProductStatus? status,
        int pageNumber,
        int pageSize,
        IDocumentSession session,
        CancellationToken ct)
    {
        var query = session.Query<Product>()
            .Where(p => !p.IsDeleted);  // Soft delete filter

        if (category is not null)
            query = query.Where(p => p.Category == category);

        if (status.HasValue)
            query = query.Where(p => p.Status == status.Value);

        var total = await query.CountAsync(ct);

        var products = await query
            .OrderBy(p => p.Name)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductSummary(p.Sku, p.Name, p.Category, p.Status))
            .ToListAsync(ct);

        return new PagedResult<ProductSummary>(products, total, pageNumber, pageSize);
    }
}
```

> **Reference:** [Marten LINQ Queries](https://martendb.io/documents/querying/linq/)

## Key Differences: Document Store vs Event Sourcing

| Aspect | Document Store | Event Sourcing |
|--------|---------------|----------------|
| **Session Type** | `IDocumentSession` | `IDocumentSession` (same) |
| **Write Operation** | `session.Store(document)` | `session.Events.Append(id, event)` |
| **Read Operation** | `session.Query<T>()` or `LoadAsync<T>()` | `session.Events.AggregateStreamAsync<T>()` |
| **Identity** | Any type (string, Guid, int) | Stream ID (typically Guid) |
| **Persistence** | Document stored directly | Events stored, aggregate rebuilt |
| **Updates** | In-place document updates | Append new events |
| **History** | No historical changes | Full event history preserved |

## Soft Delete

Configure soft delete in Marten:

```csharp
opts.Schema.For<Product>().SoftDeleted();
```

Documents are marked as deleted but not removed:

```csharp
// Soft delete
session.Delete(product);

// Query automatically filters deleted documents
var active = await session.Query<Product>().ToListAsync();  // Excludes deleted

// Include deleted if needed
var all = await session.Query<Product>()
    .Where(x => x.MaybeDeleted())
    .ToListAsync();
```

> **Reference:** [Marten Soft Deletes](https://martendb.io/documents/deletes.html#soft-deletes)

## Integration Messages

Document store BCs still publish integration messages for cross-context communication:

```csharp
public static async Task<CreationResponse> Handle(
    AddProduct command,
    IDocumentSession session,
    IMessageBus bus,
    CancellationToken ct)
{
    var product = Product.Create(/* ... */);
    session.Store(product);
    await session.SaveChangesAsync(ct);

    // Publish integration message (not a domain event)
    await bus.PublishAsync(new ProductAdded(product.Sku, product.Name, product.Category));

    return new CreationResponse($"/api/products/{product.Sku}");
}
```

Note: These are **integration messages**, not domain events. They're not persisted in an event stream.
