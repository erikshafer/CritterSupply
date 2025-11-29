---
name: modern-csharp-coding-standards
description: Write modern C# code using records, pattern matching, value objects, async/await, and best-practice API design patterns. Emphasizes functional-style programming with C# 14+ features.
---

# Modern C# Coding Standards

## When to Use This Skill

Use this skill when:
- Writing new C# code or refactoring existing code
- Designing public APIs for libraries or services
- Implementing domain models, entities, value objects, and similar models
- Building async/await-heavy applications
- Optimizing performance-critical code paths

## Core Principles

1. **Immutability by Default** - Use `record` types and `init`-only properties
2. **Type Safety** - Leverage nullable reference types and value objects
3. **Modern Pattern Matching** - Use `switch` expressions and patterns extensively
4. **Async Everywhere** - Prefer async APIs with proper cancellation support
5. **API Design** - Accept abstractions, return appropriately specific types
6. **Composition Over Inheritance** - Avoid abstract base classes, prefer composition

---

## Best Practices Summary

### DO's ✅
- Use `record` for events, messages, domain entities, and value objects
- Prefer composition over inheritance
- Avoid abstract base classes in application code
- Use `sealed` by default with records and classes unless inheritance is required
- Leverage pattern matching with `switch` expressions
- Enable and respect nullable reference types
- Use async/await for all I/O operations
- Accept `CancellationToken` in all async methods
- Use `Span<T>` and `Memory<T>` for high-performance scenarios
- Accept abstractions (`IEnumerable<T>`, `IReadOnlyList<T>`)
- Return appropriate interfaces or concrete types
- Use `ConfigureAwait(false)` in library code
- Pool buffers with `ArrayPool<T>` for large allocations

### DON'Ts ❌
- Don't use mutable classes when records work
- Don't use classes for value objects
- Don't create deep inheritance hierarchies
- Don't ignore nullable reference type warnings
- Don't block on async code (`.Result`, `.Wait()`)
- Don't use `byte[]` when `Span<byte>` suffices
- Don't forget `CancellationToken` parameters
- Don't return mutable collections from APIs
- Don't throw exceptions for expected business errors
- Don't use `string` concatenation in loops
- Don't allocate large arrays repeatedly (use `ArrayPool`)

---

## Additional Resources

- **C# Language Specification**: https://learn.microsoft.com/en-us/dotnet/csharp/
- **Pattern Matching**: https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/functional/pattern-matching
- **Async Best Practices**: https://learn.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming
- **.NET Performance Tips**: https://learn.microsoft.com/en-us/dotnet/framework/performance/

---

## Language Patterns

### Records for Immutable Data (C# 9+)

Use `record` types for messages, events, domain entities, and DTOs.

```csharp
// A simple record. In this case, it's a command. No validation or behavior. That is handled by FluentValidation or similar library.
public sealed record BeginOrder(
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset StartedAt);

// Record with validation in constructor. Useful when FluentValidation (library) is not in place or an option.
public sealed record EmailAddress
{
    public string Value { get; init; }

    public EmailAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains('@'))
            throw new ArgumentException("Invalid email address", nameof(value));

        Value = value;
    }
}

// Record representing a value object symbolozing a product item with price. Includes factory methods with validation.
public sealed record PricedProductItem
{
    public Guid ProductId => ProductItem.ProductId;

    public int Quantity => ProductItem.Quantity;

    public decimal UnitPrice { get; }

    public decimal TotalPrice => Quantity * UnitPrice;
    public ProductItem ProductItem { get; }

    private PricedProductItem(ProductItem productItem, decimal unitPrice)
    {
        ProductItem = productItem;
        UnitPrice = unitPrice;
    }

    public static PricedProductItem Create(Guid? productId, int? quantity, decimal? unitPrice) =>
        Create(
            ProductItem.From(productId, quantity),
            unitPrice
        );

    public static PricedProductItem Create(ProductItem productItem, decimal? unitPrice) =>
        unitPrice switch
        {
            null => throw new ArgumentNullException(nameof(unitPrice)),
            <= 0 => throw new ArgumentOutOfRangeException(nameof(unitPrice),
                "Unit price has to be positive number"),
            _ => new PricedProductItem(productItem, unitPrice.Value)
        };
        
    // Other methods such as MatchesProductAndPrice, HasTheSameQuantity, SubtractQuantity removed for brevity.
}

// Record with computed properties
public sealed record Order(
    Guid Id,
    decimal Subtotal,
    decimal Tax)
{
    public decimal Total => Subtotal + Tax;
}

// Records with collections - use IReadOnlyList
public sealed record ShoppingCart(
    Guid CartId,
    Guid CustomerId,
    IReadOnlyList<CartItem> Items)
{
    public decimal Total => Items.Sum(item => item.Price * item.Quantity);
}

// Simple domain event
public sealed record CartInitialized(
    Guid CartId,
    Guid CustomerId,
    DateTimeOffset InitializedAt);

// Simple message used for integration events
public sealed record CartFinalized(
    Guid CartId,
    Guid ClientId,
    IReadOnlyList<PricedProductItem> ProductItems,
    decimal TotalPrice,
    DateTime FinalizedAt
)

// Simple immutable DTO
public sealed record CustomerDto(
    string Id,
    string Name,
    string Email);
```

