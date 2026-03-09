# D1 Decision: Variant Model — Principal Architect Sign-Off & Implementation Guide

**Decision:** D1 — Variant Model Shape  
**Resolution:** ✅ Option A — One Parent ProductFamily + N Child Variant SKU Records  
**Date:** 2026-03-09  
**Authors:** Principal Software Architect · Product Owner · UX Engineer  
**Status:** 🟢 Fully Endorsed — All three sign-offs complete. Ready for Phase 3 implementation.

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
9. [Product Owner Sign-Off & Business Validation](#9-product-owner-sign-off--business-validation)
   - [PO Sign-Off Statement](#91-po-sign-off-statement)
   - [Business Rationale](#92-business-rationale)
   - [Catalog Manager Workflow](#93-catalog-manager-workflow)
   - [Impact on Outstanding Decisions](#94-impact-on-outstanding-decisions)
   - [Business Rules for the Variant Model](#95-business-rules-for-the-variant-model)
   - [Glossary Additions and Refinements](#96-glossary-additions-and-refinements)
   - [Open Questions for the Owner](#97-open-questions-for-the-owner)
10. [UX Engineer Sign-Off & UI Design Specification](#10-ux-engineer-sign-off--ui-design-specification)
    - [Sign-Off Statement](#101-sign-off-statement)
    - [Admin UI Design Specification](#102-admin-ui-design-specification)
      - [Product Family Page](#1021-product-family-page)
      - [Variant List Component (MudDataGrid)](#1022-variant-list-component-muddatagrid)
      - [Add Variant Flow](#1023-add-variant-flow)
      - [Variant Attribute Editor](#1024-variant-attribute-editor)
      - [Standalone Products — The No-Family Case](#1025-standalone-products--the-no-family-case)
    - [Storefront / Customer-Facing UX](#103-storefront--customer-facing-ux)
      - [Product Listing Page (PLP)](#1031-product-listing-page-plp)
      - [Product Detail Page (PDP) — Family + Variant Picker](#1032-product-detail-page-pdp--family--variant-picker)
      - [Add to Cart Interaction](#1033-add-to-cart-interaction)
    - [Listing Creation Flow (Now Unblocked by D1)](#104-listing-creation-flow-now-unblocked-by-d1)
      - [Family-First Listing Creation Flow](#1041-family-first-listing-creation-flow)
      - [Marketplace-Specific Variant Grouping UI](#1042-marketplace-specific-variant-grouping-ui)
    - [Domain-vs-UI Divergence Table Additions](#105-domain-vs-ui-divergence-table-additions)
    - [Updated UX Risk Register](#106-updated-ux-risk-register)
    - [UI Copy Additions](#107-ui-copy-additions)
    - [Remaining UX Questions](#108-remaining-ux-questions)

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

---

## 9. Product Owner Sign-Off & Business Validation

**Author:** Product Owner  
**Date:** 2026-03-09  
**Status:** ✅ Business Endorsed — Proceed to Phase 3 implementation

---

### 9.1 PO Sign-Off Statement

I have reviewed the Principal Architect's implementation guide, the full discovery document (`catalog-listings-marketplaces-discovery.md`), and the canonical glossary (`catalog-listings-marketplaces-glossary.md`). I formally endorse **Option A — One Parent ProductFamily + N Child Variant SKU Records** as the correct business decision for CritterSupply.

Erik's reasoning is exactly right, and I want to put it in plain business terms: this is not an experiment. Parent/child product hierarchy is the established, battle-tested pattern used by every major marketplace and every serious e-commerce platform on the planet. Amazon has operated this way for twenty years. Shopify built its entire merchant ecosystem on it. Walmart Marketplace requires it. eBay calls them variations. When we eventually go live with marketplace integrations — and we will — those APIs will expect a parent listing with children underneath it. Building anything else today means paying a rewrite tax the moment we flip on marketplace connectivity. Option A eliminates that debt before it accumulates. There is no competitive advantage in inventing a proprietary variant model for a pet supply retailer; the advantage is in executing well on the patterns the industry has already proven. We are building CritterSupply to be realistic and robust, and this decision reflects exactly that commitment.

From a day-to-day operations standpoint, Option A also produces the clearest, most intuitive experience for the catalog managers who will live inside this system. Every merchandising professional I have worked with across roles in vendor relations, product management, and marketplace channel management thinks in families and variants naturally. "We carry the PetSupreme Nylon Dog Collar in six SKUs" is how buyers talk. "We carry six loosely related collar products" is not. The data model should match the mental model of the people who will use it — and Option A does exactly that.

---

### 9.2 Business Rationale

#### 9.2.1 How Option A Aligns with Amazon, Walmart, eBay, and Shopify

Every major marketplace that CritterSupply would realistically integrate with operates on a native parent/child model. This is not a coincidence — it emerged from the same business reality we face:

**Amazon:** Uses Parent ASIN / Child ASIN structure. The parent ASIN holds shared attributes (title, brand, main description, primary image). Child ASINs each hold differentiating attributes (size, color, scent) and have their own inventory, their own price, and their own Buy Box. When a customer searches for "stainless steel dog bowl," they find one product page — the parent. They select "Large" and the page updates to show the Large child's pricing and stock. Amazon's API (`amazon-sp-api`) literally cannot accept a collection of related products without a parent binding them. If we submitted six collar SKUs without a parent ASIN, Amazon would treat them as six unrelated products with duplicate titles — a listing health violation.

**Walmart Marketplace:** Uses Item Groups (parent-child). The Item Group holds shared content; each member item has its own Item ID and variant data. Walmart's Supplier Center enforces parent-child submission for any product with multiple configurations. Flat submissions of color/size variants are rejected at intake.

**eBay:** Calls the parent a "Multi-Variation Listing" and refers to the differentiating properties as "Variations." An eBay Variation has its own quantity and price but lives under one listing URL. Customers see a single listing with a picker; eBay's data model underneath is structurally identical to Option A.

**Shopify:** Its native data model is `Product` (the family) and `ProductVariant` (the child). Every Shopify storefront, every Shopify app, every Shopify API integration is built around this shape. The Storefront API, the Admin API, the Webhooks — all speak parent/child. If CritterSupply ever runs a Shopify sales channel, Option A maps directly to Shopify's model with zero transformation.

The practical consequence: Option A means our internal catalog model and every external marketplace API speak the same language. No translation layer, no mapping tax, no "adapter that converts our flat model into their hierarchical model." The integration boundary is clean because the shapes match.

#### 9.2.2 Why Option B (Flat Products with FamilyId Reference) Creates Catalog Manager Pain at Scale

Option B — standalone product records loosely coupled by a shared `FamilyId` field — feels simple at implementation time and becomes painful to operate at catalog scale.

The central problem is **orphaned metadata**. In Option B, there is no `ProductFamily` aggregate that owns the shared display content. That means the family name, description, brand, and images have to live on every variant record simultaneously — and stay in sync. When a copywriter updates the product description for "CritterBrew Stainless Steel Dog Bowl," they update the `Description` field on `DOG-BOWL-S-001`, `DOG-BOWL-M-001`, and `DOG-BOWL-L-001` separately, or we build a "sync all siblings" batch operation that is itself a source of bugs and inconsistencies. I have seen this exact problem at two shops: you end up with three variants of the same product that have three slightly different descriptions because one was updated and the others were not. That inconsistency surfaces to customers on the marketplace and creates listing quality violations.

In Option B, there is also no authoritative event trail for family-level changes. If we want to know "when was the CritterBrew Bowl family renamed and who approved it?" — there is no stream to answer that question. Events are per-variant. We have lost the audit history at the family level.

At scale — say, a catalog of 5,000 SKUs across 1,200 families — Option B becomes operationally untenable. Bulk category reassignment ("move all dog bowls from `Dogs > Feeding` to `Dogs > Feeding > Bowls & Feeders`") requires touching every individual SKU record instead of updating one `ProductFamily` record. Any cross-variant report ("show me all families that have a variant with no active Amazon listing") requires a join that Option A makes trivial.

Option B is the model you build when you assume you will never have more than a few dozen products. We are building for realistic scale.

#### 9.2.3 Why Option C (Embedded Variants) Makes Marketplace Submission Structurally Harder

Option C — embedding variants as a list inside a single product document — is attractive in a simple read world and breaks down badly in a write world.

The fundamental problem is **independence**. Each variant SKU must be independently listable, independently priceable, independently inventoried, and independently recallable. If `DOG-COLLAR-L-RED` is involved in a safety recall but `DOG-COLLAR-S-BLACK` is not, we need to force down the Large Red listing on Amazon while leaving the Small Black listing live. With Option C, the entire "product" document is either live or not — there is no independent lifecycle per variant. A recall becomes an all-or-nothing operation that either harms inventory unnecessarily (taking down safe variants) or requires a complex extraction from an embedded list (which defeats the point of embedding).

At marketplace submission time, Option C creates a second problem: the marketplace API expects to receive child items as independent submissions with their own identifiers. The Amazon SP-API call to create a child ASIN requires a separate API call per child, with each child's attribute set submitted individually. If our internal model has variants embedded in a parent document, we have to extract them, iterate over them, and submit each individually anyway — which means we are doing the parent/child decomposition at the API boundary regardless. Option A just makes that decomposition explicit and natural.

There is also a practical Marten concern (my architect would express this more precisely than I can): an event-sourced aggregate with an embedded list of sub-items cannot give each sub-item its own audit trail. Every change to any variant — a price update, a status change, an image swap — hits the same parent stream. The event stream for a high-volume product family becomes a noise-filled log of mixed signals from different variants. Untangling "what changed on the Large Red SKU specifically" requires filtering events by attribute, which is fragile. Option A gives every variant its own clean `catalog:{sku}` event stream.

#### 9.2.4 Concrete Pet Supply Examples

To make this concrete, here is how Option A applies to real product types we will carry:

**Dog Collars — Size × Color:**
- Family: "PetSupreme Nylon Dog Collar" (`FAMILY-ID-001`)
- Variants: `DOG-COLLAR-S-BLK`, `DOG-COLLAR-M-BLK`, `DOG-COLLAR-L-BLK`, `DOG-COLLAR-S-RED`, `DOG-COLLAR-M-RED`, `DOG-COLLAR-L-RED`
- Family owns: name ("PetSupreme Nylon Dog Collar"), brand ("PetSupreme"), category (`Dogs > Collars & Leashes > Collars`), shared description ("Durable nylon construction, brass hardware..."), lifestyle images
- Variant owns: size attribute ("Small"), color attribute ("Black"), variant-specific image (black collar swatch), SKU, individual price, individual inventory

**Fountain/Bowl — Size Only:**
- Family: "AquaPaws Circulating Pet Fountain" (`FAMILY-ID-002`)
- Variants: `FOUNTAIN-SM-001` (1.5L), `FOUNTAIN-MD-001` (2.5L), `FOUNTAIN-LG-001` (4L)
- Family owns: brand ("AquaPaws"), shared description, how-it-works images, category (`Dogs > Bowls & Feeders > Water Fountains`)
- Variant owns: size ("1.5L"), weight (critical for hazmat/shipping calc), variant-specific product shot, price, inventory

**Dry Dog Food — Weight:**
- Family: "Orijen Original Dry Dog Food" (`FAMILY-ID-003`)
- Variants: `ORIJEN-OG-4LB`, `ORIJEN-OG-13LB`, `ORIJEN-OG-25LB`
- Family owns: brand ("Orijen"), AAFCO compliance text (applies to all sizes), ingredient list (same formulation), category (`Dogs > Food > Dry Food > All Breeds`)
- Variant owns: weight ("4 lb"), actual ship weight (critical for carrier rate calculation), barcode/UPC (each size has its own), price, inventory

**Flea Treatment — Species + Dosage:**
- Family: "Frontline Plus Flea & Tick Treatment" (`FAMILY-ID-004`)
- Variants: `FLEA-TX-SDOG-001` (Small dog, 5-22 lbs, 3-count), `FLEA-TX-MDOG-001` (Medium dog, 23-44 lbs, 3-count), `FLEA-TX-LDOG-001` (Large dog, 45-88 lbs, 3-count), `FLEA-TX-CAT-001` (Cats 1.5 lbs+, 3-count)
- Note: `IsHazmat: true` on all variants; the compliance metadata is variant-level because dosage-specific regulatory filings are per-SKU. The hazmat warning applies at the family level too, but the specific regulatory submission (EPA registration number) is per-variant.

**Holiday/Seasonal — Single Variant (Family with One Child):**
- Family: "CritterSupply Holiday Advent Calendar — Dogs" (`FAMILY-ID-005`)
- Variants: `ADV-CAL-DOG-2026` (one SKU; no size or color)
- This is a valid single-variant family. It was a single standalone product in Cycle 1; it becomes a family with one child because *next year* it will be `ADV-CAL-DOG-2027` — a new variant added to the same family. The family allows catalog managers to group year-over-year editions without creating unrelated products.

This last example illustrates an important business point: **a family with one variant is not a degenerate case**. It is forward-looking. We create the family at product launch even if today there is only one SKU, because we know variants may be added later. The structure allows expansion without catalog reorganization.

---

### 9.3 Catalog Manager Workflow

This section describes the end-to-end business workflow for catalog managers when creating and managing product families. These workflows will drive the Admin Portal UX requirements in Phase 3.

#### 9.3.1 Creating a New Product Family

**Scenario:** A buyer has negotiated a new private-label stainless steel dog bowl with our contract manufacturer. It will come in Small (24oz), Medium (48oz), and Large (64oz). The buyer hands a product brief to the catalog manager.

**Step 1 — Create the Product Family**

The catalog manager opens the Admin Portal, navigates to **Product Catalog > Families > Create New Family**, and fills in the family-level details:

| Field | Example Value | Notes |
|---|---|---|
| Family Name | CritterBrew Stainless Steel Dog Bowl | Required. Shared across all variants. |
| Brand | CritterBrew | Our private label brand. |
| Category | Dogs > Bowls & Feeders > Stainless Steel Bowls | Drives marketplace attribute schema |
| Base Description | Premium 18/8 stainless steel, dishwasher safe, non-slip silicone base... | Shared description; variant pages may extend it |
| Shared Images | [lifestyle-bowl-photo.jpg, detail-interior.jpg, non-slip-base.jpg] | Images that appear for all variants before a variant-specific image is selected |

The catalog manager does **not** enter pricing, inventory, or SKUs at this step. Those belong to the variants.

Clicking **Save** creates the `ProductFamily` record. The family is in **Draft** state (no variants, no listings, not visible anywhere). The system generates a UUID v7 `FamilyId`. The catalog manager receives a confirmation: "CritterBrew Stainless Steel Dog Bowl family created. Add at least one variant to make this family listable."

**Step 2 — Add Variants**

The catalog manager clicks **Add Variant** within the newly created family. For each size, they fill in the variant-level form:

| Field | Small | Medium | Large | Notes |
|---|---|---|---|---|
| SKU | CRBW-BOWL-S-001 | CRBW-BOWL-M-001 | CRBW-BOWL-L-001 | Human-readable, stable, immutable |
| Size | Small | Medium | Large | Variant attribute |
| Capacity | 24 oz | 48 oz | 64 oz | Second attribute for this family |
| Dimensions | 4.5" W × 2" H | 6" W × 2.5" H | 7.5" W × 3" H | Shipping/display |
| Ship Weight | 0.6 lbs | 0.9 lbs | 1.2 lbs | Critical for carrier rate calculation |
| Variant Image | [bowl-small.jpg] | [bowl-medium.jpg] | [bowl-large.jpg] | Size-specific product shot |
| Status | Active | Active | Active | All three ready to sell |

Each variant created fires a `ProductCreated` event (if the SKU is new to the catalog) or a `ProductVariantAdded` event (if the SKU existed as a standalone product being adopted into the family). The variant's stream is `catalog:CRBW-BOWL-S-001`.

**Step 3 — Review the Family**

Once all three variants are added, the catalog manager sees the family summary view:

```
CritterBrew Stainless Steel Dog Bowl
Brand: CritterBrew | Category: Dogs > Bowls & Feeders > Stainless Steel Bowls
Status: Active | Variants: 3

  SKU               Size    Capacity  Status   Listings
  CRBW-BOWL-S-001   Small   24 oz     Active   No active listings
  CRBW-BOWL-M-001   Medium  48 oz     Active   No active listings
  CRBW-BOWL-L-001   Large   64 oz     Active   No active listings
```

The family is now listable. The catalog manager can hand off to the merchandising team to create listings on Amazon, Walmart, or OWN_WEBSITE. Pricing and inventory are entered separately through their respective BCs.

#### 9.3.2 Adding a New Variant to an Existing Family

**Scenario:** Six months after launch, our buyer adds an XL size (96oz) for working dog breeds. The product is ready to sell.

The catalog manager opens the existing "CritterBrew Stainless Steel Dog Bowl" family and clicks **Add Variant**. They enter:

| Field | Value |
|---|---|
| SKU | CRBW-BOWL-XL-001 |
| Size | XL |
| Capacity | 96 oz |
| Ship Weight | 1.8 lbs |
| Variant Image | [bowl-xl.jpg] |
| Status | Active |

The system appends `ProductVariantAdded` to the `ProductFamily` stream and creates `catalog:CRBW-BOWL-XL-001` as a new Product stream. The `ProductVariantAdded` integration message is published via RabbitMQ. Downstream reactions:

- **Listings BC:** Updates `ProductSummaryView` to reflect the new variant's `FamilyId`. The XL variant is now available for listing.
- **Marketplaces BC:** If the family already has an active Amazon listing (parent ASIN exists), the Marketplaces BC knows this new variant should be submitted as a new child ASIN under the existing parent. It places the new child into a "Pending Submission" state for catalog manager review before live submission.
- **Storefront BC:** Updates the `ProductFamilyView` to add the XL size to the attribute picker on the bowl's browse page.

The catalog manager does not touch Amazon directly. The event cascade handles cross-BC propagation. This is the operational value of Option A — adding one SKU to the catalog automatically surfaces it across all downstream systems that care about it.

#### 9.3.3 Updating Shared Family Content

**Scenario:** The copywriter has improved the base description with new marketing copy. This change should apply to all variants across all channels.

The catalog manager opens the family, clicks **Edit Family**, and updates the `Description` field. This fires `ProductFamilyContentUpdated` on the family stream. The integration message propagates to:

- **Listings BC:** Queues a "content refresh needed" flag on all active listings for this family. Catalog managers review and re-submit to marketplace channels with the new copy.
- **Storefront BC:** Updates the family browse page description immediately (reads from the family view).

One edit, propagated to all three SKUs and all active channels. This is the content efficiency gain that Option B fundamentally cannot match.

#### 9.3.4 Data That Lives on the Family vs. the Variant

This is the clearest way to communicate the model to catalog managers, buyers, and copywriters:

**Lives on the Product Family (shared, authoritative for all variants):**
- Family display name (e.g., "CritterBrew Stainless Steel Dog Bowl")
- Brand (e.g., "CritterBrew")
- Category assignment (e.g., `Dogs > Bowls & Feeders > Stainless Steel Bowls`)
- Base description / long-form copy
- Shared images (lifestyle, detail, packaging — images that are accurate for any variant)
- Material / construction notes that apply universally (e.g., "18/8 stainless steel, BPA-free")
- Family-level compliance notes (e.g., "California Prop 65 warning applies to all variants")
- SEO keywords (shared base; channel-specific refinements live in Listings BC)

**Lives on the Product Variant (specific to one SKU):**
- SKU (immutable, human-readable, the identifier everything else points to)
- Variant attributes — the differentiating properties (Size, Color, Scent, Weight, Flavor, Count, etc.)
- Variant-specific images (e.g., the color swatch for "Red," the size-labeled product shot for "Large")
- Ship weight and dimensions (vary per size; used for carrier rate calculation)
- UPC / GTIN / EAN barcode (each variant has its own registered barcode)
- Product Status (`Active`, `OutOfSeason`, `Discontinued`, `Recalled`) — independently managed per SKU
- Compliance metadata specific to this variant (e.g., EPA registration number for a specific flea treatment dosage)

**Lives in other BCs (never on the catalog record):**
- Price (Pricing BC — per SKU)
- Inventory / stock level (Inventory BC — per SKU + warehouse)
- Listing state on a channel (Listings BC — per SKU + channel code)
- Channel-specific title / bullets / A+ content (Listings BC — marketplace copy is separate from catalog copy)

---

### 9.4 Impact on Outstanding Decisions

With D1 resolved, several of the remaining open decisions (D2, D3, D8, D9, and others) are now better scoped. Here is my assessment of each, now that we know the shape of the variant model:

#### D2 — Is OWN_WEBSITE a Formal Channel in the Listings Model?

**D1 Impact:** Under Option A, the storefront browse experience is family-centric — customers browse by family and select attributes to resolve a specific variant SKU. This is categorically different from a single-product page. If OWN_WEBSITE is a formal Listings BC channel, then:
- A Listing exists per (variant SKU, `OWN_WEBSITE`) pair
- The storefront reads listing state to determine visibility (a variant is visible on the website if its `OWN_WEBSITE` listing is `Live`)
- The storefront's family browse page renders based on which variant listings are live, not raw product status

If OWN_WEBSITE is not a formal channel, the storefront reads directly from Product Catalog BC and all `Active` variants in a family are visible on the site. The family browse page is simpler to build but we lose the per-variant channel control.

**My PO assessment:** I lean toward OWN_WEBSITE being a formal channel in the Listings model. The reason: it gives catalog managers a single, consistent mental model. "A product is live on a channel when its listing on that channel is Live." The alternative — where Amazon/Walmart/eBay work through listings but the website bypasses that — creates a two-system problem for catalog managers. They have to remember that website visibility works differently. The consistency gain is worth the upfront complexity. **Recommendation: OWN_WEBSITE = formal Listings channel. Owner decision needed to confirm.**

#### D3 — Does Listings BC Own Marketplace API Calls?

**D1 Impact:** Under Option A, when the first variant of a family is submitted to Amazon, we need to create both a parent ASIN and a child ASIN in a coordinated sequence. When subsequent siblings are submitted, we need to attach them as children to the already-created parent ASIN. This coordination logic — "does a parent ASIN already exist for this family on this channel, and if so, what is its external ID?" — is non-trivial.

If Listings BC owns the API calls directly, it must maintain a `FamilyId → ExternalParentListingId` lookup per channel. Every time a listing is submitted, Listings BC checks the lookup, conditionally creates the parent first, then submits the child. Listings BC becomes tightly coupled to the specifics of each marketplace's parent/child API.

If there is a separate Marketplaces BC (adapter layer), the family grouping logic lives there — Listings BC sends a `ListingSubmittedToMarketplace` message with the variant SKU and `FamilyId`, and Marketplaces BC handles the coordination of parent vs. child creation per channel. This is cleaner: Listings BC knows nothing about how Amazon structures parent ASINs; Marketplaces BC encapsulates that complexity.

**My PO assessment:** The adapter layer (Marketplaces BC as a distinct context) is the right call, and D1 strengthens that argument. The parent ASIN coordination logic is specific to Amazon's data model. Walmart has its own Item Group logic. eBay has Multi-Variation Listing logic. None of these are the same. Putting all of that in Listings BC means Listings BC needs to know Amazon-specific, Walmart-specific, and eBay-specific grouping rules — a clear violation of bounded context discipline. **Recommendation: Separate Marketplaces BC as adapter layer. Listings BC publishes intent; Marketplaces BC handles API specifics. Owner decision needed to confirm.**

#### D8 — Compliance Metadata Required at Listings Launch or Deferrable?

**D1 Impact:** Under Option A, compliance metadata will have a dual-level structure:
- **Family-level compliance:** Does this product class carry a Prop 65 warning? Is any variant of this family a regulated chemical? This can be a flag on the `ProductFamily` record.
- **Variant-level compliance:** What is the specific EPA registration number for this flea treatment dosage? What is the GTIN for this bag weight?

This means the compliance metadata design must account for both levels of the hierarchy. If we defer compliance metadata, we defer it at both the family and variant level — and we accept that Amazon and Walmart will reject any listings that require hazmat classification (which includes all flea/tick treatments, some grooming products with alcohol bases, and pet medications).

**My PO assessment:** Compliance metadata cannot be fully deferred if we intend to list anything in the health and wellness category. However, it does not need to be enforced for purely non-hazardous categories (collars, bowls, dry food, non-medicated grooming). **Recommendation: Implement `IsHazmat` and `HazmatClass` at the variant level at Listings BC launch; defer full Prop 65 and state restriction metadata to Phase 3+. Gate Amazon/Walmart submission of hazmat SKUs behind a compliance-complete check. Owner decision needed on which product categories we plan to activate at Phase 1 launch — that determines the urgency.**

#### D9 — Automated Seasonal Reactivation vs. Manual?

**D1 Impact:** Under Option A, seasonal reactivation operates at the **variant level**. If "Snowflake Holiday Cat Stocking" (`CAT-STKG-012`) is `OutOfSeason`, and reactivation is automated on October 1, the system fires `ProductActivated` on the variant's stream — not the family stream. The family itself does not have a status; individual variants do.

This creates a workflow question: when a catalog manager sets a variant to `OutOfSeason`, should they also be able to set a `PlannedReactivationDate` at the same time? Or is reactivation always a manual action that the catalog manager must remember to take?

**My PO assessment:** For Phase 3 launch, **manual reactivation is acceptable** — it forces a catalog manager to confirm the product is ready before it goes live again, which is a healthy operational gate. However, I strongly recommend building the `PlannedReactivationDate` field on the variant record now, even if we do not implement the automated job yet. Storing the intent gives us a dashboard view ("variants scheduled to reactivate in October") and lays the groundwork for automation in Phase 3+. **Recommendation: Manual reactivation at launch; store `PlannedReactivationDate` on variant for operational visibility. Automate in Phase 3+. Owner confirmation needed.**

#### D4 — Is a Marketplace an Aggregate or Configuration?

**D1 Impact:** Under Option A, a marketplace needs to know about the family grouping concept — specifically, whether it supports parent/child structures (Amazon: yes, Walmart: yes, eBay: yes for multi-variation listings, our own website: handled differently). If Marketplace is an aggregate, it can carry this `SupportsParentChildGrouping: true/false` flag as a managed attribute. If Marketplace is a configuration enum, we hardcode it.

**My PO assessment:** For Phase 3, a lightweight aggregate (document store, not event-sourced) is the right answer. We need to store per-marketplace configuration including parent/child support, credential references, and API endpoint metadata — and that collection will grow as we add marketplaces. An enum breaks every time we add a marketplace. **Recommendation: Marketplace as a lightweight document aggregate in Marketplaces BC. Owner decision needed to confirm scope.**

#### D5 — Does Category-to-Marketplace Mapping Live in Product Catalog BC or Marketplaces BC?

**D1 Impact:** Under Option A, the `ProductFamily` owns the internal category. When a family's listings are submitted to Amazon, the Marketplaces BC must map `Dogs > Bowls & Feeders > Stainless Steel Bowls` → Amazon browse node `2975312011`. This mapping is marketplace-specific knowledge. It does not belong in Product Catalog BC, which knows nothing about Amazon's taxonomy.

**My PO assessment:** Category-to-marketplace mapping belongs in **Marketplaces BC**. It is inherently marketplace-specific. Product Catalog BC owns the CritterSupply internal category tree; Marketplaces BC owns the translation from that tree to each marketplace's taxonomy. The separation keeps Product Catalog clean and lets us update marketplace mappings without touching the catalog. **Recommendation: Mappings in Marketplaces BC. This aligns with the Principal Architect's existing position.**

#### D6 — Credentials Management (Marketplaces BC or Infrastructure Vault?)

**D1 Impact:** D1 does not materially change this decision's analysis. However, Option A does surface one related concern: marketplace credentials are now more operationally critical because the parent/child creation sequence requires two sequential authenticated API calls. A credential failure mid-sequence (parent created, child submission fails due to credential expiry) creates a partial state that must be detected and compensated. Credentials must be reliable.

**My PO assessment:** **API credentials belong in infrastructure secrets management (Vault / AWS Secrets Manager / Azure Key Vault), not in Marketplaces BC.** Credentials are a security concern, not a domain concern. Marketplaces BC reads credentials at submission time from a secrets reference; it does not store secret values in its own database. **Owner confirmation needed on which secrets management system is in scope for CritterSupply's infrastructure.**

---

### 9.5 Business Rules for the Variant Model

The following business rules govern the family/variant model from an operations perspective. These should be encoded as domain invariants and enforced in command handlers.

#### Rule 1 — Can a Variant Exist Without a Family?

**Yes.** A variant (Product record) may exist without a `FamilyId`. This represents a **standalone product** — a product that has no siblings and does not belong to a family grouping. Every existing product in the catalog before the variant model is introduced is a standalone product. They remain valid indefinitely without being forced into a family.

**Business rationale:** Not every product has variants. "CritterSupply XL Premium Dog Kennel" may be a single-SKU product with no size variants. Forcing it into a family creates unnecessary overhead. Standalone SKUs list on Amazon as single products (no parent ASIN). Standalone products are first-class catalog citizens.

**Operational note:** The Admin Portal should make standalone products clearly distinguishable from family members — a visual indicator like "Standalone Product (no family)" prevents catalog managers from thinking a product is "broken" because it has no family link.

#### Rule 2 — Can a Family Have Only One Variant?

**Yes.** A `ProductFamily` with exactly one variant is a valid and intentional state. This most commonly represents:

- A single-SKU product that **might** gain siblings in the future (e.g., the advent calendar example above — current year's edition, with future years expected)
- A product where the catalog manager created the family structure in anticipation of variants that have not yet been sourced (e.g., "we know a green colorway is coming Q3")
- A product transitioning from standalone to family (the original SKU is the first member; more are expected)

The Admin Portal should display a **soft warning** ("This family has only one variant — is this intentional?") but must not block listing or publishing. A one-variant family is not an error state.

**Hard rule:** A family must have at least one variant to have any listings created against it. An **empty family** (zero variants) cannot have listings. This is enforced by Listings BC at draft creation time: `FamilyId` must resolve to a family with at least one `Active` variant.

#### Rule 3 — What Happens to a Family When All Variants Are Discontinued?

When every variant in a family reaches `Discontinued` status, the family itself transitions to an **Archived** state automatically (system event, not manual). The family record is never deleted — it is retained for historical integrity and audit trail. Archived families:
- Do not appear in active catalog views (filtered out by default)
- Cannot accept new variants (an Archived family is a terminal state for the family grouping itself)
- Retain all historical events, listings, and order history referencing their variant SKUs
- Can be surfaced via an "Archived families" admin view for historical lookup

**Business rationale:** We never delete product records. Orders placed five years ago reference SKUs that must remain resolvable. An archived family + discontinued variants preserve full historical integrity without polluting the active catalog view.

**Edge case — Recalled families:** If a family has one or more `Recalled` variants and the remaining variants are `Active`, the family does not archive. The active variants continue to be managed normally. If all variants are eventually `Recalled` or `Discontinued`, the family archives. Recall state on a variant is a permanent terminal state; the SKU can never return to `Active`.

#### Rule 4 — Can a Variant Belong to More Than One Family?

**No.** A variant (Product record) may belong to exactly one `ProductFamily` at a time, or no family at all (standalone). This is a hard invariant enforced at the domain level.

**Business rationale:** Multi-family membership creates ambiguity about which family's shared content is authoritative. If `DOG-COLLAR-M-BLK` belongs to both "PetSupreme Nylon Dog Collar" and "PetSupreme All-Weather Collar Collection," which family name appears on its Amazon listing? Which family's description does the storefront inherit? The ambiguity is irresolvable without a priority system, which defeats the simplicity of the model. More importantly, Amazon, Walmart, and eBay all enforce single parent ASIN per child — a product cannot appear under two Amazon parent ASINs simultaneously.

**Operational escape valve:** If a variant genuinely needs to appear in two contexts (a rare merchandising need), the correct solution is to create a separate "collection" or "bundle" as a Listings BC concern — not a Product Catalog family membership change. The catalog stays clean; the channel-specific grouping logic lives in Listings/Marketplaces.

#### Rule 5 — Can a Standalone Product Be Added to a Family Post-Hoc?

**Yes, with constraints.** An existing standalone product may be assigned to a `ProductFamily` at a later date via the `AddVariantToFamily` command. This is a common operational scenario: a product launches as standalone, then the buyer adds a second size, and the catalog manager retroactively groups both under a family.

**Constraints:**
- **Existing listings are not retroactively affected.** If `DOG-BOWL-001` has three active Amazon listings (submitted as a standalone product, no parent ASIN), those listings retain their current state. They are not automatically reorganized under a parent ASIN just because the product joined a family. New listings created *after* the family assignment will use the family grouping.
- **Catalog manager must review active listings** after a family assignment and decide whether to delist the old standalone listing and re-list as a child under the family's parent ASIN. This is a manual merchandising decision, not an automated cascade.
- The Admin Portal should surface a **post-assignment notice:** "DOG-BOWL-001 has 3 active listings as a standalone product. Review and update listings to reflect the new family grouping."

#### Rule 6 — Who Can Create, Edit, and Archive Variants?

| Action | Permitted Roles | Notes |
|---|---|---|
| Create Product Family | MerchandisingManager, SystemAdmin | Requires category assignment (buyer input) |
| Add Variant to Family | MerchandisingManager, SystemAdmin | Requires SKU, at least one variant attribute |
| Edit Family Shared Content | MerchandisingManager, CopyWriter, SystemAdmin | CopyWriter edits description/images only; cannot change category or archive |
| Edit Variant Attributes | MerchandisingManager, SystemAdmin | Variant attribute changes may invalidate active listings; system warns |
| Set Variant Status (Active / OutOfSeason / ComingSoon) | MerchandisingManager, SystemAdmin | |
| Discontinue a Variant | MerchandisingManager, SystemAdmin | Requires confirmation; triggers cascade review of active listings |
| Initiate Recall | ComplianceOfficer, SystemAdmin | Emergency action; bypass standard review; immediate cascade |
| Archive Product Family | SystemAdmin | Typically triggered automatically when all variants are discontinued |
| Remove Variant from Family | MerchandisingManager, SystemAdmin | Returns variant to standalone status; does not delete it |

**Note:** Vendor Portal users (vendor-side) may submit product data for catalog manager review, but they do not have direct write access to create families or assign variants. Vendor submissions enter a review queue; a MerchandisingManager approves or rejects.

#### Rule 7 — Is There a Maximum Number of Variants Per Family?

**No hard system limit at this time.** In practice, e-commerce catalogs rarely exceed 100 variants per family (most fall under 20). A dog collar in 5 sizes × 10 colors = 50 variants, which is realistic. A pet food in 6 bag sizes × 3 protein sources = 18 variants.

**Soft operational guidance:** Families with more than 50 variants should trigger a catalog manager review — this often indicates a family that should be split into sub-families (e.g., "Chicken Recipe" family and "Salmon Recipe" family as separate groupings rather than one giant multi-protein/multi-size family). The Admin Portal may surface this as an advisory: "This family has 52 variants. Consider splitting by primary attribute for clearer customer navigation."

**Marketplace reality check:** Amazon limits the number of child ASINs under a single parent ASIN to a practical maximum (varies by category, typically 50-200). For families that would exceed marketplace limits, Marketplaces BC must handle the splitting logic — this is a Phase 3+ concern.

#### Rule 8 — What Variant Attributes Are Supported — Free-Form or Constrained?

For **Phase 3 launch**, variant attributes will be **free-form key-value pairs** (`Dictionary<string, string>`) validated at the UI layer rather than enforced by domain schema. The Admin Portal will present a **dropdown of recommended attribute keys** based on the product's category to guide consistent data entry:

| Category | Suggested Attribute Keys |
|---|---|
| Collars, Harnesses, Leashes | Size, Color |
| Bowls & Feeders | Size, Capacity |
| Dry / Wet Food | Weight, Flavor/Protein Source, Life Stage |
| Treats & Chews | Weight, Flavor |
| Flea & Tick / Medications | Pet Size/Weight Range, Count |
| Toys | Size, Color, Material |
| Beds & Furniture | Size, Color |
| Grooming | Size, Scent/Formula |

The domain accepts any string key — the UI dropdown constrains the inputs. A catalog manager who types a custom key (e.g., "Texture") is permitted; the system will not reject it. This avoids domain-layer brittleness while keeping practical catalog data clean.

**Phase 3+ aspiration:** Introduce a `VariantAttributeSchema` owned by Product Catalog BC, defining permitted attribute keys per category node. This enables validation of marketplace attribute mapping and catches catalog manager typos at submission time. Until then, UI guidance is the gate.

---

### 9.6 Glossary Additions and Refinements

The following terms should be added to `catalog-listings-marketplaces-glossary.md` now that the D1 model is confirmed. These are business-operational terms that catalog managers and buyers will use daily.

---

#### Standalone Product

**Definition:** A Product record that does not belong to any Product Family. A standalone product has no `FamilyId` and no variant siblings. It is a single purchasable unit in the catalog. Standalone products are valid catalog citizens; not every product requires a family grouping. On marketplaces, a standalone product is submitted as a single listing with no parent/child structure.

**Canonical examples:** A one-size-only luxury dog crate; a single-edition holiday item; an accessory that has no meaningful variant dimensions.

**Code note:** `FamilyId == null` on the `Product` record. No change to SKU, stream key, or listing behavior from a standalone product's perspective.

**Aliases / Rejected Terms:**
- ~~Orphan product~~ — rejected; "orphan" implies something broken or unwanted. A standalone product is intentional.
- ~~Single SKU product~~ — acceptable informally, but "Standalone Product" is the canonical term.

---

#### Family Archive

**Definition:** The terminal state of a Product Family when all of its member variants have been discontinued or recalled. An archived family retains its full event history and is never deleted, but it no longer appears in active catalog management views. No new variants may be added to an archived family; no new listings may be created for its variants (which are themselves in terminal status).

**Business significance:** Family archive is a system-triggered transition, not a manual action. It fires automatically when the last active variant in a family reaches a terminal status (`Discontinued` or `Recalled`). It is not a user-initiated archival action.

**Code note:** `ProductFamilyArchived` domain event; `Status: Archived` on the `ProductFamily` aggregate.

---

#### Variant Attribute

**Definition:** A key-value property that distinguishes one Product Variant from its siblings within the same Product Family. Each variant carries one or more variant attributes that encode the dimension(s) along which variants differ: Size, Color, Scent, Weight, Flavor, Count, or any other differentiating property. The combination of attribute values for a variant must be unique within its family — no two variants in the same family may have identical attribute sets.

**Examples:**
- `{ "Size": "Large", "Color": "Red" }` on `DOG-COLLAR-L-RED`
- `{ "Weight": "25 lb" }` on `ORIJEN-OG-25LB`
- `{ "Size": "Large Dog (45-88 lbs)", "Count": "3" }` on `FLEA-TX-LDOG-001`

**Business significance:** Variant attributes are the customer-facing selectors on a product page. "Choose your size" and "Choose your color" each correspond to an attribute key. The attribute set drives the attribute picker UI on the storefront browse page and the variation selector on marketplace listings.

**Code note:** `VariantAttributes` value object wrapping `IReadOnlyDictionary<string, string>`. Free-form for Phase 3; schema-constrained in Phase 3+.

**Aliases / Rejected Terms:**
- ~~Option~~ — rejected; "Option" is Shopify's term for the attribute key (e.g., "Size" is an option). We use "Attribute" to avoid confusion with Shopify jargon.
- ~~Property~~ — too generic; "Attribute" is the canonical term in marketplace APIs (Amazon's "item-attribute", Walmart's "variantGroupAttribute").
- ~~Dimension~~ — acceptable informally in PO/UX discussion, but "Attribute" is the canonical code term.

---

#### Shared Content

**Definition:** The product information that is common to all variants within a Product Family and lives on the `ProductFamily` record — not on any individual variant. Shared content includes: family display name, brand, base description, shared product images, and category assignment. Shared content is the catalog manager's single point of control for content that does not vary by size, color, or configuration.

**Business significance:** Updating shared content once propagates the change to all variant listings on all channels. This is the primary efficiency gain of the parent/child model versus a flat model (Option B), where equivalent content updates require touching every individual product record.

**Aliases / Rejected Terms:**
- ~~Parent content~~ — acceptable informally, but "Shared Content" communicates the purpose more clearly.
- ~~Inherited content~~ — rejected; "inheritance" implies that variants override the parent value, which is not always accurate. Variants extend shared content; they do not necessarily override it.

---

#### Variant Disambiguation

**Definition:** The process by which a customer (on the storefront) or a marketplace buyer selects the specific variant SKU they want to purchase from within a Product Family. Variant disambiguation happens through the attribute picker UI — the customer selects "Large" from the Size dropdown and "Red" from the Color dropdown, and the system resolves their selection to a specific SKU (e.g., `DOG-COLLAR-L-RED`). The resolved SKU is the unit added to cart.

**Business significance:** Variant disambiguation is the boundary between browsing (family-level) and purchasing (variant-level). A customer browses the "PetSupreme Nylon Dog Collar" family; they add `DOG-COLLAR-L-RED` to cart. The Cart BC and all downstream BCs (Orders, Inventory, Fulfillment) operate exclusively on the resolved SKU — they are never aware of the family grouping.

**Operational note:** The attribute picker must validate that the selected combination resolves to an in-stock, `Active` variant. Combinations that do not exist (e.g., a color/size pairing that was never created) or variants that are `OutOfSeason` or `Discontinued` must be handled gracefully by the storefront (greyed out, "currently unavailable," not added to cart).

---

#### Refinements to Existing Glossary Terms

**Product Variant** (refine existing definition):

Add the following to the existing "Code Usage" section:

> A Variant's `variantAttributes` field holds its differentiating properties (see Variant Attribute above). A Variant's `familyId` field is the UUID v7 of its parent `ProductFamily`. Both fields are nullable — a Product record with `familyId == null` is a Standalone Product.

**Product Family** (refine existing "Code Usage"):

Add:

> `ProductFamily` transitions through the following states: `Active` (has at least one non-terminal variant), `Archived` (all variants discontinued or recalled, system-triggered). There is no `Discontinued` or `Recalled` status on `ProductFamily` itself — those statuses live on individual variants.

---

### 9.7 Open Questions for the Owner

The following business policy questions require Owner input. D1 is fully resolved; these questions arise from the implications of the D1 model choice and should be answered before Phase 3 Admin Portal UI design begins.

---

**PO-Q1 — Variant Creation: Catalog Manager Only, or Can Vendors Propose Variants?** 🔴 Blocks Vendor Portal Phase 3 scope

> Under Option A, variants are SKU-level products that require careful data quality control — a rogue variant with a bad SKU, wrong weight, or mis-assigned family causes downstream problems in Listings, Inventory, and Fulfillment.
>
> **Option A:** Catalog managers create all variants. Vendors submit product briefs through the Vendor Portal (or email/spreadsheet), and a catalog manager enters them. This maximizes data quality but adds operational overhead.
>
> **Option B:** Vendors can propose variants through the Vendor Portal, which creates a "proposed variant" in a pending state. A catalog manager reviews and approves/rejects before the variant is live in the catalog. This reduces catalog manager data entry but requires a review queue workflow.
>
> **My recommendation:** Option B (vendor proposals with mandatory catalog manager approval) is the right long-term model for a realistic e-commerce operation. Vendors know their products; catalog managers ensure quality and consistency. But if Vendor Portal Phase 3 is deprioritized, Option A (catalog manager entry only) is acceptable at launch.
>
> **Owner decision needed:** Is vendor-proposed variant creation in scope for Phase 3 Vendor Portal, or deferred to Phase 4?

---

**PO-Q2 — How Do We Handle UPC/GTIN at the Variant Level?** 🟡 Affects marketplace listing compliance

> Amazon and Walmart require a valid UPC, EAN, or GTIN for every child ASIN/item submitted. Private-label products (our CritterBrew brand) need GS1-registered barcodes that we control. Third-party brand products (PetSupreme, Orijen, Frontline) have manufacturer-assigned UPCs.
>
> Do we store UPC/GTIN as a variant attribute (free-form, alongside Size and Color)? Or as a dedicated field on the `Product` record? And do we have a process for registering GS1 barcodes for private-label variants before they can be listed on Amazon?
>
> **My recommendation:** GTIN deserves a dedicated, typed field on the `Product` record — not a free-form attribute — because it has a specific validation format (8, 12, 13, or 14 digits) and is required at marketplace submission time. The absence of a GTIN should block Amazon/Walmart listing creation.
>
> **Owner decision needed:** Confirm GTIN as a required (or strongly recommended) variant-level field. Confirm whether CritterSupply has a GS1 account for private-label barcode registration.

---

**PO-Q3 — What Is the Policy on Variant Attribute Changes Post-Listing?** 🟡 Affects listing validity

> If a catalog manager changes a variant's attributes *after* the variant has active listings — e.g., corrects "Size: Lg" to "Size: Large" — does that change:
> (a) Automatically propagate to all active listings, triggering a re-submission to marketplaces?
> (b) Flag the active listings as "pending content refresh" and require catalog manager action?
> (c) Have no effect on active listings (listings are snapshots at creation time)?
>
> **My recommendation:** Option (b). Attribute changes should flag active listings for review rather than auto-propagating. This prevents accidental listing updates on live marketplace content. The catalog manager sees a "listings affected by this change: 3 [Review]" prompt.
>
> **Owner decision needed:** Confirm the preferred propagation policy for variant attribute changes.

---

**PO-Q4 — Can a Variant Be Transferred Between Families?** 🟢 Low urgency

> Can `DOG-COLLAR-M-BLK` be moved from the "PetSupreme Nylon Dog Collar" family to the "PetSupreme Everyday Collar Collection" family without discontinuing and recreating it? The SKU stays the same; only the family membership changes.
>
> **My recommendation:** Yes, transfer should be permitted — a catalog manager may need to reorganize families as the catalog matures. The operation is `RemoveVariantFromFamily` (returns to standalone) + `AddVariantToFamily` (assigns to new family). This should be a single "Transfer Variant" action in the Admin UI to prevent an accidental standalone-limbo state. Existing listings carry the old `FamilyId` until explicitly updated (same policy as post-hoc family assignment in Rule 5).
>
> **Owner decision needed:** Confirm that variant transfer between families is a permitted operation and that the "carry old FamilyId on existing listings" behavior is acceptable.

---

**PO-Q5 — What Is the "Minimum Viable Family" for Launch? What Must Be Present Before a Listing Can Be Created?** 🔴 Blocks Listings BC validation logic

> When a catalog manager tries to create a listing for a variant, what must be true of both the variant and its family?
>
> **Proposed minimum gates (variant level):**
> - Variant status = `Active` or `ComingSoon`
> - At least one variant attribute defined (no "blank" variants)
> - At least one variant-specific or family-level image exists
> - Ship weight populated (required for carrier rate calculation)
>
> **Proposed minimum gates (family level, if variant is a family member):**
> - Family name populated
> - Category assigned
> - At least one shared image OR the variant has its own image
>
> **Owner decision needed:** Are these the right gates? Are there additional required fields that should block listing creation (e.g., brand, GTIN, base description minimum length)?

---

*All five questions above require Owner input before Phase 3 Admin Portal UI and Listings BC validation logic are finalized. None of them block the core variant model implementation (ProductFamily aggregate, Product stream changes) — that engineering work can begin immediately. These questions specifically gate the Admin Portal UX design and the Listings BC validation rule set.*

---

*This Product Owner section was added 2026-03-09 as part of the D1 formal sign-off process. It is the business counterpart to the Principal Architect's technical implementation guide in Sections 1–8. Both sections together constitute the full D1 decision record for CritterSupply.*
