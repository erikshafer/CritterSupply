# Modern C# Coding Standards

C# language features, code style, and best practices for CritterSupply.

## Core Principles

1. **Immutability by default** — Use records, readonly collections, `with` expressions
2. **Sealed by default** — Prevent unintended inheritance
3. **Value objects for domain concepts** — Wrap primitives with validation
4. **Pure functions where possible** — Separate decisions from side effects

## Records and Immutability

Use records for commands, queries, events, DTOs, and value objects:

```csharp
// Commands
public sealed record ProcessPayment(Guid PaymentId, decimal Amount);

// Events
public sealed record PaymentProcessed(Guid PaymentId, DateTimeOffset ProcessedAt);

// Value objects
public sealed record Money(decimal Amount, string Currency);

// DTOs / View models
public sealed record PaymentView(Guid Id, decimal Amount, PaymentStatus Status);
```

Use `with` expressions for immutable updates:

```csharp
public sealed record Payment(Guid Id, PaymentStatus Status, DateTimeOffset? ProcessedAt)
{
    public Payment MarkAsProcessed() =>
        this with
        {
            Status = PaymentStatus.Processed,
            ProcessedAt = DateTimeOffset.UtcNow
        };
}
```

## Sealed by Default

All commands, queries, events, and models should be `sealed`:

```csharp
// GOOD
public sealed record GetCustomerAddress(Guid CustomerId);
public sealed record CustomerShippingAddress(Guid Id, string AddressLine1, /* ... */);

// BAD — allows unintended inheritance
public record GetCustomerAddress(Guid CustomerId);
```

## Collection Patterns

**Always prefer immutable collections:**

```csharp
// GOOD — Immutable
public sealed record Product(
    string Sku,
    string Name,
    IReadOnlyList<ProductImage> Images,
    IReadOnlyList<string> Tags);

// BAD — Mutable
public sealed record Product(
    string Sku,
    string Name,
    List<ProductImage> Images,  // Can be modified externally
    List<string> Tags);
```

**Prefer:**
- `IReadOnlyList<T>` for ordered collections
- `IReadOnlyCollection<T>` for unordered collections
- `IReadOnlyDictionary<TKey, TValue>` for key-value pairs

**Creating immutable collections:**

```csharp
// From array
var images = imageArray.ToList().AsReadOnly();

// Empty collection (C# 12+)
var tags = new List<string>().AsReadOnly();
// Or using collection expressions
IReadOnlyList<string> tags = [];
```

## Value Object Pattern

Use value objects to wrap primitives with domain-specific validation. All value objects follow a standard pattern:

1. **Sealed record** with `Value` property
2. **Factory method** (`From(string)`) with validation
3. **Private parameterless constructor** for Marten/JSON
4. **Implicit string operator** for seamless queries
5. **JSON converter** for transparent serialization

### Example: Sku (uppercase, alphanumeric + hyphens, max 24 chars)

```csharp
[JsonConverter(typeof(SkuJsonConverter))]
public sealed record Sku
{
    private const int MaxLength = 24;
    private static readonly Regex ValidPattern = new(@"^[A-Z0-9\-]+$", RegexOptions.Compiled);

    public string Value { get; init; } = null!;
    private Sku() { }

    public static Sku From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("SKU cannot be empty", nameof(value));
        if (value.Length > MaxLength)
            throw new ArgumentException($"SKU cannot exceed {MaxLength} characters", nameof(value));
        if (!ValidPattern.IsMatch(value))
            throw new ArgumentException("SKU must be uppercase letters, numbers, and hyphens only", nameof(value));

        return new Sku { Value = value };
    }

    public static implicit operator string(Sku sku) => sku.Value;
    public override string ToString() => Value;
}

public sealed class SkuJsonConverter : JsonConverter<Sku>
{
    public override Sku Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() is { } value ? Sku.From(value) : throw new JsonException("SKU cannot be null");

    public override void Write(Utf8JsonWriter writer, Sku value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
```

### Example: ProductName (mixed case, special chars allowed, max 100 chars)

```csharp
[JsonConverter(typeof(ProductNameJsonConverter))]
public sealed record ProductName
{
    private const int MaxLength = 100;
    private static readonly Regex ValidPattern = new(@"^[A-Za-z0-9\s.,!&()\-]+$", RegexOptions.Compiled);

    public string Value { get; init; } = null!;
    private ProductName() { }

    public static ProductName From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Product name cannot be empty", nameof(value));

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
            throw new ArgumentException($"Product name cannot exceed {MaxLength} characters", nameof(value));
        if (!ValidPattern.IsMatch(trimmed))
            throw new ArgumentException("Invalid characters in product name", nameof(value));

        return new ProductName { Value = trimmed };
    }

    public static implicit operator string(ProductName name) => name.Value;
    public override string ToString() => Value;
}

public sealed class ProductNameJsonConverter : JsonConverter<ProductName>
{
    public override ProductName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() is { } value ? ProductName.From(value) : throw new JsonException("Name cannot be null");

    public override void Write(Utf8JsonWriter writer, ProductName value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
```

### Usage in Domain Models