---

### Pattern Matching (C# 8-14)

Leverage modern pattern matching for cleaner, more expressive code.

```csharp
// Switch expressions with value objects
public string GetPaymentMethodDescription(PaymentMethod payment) => payment switch
{
    { Type: PaymentType.CreditCard, Last4: var last4 } => $"Credit card ending in {last4}",
    { Type: PaymentType.BankTransfer, AccountNumber: var account } => $"Bank transfer from {account}",
    { Type: PaymentType.Cash } => "Cash payment",
    _ => "Unknown payment method"
};

// Property patterns
public decimal CalculateDiscount(Order order) => order switch
{
    { Total: > 1000m } => order.Total * 0.15m,
    { Total: > 500m } => order.Total * 0.10m,
    { Total: > 100m } => order.Total * 0.05m,
    _ => 0m
};

// Relational and logical patterns
public string ClassifyTemperature(int temp) => temp switch
{
    < 0 => "Freezing",
    >= 0 and < 10 => "Cold",
    >= 10 and < 20 => "Cool",
    >= 20 and < 30 => "Warm",
    >= 30 => "Hot",
    _ => throw new ArgumentOutOfRangeException(nameof(temp))
};

// List patterns (C# 11+)
public bool IsValidSequence(int[] numbers) => numbers switch
{
    [] => false,                                      // Empty
    [_] => true,                                      // Single element
    [var first, .., var last] when first < last => true,  // First < last
    _ => false
};

// Type patterns with null checks
public string FormatValue(object? value) => value switch
{
    null => "null",
    string s => $"\"{s}\"",
    int i => i.ToString(),
    double d => d.ToString("F2"),
    DateTime dt => dt.ToString("yyyy-MM-dd"),
    Money m => m.ToString(),
    IEnumerable<object> collection => $"[{string.Join(", ", collection)}]",
    _ => value.ToString() ?? "unknown"
};

// Combining patterns for complex logic
public record OrderState(bool IsPaid, bool IsShipped, bool IsCancelled);

public string GetOrderStatus(OrderState state) => state switch
{
    { IsCancelled: true } => "Cancelled",
    { IsPaid: true, IsShipped: true } => "Delivered",
    { IsPaid: true, IsShipped: false } => "Processing",
    { IsPaid: false } => "Awaiting Payment",
    _ => "Unknown"
};

// Pattern matching with value objects
public decimal CalculateShipping(Money total, Country destination) => (total, destination) switch
{
    ({ Amount: > 100m }, _) => 0m,                    // Free shipping over $100
    (_, { Code: "US" or "CA" }) => 5m,                // North America
    (_, { Code: "GB" or "FR" or "DE" }) => 10m,       // Europe
    _ => 25m                                           // International
};
```

---

### Nullable Reference Types (C# 8+)

Enable nullable reference types in your project and handle nulls explicitly.

