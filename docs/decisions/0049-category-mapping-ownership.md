# ADR 0049: Category Mapping Ownership

**Status:** ✅ Accepted

**Date:** 2026-03-31

**Context:**

During M36.1 Phase 2 (Sessions 6–8), the Marketplaces bounded context was built to manage external marketplace channel integrations. A key design question was where `CategoryMapping` documents should live — in the Marketplaces BC, the Product Catalog BC, or a shared taxonomy BC.

Category mappings translate CritterSupply's internal product categories (e.g., "Dogs", "Cats", "Fish & Aquatics") to marketplace-specific category identifiers (e.g., `AMZN-PET-DOGS-001`). Each marketplace has its own category taxonomy, and the mapping is required when submitting listings to external channels via the `ListingApprovedHandler`.

This ADR records the ownership and key design decisions for category mapping persistence.

**Decisions:**

### 1. Category Mappings Owned by Marketplaces BC

`CategoryMapping` documents live in the Marketplaces BC, not in Product Catalog or Listings. The Marten configuration registers them alongside `Marketplace` documents:

```csharp
opts.Schema.For<CategoryMapping>().Identity(x => x.Id);
```

**Rationale:** Category mappings are a concern of marketplace channel integration — they translate the internal taxonomy to each channel's category system. They have no meaning outside of a marketplace context. Product Catalog owns the authoritative product data (including the `Category` field), but the *translation* of that category to `AMZN-PET-DOGS-001` is purely a marketplace concern. Placing mappings in Product Catalog would leak channel-specific knowledge into a BC that should be channel-agnostic.

### 2. Composite Key Format: `{ChannelCode}:{InternalCategory}`

Each `CategoryMapping` document uses a composite string key as its Marten document ID:

```csharp
public sealed class CategoryMapping
{
    public string Id { get; init; } = default!;  // e.g., "AMAZON_US:Dogs"
    public string ChannelCode { get; init; } = default!;
    public string InternalCategory { get; init; } = default!;
    public string MarketplaceCategoryId { get; set; } = default!;
    public string? MarketplaceCategoryPath { get; set; }
    public DateTimeOffset LastVerifiedAt { get; set; }
}
```

The composite key format `"{ChannelCode}:{InternalCategory}"` (e.g., `"AMAZON_US:Dogs"`) makes lookups in the `ListingApprovedHandler` a single `session.LoadAsync<CategoryMapping>(key)` call with no query required.

**Rationale:** The composite key encodes both dimensions of the mapping (which channel, which category) into a single document ID. This eliminates the need for multi-field queries, index configuration, or a separate lookup projection. The `:` delimiter was chosen because neither channel codes nor category names contain colons by convention.

### 3. InternalCategory Aligned with Product Catalog Category Field

The `InternalCategory` field on `CategoryMapping` aligns with the `Category` field on `ProductSummaryView` in the Listings BC, which in turn reflects the `Category` on `CatalogProduct` in the Product Catalog BC.

**Rationale:** This string-based alignment is the simplest integration path — the `ListingApprovedHandler` receives a `ListingApproved` message containing a `Category` field and constructs the composite key directly. No formal anti-corruption layer exists today.

**Known coupling risk:** If the Listings BC or Product Catalog BC renames its category taxonomy (e.g., "Fish & Aquatics" → "Aquarium & Fish"), all category mappings break silently. This is documented as a follow-up concern. The planned M37.0 work to replace `ListingApproved` message enrichment with a proper `ProductSummaryView` ACL in the Marketplaces BC would mitigate this risk by giving Marketplaces BC control over how it resolves product metadata.

### 4. 18 Seed Mappings (6 Categories × 3 Channels)

Development and test environments seed 18 category mappings covering all combinations of 6 internal categories and 3 marketplace channels:

| Internal Category | AMAZON_US | WALMART_US | EBAY_US |
|---|---|---|---|
| Dogs | AMZN-PET-DOGS-001 | WMT-PET-DOGS-001 | EBAY-PET-DOGS-001 |
| Cats | AMZN-PET-CATS-001 | WMT-PET-CATS-001 | EBAY-PET-CATS-001 |
| Birds | AMZN-PET-BIRDS-001 | WMT-PET-BIRDS-001 | EBAY-PET-BIRDS-001 |
| Reptiles | AMZN-PET-REPT-001 | WMT-PET-REPT-001 | EBAY-PET-REPT-001 |
| Fish & Aquatics | AMZN-PET-FISH-001 | WMT-PET-FISH-001 | EBAY-PET-FISH-001 |
| Small Animals | AMZN-PET-SMALL-001 | WMT-PET-SMALL-001 | EBAY-PET-SMALL-001 |

Seeding uses the same idempotency guard (`AnyAsync()`) and dual-invocation pattern as marketplace seed data (see ADR 0048).

**Rationale:** Complete seed coverage enables immediate development and testing of the `ListingApprovedHandler` flow without manual data setup. The marketplace category IDs follow a consistent naming convention per channel, making test assertions predictable.

**Alternatives Considered:**

1. **Category mappings in Product Catalog BC** — Rejected. Product Catalog owns product data, not channel-specific translation rules. Placing mappings there would leak marketplace knowledge into a BC that should remain channel-agnostic.

2. **Category mappings in Listings BC** — Rejected. Listings BC owns the listing lifecycle (draft → approved → live → ended), not the mechanics of translating categories for external channels. The Listings BC publishes `ListingApproved`; the Marketplaces BC decides how to submit it.

3. **Shared `CategoryTaxonomy` BC** — Rejected as premature abstraction. With only 6 internal categories and 3 channels, a dedicated BC for taxonomy management adds organizational overhead without proportional benefit. If CritterSupply grows to support 50+ categories across 10+ channels with versioned taxonomy migrations, a dedicated BC would be warranted.

**Consequences:**

- Category mapping lookups in the `ListingApprovedHandler` are a single `LoadAsync` call — no queries, no projections
- The string-based alignment between `InternalCategory` and Product Catalog's `Category` is a known coupling risk that will be mitigated by the M37.0 ACL work
- Adding a new marketplace channel requires seeding new category mappings (one per internal category)
- Adding a new internal category requires seeding new mappings across all existing channels
- The composite key convention (`{ChannelCode}:{InternalCategory}`) must be maintained consistently wherever category mappings are constructed or referenced

**References:**

- `src/Marketplaces/Marketplaces/CategoryMappings/CategoryMapping.cs` — document entity
- `src/Marketplaces/Marketplaces.Api/MarketplacesSeedData.cs` — seed data (18 mappings)
- `src/Marketplaces/Marketplaces.Api/Program.cs` — Marten identity configuration
- `src/Marketplaces/Marketplaces.Api/Listings/ListingApprovedHandler.cs` — composite key usage
- `docs/decisions/0048-marketplace-document-entity-design.md` — companion ADR for `Marketplace` entity