```csharp
public sealed record Product
{
    public Sku Sku { get; init; } = null!;
    public ProductName Name { get; init; } = null!;
    public CategoryName Category { get; init; } = null!;
    // ...

    private Product() { }

    public static Product Create(string sku, string name, string category)
    {
        return new Product
        {
            Sku = Sku.From(sku),              // Validates constraints
            Name = ProductName.From(name),
            Category = CategoryName.From(category),
            AddedAt = DateTimeOffset.UtcNow
        };
    }
}
```

### Usage in Marten Configuration

```csharp
opts.Schema.For<Product>()
    .Identity(x => x.Sku)  // Implicit conversion to string
    .SoftDeleted();
```

### Usage in HTTP Commands

```csharp
public sealed record AddProduct(string Sku, string Name, string Category);

public static class AddProductHandler
{
    [WolverinePost("/api/products")]
    public static async Task<CreationResponse> Handle(
        AddProduct command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var product = Product.Create(command.Sku, command.Name, command.Category);
        session.Store(product);
        await session.SaveChangesAsync(ct);
        return new CreationResponse($"/api/products/{command.Sku}");
    }
}
```

### JSON Serialization

Value objects serialize as plain strings (not wrapped objects):

```json
// HTTP Request
{ "sku": "DOG-BOWL-001", "name": "Premium Dog Bowl" }

// Marten Document
{ "sku": "DOG-BOWL-001", "name": "Premium Dog Bowl", "status": "Active" }
```

### When to Use

**Use value objects for:**
- Identity values with constraints (Sku, OrderId, CustomerId)
- Domain concepts with rules (ProductName, EmailAddress)
- Values requiring validation (Money, Percentage)

**Don't use for:**
- Strings with no constraints (descriptions, free-text)
- Primitives with no business rules (counts, flags)

## FluentValidation

Use FluentValidation for command/query validation, nested inside the command:

```csharp
public sealed record AddProduct(string Sku, string Name, string Description)
{
    public class AddProductValidator : AbstractValidator<AddProduct>
    {
        public AddProductValidator()
        {
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(24);
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Description).MaximumLength(2000);
        }
    }
}
```

