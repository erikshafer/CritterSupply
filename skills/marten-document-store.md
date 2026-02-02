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

## Value Objects and Queryable Fields

**IMPORTANT:** When using Marten's document store with LINQ queries, value objects on queryable fields create friction.

### The Architecture Signal

Value objects + JSON serialization + Marten LINQ queries = translation issues.

**Marten cannot translate** expressions like:
- `p.Category.Value == "Dogs"` ❌
- `p.Category.ToString() == "Dogs"` ❌
- `(string)p.Category == "Dogs"` ❌

Even with custom JSON converters that serialize value objects as simple strings, Marten's LINQ-to-SQL translator doesn't understand how to access nested properties or call methods on custom types.

### When to Use Value Objects with Marten

**✅ Use Value Objects for:**
- **Complex nested objects** (Address, Dimensions, Money)
  ```csharp
  public IReadOnlyList<ProductImage> Images { get; init; }  // Not queried
  public ProductDimensions? Dimensions { get; init; }       // Not queried
  public ShippingAddress Address { get; init; }            // Not queried
  ```
- **Non-queryable fields** (descriptions, metadata, nested structures)
- **Strong domain concepts with behavior** (Money with currency conversion, DateRange with overlap logic)

**❌ Use Primitives for:**
- **Queryable filter fields** - Any field you'll filter on in LINQ
  ```csharp
  public string Category { get; init; }      // Queried: WHERE category = 'Dogs'
  public ProductStatus Status { get; init; } // Queried: WHERE status = 'Active'
  public string Brand { get; init; }         // Queried: WHERE brand = 'Acme'
  ```
- **Simple string wrappers with no behavior** - If it's just a string with validation, keep it a string
- **Sort/filter/group fields** - Marten needs direct primitive access

**🤔 Identity Fields (Special Case):**
- Can use value objects (like `Sku`) if you also provide a primitive `Id` property
  ```csharp
  public string Id { get; init; }      // For Marten's identity system
  public Sku Sku { get; init; }        // For domain logic
  ```

### Validation Strategy

Since queryable fields should be primitives, **validate at the boundary** with FluentValidation:

```csharp
public sealed record AddProduct(string Category, string Name, string Description);

public class AddProductValidator : AbstractValidator<AddProduct>
{
    public AddProductValidator()
    {
        RuleFor(x => x.Category)
            .NotEmpty()
            .MaximumLength(50)
            .WithMessage("Category is required and cannot exceed 50 characters");

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100)
            .Matches(@"^[A-Za-z0-9\s.,!&()\-]+$")
            .WithMessage("Name contains invalid characters");
    }
}
```

This gives you:
- ✅ Type safety at the domain boundary
- ✅ 400 errors for bad input (not 500s)
- ✅ Simple queries: `p.Category == "Dogs"`
- ✅ No LINQ translation issues

### JSON Serialization Assumption

**Work Pattern:** Any non-primitive property in a Marten document requires explicit JSON serialization handling.

When adding custom types to Marten documents, **immediately consider**:
1. **Does this need a JSON converter?** (Value objects, enums, complex types)
2. **Will this be queried in LINQ?** (If yes, strongly consider primitives)
3. **Does Marten need special configuration?** (Indexes, identity fields)

This applies to:
- Value objects
- Aggregates as nested documents
- Projections with custom types
- Any non-C# primitive

### Real Example: Product Catalog

**Initial attempt (failed):**
```csharp
public CategoryName Category { get; init; }  // Value object

// Query (doesn't work):
query.Where(p => p.Category.Value == "Dogs")  // Marten can't translate
```

**Pragmatic solution:**
```csharp
public string Category { get; init; }  // Primitive

// Query (works):
query.Where(p => p.Category == "Dogs")  // Direct SQL: WHERE data->>'Category' = 'Dogs'
```

**Validation moved to FluentValidation:**
```csharp
RuleFor(x => x.Category)
    .NotEmpty()
    .MaximumLength(50);
```

### Lessons Learned from Product Catalog BC

During implementation, we experienced:
1. Tests passed for CRUD operations ✅
2. Tests failed for category filtering ❌ (Marten LINQ translation error)
3. Changed `CategoryName` value object → `string` primitive
4. All tests passed ✅

**The signal:** When 22/24 tests pass, then you change a value object to a primitive and get 24/24, that's an **architecture signal**, not a test problem.

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
