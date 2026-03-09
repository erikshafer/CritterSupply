# D1 Decision: Variant Model — Principal Architect Sign-Off & Implementation Guide

**Decision:** D1 — Variant Model Shape  
**Resolution:** ✅ Option A — One Parent ProductFamily + N Child Variant SKU Records  
**Date:** 2026-06-14  
**Author:** Principal Software Architect  
**Status:** 🟢 Approved — Ready for Phase 3 implementation planning

---

## Table of Contents

1. [Principal Architect Sign-Off](#1-principal-architect-sign-off)
2. [Data Model Design](#2-data-model-design)
   - [ProductFamily Aggregate](#21-productfamily-aggregate)
   - [ProductVariant — No Separate Class; Variant IS a Product](#22-productvariant--no-separate-class-variant-is-a-product)
   - [VariantAttributes Value Object](#23-variantattributes-value-object)
   - [Domain Events](#24-domain-events)
   - [Integration Messages (Messages.Contracts.ProductCatalog)](#25-integration-messages-messagescontractsproductcatalog)
3. [Stream Key Design](#3-stream-key-design)
4. [Listings BC Impact](#4-listings-bc-impact)
5. [Downstream BC Impact Summary](#5-downstream-bc-impact-summary)
6. [Phase 3 Implementation Tasks](#6-phase-3-implementation-tasks)
7. [ADR Additions](#7-adr-additions)
8. [Outstanding Questions for Owner](#8-outstanding-questions-for-owner)

---

## 1. Principal Architect Sign-Off

Having reviewed the full evolution plan (`catalog-listings-marketplaces-evolution-plan.md`), the canonical glossary (`catalog-listings-marketplaces-glossary.md`), ADR 0016 (UUID v5 stream key convention), and the existing codebase — including the `ProductPrice` event-sourced aggregate in the Pricing BC as the reference implementation — I formally endorse **Option A: One parent ProductFamily + N child variant SKU records** as the correct variant model shape for CritterSupply.

This is not a novel decision. It is the dominant industry pattern: Amazon's parent ASIN / child ASIN structure, Shopify's Product / ProductVariant model, Walmart Marketplace's parent-child item relationship, and every mature marketplace API I have worked with across fifteen years of e-commerce architecture converge on this shape. Erik's reasoning — "go with the crowd, go with what's common and established" — is precisely correct, and I endorse it on architectural grounds as well as business grounds. Option A is the *only* option that doesn't require a domain model rewrite when we eventually integrate with Amazon SP-API or Walmart Marketplace API, because those APIs speak parent/child natively. Building anything else means an impedance mismatch at the integration boundary — technical debt from day one.

The `ProductFamily` aggregate is a first-class event-sourced domain object within the Product Catalog BC. A Variant is **not** a new class; per the canonical glossary, a Variant IS a `Product` record with `FamilyId` set — maintaining a single, coherent event-sourced stream per SKU in the existing `catalog:{sku}` namespace. This two-stream model (one `ProductFamily` stream tracking the grouping and shared content, plus N existing `Product` streams for variant SKUs with a new `FamilyId` property) maps cleanly onto Marten's event store, requires zero migration of existing product data, and is additive rather than destructive. Implementation can begin immediately once Phase 0 (Product Catalog ES migration) and Phase 1 (Listings BC foundation) are complete.

---

## 2. Data Model Design

### 2.1 ProductFamily Aggregate

`ProductFamily` is an event-sourced Marten aggregate. It is a **grouping container** that owns:
- Shared display content (name, description, brand) that all variants inherit
- The canonical set of variant SKUs belonging to this family
- The category assignment (which drives marketplace attribute schema selection)

**What ProductFamily does NOT own:**
- SKU-level pricing (owned by Pricing BC per existing design)
- SKU-level inventory (owned by Inventory BC)
- SKU-level listing state (owned by Listings BC)
- Variant-specific attributes (owned by the `Product` variant record)

**Stream key:** `Guid.CreateVersion7()` — UUID v7 generated at creation time. `ProductFamily` has **no stable natural string key** to hash (unlike `Product` which has SKU). The `FamilyId` is generated once in the `CreateProductFamily` command handler and is included in every subsequent command that writes to this stream. All handlers that touch a family stream receive `FamilyId` in the inbound command — no lookup is needed, because the family is referenced by ID everywhere.

> **Why not UUID v5 for ProductFamily?** UUID v5 is for aggregates with a stable natural domain key — a string you can normalize and hash deterministically, like `catalog:DOG-BOWL-001`. ProductFamily has no such key. A product family named "AquaPaws Fountain" is not uniquely keyed by name (names can change). UUID v7 is the correct choice here, matching the existing pattern used by Cart, Order, Checkout, Shipment, and Payment (see ADR 0016). The `FamilyId` IS the stream key — no UUID v5 derivation involved.

```csharp
/// <summary>
/// Event-sourced aggregate representing the parent grouping container for related product variants.
/// Holds shared content (name, description, brand, category) and tracks which variant SKUs belong.
///
/// Stream key: Guid.CreateVersion7() — generated at creation, carried in all subsequent commands.
/// See catalog-variant-model.md and ADR 0030 for the full D1 rationale.
///
/// WHAT THIS IS: A grouping container. Customers browse by family; they buy a specific variant SKU.
/// WHAT THIS IS NOT: A purchasable unit (variants are purchasable); a price or inventory owner.
/// </summary>
public sealed record ProductFamily
{
    /// <summary>
    /// UUID v7 stream ID — generated at family creation, immutable.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Display name shared across all variants (e.g., "AquaPaws Fountain").
    /// Variants may have channel-specific titles in Listings BC, but this is the catalog authority.
    /// </summary>
    public string Name { get; init; } = null!;

    /// <summary>
    /// Shared base description. Variants may override in channel-specific Listing content.
    /// </summary>
    public string Description { get; init; } = null!;

    /// <summary>
    /// Brand name. Applies to all variants in this family.
    /// </summary>
    public string? Brand { get; init; }

    /// <summary>
    /// Product category — determines marketplace attribute schema selection in Listings BC.
    /// Simple string for Phase 3; will reference structured taxonomy node in Phase 3+.
    /// </summary>
    public string Category { get; init; } = null!;

    /// <summary>
    /// Shared product images. Variant-specific images (e.g., color swatches) live on the Product record.
    /// Marten serializes IReadOnlyList<T> correctly as a JSON array.
    /// </summary>
    public IReadOnlyList<ProductImage> SharedImages { get; init; } = [];

    /// <summary>
    /// The ordered set of variant SKUs belonging to this family.
    /// Order is preserved: first SKU added is the default display variant.
    /// </summary>
    public IReadOnlyList<string> VariantSkus { get; init; } = [];

    /// <summary>
    /// Family lifecycle status. Active families accept new variants and support listing creation.
    /// A family becomes Archived when all its variants are Discontinued or Deleted.
    /// </summary>
    public ProductFamilyStatus Status { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    // -------------------------------------------------------------------------
    // Apply methods — pure state transitions, no side effects.
    // -------------------------------------------------------------------------

    public static ProductFamily Create(ProductFamilyCreated @event) =>
        new()
        {
            Id = @event.FamilyId,
            Name = @event.Name,
            Description = @event.Description,
            Brand = @event.Brand,
            Category = @event.Category,
            SharedImages = @event.SharedImages,
            VariantSkus = [],
            Status = ProductFamilyStatus.Active,
            CreatedAt = @event.CreatedAt
        };

    public ProductFamily Apply(ProductFamilyUpdated @event) =>
        this with
        {
            Name = @event.Name ?? Name,
            Description = @event.Description ?? Description,
            Brand = @event.Brand ?? Brand,
            Category = @event.Category ?? Category,
            SharedImages = @event.SharedImages ?? SharedImages,
            UpdatedAt = @event.UpdatedAt
        };

    public ProductFamily Apply(ProductVariantAdded @event) =>
        this with
        {
            VariantSkus = [.. VariantSkus, @event.VariantSku],
            UpdatedAt = @event.AddedAt
        };

    public ProductFamily Apply(ProductVariantRemoved @event) =>
        this with
        {
            // Filter then collect using a collection expression — single allocation, no double-wrap.
            VariantSkus = [.. VariantSkus
                .Where(sku => !string.Equals(sku, @event.VariantSku, StringComparison.OrdinalIgnoreCase))],
            UpdatedAt = @event.RemovedAt
        };

    public ProductFamily Apply(ProductFamilyArchived @event) =>
        this with
        {
            Status = ProductFamilyStatus.Archived,
            UpdatedAt = @event.ArchivedAt
        };

    // -------------------------------------------------------------------------
    // Invariant helpers — used by handlers; pure boolean functions.
    // -------------------------------------------------------------------------

    public bool HasVariant(string sku) =>
        VariantSkus.Any(s => string.Equals(s, sku, StringComparison.OrdinalIgnoreCase));

    public bool IsActive => Status == ProductFamilyStatus.Active;
    public bool CanAcceptVariants => IsActive;
}

/// <summary>
/// Lifecycle status of a ProductFamily.
/// Active families are the only valid target for new variant additions.
/// Archived is a soft terminal state — family still exists for historical queries.
/// </summary>
public enum ProductFamilyStatus
{
    Active,
    Archived
}
```

**Marten registration** (add to Product Catalog BC `Program.cs`):

```csharp
opts.Events.AddEventTypes([
    typeof(ProductFamilyCreated),
    typeof(ProductFamilyUpdated),
    typeof(ProductVariantAdded),
    typeof(ProductVariantRemoved),
    typeof(ProductFamilyArchived)
]);

// Inline projection: always-current family read model for admin UI queries.
opts.Projections.Add<ProductFamilyViewProjection>(ProjectionLifecycle.Inline);
```

---

### 2.2 ProductVariant — No Separate Class; Variant IS a Product

Per the canonical glossary (§ "Product Variant"):

> **No separate class initially — a Variant IS a `Product` record with `familyId` set.**

This is the right call for Phase 3 and I endorse it. Each variant already has its own event-sourced stream keyed by `catalog:{sku}`. Adding variant support means:

1. Adding `FamilyId?` to the `Product` aggregate (null = standalone product, not part of a family)
2. Adding `VariantAttributes?` to the `Product` aggregate
3. Appending a new `ProductFamilyAssigned` domain event to the **product's existing stream** when it joins a family

This design is **purely additive**. It does not require migrating existing product streams. Existing standalone products (no family) continue working exactly as before — `FamilyId` is null, `VariantAttributes` is null, nothing changes downstream.

```csharp
// Phase 3 additions to the existing Product aggregate record.
// These are additive — null values mean "standalone product, not a variant."

public sealed record Product
{
    // ... (all existing properties remain unchanged) ...

    /// <summary>
    /// If set, this Product is a variant belonging to the specified ProductFamily.
    /// Null for standalone products that are not part of any family.
    /// Set by the ProductFamilyAssigned domain event.
    /// </summary>
    public Guid? FamilyId { get; init; }

    /// <summary>
    /// Differentiating attributes that distinguish this variant from its siblings.
    /// E.g., { "Size": "Large", "Color": "Red" }.
    /// Null for standalone products.
    /// </summary>
    public VariantAttributes? VariantAttributes { get; init; }

    // New Apply method — product joins a family.
    public Product Apply(ProductFamilyAssigned @event) =>
        this with
        {
            FamilyId = @event.FamilyId,
            VariantAttributes = @event.Attributes,
            UpdatedAt = @event.AssignedAt
        };

    // New Apply method — product leaves a family (admin removes variant from family).
    public Product Apply(ProductFamilyUnassigned @event) =>
        this with
        {
            FamilyId = null,
            VariantAttributes = null,
            UpdatedAt = @event.UnassignedAt
        };

    // Convenience: whether this Product record is currently a variant.
    public bool IsVariant => FamilyId.HasValue;
}
```

**Handler pattern — Adding a variant to a family** (compound handler, Wolverine A-Frame):

```csharp
// Command: issued by admin when linking an existing product to a family.
public sealed record AddVariantToFamily(
    Guid FamilyId,
    string VariantSku,
    IDictionary<string, string> Attributes);

// Validator (FluentValidation — runs before Load).
public sealed class AddVariantToFamilyValidator : AbstractValidator<AddVariantToFamily>
{
    public AddVariantToFamilyValidator()
    {
        RuleFor(x => x.FamilyId).NotEmpty();
        RuleFor(x => x.VariantSku).NotEmpty()
            // Constant extracted to SkuValidation.Pattern in production — shown inline here for clarity.
            .Matches(@"^[A-Z0-9\-]+$").WithMessage("SKU must be uppercase alphanumeric with hyphens.");
        RuleFor(x => x.Attributes).NotNull().NotEmpty()
            .WithMessage("At least one variant attribute is required (e.g., Size or Color).");
    }
}

// Handler — Wolverine compound handler pattern.
public static class AddVariantToFamilyHandler
{
    // Load phase: Wolverine resolves both aggregates from their streams.
    // [Aggregate] attribute tells Wolverine to use Marten event sourcing.
    public static async Task<(ProductFamily, Product)> LoadAsync(
        AddVariantToFamily command,
        IQuerySession session,
        CancellationToken ct)
    {
        var familyStreamId = command.FamilyId;
        var productStreamId = Product.StreamId(command.VariantSku);  // catalog:{sku} UUID v5

        var family = await session.Events.AggregateStreamAsync<ProductFamily>(familyStreamId, token: ct)
            ?? throw new InvalidOperationException($"ProductFamily {command.FamilyId} not found. Ensure the family was created before adding variants.");

        var product = await session.Events.AggregateStreamAsync<Product>(productStreamId, token: ct)
            ?? throw new InvalidOperationException($"Product with SKU '{command.VariantSku}' not found in catalog. Ensure the product was added via AddProduct before assigning it to a family.");

        return (family, product);
    }

    // Validate phase: pure business rule checks.
    public static ProblemDetails? Validate(
        AddVariantToFamily command,
        ProductFamily family,
        Product product)
    {
        if (!family.IsActive)
            return new ProblemDetails { Detail = $"ProductFamily {command.FamilyId} is not Active." };

        if (family.HasVariant(command.VariantSku))
            return new ProblemDetails { Detail = $"SKU {command.VariantSku} is already a variant of this family." };

        if (product.IsTerminal)
            return new ProblemDetails { Detail = $"Product {command.VariantSku} is Discontinued or Deleted and cannot be added to a family." };

        if (product.IsVariant)
            return new ProblemDetails { Detail = $"Product {command.VariantSku} already belongs to family {product.FamilyId}. Remove it from that family first." };

        return null;
    }

    // Handle phase: emit domain events to BOTH streams.
    // Returns OutgoingMessages to append events to two different streams in one SaveChangesAsync.
    public static IEnumerable<object> Handle(
        AddVariantToFamily command,
        ProductFamily family,
        Product product,
        IDocumentSession session)
    {
        var attributes = VariantAttributes.From(command.Attributes);
        var now = DateTimeOffset.UtcNow;

        // Event on the ProductFamily stream (family learns of its new variant).
        session.Events.Append(
            command.FamilyId,
            new ProductVariantAdded(command.FamilyId, command.VariantSku, attributes, now));

        // Event on the Product stream (product learns its family membership).
        session.Events.Append(
            Product.StreamId(command.VariantSku),
            new ProductFamilyAssigned(command.VariantSku, command.FamilyId, attributes, now));

        // Integration message for downstream BCs (Listings, Storefront).
        yield return new Messages.Contracts.ProductCatalog.ProductVariantAdded(
            command.FamilyId, command.VariantSku, attributes.Values, now);
    }
}
```

> **Architectural note on dual-stream writes:** Appending to both the `ProductFamily` stream and the `Product` stream within a single `IDocumentSession.SaveChangesAsync()` is safe in Marten. Both appends participate in the same PostgreSQL transaction. Either both commit or neither does. This is the correct pattern for bi-directional association events.

---

### 2.3 VariantAttributes Value Object

`VariantAttributes` wraps a string-to-string dictionary representing the differentiating properties of a variant (Size, Color, Scent, Weight, Count, Flavor, etc.). Keys are case-normalized to PascalCase on write. Values are stored as-received (case preserved: "Large" not "large").

```csharp
using System.Collections.Frozen;
using System.Text.Json.Serialization;

/// <summary>
/// Immutable value object representing the differentiating attributes of a Product Variant.
/// E.g., { "Size": "Large", "Color": "Red" } distinguishes DOG-COLLAR-L-RED from DOG-COLLAR-M-BLK.
///
/// Keys are normalized to PascalCase. Values preserve original casing.
/// Equality is structural (two instances with same key-value pairs are equal).
/// </summary>
[JsonConverter(typeof(VariantAttributesJsonConverter))]
public sealed record VariantAttributes
{
    /// <summary>
    /// The underlying attribute map. FrozenDictionary provides O(1) reads and immutability.
    /// </summary>
    public IReadOnlyDictionary<string, string> Values { get; }

    private VariantAttributes(IReadOnlyDictionary<string, string> values)
    {
        Values = values;
    }

    /// <summary>
    /// Factory method — normalizes keys to PascalCase, validates non-empty.
    /// </summary>
    public static VariantAttributes From(IDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0) throw new ArgumentException("VariantAttributes cannot be empty.", nameof(values));

        // Normalize keys: trim whitespace, PascalCase first letter. Filter empty/whitespace keys and values.
        var normalized = values
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(
                kv => NormalizeKey(kv.Key),
                kv => kv.Value.Trim(),
                StringComparer.OrdinalIgnoreCase);

        return new VariantAttributes(normalized.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Convenience indexer — null if key not present.
    /// </summary>
    public string? this[string key] =>
        Values.TryGetValue(key, out var v) ? v : null;

    /// <summary>
    /// Whether this variant contains the given attribute key.
    /// </summary>
    public bool Has(string key) => Values.ContainsKey(key);

    /// <summary>
    /// Human-readable display string for UI. E.g., "Size: Large, Color: Red"
    /// </summary>
    public string ToDisplayString() =>
        string.Join(", ", Values.Select(kv => $"{kv.Key}: {kv.Value}"));

    public static readonly VariantAttributes Empty =
        new(FrozenDictionary<string, string>.Empty);

    private static string NormalizeKey(string key)
    {
        var trimmed = key.Trim();
        // Length <= 1: single char → just uppercase it (trimmed[1..] would be empty anyway, but this is explicit).
        return trimmed.Length <= 1
            ? trimmed.ToUpperInvariant()
            : char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }

    // Record equality is structural via Values comparison (FrozenDictionary implements IEquatable).
    // No additional override needed if using default record equality on the property.
    // However, dictionary equality is reference-based by default — override for value equality:

    public virtual bool Equals(VariantAttributes? other) =>
        other is not null &&
        Values.Count == other.Values.Count &&
        Values.All(kv => other.Values.TryGetValue(kv.Key, out var otherVal) && kv.Value == otherVal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var kv in Values.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            hash.Add(kv.Key, StringComparer.OrdinalIgnoreCase);
            hash.Add(kv.Value);
        }
        return hash.ToHashCode();
    }
}

/// <summary>
/// JSON converter for VariantAttributes — serializes as a flat JSON object.
/// { "Size": "Large", "Color": "Red" } not { "Values": { "Size": "Large" } }.
/// </summary>
public sealed class VariantAttributesJsonConverter : JsonConverter<VariantAttributes>
{
    public override VariantAttributes Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader, options)
            ?? throw new JsonException("JSON input is null or empty when deserializing VariantAttributes. Ensure the JSON property is a non-null object (e.g., { \"Size\": \"Large\" }).");
        return dict.Count == 0 ? VariantAttributes.Empty : VariantAttributes.From(dict);
    }

    public override void Write(
        Utf8JsonWriter writer, VariantAttributes value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value.Values, options);
    }
}
```

---

### 2.4 Domain Events

All domain events are **sealed records** — immutable by construction. No setters, no mutable state.

#### ProductFamily Domain Events (emitted on the ProductFamily stream)

```csharp
namespace ProductCatalog.Families;

/// <summary>
/// Emitted when a new ProductFamily grouping container is created.
/// This is the birth event for the ProductFamily stream.
/// </summary>
public sealed record ProductFamilyCreated(
    Guid FamilyId,
    string Name,
    string Description,
    string? Brand,
    string Category,
    IReadOnlyList<ProductImage> SharedImages,
    string CreatedBy,
    DateTimeOffset CreatedAt);

/// <summary>
/// Emitted when shared content on the ProductFamily is updated.
/// Null fields mean "no change to this field."
/// Downstream: Listings BC may refresh listing content for all variants in this family.
/// </summary>
public sealed record ProductFamilyUpdated(
    Guid FamilyId,
    string? Name,
    string? Description,
    string? Brand,
    string? Category,
    IReadOnlyList<ProductImage>? SharedImages,
    string UpdatedBy,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Emitted on the ProductFamily stream when an existing Product is added as a variant.
/// The variant's SKU is added to VariantSkus on the ProductFamily aggregate.
///
/// IMPORTANT: A corresponding ProductFamilyAssigned event is emitted on the Product's own stream
/// (catalog:{sku}) in the same transaction. Both streams are updated atomically.
/// </summary>
public sealed record ProductVariantAdded(
    Guid FamilyId,
    string VariantSku,
    VariantAttributes Attributes,
    DateTimeOffset AddedAt);

/// <summary>
/// Emitted on the ProductFamily stream when a variant SKU is removed from the family.
/// The product continues to exist as a standalone product; it simply leaves this family.
///
/// A corresponding ProductFamilyUnassigned event is emitted on the Product's stream.
/// </summary>
public sealed record ProductVariantRemoved(
    Guid FamilyId,
    string VariantSku,
    string RemovedBy,
    string? Reason,
    DateTimeOffset RemovedAt);

/// <summary>
/// Emitted when a ProductFamily is archived — typically when all its variants have been
/// Discontinued or Deleted. Archived families are read-only; no new variants can be added.
/// </summary>
public sealed record ProductFamilyArchived(
    Guid FamilyId,
    string ArchivedBy,
    string? Reason,
    DateTimeOffset ArchivedAt);
```

#### Product (Variant) Domain Events (emitted on the existing Product stream)

These are **new events added to the existing Product event stream** at `catalog:{sku}`:

```csharp
namespace ProductCatalog.Products;

/// <summary>
/// Emitted on the Product's stream (catalog:{sku}) when the product joins a ProductFamily.
/// Sets FamilyId and VariantAttributes on the Product aggregate.
///
/// IMPORTANT: Emitted in the same transaction as ProductVariantAdded on the family stream.
/// Downstream BCs should react to the integration message (ProductVariantAdded in Messages.Contracts)
/// rather than these internal domain events.
/// </summary>
public sealed record ProductFamilyAssigned(
    string Sku,
    Guid FamilyId,
    VariantAttributes Attributes,
    DateTimeOffset AssignedAt);

/// <summary>
/// Emitted on the Product's stream when the product leaves its family.
/// Clears FamilyId and VariantAttributes on the Product aggregate.
/// Product becomes a standalone product again.
/// </summary>
public sealed record ProductFamilyUnassigned(
    string Sku,
    Guid PreviousFamilyId,
    string UnassignedBy,
    string? Reason,
    DateTimeOffset UnassignedAt);
```

---

### 2.5 Integration Messages (Messages.Contracts.ProductCatalog)

These messages are added to `src/Shared/Messages.Contracts/ProductCatalog/` and published via RabbitMQ to downstream BCs. They are **integration events** — named after what happened in the source BC, carrying only what downstream consumers need.

```csharp
namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Published when a new ProductFamily is created in the catalog.
/// Downstream: Storefront BC creates a family browse record.
/// Listings BC: family context available for parent listing grouping.
/// </summary>
public sealed record ProductFamilyCreated(
    Guid FamilyId,
    string Name,
    string? Brand,
    string Category,
    DateTimeOffset CreatedAt);

/// <summary>
/// Published when shared content on a ProductFamily changes.
/// Downstream: Storefront refreshes family browse page. Listings BC may refresh listing content.
/// Null fields = not changed in this event (consumers should treat null as "keep existing value").
/// </summary>
public sealed record ProductFamilyUpdated(
    Guid FamilyId,
    string? NewName,
    string? NewCategory,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Published when a Product is added to a ProductFamily as a variant.
/// This is the PRIMARY signal for downstream BCs to model the variant relationship.
///
/// Downstream consumers:
/// - Listings BC: a listing can now be created for this (FamilyId, VariantSku) pair;
///   Listings BC's ProductSummaryView should be extended to include FamilyId.
/// - Storefront BC: add this variant to the family's selector (size/color picker).
/// - Inventory BC: no action needed — already tracks stock per SKU.
/// - Pricing BC: no action needed — already tracks price per SKU.
///
/// Attributes carries the differentiating values (e.g., { "Size": "Large", "Color": "Red" }).
/// Downstream BCs that don't understand VariantAttributes can ignore it; it's informational.
/// </summary>
public sealed record ProductVariantAdded(
    Guid FamilyId,
    string VariantSku,
    IReadOnlyDictionary<string, string> Attributes,
    DateTimeOffset AddedAt);

/// <summary>
/// Published when a variant is removed from a ProductFamily.
/// The product still exists in the catalog; it simply no longer belongs to this family.
///
/// Downstream consumers:
/// - Listings BC: the family-grouped listing on marketplace channels should be updated
///   to remove this variant's child listing from the parent. Active standalone listings
///   for this SKU are unaffected.
/// - Storefront BC: remove this variant from the family's selector.
/// </summary>
public sealed record ProductVariantRemoved(
    Guid FamilyId,
    string VariantSku,
    string? Reason,
    DateTimeOffset RemovedAt);
```

**Exchange routing note:** All four variant integration messages route through the standard `product-catalog` RabbitMQ exchange. No priority queue is needed — variant additions/removals are not time-critical like recall cascades.

---

## 3. Stream Key Design

### Summary Table

| Aggregate | Stream Key Type | Derivation | Example |
|---|---|---|---|
| `ProductFamily` | UUID v7 | `Guid.CreateVersion7()` at creation | `a1b2c3d4-e5f6-7890-...` |
| `Product` (standalone or variant) | UUID v5 | `catalog:{sku.ToUpperInvariant()}` SHA-1 | `3f7a9c12-...` |
| `ProductPrice` (Pricing BC) | UUID v5 | `pricing:{sku.ToUpperInvariant()}` SHA-1 | `8e4b1d56-...` |
| `ProductInventory` (Inventory BC) | MD5* | `{sku}:{warehouseId}` MD5 | `d41d8cd9-...` |
| `Listing` (Listings BC) | UUID v5 | `listing:{sku}:{channelCode}` SHA-1 | `c9f2a8e1-...` |

*The Inventory BC's MD5 pattern is pre-existing technical debt per ADR 0016 — do not replicate.

### ProductFamily Stream Key: Why UUID v7

`ProductFamily` is **not a natural-key singleton**. It has no stable, derivable string identity — unlike `Product` (keyed by SKU) or `ProductPrice` (also keyed by SKU). The family name can change; there is no human-assigned code for a family. Therefore, the family ID is generated once at creation and stored everywhere it is referenced.

This means UUID v7 is correct: the ID is generated once, stored in the `ProductFamilyCreated` event, carried in all commands (`AddVariantToFamily { FamilyId, VariantSku, ... }`), and Wolverine/Marten load the stream directly by this ID. No lookup is needed — the command always carries the `FamilyId`.

```csharp
// Command handler — creating a new ProductFamily.
public static class CreateProductFamilyHandler
{
    public static IStartStream Handle(
        CreateProductFamily command,
        IDocumentSession session)
    {
        // UUID v7 — generated once, immutable forever.
        // NOT UUID v5: family has no stable natural string key to hash.
        var familyId = Guid.CreateVersion7();

        var @event = new ProductFamilyCreated(
            familyId,
            command.Name.Trim(),
            command.Description.Trim(),
            command.Brand?.Trim(),
            command.Category.Trim(),
            command.SharedImages ?? [],
            command.CreatedBy,
            DateTimeOffset.UtcNow);

        // Marten StartStream: creates the stream at familyId with this first event.
        return MartenOps.StartStream<ProductFamily>(familyId, @event);
    }
}
```

### Product Stream Key: UUID v5 (unchanged from Phase 0)

Variant `Product` records continue using the `catalog:{sku}` UUID v5 key established in Phase 0. The variant relationship is added by **appending new event types** (`ProductFamilyAssigned`) to the existing stream — no stream ID changes, no migration.

```csharp
// Existing pattern (unchanged) — all handlers derive this locally, no lookup needed.
public static Guid StreamId(string sku)
{
    // UUID v5 (RFC 4122 §4.3) — deterministic, namespaced SHA-1.
    // See ADR 0016 for full rationale. See ADR 0023 for 'catalog:' namespace prefix.
    //
    // NAMESPACE: RFC 4122 URL namespace UUID (6ba7b810-9dad-11d1-80b4-00c04fd430c8).
    // This is the well-known UUID for the URL namespace defined in RFC 4122 Appendix C.
    // Used here as the base namespace for all CritterSupply UUID v5 derivations.
    // Each BC uses a different string prefix ("catalog:", "pricing:", "listing:") to
    // ensure UUIDs from different BCs are distinct even for the same SKU input.
    var namespaceBytes = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8").ToByteArray();
    var nameBytes = Encoding.UTF8.GetBytes($"catalog:{sku.ToUpperInvariant()}");
    var hash = SHA1.HashData([.. namespaceBytes, .. nameBytes]);
    hash[6] = (byte)((hash[6] & 0x0F) | 0x50);  // Version 5
    hash[8] = (byte)((hash[8] & 0x3F) | 0x80);  // Variant RFC 4122
    return new Guid(hash[..16]);
}
```

### Namespace Collision Safety

No two streams for the same SKU can produce the same UUID, because each BC's `StreamId()` uses a **different namespace prefix string**:

| BC / Purpose | Prefix string hashed | Collision risk with others |
|---|---|---|
| Product Catalog | `catalog:{sku}` | ❌ None — unique prefix |
| Pricing | `pricing:{sku}` | ❌ None — unique prefix |
| Listings | `listing:{sku}:{channelCode}` | ❌ None — composite key |
| Inventory | *(MD5 legacy — not UUID v5)* | ⚠️ Technical debt; can't collide with v5 by construction |
| ProductFamily | *(UUID v7 — no hash)* | ❌ None — random, effectively no collision risk |

---

## 4. Listings BC Impact

**Verdict: The Listing stream key `listing:{sku}:{channelCode}` is confirmed correct under Option A. No changes required.**

Here is the reasoning, stated precisely so there is no ambiguity:

Under Option A, **listings are created against child variant SKUs, not parent family IDs.** A `ProductFamily` is a grouping container for catalog browsing. The unit of commerce — the thing that gets listed on a marketplace channel, gets priced, gets inventoried, gets shipped — is the **variant SKU**.

When a vendor lists "AquaPaws Fountain" on Amazon US, they are actually creating *three* listings: one for `AQUA-FOUNTAIN-SM` (stream: `listing:AQUA-FOUNTAIN-SM:AMAZON_US`), one for `AQUA-FOUNTAIN-MD`, and one for `AQUA-FOUNTAIN-LG`. Amazon's parent ASIN / child ASIN structure is built by the Marketplaces BC when it publishes these three listings to Amazon — the Marketplaces BC knows to group them under one parent ASIN because they share a `FamilyId` in the integration message payload. The **Listings BC does not care about families** — it creates, manages, and terminates listings at the SKU level. Family grouping is the Marketplaces BC's integration concern.

### Listings BC Changes Required for Variant Support

While the stream key is unchanged, the following additions ARE required in Phase 3:

**1. `ProductSummaryView` gains `FamilyId`:**

```csharp
// Updated local read model in Listings BC — Phase 3 addition.
public sealed record ProductSummaryView
{
    public string Id { get; init; } = null!;   // = SKU
    public string Name { get; init; } = null!;
    public string Category { get; init; } = null!;
    public string? Brand { get; init; }
    public ProductStatus CatalogStatus { get; init; }

    // Phase 3 addition: null if standalone product (not a variant).
    public Guid? FamilyId { get; init; }

    // Phase 3 addition: populated if this is a variant.
    // Informational — used by Listings BC to include family context in integration messages
    // sent to Marketplaces BC (so Amazon/Walmart know to group under parent listing).
    public IReadOnlyDictionary<string, string>? VariantAttributes { get; init; }

    public DateTimeOffset LastSyncedAt { get; init; }
}
```

**2. `ListingDraftCreated` carries optional `FamilyId`:**

```csharp
// Phase 3 extension to the Listing domain event.
// FamilyId is denormalized into the listing at creation time for marketplace grouping.
// If null: listing is for a standalone product; no parent grouping on marketplace.
public sealed record ListingDraftCreated(
    Guid ListingId,
    string Sku,
    string ChannelCode,
    Guid? FamilyId,         // Phase 3 addition — null for standalone products
    string CreatedBy,
    DateTimeOffset CreatedAt);
```

**3. `ListingSubmittedToMarketplace` integration message carries `FamilyId`:**

The Marketplaces BC needs `FamilyId` to determine whether to create a child ASIN under an existing parent ASIN (family already has a parent listing on Amazon) or create a new parent + child pair (first variant from this family being listed on this channel).

**What does NOT change:** The stream key, the state machine, the recall cascade, the `ListingsActiveView` projection, or any existing Listings BC behavior. Variant support is purely additive in the Listings BC.

---

## 5. Downstream BC Impact Summary

| Bounded Context | How it uses variants | Impact of Option A | Changes needed? |
|---|---|---|---|
| **Product Catalog** | Owns `ProductFamily` + `Product` (variant) | Gains `ProductFamily` aggregate, `FamilyId?` on `Product`, new domain events | ✅ **Yes** — this is the primary implementation target for Phase 3 |
| **Pricing BC** | Prices per SKU (`ProductPrice` stream = `pricing:{sku}`) | No change — every variant is a distinct SKU with its own price stream. Pricing BC has no concept of family. | ❌ No changes needed |
| **Inventory BC** | Stock per SKU + warehouse (`ProductInventory` stream = `{sku}:{warehouseId}` MD5) | No change — every variant is a distinct SKU with its own stock level. Inventory BC has no concept of family. | ❌ No changes needed |
| **Listings BC** | Listing per (SKU, channelCode) pair | Stream key unchanged. `ProductSummaryView` gains `FamilyId?`. `ListingDraftCreated` event and integration message gain `FamilyId?`. Marketplaces BC uses it for parent/child grouping on marketplace APIs. | ⚠️ **Minor additions** (see §4 above) |
| **Marketplaces BC** | Groups variant listings under one marketplace parent (Amazon parent ASIN) | Marketplaces BC must implement the "family grouping" logic: when `FamilyId` is present, look up or create the parent listing on the external marketplace, then create child listings under it. This is Phase 3 scope. | ✅ **Yes** — new family grouping logic in marketplace adapter implementations |
| **Shopping BC (Cart)** | Adds item to cart by SKU (already per-variant) | No change — cart already tracks by SKU. The storefront UI shows the family selector; once the customer picks Size + Color, it resolves to a SKU. The cart receives the SKU, not the family. | ❌ No changes needed |
| **Orders BC** | Processes order line items by SKU | No change — order lines are per-SKU. | ❌ No changes needed |
| **Storefront BC (Customer Experience)** | Browse by family, add-to-cart by variant SKU | **Primary UI change:** family browse page groups variants under one parent. The `ProductFamilyView` becomes the browse unit; the variant SKU is selected via attribute pickers (Size, Color) before add-to-cart. Storefront BC reacts to `Messages.Contracts.ProductCatalog.ProductVariantAdded` to build the family view. | ✅ **Yes** — family browse page and attribute selector UI |
| **Fulfillment BC** | Ships variant SKU per order line | No change — fulfillment receives the SKU from the order line. Hazmat routing, carrier selection, and label generation are all per-SKU. | ❌ No changes needed |
| **Vendor Portal BC** | Vendor manages their products | Vendors will want to see their products grouped by family in the product management UI. The Vendor Portal currently shows products as a flat list by SKU — Phase 3+ will group them by family. Vendor Portal BC may react to `ProductFamilyCreated` and `ProductVariantAdded` to build a family-aware product list. | ⚠️ **UI enhancement** — Phase 3+ scope |
| **Admin Portal BC** | Catalog manager creates families, adds variants | Gains new UI surface: "Create Product Family" flow and "Add Variant" workflow. This is a Phase 3 implementation deliverable for the Admin Portal. | ✅ **Yes** — new admin UI surfaces |

---

## 6. Phase 3 Implementation Tasks

Phase 3 prerequisite: **Phase 0 (Product Catalog ES migration) and Phase 1 (Listings BC foundation) must be complete.**

D1 is now resolved. The following items from Phase 3 in the evolution plan can be expanded into concrete deliverables:

### 6.1 Product Catalog BC — Variant Model

| Task | Description | Complexity |
|---|---|---|
| `ProductFamily` aggregate | Implement per §2.1 above (events, Apply methods, stream key) | Medium |
| `ProductFamily` Marten registration | Register event types + `ProductFamilyViewProjection` inline projection | Low |
| `ProductFamilyViewProjection` | `SingleStreamProjection<ProductFamilyView>` for admin UI queries; reads family name + variant list | Low |
| `FamilyId?` + `VariantAttributes?` on `Product` | Additive properties + new Apply methods per §2.2 above | Low |
| `VariantAttributes` value object + JSON converter | Per §2.3 above | Low |
| `CreateProductFamily` command + handler + validator | Wolverine handler, FluentValidation rules | Medium |
| `UpdateProductFamily` command + handler + validator | Null-diff updates to shared content | Medium |
| `AddVariantToFamily` command + handler + validator | Dual-stream write per §2.2 handler example | Medium |
| `RemoveVariantFromFamily` command + handler | Reverse of above; product becomes standalone again | Medium |
| `ArchiveProductFamily` command + handler | Terminal state when all variants are discontinued | Low |
| Integration messages | Add `ProductFamilyCreated`, `ProductFamilyUpdated`, `ProductVariantAdded`, `ProductVariantRemoved` to `Messages.Contracts.ProductCatalog` | Low |
| RabbitMQ routing | New messages on standard `product-catalog` exchange | Low |
| Integration tests (Alba + Testcontainers) | Create family → add variants → verify projection + messages | High |
| HTTP endpoints | `POST /families`, `PUT /families/{id}`, `POST /families/{id}/variants`, `DELETE /families/{id}/variants/{sku}` | Medium |
| Admin Portal UI | "Create Family", "Add Variant to Family", "Manage Family" MudBlazor components | High |

### 6.2 Listings BC — Variant-Aware Updates

| Task | Description | Complexity |
|---|---|---|
| `ProductSummaryView` gains `FamilyId?` | React to `Messages.Contracts.ProductCatalog.ProductVariantAdded` | Low |
| `ListingDraftCreated` gains `FamilyId?` | Denormalize from `ProductSummaryView` at draft creation | Low |
| Handler for `ProductVariantAdded` message | Update `ProductSummaryView` when variant is added | Low |
| Handler for `ProductVariantRemoved` message | Update `ProductSummaryView` when variant leaves family | Low |
| Integration tests | Verify `ProductSummaryView` correctly reflects family membership | Medium |

### 6.3 Marketplaces BC — Family Grouping Logic

| Task | Description | Complexity |
|---|---|---|
| Track `FamilyId → ExternalParentListingId` mapping | Document store: when first variant of a family is submitted to Amazon, create parent ASIN; subsequent variants attach to it | High |
| Amazon SP-API: parent ASIN creation | Only when family has no existing parent ASIN on Amazon US | High |
| Amazon SP-API: child ASIN submission | Submit variant with parent ASIN reference | High |
| `MarketplaceParentListingCreated` integration message | Notify Listings BC of the external parent listing ID | Medium |

### 6.4 Storefront BC — Family Browse

| Task | Description | Complexity |
|---|---|---|
| `ProductFamilyView` local projection | React to `ProductFamilyCreated`, `ProductVariantAdded`, `ProductVariantRemoved` | Medium |
| Attribute selector component | Size/Color picker resolves to SKU → routes to add-to-cart by SKU | High |
| Family browse page | Replaces current flat product detail page for variant products | High |

### 6.5 ADRs to Write (Phase 3)

See §7 below for the full ADR additions list.

### 6.6 Testing Strategy

- **Unit:** `ProductFamily` Apply methods + `VariantAttributes` value object (no Marten needed)
- **Integration:** Alba + Testcontainers covering:
  - Create family → add 3 variants → verify `ProductFamilyView` projection
  - Add variant → verify `ProductSummaryView` in Listings BC reflects `FamilyId`
  - Remove variant from family → verify product becomes standalone
  - Archive family → verify no new variants accepted (invariant)
  - Dual-stream write atomicity: verify both `ProductFamily` and `Product` streams updated in same transaction
- **BDD (Reqnroll):** "As a catalog manager, I can create a product family and add size variants" — full Gherkin scenario covering the happy path from HTTP endpoint through projection

---

## 7. ADR Additions

### ADR 0030 — Product Family / Variant Model: Option A (Parent/Child Hierarchy)

**Status:** Required before Phase 3 implementation begins  
**Scope:** Captures the D1 decision rationale, the two-aggregate model, and the dual-stream write pattern

**Draft title:** `0030-product-family-variant-model-option-a.md`

**What it must cover:**

1. **The decision:** Option A (parent/child hierarchy) is chosen. Options B (loosely linked standalone SKUs) and C (embedded variants on a flat product record) are rejected.

2. **Why Option A:** Industry standard alignment (Amazon parent/child ASIN, Shopify Product/Variant, Walmart parent-child item). The Marketplaces BC integration boundary demands this shape — the marketplace APIs speak parent/child natively. Any other model requires an impedance mismatch adapter at the API boundary.

3. **Why Option B was rejected:** A `ProductFamilyId` reference without a `ProductFamily` aggregate means family metadata (shared name, description, brand) has no single authoritative home. Every query that needs "what is this product's family name?" must join across N product records to find the family name — because family name is scattered across all variant records. This is denormalization without an aggregate owner. Also, Option B provides no stream for recording family-level events (category changes, image updates) in a meaningful audit trail.

4. **Why Option C was rejected:** Embedding variants as `IReadOnlyList<ProductVariant>` on the product record violates the invariant that every purchasable unit needs its own dedicated event stream with its own SKU, pricing stream, inventory stream, and listing streams. An embedded list cannot be independently event-sourced per variant. Recall cascade precision (force down only the discontinued variant's listings, not the entire family) is impossible with an embedded model. Option C also collides with the Marten document store model — a document with an embedded list of variants is either a document (lost ES benefits) or an event-sourced aggregate with a coarse event model.

5. **Two-aggregate model:** `ProductFamily` (UUID v7 stream, owns shared content + variant SKU list) + `Product` (UUID v5 `catalog:{sku}` stream, owns variant-specific data + `FamilyId?`). Dual-stream writes are atomic via Marten's `IDocumentSession.SaveChangesAsync()`.

6. **The "Variant is a Product" decision:** No separate `ProductVariant` class. A variant IS a `Product` record with `FamilyId` set. Rationale: avoids parallel class hierarchies; preserves the single `catalog:{sku}` stream per product; additive and non-breaking for existing standalone products.

7. **Consequences:** Listings BC stream key `listing:{sku}:{channelCode}` is confirmed correct. All BCs that reference products by SKU continue unchanged. Storefront BC gains a family browse layer. Marketplaces BC gains family grouping logic.

---

## 8. Outstanding Questions for Owner

The following questions remain open after D1. They are ordered by phase dependency — earlier items block more Phase 3 work.

---

**Q1 — Variant Attribute Schema: Free-form or Constrained?** 🔴 Blocks Admin UI design

> `VariantAttributes` is currently a free-form `Dictionary<string, string>`. This means a catalog manager could create any attribute key they want: "Size", "size", "SIZEEE", "Colour", "Color". This is flexible but produces inconsistent data.
>
> **Option A (recommended):** Free-form, validated only at the UI layer (dropdown of known attribute types: "Size", "Color", "Scent", "Weight", "Count", "Flavor"). The domain accepts any string key — the UI constrains the inputs. Simple to implement; no schema enforcement in the domain.
>
> **Option B:** A `VariantAttributeSchema` owned by Product Catalog BC defines permitted attribute keys per category (e.g., Dog Collars may have "Size" and "Color"; Dog Food may have "Weight" and "Flavor"). Catalog managers configure schemas; variants are validated against their category's schema. More complex; prevents typos; required for robust marketplace attribute mapping.
>
> **Architect's recommendation: Option A for Phase 3 launch, Option B for Phase 3+.** Start free-form with UI constraints. Add schema enforcement once we have real data about which attributes we actually use.
>
> **Owner decision needed:** Is free-form acceptable for launch, or is schema enforcement a launch requirement?

---

**Q2 — Can a Standalone Product Be Added to a Family Post-Hoc?** 🟡 Affects handler invariants

> Can an existing standalone product (already in the catalog, possibly with active listings) be added to a `ProductFamily` at a later date? Or must the product be created into a family at birth?
>
> The current design in §2.2 supports post-hoc family assignment (`AddVariantToFamily` command against an existing product). But this raises a question: if `DOG-BOWL-001` already has 3 active listings on Amazon and Walmart, and we now add it to the "DogPro Bowl" family, what happens to those listings? Do they retroactively inherit the `FamilyId` (and therefore require the Marketplaces BC to group them under a parent ASIN)? Or do existing listings remain standalone until they are ended and re-created?
>
> **Architect's recommendation:** Existing listings retain their current state — no retroactive family grouping. Only *new* listings created *after* the `ProductFamilyAssigned` event will include `FamilyId`. This avoids retroactive Marketplaces BC changes on live listings and keeps the operation safe.
>
> **Owner decision needed:** Confirm this is acceptable. If existing listings must immediately reflect family membership, that is a significantly more complex cascade operation (touching active listing records).

---

**Q3 — Can a Variant Belong to Multiple Families?** 🟢 Low urgency, but affects invariant code

> The current design enforces a one-family-per-variant invariant (a variant can only have one `FamilyId` at a time). Is this correct for CritterSupply? Edge case: a product physically appears in two catalog families (e.g., "Orijen Original" appears in both the "Orijen Dry Food" family and the "Large Breed" family for merchandising purposes)?
>
> **Architect's recommendation:** Reject multi-family membership. One variant, one family. This is how Amazon, Shopify, and Walmart all model it. Multi-family membership creates ambiguity about which family's shared content (name, description) is authoritative for a given listing. The complexity cost is not worth any merchandising flexibility.
>
> **Owner decision needed:** Confirm the one-family-per-variant invariant is correct, or identify specific use cases that require multi-family membership.

---

**Q4 — D2 (Is Own Website a Formal Channel?) — Still open, blocks Phase 1 scope**

> Carried forward from the evolution plan. If `OWN_WEBSITE` is a `ChannelCode`, then the Storefront BC reads listing state from Listings BC to determine product visibility. If not, the Storefront BC reads directly from Product Catalog.
>
> Under Option A, this decision interacts with family browse: if OWN_WEBSITE is a formal channel, should the storefront display *per-variant listings* or should it display the *ProductFamily* directly (bypassing Listings)? These are different browse models.
>
> **Owner decision needed:** D2 resolution is required before Phase 1 scope is finalized. This question's interaction with Option A makes it slightly more urgent than before.

---

**Q5 — Minimum Viable Variant Set: Must all variants exist before family is created, or can a family exist with one variant?**  🟡 Affects Admin UX flow

> Can a `ProductFamily` be created with zero variants (family as an empty shell, populated later)? Or must at least one variant be added atomically with family creation? Or must at least two variants exist before a family is meaningful?
>
> **Architect's recommendation:** Allow a family to be created with zero variants. The catalog manager creates the family, sets shared content, then adds variants one by one. A family with zero variants is valid but cannot have listings created against it (nothing to list). A family with one variant is valid — it represents a product that *might* get siblings later.
>
> **Owner decision needed:** Is a one-variant family a valid long-term state, or is it a transient state that the UI should warn about? Does the Admin UI need a "minimum 2 variants" enforcement?

---

*Questions Q1–Q5 above are the only Owner-input blockers remaining for Phase 3 implementation. D1 is fully resolved. D3, D6, D7, D8, D9, D10 from the evolution plan remain open but do not block variant model implementation — they affect other Phase 3 tracks (marketplace API calls, compliance gates, seasonal scheduling).*

---

*This document supersedes the stub Phase 3 section in `catalog-listings-marketplaces-evolution-plan.md` for all D1-related detail. The evolution plan's phase structure remains authoritative for overall sequencing; this document is the detailed technical specification for the variant model decisions within Phase 3.*
