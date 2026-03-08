# ADR 0018: Money Value Object as Canonical Monetary Representation

**Status:** ✅ Accepted

**Date:** 2026-03-08

**Context:** Pricing BC Phase 1 — How do we represent monetary values across bounded contexts?

---

## Context

Pricing BC is the first bounded context in CritterSupply that explicitly models **currency** alongside monetary amounts. Existing BCs use `decimal` for all prices:

- **Shopping BC:** `CartLineItem.UnitPrice` (decimal)
- **Product Catalog BC:** `Product.Price` (decimal)
- **Orders BC:** `OrderLineItem.Price` (decimal)
- **Payments BC:** `Payment.Amount` (decimal)

This works for single-currency systems (USD-only), but introduces several problems:

1. **Type Safety:** `decimal > decimal` compares amounts, but what if currencies differ? No compile-time or runtime protection.
2. **Future-Proofing:** Multi-currency support requires surgery across all BCs (add `Currency` string field everywhere).
3. **Semantic Clarity:** `decimal price` vs `Money price` — which communicates intent better?
4. **Domain Modeling:** "Money" is a **value object** in DDD terms — it has no identity, only value equality.

Pricing BC introduces the `Money` value object. This ADR establishes `Money` as the **canonical** monetary representation for ALL CritterSupply bounded contexts, with a migration path for existing `decimal` usages.

---

## Decision

**All domain-internal monetary values use the `Money` value object. Integration boundaries (messages, HTTP APIs) use primitives (`decimal` + `string Currency`).**

### Money Value Object Structure

```csharp
namespace Pricing;

[JsonConverter(typeof(MoneyJsonConverter))]
public sealed record Money
{
    public decimal Amount { get; init; }
    public string Currency { get; init; }  // ISO 4217 (e.g., "USD", "EUR", "GBP")

    private Money() { }  // Prevent deserialization bypass

    public static readonly Money Zero = Of(0m, "USD");

    /// <summary>
    /// Factory method to create Money value object with validation.
    /// </summary>
    public static Money Of(decimal amount, string currency = "USD")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency, nameof(currency));

        if (currency.Length != 3)
            throw new ArgumentException($"Currency must be ISO 4217 3-letter code, got: '{currency}'", nameof(currency));

        if (amount < 0)
            throw new ArgumentException($"Money amount cannot be negative: {amount}", nameof(amount));

        return new Money
        {
            Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero),
            Currency = currency.ToUpperInvariant()
        };
    }

    /// <summary>
    /// Operator overloads for comparison. Throws if currencies differ.
    /// </summary>
    public static bool operator >(Money a, Money b)  { AssertSameCurrency(a, b); return a.Amount > b.Amount; }
    public static bool operator <(Money a, Money b)  { AssertSameCurrency(a, b); return a.Amount < b.Amount; }
    public static bool operator >=(Money a, Money b) { AssertSameCurrency(a, b); return a.Amount >= b.Amount; }
    public static bool operator <=(Money a, Money b) { AssertSameCurrency(a, b); return a.Amount <= b.Amount; }

    /// <summary>
    /// Explicit cast to decimal. NOT implicit to prevent silent currency loss.
    /// Call sites must acknowledge currency context: (decimal)money or money.Amount
    /// </summary>
    public static explicit operator decimal(Money money) => money.Amount;

    public override string ToString() => $"{Amount:C2} {Currency}";

    private static void AssertSameCurrency(Money a, Money b)
    {
        if (a.Currency != b.Currency)
            throw new InvalidOperationException($"Cannot compare Money with different currencies: {a.Currency} vs {b.Currency}");
    }
}
```

### MoneyJsonConverter (API Boundary Serialization)

```csharp
public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var amount = root.GetProperty("amount").GetDecimal();
        var currency = root.GetProperty("currency").GetString() ?? "USD";

        return Money.Of(amount, currency);
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("amount", value.Amount);
        writer.WriteString("currency", value.Currency);
        writer.WriteEndObject();
    }
}
```

---

## Rationale

### Why Money Value Object?

**1. Type Safety**

```csharp
// BEFORE (decimal):
var price = 24.99m;
var discount = 5.00m;  // In EUR? USD? Who knows?
var final = price - discount;  // Compiles, but semantically wrong if currencies differ

// AFTER (Money):
var price = Money.Of(24.99m, "USD");
var discount = Money.Of(5.00m, "EUR");
var final = price - discount;  // ❌ Throws: "Cannot compare Money with different currencies"
```