```csharp
// In .csproj
<PropertyGroup>
    <Nullable>enable</Nullable>
</PropertyGroup>

// Explicit nullability
public class UserService
{
    // Non-nullable by default
    public string GetUserName(User user) => user.Name;

    // Explicitly nullable return
    public string? FindUserName(string userId)
    {
        var user = _repository.Find(userId);
        return user?.Name;  // Returns null if user not found
    }

    // Null-forgiving operator (use sparingly!)
    public string GetRequiredConfigValue(string key)
    {
        var value = Configuration[key];
        return value!;  // Only if you're CERTAIN it's not null
    }

    // Nullable value objects
    public Money? GetAccountBalance(string accountId)
    {
        var account = _repository.Find(accountId);
        return account?.Balance;
    }
}

// Pattern matching with null checks
public decimal GetDiscount(Customer? customer) => customer switch
{
    null => 0m,
    { IsVip: true } => 0.20m,
    { OrderCount: > 10 } => 0.10m,
    _ => 0.05m
};

// Null-coalescing patterns
public string GetDisplayName(User? user) =>
    user?.PreferredName ?? user?.Email ?? "Guest";

// Guard clauses with ArgumentNullException.ThrowIfNull (C# 11+)
public void ProcessOrder(Order? order)
{
    ArgumentNullException.ThrowIfNull(order);

    // order is now non-nullable in this scope
    Console.WriteLine(order.Id);
}
```

---

## Composition Over Inheritance

**Avoid abstract base classes and inheritance hierarchies.** Use composition and interfaces instead.

```csharp
// ❌ BAD: Abstract base class hierarchy
public abstract class PaymentProcessor
{
    public abstract Task<PaymentResult> ProcessAsync(Money amount);

    protected async Task<bool> ValidateAsync(Money amount)
    {
        // Shared validation logic
        return amount.Amount > 0;
    }
}

public class CreditCardProcessor : PaymentProcessor
{
    public override async Task<PaymentResult> ProcessAsync(Money amount)
    {
        await ValidateAsync(amount);
        // Process credit card...
    }
}

// ✅ GOOD: Composition with interfaces
public interface IPaymentProcessor
{
    Task<PaymentResult> ProcessAsync(Money amount, CancellationToken cancellationToken);
}

public interface IPaymentValidator
{
    Task<ValidationResult> ValidateAsync(Money amount, CancellationToken cancellationToken);
}

// Concrete implementations compose validators
public sealed class CreditCardProcessor : IPaymentProcessor
{
    private readonly IPaymentValidator _validator;
    private readonly ICreditCardGateway _gateway;

    public CreditCardProcessor(IPaymentValidator validator, ICreditCardGateway gateway)
    {
        _validator = validator;
        _gateway = gateway;
    }

    public async Task<PaymentResult> ProcessAsync(Money amount, CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(amount, cancellationToken);
        if (!validation.IsValid)
            return PaymentResult.Failed(validation.Error);

        return await _gateway.ChargeAsync(amount, cancellationToken);
    }
}

// ✅ GOOD: Static helper classes for shared logic (no inheritance)
public static class PaymentValidation
{
    public static ValidationResult ValidateAmount(Money amount)
    {
        if (amount.Amount <= 0)
            return ValidationResult.Invalid("Amount must be positive");

        if (amount.Amount > 10000m)
            return ValidationResult.Invalid("Amount exceeds maximum");

        return ValidationResult.Valid();
    }
}

// ✅ GOOD: Records for modeling variants (not inheritance)
public enum PaymentType { CreditCard, BankTransfer, Cash }

public record PaymentMethod
{
    public PaymentType Type { get; init; }
    public string? Last4 { get; init; }           // For credit cards
    public string? AccountNumber { get; init; }    // For bank transfers

    public static PaymentMethod CreditCard(string last4) => new()
    {
        Type = PaymentType.CreditCard,
        Last4 = last4
    };

    public static PaymentMethod BankTransfer(string accountNumber) => new()
    {
        Type = PaymentType.BankTransfer,
        AccountNumber = accountNumber
    };

    public static PaymentMethod Cash() => new() { Type = PaymentType.Cash };
}
```

**When inheritance is acceptable:**
- Framework requirements (e.g., `ControllerBase` in ASP.NET Core)
- Library integration (e.g., custom exceptions inheriting from `Exception`)
- **These should be rare cases in your application code**

---