> **Reference:** [FluentValidation Documentation](https://docs.fluentvalidation.net/)

## Status Enums

Use enums for aggregate lifecycle state instead of booleans:

```csharp
// GOOD
public enum PaymentStatus
{
    Pending,
    Authorized,
    Captured,
    Failed,
    Refunded
}

public sealed record Payment(Guid Id, PaymentStatus Status)
{
    public bool IsTerminal => Status is PaymentStatus.Captured or PaymentStatus.Failed or PaymentStatus.Refunded;
}

// BAD — multiple booleans create ambiguity
public sealed record Payment(Guid Id, bool IsAuthorized, bool IsCaptured, bool IsFailed);
```

## Factory Methods

Use static factory methods for object creation:

```csharp
public sealed record Customer
{
    public Guid Id { get; init; }
    public string Email { get; init; } = null!;
    public DateTimeOffset CreatedAt { get; init; }

    private Customer() { }  // Private constructor

    public static Customer Create(string email)
    {
        return new Customer
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
```

## Nullable Reference Types

Enable nullable reference types and be explicit about nullability:

```csharp
// Explicit nullable
public sealed record CustomerAddress(
    string AddressLine1,
    string? AddressLine2,  // Explicitly nullable
    string City,
    string Postcode);

// In handlers
public static ProblemDetails Before(GetCustomer query, Customer? customer)
{
    if (customer is null)
        return new ProblemDetails { Detail = "Not found", Status = 404 };

    return WolverineContinue.NoProblems;
}
```

### Message Record Field Nullability

**Fields on message records (commands, events, integration messages) that are always populated
should be required and non-nullable.** Optional-with-default is a code smell that:
- Suggests the field might be legitimately absent when it never is
- Hides type-system guarantees at call sites
- Invites "I'll fill it in later" patterns that defer correctness checks

```csharp
// ❌ Nullable-with-default: implies ChangeType might be absent — it never is
public sealed record ChangeRequestDecisionPersonal(
    Guid VendorUserId,
    Guid RequestId,
    string Decision,
    string? ChangeType = null) : IVendorUserMessage;

// ✅ Required: compiler enforces population at all construction sites
public sealed record ChangeRequestDecisionPersonal(
    Guid VendorUserId,
    Guid RequestId,
    string Decision,
    string ChangeType) : IVendorUserMessage;
```

**When nullable fields ARE appropriate on messages:**
- Fields that are genuinely optional by business logic (e.g., `string? RejectionReason` — only set when rejected)
- Fields added in a later version for backward compatibility with existing serialized messages

**Rule of thumb:** If every handler or factory method that creates this record always passes a value,
the field is not optional — make it required.

## Pattern Matching

Use modern pattern matching:

```csharp
// Type patterns
if (result is SuccessResult success)
{
    // Use success
}

// Property patterns
if (payment is { Status: PaymentStatus.Pending, Amount: > 0 })
{
    // Process payment
}

// Switch expressions
var message = status switch
{
    PaymentStatus.Pending => "Awaiting processing",
    PaymentStatus.Authorized => "Payment authorized",
    PaymentStatus.Captured => "Payment complete",
    PaymentStatus.Failed => "Payment failed",
    _ => "Unknown status"
};
```

## Async/Await

Follow async best practices:

```csharp
// GOOD — async all the way, with cancellation
public static async Task<Payment?> Handle(
    GetPayment query,
    IDocumentSession session,
    CancellationToken ct)
{
    return await session.LoadAsync<Payment>(query.PaymentId, ct);
}

// GOOD — return Task directly when no await needed
public static Task<Payment?> Handle(GetPayment query, IDocumentSession session, CancellationToken ct)
    => session.LoadAsync<Payment>(query.PaymentId, ct);

// BAD — blocking on async
public static Payment? Handle(GetPayment query, IDocumentSession session)
    => session.LoadAsync<Payment>(query.PaymentId).Result;  // Deadlock risk!
```

## GUIDs

Use `Guid.CreateVersion7()` for new identifiers (time-ordered, better for database indexing):

```csharp
var paymentId = Guid.CreateVersion7();
var orderId = Guid.CreateVersion7();
```

## DateTimeOffset

Always use `DateTimeOffset` instead of `DateTime` for timestamps:

```csharp
// GOOD — includes timezone information
public DateTimeOffset CreatedAt { get; init; }
public DateTimeOffset? ProcessedAt { get; init; }

// Use UTC
var now = DateTimeOffset.UtcNow;
```

## Decimal and Financial Calculations

### Banker's Rounding ⚠️ **CRITICAL for Financial Operations**

**Problem:** `Math.Round()` uses banker's rounding (round-to-even) by default, NOT round-away-from-zero.

```csharp
// Examples of banker's rounding behavior:
Math.Round(6.825m, 2)  // → 6.82 (rounds DOWN to even)
Math.Round(6.835m, 2)  // → 6.84 (rounds UP to even)
Math.Round(4.5m, 0)    // → 4 (rounds DOWN to even)
Math.Round(5.5m, 0)    // → 6 (rounds UP to even)
```

**Why this matters:**
- Default .NET rounding mode: `MidpointRounding.ToEven`
- Affects discount calculations, tax calculations, currency conversions
- Can cause test failures when expecting traditional "round away from zero" behavior
- Small differences accumulate in bulk calculations

**When discovered:** M30.0 discount calculation tests expected `28.66m` but got `28.64m` due to banker's rounding of `6.825m → 6.82m`.

**Example from Promotions BC (M30.0):**

```csharp
// Calculating percentage discount
private static LineItemDiscount CalculatePercentageDiscount(
    CartLineItem item,
    decimal discountPercentage,
    string couponCode)
{
    var originalPrice = item.UnitPrice;

    // Math.Round uses banker's rounding by default!
    // 15% of 45.50 = 6.825 → rounds to 6.82 (not 6.83)
    var discountAmount = Math.Round(originalPrice * (discountPercentage / 100m), 2);
    var discountedPrice = originalPrice - discountAmount;

    return new LineItemDiscount(
        Sku: item.Sku,
        OriginalPrice: originalPrice,
        DiscountedPrice: Math.Max(discountedPrice, 0),
        DiscountAmount: discountAmount * item.Quantity,
        AppliedCouponCode: couponCode);
}
```

**Test expectations must account for banker's rounding:**

```csharp
// Calculate expected discount: 15% of each item's unit price * quantity
// SKU-001: 15% of 29.99 = 4.4985, rounded to 4.50 * 3 = 13.50
// SKU-002: 15% of 10.01 = 1.5015, rounded to 1.50 * 1 = 1.50
// SKU-003: 15% of 45.50 = 6.825, banker's rounding to 6.82 * 2 = 13.64
// Total discount: 28.64 (not 28.66 due to banker's rounding)
response.TotalDiscount.ShouldBe(28.64m);
```

**If you need traditional "round away from zero":**

```csharp
// Explicitly specify rounding mode
var rounded = Math.Round(6.825m, 2, MidpointRounding.AwayFromZero); // → 6.83
```

**Best practices:**

1. **Document rounding behavior** in financial calculation methods
2. **Test with midpoint values** (X.X25, X.X75, etc.) to catch rounding assumptions
3. **Be consistent** — use same rounding mode throughout a calculation pipeline
4. **Consider regulatory requirements** — some jurisdictions mandate specific rounding rules

**Reference:** [M30.0 Retrospective - D3: Banker's Rounding in Discount Calculations](../../planning/milestones/m30-0-retrospective.md#d3-bankers-rounding-in-discount-calculations)

---

## Naming Conventions

| Type | Convention | Example |
|------|------------|---------|
| Commands | Verb + Noun | `ProcessPayment`, `AddItemToCart` |
| Queries | Get + Noun | `GetPaymentById`, `GetCartSummary` |
| Events | Noun + Past Verb | `PaymentProcessed`, `ItemAdded` |
| Handlers | Command/Query + Handler | `ProcessPaymentHandler` |
| Validators | Command/Query + Validator | `ProcessPaymentValidator` |