**2. Future-Proof for Multi-Currency**

Phase 1 hardcodes `"USD"`, but the infrastructure is ready. Phase 2+ adds multi-currency by:
- Removing `currency = "USD"` default parameter
- Updating factory callers to pass explicit currency
- No structural changes to `Money` value object

**3. Semantic Clarity**

```csharp
// Clear intent
public sealed record ProductPriced(Guid ProductPriceId, string Sku, Money Price, ...);

// vs ambiguous
public sealed record ProductPriced(Guid ProductPriceId, string Sku, decimal Price, ...);
```

**4. DDD Value Object Pattern**

`Money` has no identity — two `Money` instances with same `Amount` and `Currency` are equal. Perfect fit for value object semantics.

**5. Prevents Silent Currency Loss**

```csharp
Money price = Money.Of(24.99m, "USD");

// ❌ Implicit cast would lose currency context
decimal amt = price;  // Compile error: cannot implicitly convert

// ✅ Explicit cast forces acknowledgment
decimal amt = (decimal)price;  // OK - caller acknowledges currency context loss
decimal amt = price.Amount;     // OK - explicit field access
```

---

## Consequences

### Positive

✅ **Type safety:** Cannot mix currencies accidentally
✅ **Future-proof:** Multi-currency support is structural change, not surgery
✅ **Clearer domain model:** `Money` vs `decimal` communicates intent
✅ **Operator overloads:** `price > threshold` reads naturally
✅ **Validation at construction:** No negative amounts, invalid currency codes rejected immediately

### Negative

⚠️ **Factory method ceremony:** Must use `Money.Of()`, cannot use constructor directly

```csharp
// ❌ Cannot do this
var money = new Money { Amount = 24.99m, Currency = "USD" };

// ✅ Must do this
var money = Money.Of(24.99m, "USD");
```

**Mitigation:** Factory method enforces validation. Ceremony is intentional (prevents invalid state).

⚠️ **Migration debt:** Existing BCs use `decimal` — gradual migration required

**Mitigation:** See "Migration Path" section below.

⚠️ **JSON serialization complexity:** API clients see `{ "amount": 24.99, "currency": "USD" }` not `24.99`

**Mitigation:** Integration boundaries use primitives (see "Integration Boundaries" section).

---

## Migration Path

### Phase 1: Pricing BC Uses Money Internally

- **Pricing BC:** All domain events use `Money`
- **Shopping BC:** Continues using `decimal UnitPrice` (integration boundary)
- **Catalog BC:** Continues using `decimal Price` (technical debt)
- **Orders BC:** Continues using `decimal Price` (receives from Shopping)

**Integration messages use primitives:**

```csharp
// Outbound from Pricing BC
public sealed record PricePublished(
    string Sku,
    decimal BasePrice,      // ← Primitive for integration boundary
    string Currency,        // ← Always "USD" in Phase 1
    DateTimeOffset PublishedAt);
```

**Why primitives at boundaries?** Integration events cross BC boundaries. Recipients may not have `Money` value object. Primitives are universal.

---

### Phase 2+: Gradual Adoption in Shopping BC

**Candidate:** `CartLineItem.UnitPrice` → `Money`

**Before:**
```csharp
public sealed record CartLineItem(string Sku, int Quantity, decimal UnitPrice);
```

**After:**
```csharp
public sealed record CartLineItem(string Sku, int Quantity, Money UnitPrice);
```

**Migration:**
- Marten schema migration: Add `Currency` column (default: `"USD"`)
- Event upcaster: Old `ItemAddedToCart` events with `decimal UnitPrice` → upcast to `Money.Of(unitPrice, "USD")`
- Integration tests verify backward compatibility

**Deferred:** Not required for Phase 1. Shopping BC can continue using `decimal` until multi-currency becomes a business requirement.

---

### Phase 3+: Catalog BC Adoption

**Technical Debt:** `Product.Price` is `decimal` in Catalog BC.

**Migration Path:**
- Add `Currency` field to `Product` (default: `"USD"`)
- Update `ProductAdded` event schema
- Integration messages already carry both fields (no BC-to-BC breaking change)

**Timing:** Deferred until multi-currency catalog support is a business requirement.

---

## Integration Boundaries

**Rule:** Domain events use `Money`. Integration events use `decimal + string Currency`.

### Domain Event (Internal to Pricing BC)

```csharp
public sealed record PriceChanged(
    Guid ProductPriceId,
    string Sku,
    Money OldPrice,         // ← Money value object
    Money NewPrice,         // ← Money value object
    DateTimeOffset PreviousPriceSetAt,
    string? Reason,
    Guid ChangedBy,
    DateTimeOffset ChangedAt);
```

### Integration Event (Cross-BC)

```csharp
public sealed record PriceUpdated(
    string Sku,
    decimal OldPrice,       // ← Primitive
    decimal NewPrice,       // ← Primitive
    string Currency,        // ← Always "USD" in Phase 1
    DateTimeOffset EffectiveAt);
```

### HTTP API Response (Customer Experience BFF)

```json
{
  "sku": "DOG-FOOD-5LB",
  "basePrice": 24.99,
  "currency": "USD",
  "status": "Published"
}
```

**Why not expose Money in JSON?** Simpler for frontend clients. Money value object is a backend concern.

---

## Alternatives Considered

### Alternative 1: Decimal + Separate Currency Field Everywhere

**Pattern:** Keep `decimal`, add `Currency` string field to every aggregate/event

```csharp
public sealed record ProductPrice(
    Guid Id,
    string Sku,
    decimal BasePrice,
    string Currency);  // ← Added field
```

**Pros:**
- ✅ No value object overhead
- ✅ Simpler JSON serialization

**Cons:**
- ❌ No type safety (can still mix currencies accidentally)
- ❌ No operator overload support (`price > threshold` requires manual currency check)
- ❌ Validation logic scattered (must validate currency on every factory method)

**Why rejected:** Type safety and operator overloads are too valuable. Value object centralizes validation.

---

### Alternative 2: Implicit Decimal Cast

**Pattern:** Allow `decimal amt = moneyValue;` (implicit conversion)

**Pros:**
- ✅ Less ceremony when extracting amount

**Cons:**
- ❌ Silent currency loss (defeats purpose of Money value object)
- ❌ Easy to accidentally drop currency context

**Why rejected:** Explicit cast forces caller acknowledgment. Ceremony is intentional.

---

### Alternative 3: Money as Mutable Class

**Pattern:** `public class Money { public decimal Amount { get; set; } }`

**Pros:**
- ✅ Can modify amount after construction

**Cons:**
- ❌ Mutability breaks value object semantics
- ❌ Reference equality instead of value equality
- ❌ Violates immutability-by-default principle (CLAUDE.md coding standards)

**Why rejected:** Immutability is non-negotiable for value objects.

---

## Implementation Checklist

- [ ] Create `Money.cs` in `src/Pricing/Pricing/` (Issue #192)
- [ ] Create `MoneyJsonConverter.cs` in same folder
- [ ] Unit tests for `Money.Of()` validation (negative amounts, invalid currency codes)
- [ ] Unit tests for operator overloads (same-currency pass, different-currency throw)
- [ ] Unit tests for explicit decimal cast
- [ ] Unit tests for JSON serialization (round-trip)
- [ ] Integration test: Domain event with `Money` → persists → rehydrates correctly

---

## References

- **Event Modeling:** `docs/planning/pricing-event-modeling.md` — "The Money Value Object" section
- **Skill:** `docs/skills/modern-csharp-coding-standards.md` — Value object patterns
- **Issue #192:** Implement Money value object with JsonConverter
- **Issue #193:** ProductPrice aggregate + domain events (uses Money)
- **Related ADRs:**
  - ADR 0017: Price freeze at add-to-cart (impacts Shopping BC `decimal` → `Money` migration timing)
  - ADR 0003: Value Objects vs Primitives for Queryable Fields (established value object precedent)

---

## Open Questions

**Q: Should Money support arithmetic operators (+, -, *, /)?**

**A (Phase 1 decision):** No. Arithmetic on money often requires rounding rules, tax calculations, and business logic. Keep Money simple (comparison only). Arithmetic happens in domain logic, returns new Money instances.

```csharp
// ❌ NOT supported
var total = price + tax;

// ✅ Domain logic handles arithmetic
var taxAmount = price.Amount * 0.08m;
var total = Money.Of(price.Amount + taxAmount, price.Currency);
```

**Q: Should Money be in Shared/Messages.Contracts?**

**A (Phase 1 decision):** No. Money is domain-internal. Integration messages use primitives (`decimal + string Currency`). Only Pricing BC uses Money in Phase 1. If other BCs adopt Money (Phase 2+), consider moving to shared library.

---

**This ADR establishes Money as the canonical monetary representation for CritterSupply, with a pragmatic migration path that allows existing BCs to continue using `decimal` until multi-currency becomes a business requirement.**
