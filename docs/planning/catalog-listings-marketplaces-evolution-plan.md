# Product Catalog · Listings · Marketplaces BC: Architecture Evolution Plan

**Document Owner:** Principal Software Architect  
**Status:** 🟡 Active — PO + UX + PSA findings incorporated; Owner decisions outstanding  
**Date:** 2026-03-10  
**Last Updated:** 2026-03-10  
**Triggered by:** PO + UX Engineer discovery session on Product Catalog evolution and marketplace selling  
**Source documents:**
- [`docs/planning/catalog-listings-marketplaces-discovery.md`](catalog-listings-marketplaces-discovery.md)
- [`CONTEXTS.md`](../../CONTEXTS.md) — Product Catalog BC section
- Current implementation: `src/Product Catalog/`
- ADR references: ADR 0016 (UUID v5), ADR 0008 (RabbitMQ consistency)

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Event Sourcing Analysis — Should Product Catalog Migrate?](#2-event-sourcing-analysis--should-product-catalog-migrate)
3. [Listings BC Architecture](#3-listings-bc-architecture)
4. [Marketplaces BC Architecture](#4-marketplaces-bc-architecture)
5. [Integration Contract Design](#5-integration-contract-design)
6. [Questions for Owner/Erik](#6-questions-for-ownererik)
7. [Recommended Phasing](#7-recommended-phasing)
8. [ADR Candidates](#8-adr-candidates)

**Part C — UX Engineer Perspective (Addendum)**
- [C.1 UX Assessment of the Event Sourcing Decision](#c1--ux-assessment-of-the-event-sourcing-decision)
- [C.2 Event Granularity: UX Business Use Cases](#c2--event-granularity-ux-business-use-cases)
- [C.3 Listings Event Granularity: UX Implications](#c3--listings-event-granularity-ux-implications)
- [C.4 Product Change History: A New Admin Feature](#c4--product-change-history-a-new-admin-feature)
- [C.5 Marketplace Document Store: UX Fit](#c5--marketplace-document-store-ux-fit)
- [C.6 Real-Time Admin Notifications (SignalR)](#c6--real-time-admin-notifications-signalr)
- [C.7 UX Risk Register: Event Sourcing Specific Risks](#c7--ux-risk-register-event-sourcing-specific-risks)
- [C.8 UX Recommendations: Admin UI for Event-Sourced Product Catalog](#c8--ux-recommendations-admin-ui-for-event-sourced-product-catalog)

---

## 1. Executive Summary

The PO + UX discovery is well-grounded. The five missing catalog concepts — variant model, vendor association, compliance metadata, structured taxonomy, and channel-specific attributes — are genuine blocking gaps, not wishlist items. None of the three proposed BCs (evolved Product Catalog, Listings, Marketplaces) can be correctly implemented until the variant model is resolved; that decision has the longest dependency chain and must be made first.

The Owner's hypothesis is correct: migrating Product Catalog to event sourcing provides the notification substrate that Listings and Marketplaces need, and it is the right architectural move — but for a specific, narrow reason. The value is not "audit trail" or "time-travel queries." The value is **deterministic, granular integration events that eliminate the "something changed" anti-pattern** currently represented by `ProductUpdated(Sku, Name, Category)`. A `ProductCategoryAssigned` event tells Listings BC that listings may need category remapping. A `ProductDiscontinued` event — when elevated as a high-priority integration message — enables the near-synchronous recall cascade the PO identified as non-negotiable. These capabilities cannot be cleanly retrofitted onto the current CRUD model without bespoke logic that event sourcing provides naturally. The migration is non-trivial but the strategy is clear: a `ProductMigrated` bootstrap event for each existing document, with inline `SingleStreamProjection<ProductCatalogView>` providing identical read performance to the current document store. The read path changes zero; the write path gains an event store.

---

## 2. Event Sourcing Analysis — Should Product Catalog Migrate?

### 2.1 The Case FOR Event Sourcing

**Granular events eliminate the "something changed" anti-pattern.**  
The current `ProductUpdated` integration message carries name and category but nothing else. When Listings BC receives it, it cannot tell whether a category reassignment (requires remapping to all marketplace category trees) or a typo fix in the description (requires only a content refresh on live listings) occurred. The downstream BC must either treat all updates as requiring maximum reprocessing, or query Product Catalog over HTTP to diff the fields — adding coupling and a synchronous dependency. Domain events solve this: `ProductCategoryAssigned` triggers remapping workflows; `ProductDescriptionUpdated` triggers content refresh only.

**The recall cascade is architecturally dependent on ES.**  
The PO's non-negotiable requirement — that a product discontinuation / recall cascade to all active listings cannot be eventually consistent with normal queue lag — is achievable through event sourcing plus a prioritized RabbitMQ exchange. The `ProductDiscontinued` domain event becomes a `ProductRecalled` integration message (when the reason is regulatory) published to a dedicated priority exchange. Listings BC consumes it on a priority consumer. This path requires the discontinuation to be a first-class domain event, not a `session.Store()` side effect with a manually published message.

**Future features are substantially cleaner with ES.**  
Variant model, compliance metadata, vendor association, and category taxonomy all produce events worth capturing: `ProductVariantAdded`, `ComplianceMetadataSet`, `VendorAssociated`, `CategoryTaxonomyNodeAssigned`. Each of these has downstream consumers. Without ES, every new feature requires bespoke "publish integration message" wiring inside the handler. With ES, new events flow automatically through the projection + integration pipeline.

**Read performance is unchanged.**  
This is the most common objection and it is a non-issue in Marten. A `SingleStreamProjection<ProductCatalogView>` registered as an inline projection delivers current-state documents on every read — identical performance to `session.LoadAsync<Product>(sku)` today. The read path does not touch the event stream. `ListProducts` queries the projection document table, not `mt_events`. Zero regression.

**Marten inline projections make the migration mechanical.**  
The migration is a data migration plus a projection registration, not a framework change. Existing HTTP endpoints keep the same signatures. The difference is that handlers call `session.Events.Append(streamId, new ProductNameChanged(...))` instead of `session.Store(product with { Name = ... })`.

### 2.2 The Case AGAINST (Honest Assessment)

**Genuine added complexity.** Every Product Catalog developer now needs to understand stream IDs, optimistic concurrency tokens, and the difference between loading an aggregate from a stream versus querying a projection. The `ChangeProductStatus` handler today is 12 lines. After migration, it involves `FetchForWriting<Product>`, stream-writing, and projection application. That is real cognitive overhead.

**Is Product data "interesting" to audit?** The PO noted that product data changes infrequently. A 5-year event stream for a product that had its description changed twice and its category changed once is not compelling event sourcing territory on its own. Event sourcing earns its complexity at Product Catalog because of the *downstream reactive value*, not because the Product aggregate itself is complex.

**Migration risk on existing data.** The 24 integration tests passing against the current document store must continue passing after migration. The `ProductMigrated` bootstrapping event approach works, but it must be implemented carefully — migrated products must project to identical state as the pre-migration documents.

**Reference architecture dilution risk.** If every BC is event-sourced, the reference architecture stops teaching *when* to use ES and starts looking like a dogmatic rulebook. The current distinction (Orders/Payments/Inventory/Pricing = event-sourced; Product Catalog = document store) is itself a teaching moment. Migrating Product Catalog loses that contrast.

### 2.3 Recommendation

**Yes — migrate Product Catalog to event sourcing. The dilution risk is acceptable given the concrete architectural benefit.**

The "teaching moment" about document store vs. event sourcing is better served by showing *why a BC migrates* — a richer lesson than a static contrast. The reactive integration value (granular events, recall cascade, downstream signal precision) is concrete and demonstrable. The compliance metadata and variant model coming in Phase 2 would each require bespoke notification logic without ES; with ES, they are free.

**Model: Full ES on the Product aggregate with a `SingleStreamProjection<ProductCatalogView>` for all reads.** A hybrid (ES for lifecycle events, document for content) introduces a split persistence model with no meaningful benefit — it's the worst of both worlds: event stream complexity with a separate document write path. All state lives in events. All reads go to the projection.

### 2.4 Migration Strategy

**Stream key:** UUID v5 derived from SKU, following ADR 0016. Namespace prefix `catalog:` distinguishes from the Pricing BC's `pricing:` prefix, ensuring no UUID collision between two streams for the same SKU.

```csharp
public static Guid StreamId(string sku)
{
    // ADR 0016: UUID v5 (RFC 4122 §4.3) — deterministic, namespaced SHA-1.
    // Namespace: URL namespace (6ba7b810-...) scoped with "catalog:" prefix.
    // Ensures: catalog:DOG-BOWL-001 ≠ pricing:DOG-BOWL-001 ≠ inventory:DOG-BOWL-001
    var namespaceBytes = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8").ToByteArray();
    var nameBytes = Encoding.UTF8.GetBytes($"catalog:{sku.ToUpperInvariant()}");
    var hash = SHA1.HashData([.. namespaceBytes, .. nameBytes]);
    hash[6] = (byte)((hash[6] & 0x0F) | 0x50);  // Version 5
    hash[8] = (byte)((hash[8] & 0x3F) | 0x80);  // Variant RFC 4122
    return new Guid(hash[..16]);
}
```

**Migration bootstrap event:**

```csharp
// One-time event emitted during migration for every pre-existing Product document.
// The ProductCatalogView projection applies this identically to ProductAdded.
// Idempotent: if migration re-runs, Marten's stream-already-exists check skips duplicates.
public sealed record ProductMigrated(
    string Sku, string Name, string Description, string? LongDescription,
    string Category, string? Subcategory, string? Brand,
    IReadOnlyList<ProductImage> Images, IReadOnlyList<string> Tags,
    ProductDimensions? Dimensions, ProductStatus Status,
    DateTimeOffset OriginalAddedAt, DateTimeOffset MigratedAt);
```

**Migration script shape (conceptual):**

```csharp
// Background job — runs once, idempotent on retry.
// New products start on ES immediately; existing products migrate in batches.
var products = await session.Query<Product>().ToListAsync(ct);  // existing documents
foreach (var batch in products.Chunk(100))
{
    foreach (var product in batch)
    {
        var streamId = Product.StreamId(product.Sku);
        var exists = await session.Events.FetchStreamStateAsync(streamId, ct);
        if (exists is not null) continue;  // already migrated

        var migrateEvent = new ProductMigrated(
            product.Sku, product.Name, product.Description, product.LongDescription,
            product.Category, product.Subcategory, product.Brand,
            product.Images, product.Tags, product.Dimensions, product.Status,
            product.AddedAt, DateTimeOffset.UtcNow);

        session.Events.StartStream<Product>(streamId, migrateEvent);
    }
    await session.SaveChangesAsync(ct);
}
// After migration: disable the old Product document store config.
// opts.Schema.For<Product>() table remains for rollback window; removed in Phase 1 cleanup.
```

**Correctness verification tests:**
- For every migrated product: `ProductCatalogView` projected state equals the pre-migration `Product` document state (field-by-field assertion)
- Round-trip: migrate → load projection → compare to original document snapshot
- Idempotency: run migration twice; assert no duplicate streams, no changed state
- New product created post-migration: confirm it never touches the old document table

### 2.5 Proposed Domain Event Vocabulary

| Domain Event | Granularity Rationale | Downstream BCs | Integration Message? |
|---|---|---|---|
| `ProductAdded` | Birth event. All fields present. Replaces current `ProductAdded` integration message. | Pricing (initialize price stream), Inventory (create stock record), Listings (enable listing creation) | ✅ Yes — `ProductCatalog.ProductAdded` |
| `ProductMigrated` | One-time bootstrap. Treated as `ProductAdded` by projections. | None (internal only) | ❌ No |
| `ProductNameChanged` | Name changes alone are frequent enough (copywriter edits) to warrant own event. | Listings (update listing titles), Customer Experience (cache invalidation) | ✅ Yes — `ProductCatalog.ProductContentUpdated` (batched with description/tags) |
| `ProductDescriptionUpdated` | Content team changes. Merge into `ProductContentUpdated` integration message with `ProductNameChanged`. | Listings (listing content refresh) | ✅ Merged into `ProductContentUpdated` |
| `ProductCategoryAssigned` | Category changes trigger marketplace remapping — must be a distinct signal. | Listings (remap all listings for this SKU), Marketplaces (attribute schema may change) | ✅ Yes — `ProductCatalog.ProductCategoryChanged` |
| `ProductImagesUpdated` | Image changes have distinct downstream workflow (CDN propagation, marketplace re-upload). | Listings (image refresh per channel) | ✅ Yes — `ProductCatalog.ProductImagesUpdated` |
| `ProductTagsUpdated` | SEO/search tags. Low-priority downstream. | Customer Experience (search index refresh) | ⚠️ Optional — low priority, can be batched |
| `ProductDimensionsSet` | Affects shipping calculations and hazmat routing. Must be distinct. | Fulfillment (routing re-evaluation for active orders) | ✅ Yes — `ProductCatalog.ProductDimensionsChanged` |
| `ProductActivated` | ComingSoon → Active or OutOfSeason → Active. | Listings (enable listing submission), Customer Experience | ✅ Yes — `ProductCatalog.ProductActivated` |
| `ProductDiscontinued` | Terminal status. Triggers recall cascade path. | Listings (IMMEDIATE de-activation), Pricing (cease price updates), Inventory (no new reservations) | ✅ Yes — `ProductCatalog.ProductDiscontinued` (HIGH PRIORITY exchange) |
| `ProductSetToComingSoon` | Forward-looking status. Different downstream from Active/Discontinued. | Listings (pre-listing draft allowed; not yet submittable) | ✅ Yes — merged into `ProductStatusChanged` |
| `ProductSetToOutOfSeason` | Planned pause. Listings should pause (not end) listings for this SKU. | Listings (pause all Live listings), Pricing (pause promotional rules) | ✅ Yes — `ProductCatalog.ProductStatusChanged` with explicit prior/new status |
| `ProductDeleted` | Soft delete (irreversible for operations). | Listings (end all listings), Pricing (terminate price stream) | ✅ Yes — same handler chain as `ProductDiscontinued` |
| `ProductRestored` | Soft-deleted product restored (admin only, rare). | Listings (enable listing creation), Pricing | ✅ Yes — `ProductCatalog.ProductRestored` |

**Phase 2+ events (not implemented at migration):**

| Domain Event | Downstream Consumer | Notes |
|---|---|---|
| `ProductVariantAdded` | Listings, Storefront | D1 must be resolved first |
| `ProductVariantRemoved` | Listings, Storefront | |
| `ComplianceMetadataSet` | Listings (re-validate marketplace compliance gates) | D8 dependency |
| `HazmatClassificationChanged` | Fulfillment (re-evaluate routing), Listings (marketplace restrictions apply) | |
| `VendorAssociated` | Vendor Portal (show product in vendor's catalog view) | |
| `VendorDisassociated` | Vendor Portal | |
| `CategoryTaxonomyNodeAssigned` | Listings (structured node replaces string category) | D5 dependency |
| `ProductRecallInitiated` | Listings (immediate de-activation), Notifications (customer alert) | Distinct from `ProductDiscontinued` — carries regulatory metadata |

### 2.6 Integration vs. Domain Events

**Retire these current integration messages:**

| Current Message | Problem | Replacement |
|---|---|---|
| `ProductUpdated(Sku, Name, Category, UpdatedAt)` | Coarse-grained — name + category conflated; no other fields | Split into `ProductContentUpdated` + `ProductCategoryChanged` |
| `ProductAdded(Sku, Name, Category, AddedAt)` | Too sparse — Pricing BC needs more fields to initialize | Extend: add `Status`, `Brand`, `HasDimensions` |

**Proposed `Messages.Contracts.ProductCatalog` redesign:**

```csharp
// Replaces ProductAdded. Richer payload — Pricing and Listings need more than name+category.
public sealed record ProductAdded(
    string Sku, string Name, string Category, string? Brand,
    ProductStatus InitialStatus, bool HasDimensions, DateTimeOffset AddedAt);

// Replaces ProductUpdated. Granular — split into two messages.
// Carries only the fields that changed (null = not changed in this event).
public sealed record ProductContentUpdated(
    string Sku, string? NewName, string? NewDescription,
    IReadOnlyList<string>? NewTags, DateTimeOffset UpdatedAt);

// NEW — category change is a first-class event for Listings remapping.
public sealed record ProductCategoryChanged(
    string Sku, string PreviousCategory, string NewCategory, DateTimeOffset ChangedAt);

// NEW — images have distinct downstream workflow.
public sealed record ProductImagesUpdated(
    string Sku, int ImageCount, DateTimeOffset UpdatedAt);

// NEW — physical attributes affect shipping and marketplace restrictions.
public sealed record ProductDimensionsChanged(
    string Sku, decimal LengthCm, decimal WidthCm, decimal HeightCm,
    decimal WeightKg, DateTimeOffset ChangedAt);

// NEW — explicit prior/new status for downstream conditional logic.
public sealed record ProductStatusChanged(
    string Sku, ProductStatus PreviousStatus, ProductStatus NewStatus,
    string? Reason, DateTimeOffset ChangedAt);

// Enriched — carries DiscontinuedAt AND Reason for recall audit trail.
public sealed record ProductDiscontinued(
    string Sku, string? Reason, bool IsRecall, DateTimeOffset DiscontinuedAt);

// NEW — soft-delete notification (distinct from Discontinued).
public sealed record ProductDeleted(string Sku, DateTimeOffset DeletedAt);

// NEW — restoration (rare; enables downstream to re-enable workflows).
public sealed record ProductRestored(string Sku, DateTimeOffset RestoredAt);
```

**Exchange routing:** `ProductDiscontinued` where `IsRecall = true` is published to a dedicated `product-recall` RabbitMQ exchange (priority queue, max-priority 10). All other Product Catalog messages go to the standard `product-catalog` exchange. Listings BC has a high-priority consumer on `product-recall`.

---

## 3. Listings BC Architecture

### 3.1 Aggregate Design

**Yes — Listing is event-sourced.** The Listing lifecycle (`Draft → ReadyForReview → Submitted → Live → Paused → Ended`) is precisely the kind of explicit, audited state machine that event sourcing excels at. Every state transition has business meaning. Historical transitions matter: when did a listing go Live? How many times was it Paused? What caused it to End? These questions are free with ES; expensive to reconstruct with a document store.

**Stream key:** UUID v5 from `{sku}:{channelCode}` composite key, with namespace prefix `listing:`.

```csharp
public static Guid StreamId(string sku, string channelCode)
{
    // A Listing is a natural-key singleton: one per (SKU, channel) pair.
    // UUID v5 per ADR 0016 — all Listing handlers can derive this locally,
    // no lookup needed. channelCode must be the stable ChannelCode string
    // (e.g., "AMAZON_US"), not a display name that might change.
    var namespaceBytes = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8").ToByteArray();
    var key = $"listing:{sku.ToUpperInvariant()}:{channelCode.ToUpperInvariant()}";
    var nameBytes = Encoding.UTF8.GetBytes(key);
    var hash = SHA1.HashData([.. namespaceBytes, .. nameBytes]);
    hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
    hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
    return new Guid(hash[..16]);
}
```

**Local product data projection:** Listings BC must NOT call Product Catalog over HTTP for product data. It maintains a local `ProductSummaryView` document (Marten document store within Listings BC's schema) by reacting to Product Catalog integration messages. When a Listing is submitted, the handler reads `ProductSummaryView` locally — no cross-BC HTTP call.

```csharp
// Within Listings BC — local read model of Product Catalog data.
// Updated by reacting to ProductCatalog.* integration messages.
public sealed record ProductSummaryView
{
    public string Id { get; init; } = null!;   // = SKU
    public string Name { get; init; } = null!;
    public string Category { get; init; } = null!;
    public string? Brand { get; init; }
    public ProductStatus CatalogStatus { get; init; }
    public DateTimeOffset LastSyncedAt { get; init; }
}
```

**Proposed Listing domain events:**

| Event | Notes |
|---|---|
| `ListingDraftCreated` | `(ListingId, Sku, ChannelCode, CreatedBy, CreatedAt)` — start of lifecycle |
| `ListingContentSet` | `(ListingId, Title, Description, Attributes, Images, SetAt)` — channel-specific content applied |
| `ListingSubmittedForReview` | `(ListingId, SubmittedBy, SubmittedAt)` |
| `ListingApproved` | `(ListingId, ApprovedBy, ApprovedAt)` — ReadyForReview → approved |
| `ListingRejected` | `(ListingId, RejectedBy, RejectionReason, RejectedAt)` — back to Draft |
| `ListingSubmittedToMarketplace` | `(ListingId, MarketplaceListingId, SubmittedAt)` — API call made |
| `ListingActivated` | `(ListingId, MarketplaceListingId, LiveAt)` — marketplace confirmed Live |
| `ListingPaused` | `(ListingId, PausedBy, Reason, PausedAt)` |
| `ListingResumed` | `(ListingId, ResumedBy, ResumedAt)` |
| `ListingEnded` | `(ListingId, Cause, EndedBy?, EndedAt)` — terminal; `Cause` captures why |
| `ListingCategoryRemapped` | `(ListingId, PreviousMarketplaceCategory, NewMarketplaceCategory, RemappedAt)` |
| `ListingContentRefreshed` | `(ListingId, FieldsRefreshed, RefreshedAt)` — triggered by product content update |
| `ListingForcedDown` | `(ListingId, Reason, ForcedAt)` — immediate de-activation, bypasses normal Paused state |

### 3.2 State Machine

**Validated lifecycle with architectural notes:**

```
             ┌─────────────────────────────────────────────────┐
             │  [ProductDiscontinued / ProductDeleted]          │ ← IMMEDIATE (recall path)
             │  → ListingForcedDown → Ended                     │
             └─────────────────────────────────────────────────┘
                                 ↑
Draft ──► ReadyForReview ──► Submitted ──► Live ──► Paused ──► Ended (terminal)
  ▲             │                             │       ↑  │
  └─────────────┘ (Rejected back to Draft)    └───────┘  └──► Ended (manual end)
                                          (Resumed from Paused)
```

**Key architectural observations:**

1. **`Ended` carries a `Cause`** — this is the UX non-negotiable. `Cause` is a discriminated union: `ManualEnd`, `ProductDiscontinued`, `ProductDeleted`, `RecallCascade`, `MarketplacePolicyViolation`, `SeasonalEnd`. The listing admin UI must surface this.

2. **`ForcedDown` is a separate event from `Paused`.** The recall cascade must bypass the normal Paused state — a forced-down listing cannot be resumed without explicit operator action. `ForcedDown → Ended` is a one-way path.

3. **Invariants:**
   - Cannot submit a listing if `ProductSummaryView.CatalogStatus != Active`
   - Cannot activate a listing if the product is discontinued
   - `Ended` is terminal — no transitions out
   - A `ForcedDown` listing can only transition to `Ended`, never to `Live` directly

4. **`ReadyForReview` is optional workflow.** Small team? Route directly Draft → Submitted. The state exists structurally and can be skipped with a policy: `if (listing.Status == Draft && skipReview) { ApplyEvents(new ListingApproved(...), new ListingSubmittedToMarketplace(...)); }`. Never remove the state from the aggregate.

### 3.3 Recall Cascade Design

**The constraint:** De-activation of all active listings for a discontinued/recalled product cannot wait behind normal queue backlog (minutes to hours lag). The PO called this "near-synchronous." The architectural target is sub-second propagation to the Listings BC consumer.

**Mechanism — dedicated priority exchange:**

```
ProductCatalog.Api
  └─► RabbitMQ: [product-recall exchange, priority=10]
        └─► Listings.Api: [product-recall queue, prefetch=1, dedicated consumer]
              └─► RecallCascadeHandler
                    ├─► Query ListingsActiveView WHERE Sku = {sku}   // local projection
                    ├─► For each active ListingId:
                    │     session.Events.Append(streamId, new ListingForcedDown(...))
                    └─► Publish: ListingsCascadeCompleted(Sku, Count, CompletedAt)
```

**Why not a synchronous HTTP call?** Listings BC may be processing other messages. A synchronous HTTP call from Product Catalog to Listings BC creates a direct coupling and a failure mode: if Listings BC is down during the discontinuation command, the recall silently fails. RabbitMQ with a dedicated priority queue gives us durability + near-synchronous delivery + decoupling.

**`ListingsActiveView` projection:** Listings BC maintains an inline projection — a document indexed by SKU containing `IReadOnlyList<Guid> ActiveListingStreamIds`. `RecallCascadeHandler` queries this document (a single `LoadAsync<ListingsActiveView>(sku)`) to find all streams that need `ListingForcedDown` appended. The entire cascade is a single projection query + N event appends in one `SaveChangesAsync`.

**Why this satisfies the near-synchronous constraint:**
- Priority queue consumer processes recall messages ahead of all other Listings BC messages
- `ListingsActiveView` lookup is a single indexed document read (~1ms)
- Event appends are batched in one `SaveChangesAsync` call (N appends, 1 roundtrip)
- End-to-end: discontinuation command completes → RabbitMQ delivery → cascade handler → all listings forced down, target <2 seconds under normal load

**Lot/batch tracking dependency (D7):** If Owner decides that recalls can be lot-specific (only batches manufactured before a certain date), the recall message must carry `AffectedLotNumbers?`. If null, all active listings for the SKU are forced down. If populated, only listings where `BatchLot ∈ AffectedLotNumbers` are targeted. This requires Listings to track `BatchLot` on listing creation — a D7 dependency.

---

## 4. Marketplaces BC Architecture

### 4.1 Marketplace Identity Decision (D4)

**Decision: Document entity within Marketplaces BC.** Not an aggregate, not an enum, not config.

**Evaluation:**

| Option | Assessment | Verdict |
|---|---|---|
| **Enum** | Zero flexibility. Adding `TIKTOK_SHOP` requires recompilation and redeployment. Cannot carry metadata. Marketplace-specific attribute schemas cannot live on an enum value. | ❌ Rejected |
| **Config** (appsettings.json) | Slightly better than enum — no recompilation — but still cannot carry structured attribute schemas, API credentials references, or feature flags in a queryable way. Config is not a domain model. | ❌ Rejected |
| **Event-sourced Aggregate** | Marketplaces don't have the lifecycle richness that justifies ES. Adding a marketplace happens once. "Amazon US was configured" is not a business event worth auditing across years. The operational overhead of stream management is unjustified. | ❌ Rejected |
| **Document entity** | A `Marketplace` document in Marten with a stable `ChannelCode` string ID. Carries: display name, feature flags, API credential reference (Vault path, not the credential itself), active attribute schema version. Queryable. Updatable without event overhead. Naturally represents "what a marketplace *is* right now." | ✅ Selected |

```csharp
// Marketplaces BC — Marketplace document entity.
// ChannelCode is the stable identity: "AMAZON_US", "EBAY_US", "WALMART_US", "OWN_WEBSITE"
public sealed record Marketplace
{
    public string Id { get; init; } = null!;           // = ChannelCode
    public string ChannelCode { get; init; } = null!;  // e.g., "AMAZON_US"
    public string DisplayName { get; init; } = null!;  // e.g., "Amazon US"
    public bool IsActive { get; init; }
    public bool IsOwnWebsite { get; init; }            // D2 flag
    public string? ApiCredentialVaultPath { get; init; } // D6: path to Vault secret, not the secret
    public string AttributeSchemaVersion { get; init; } = null!; // D10: semver
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
```

**ChannelCode is the stable natural key.** It is used in Listing stream IDs (`listing:{sku}:{channelCode}`), in all integration messages, and in category mapping keys. It must never change after a marketplace is registered. Display name changes are fine — `ChannelCode` does not.

### 4.2 Should Marketplaces BC Be Event-Sourced?

**No.** Marketplace configuration changes are administrative, infrequent, and do not drive reactive downstream workflows in the way Product Catalog events do. A `Marketplace` document updated via standard handlers is correct. If we ever need an audit trail of marketplace config changes, Marten's document versioning (`UseOptimisticConcurrency`) is sufficient — no event stream needed.

**However:** The `MarketplaceAttributeSchema` — the per-marketplace definition of required fields and their types (D10) — is more interesting. Schema evolution *does* have downstream effects: a new required field means all existing `Draft` listings for that marketplace are now missing a field. This is worth an event: `MarketplaceAttributeSchemaPublished(ChannelCode, Version, SchemaDefinition, PublishedAt)`. This event notifies Listings BC to validate existing drafts against the new schema.

### 4.3 Schema and Attribute Ownership Model

**Category-to-marketplace mapping (D5): Marketplaces BC owns it.**

The mapping from CritterSupply's internal category taxonomy to marketplace category trees (`Dogs > Bowls` → Amazon `2975448011`) is marketplace-specific knowledge. The Product Catalog BC knows what category *we* call a product. The Marketplaces BC knows how each channel represents that category. When Amazon reorganizes their taxonomy, it's the Marketplaces BC that needs updating — not the Product Catalog. Ownership follows the change rate.

```csharp
// Within Marketplaces BC — category mapping document.
public sealed record CategoryMapping
{
    public string Id { get; init; } = null!;  // = "{channelCode}:{internalCategory}"
    public string ChannelCode { get; init; } = null!;
    public string InternalCategory { get; init; } = null!;  // CritterSupply taxonomy node
    public string MarketplaceCategoryId { get; init; } = null!;  // marketplace's identifier
    public string MarketplaceCategoryPath { get; init; } = null!; // human-readable path
    public string AttributeSchemaVersion { get; init; } = null!;  // D10: which schema applies
    public DateTimeOffset LastVerifiedAt { get; init; }
}
```

**Attribute schema ownership (D10):** `MarketplaceAttributeSchema` is a versioned document in Marketplaces BC. Version format: `{channelCode}:{semver}` (e.g., `AMAZON_US:2.1.0`). Listings BC stores the schema version that was active when the listing was created. Schema upgrades are non-breaking if additive (new optional fields); breaking if a previously optional field becomes required (triggers `MarketplaceAttributeSchemaPublished` with a migration notification).

---

## 5. Integration Contract Design

### 5.1 Message Namespace Additions

**`Messages.Contracts.ProductCatalog` — replacements and additions:**

```
ProductCatalog.ProductAdded              ← enriched (was: Sku, Name, Category, AddedAt)
ProductCatalog.ProductContentUpdated     ← NEW (replaces ProductUpdated)
ProductCatalog.ProductCategoryChanged    ← NEW (granular separation from content)
ProductCatalog.ProductImagesUpdated      ← NEW
ProductCatalog.ProductDimensionsChanged  ← NEW
ProductCatalog.ProductStatusChanged      ← NEW (ComingSoon ↔ OutOfSeason ↔ Active)
ProductCatalog.ProductDiscontinued       ← enriched (adds IsRecall, Reason)
ProductCatalog.ProductDeleted            ← NEW
ProductCatalog.ProductRestored           ← NEW
```

**`Messages.Contracts.Listings` — new namespace:**

```
Listings.ListingCreated                 (Sku, ChannelCode, ListingId, CreatedAt)
Listings.ListingActivated               (ListingId, Sku, ChannelCode, LiveAt)
Listings.ListingPaused                  (ListingId, Sku, ChannelCode, Reason, PausedAt)
Listings.ListingEnded                   (ListingId, Sku, ChannelCode, Cause, EndedAt)
Listings.ListingForcedDown              (ListingId, Sku, ChannelCode, Reason, ForcedAt)
Listings.ListingsCascadeCompleted       (Sku, ListingsDeactivated, CompletedAt)
```

**`Messages.Contracts.Marketplaces` — new namespace:**

```
Marketplaces.MarketplaceRegistered      (ChannelCode, DisplayName, RegisteredAt)
Marketplaces.MarketplaceDeactivated     (ChannelCode, DeactivatedAt)
Marketplaces.CategoryMappingUpdated     (ChannelCode, InternalCategory, NewMarketplaceCategory)
Marketplaces.AttributeSchemaPublished   (ChannelCode, Version, PublishedAt, IsBreakingChange)
```

### 5.2 Recall Cascade — End-to-End Flow

```
1. [HTTP] POST /api/products/{sku}/status  { NewStatus: "Discontinued", Reason: "Safety recall", IsRecall: true }
          │
          ▼
2. [ProductCatalog.Api] ChangeProductStatus handler
   → session.Events.Append(streamId, new ProductDiscontinued(Sku, Reason, IsRecall=true, Now))
   → SaveChangesAsync → inline projection updates ProductCatalogView
   → Wolverine publishes: ProductDiscontinued (IsRecall=true) to [product-recall exchange, priority=10]
          │
          ▼
3. [RabbitMQ: product-recall exchange] — priority consumer, ahead of standard queue backlog
          │
          ▼
4. [Listings.Api] RecallCascadeHandler
   → var view = await session.LoadAsync<ListingsActiveView>(sku)
   → foreach active stream: session.Events.Append(streamId, new ListingForcedDown(reason: "RecallCascade"))
   → SaveChangesAsync (N appends, 1 roundtrip)
   → Publish: ListingsCascadeCompleted(Sku, Count: N, CompletedAt)
          │
          ▼
5. [ProductCatalog.Api] ListingsCascadeCompletedHandler (optional confirmation sink)
   → Log audit record: recall cascade confirmed for Sku, N listings forced down
          │
          ▼
6. [Marketplaces.Api] (D3 dependency) — if Listings BC owns API calls:
   → For each ForcedDown listing: call marketplace API to de-activate
   → Publish: MarketplaceListingDeactivated per channel
```

**Target end-to-end latency:** Steps 1–4 (catalog event → all listings forced down in Marten): **<2 seconds** under normal load. Step 6 (actual marketplace API calls) is network-dependent and asynchronous — this is acceptable because the *business record* is updated in step 4. Marketplace API failures in step 6 enter the standard retry/outbox pattern.

### 5.3 BC Dependency Map

```
ProductCatalog ──publishes──► Listings ──────────────────► Marketplaces
     │                           │                               │
     └──publishes──► Pricing      └─queries──► ProductSummaryView │
     │                           └─queries──► CategoryMapping ◄──┘
     └──publishes──► Inventory
     └──publishes──► CustomerExperience
```

**Anti-corruption layers:**
- Listings BC maintains `ProductSummaryView` (from ProductCatalog events) — no HTTP calls to Catalog
- Listings BC maintains `CategoryMappingView` (from Marketplaces events) — no HTTP calls to Marketplaces at listing submission time
- Marketplaces BC does not consume Listings events directly (listings data flows from Listings → Marketplaces via integration messages only if D3 routes API calls through Marketplaces)

---

## 6. Questions for Owner/Erik

The following decisions require Owner input before implementation begins. They are ordered by blocking dependency — earlier items block more downstream work.

---

**D1 — Variant Model Shape** 🔴 BLOCKS EVERYTHING ELSE

> Do variants use parent/child SKUs (Option A: one parent document, N child SKU records), a flat product with embedded variants (Option C: `IReadOnlyList<ProductVariant>` on the product record), or loosely linked standalone SKUs (Option B: `ProductFamilyId` reference)?
>
> **Architect's recommendation: Option A (parent/child).** It matches how Amazon, Shopify, and every major marketplace model variants natively. Parent holds shared content (name, description, brand, images). Child holds differentiating attributes (size, color, weight) plus its own SKU, pricing, and inventory. Listings BC creates listings against child SKUs, not parent products. The parent is a grouping container.
>
> **Why it must be decided first:** Listing stream key (`listing:{sku}:{channelCode}`) anchors on SKU — if variants share a parent SKU, the key design changes. Inventory BC's reservation model changes. Pricing BC's `ProductPrice` stream key is per-SKU — that holds. But the storefront's "select your size" UX only exists after this is resolved. The UX Engineer explicitly blocked Listings UI design on this answer.

---

**D2 — Is Own Website a Formal Channel?** 🟡 Blocks Listings initial scope

> Is `OWN_WEBSITE` a `ChannelCode` in the Listings + Marketplaces model, or is the own storefront handled separately (i.e., the Storefront BC directly reads Product Catalog, not a Listings record)?
>
> **Architect's recommendation: Yes, formal channel.** Treating OwnWebsite as a Listings channel means every product visible on the CritterSupply storefront goes through the same Draft → Live lifecycle. This gives you a single point of truth for "is this product active on channel X?" including the own website. It also means the recall cascade automatically covers the own website. The Storefront BC reads `Listings.ListingActivated` events to show/hide products.

---

**D3 — Does Listings BC Own Marketplace API Calls?** 🔴 Blocks Marketplaces BC scope

> When a listing reaches `Submitted` state, which BC makes the actual API call to Amazon/Walmart/eBay?
>
> **Architect's recommendation: Marketplaces BC.** Listings BC owns the *intent* (a listing should exist on channel X). Marketplaces BC owns the *mechanism* (how to actually call Amazon's API, what credentials to use, how to handle throttling and error responses). Listings BC publishes `ListingSubmittedToMarketplace`; Marketplaces BC handles it and publishes `MarketplaceListingActivated` back. This is clean separation of domain concern (what we're listing) from integration concern (how marketplace APIs work).
>
> **Risk if deferred:** Listings BC grows API call logic and becomes a God-BC managing both its own lifecycle and marketplace integration protocols.

---

**D4 — Marketplace as Aggregate/Document/Enum/Config** ✅ Decided Above

> See Section 4.1. **Decision: Document entity in Marketplaces BC.** No Owner input needed.

---

**D5 — Category-to-Marketplace Mapping Ownership** ✅ Decided Above

> See Section 4.3. **Decision: Marketplaces BC owns it.** No Owner input needed.

---

**D6 — API Credentials: Marketplaces BC or Vault?** 🟡 Blocks Marketplaces implementation

> Should marketplace API credentials (Amazon SP-API client ID/secret, Walmart API key) live in Vault (injected as environment variables at startup), or managed as documents within Marketplaces BC?
>
> **Architect's recommendation: Vault (or equivalent secrets manager), with the Vault path stored on the `Marketplace` document.** Never store API credentials in Postgres or in appsettings.json. The `Marketplace` document stores the Vault path (e.g., `secret/marketplaces/amazon-us`); the Marketplaces BC service retrieves the credential at startup or on demand via the Vault client. This is the correct security boundary.

---

**D7 — Lot/Batch Tracking Scope** 🟡 Affects recall cascade precision

> If a product recall is lot-specific (only units from manufacturing batch X are affected), does the Listings BC need to track which lot a listing represents? Is lot tracking in scope for Listings launch, or deferred?
>
> If deferred: the recall cascade forces down ALL active listings for the SKU regardless of lot. This is the safe default — more aggressive than necessary for lot-specific recalls, but not harmful. **Architect's recommendation: Defer to Phase 3.** Launch with full-SKU cascade. Add lot-specific targeting as a Phase 3 enhancement once Inventory BC has lot tracking wired.

---

**D8 — Compliance Metadata: Listings Launch or Deferred?** 🔴 Blocks Amazon/Walmart listing submission

> Amazon and Walmart reject listings for hazmat products without proper hazmat classification. If CritterSupply sells *any* products that require hazmat classification (batteries, certain chemicals, some medications), those products cannot be listed without compliance fields.
>
> **Architect's recommendation: Minimum viable compliance at Listings launch.** `IsHazmat: bool` and `HazmatClass: string?` on the Product aggregate (matching the existing `FulfillmentLineItem` field established in Fulfillment BC Q6 answer). Full compliance suite (PropSixtyFive, AgeRestriction, RestrictedStates) as Phase 2. This unblocks marketplace submission while avoiding the full regulatory schema.

---

**D9 — Automated Seasonal Reactivation** 🟢 Low urgency

> Should OutOfSeason products reactivate automatically on a scheduled date, or manually by a catalog manager?
>
> **Architect's recommendation: Manual for launch, scheduled for Phase 2.** A Wolverine scheduled message (`ScheduledActivation`) stored at the time of `ProductSetToOutOfSeason` is the correct pattern. The scheduled message fires a `ProductActivated` command at the planned date. Phase 1: manual only. Phase 2: add `PlannedReactivationDate?` to `ProductSetToOutOfSeason` event and wire the scheduler.

---

**D10 — Schema Versioning for Marketplace Attribute Definitions** 🟡 Blocks Listings content completeness

> When Amazon updates their attribute schema (new required field for dog bowls), how does CritterSupply detect and respond? Does the Marketplaces BC expose a schema version that Listings BC stamps on each draft listing? What is the migration path for existing draft listings when a breaking schema change is published?
>
> **Architect's recommendation: Semantic versioning on `MarketplaceAttributeSchema` documents.** `MarketplaceAttributeSchemaPublished(IsBreakingChange: true)` triggers Listings BC to flag all draft listings for that channel as `RequiresSchemaReview`. Non-breaking schema changes (new optional fields) do not trigger a flag. **Owner must decide:** Do breaking schema changes block submission of existing drafts, or merely warn? This is a business policy decision, not a technical one.

---

## 7. Recommended Phasing

### Phase 0 — Foundation (Prerequisite, no new BCs)
**Duration estimate: 1–1.5 cycles**

**Deliverables:**
1. Product Catalog BC migrated to event sourcing
   - `Product` aggregate with `SingleStreamProjection<ProductCatalogView>`
   - `ProductMigrated` bootstrap event + migration job
   - All 24 existing integration tests passing against new ES model
   - UUID v5 `StreamId(sku)` with `catalog:` namespace prefix
2. Enriched `Messages.Contracts.ProductCatalog` integration messages (granular replacements)
3. Pricing BC `ProductRegistered` handler updated to consume new `ProductAdded` contract
4. Inventory BC updated to consume new `ProductAdded` contract
5. `product-recall` RabbitMQ exchange configured (priority queue, max-priority 10)
6. `ProductDiscontinued` (IsRecall=true) routed to priority exchange

**Gate: D1 (variant model) does NOT block Phase 0.** Variants are Phase 2+. Phase 0 is the event sourcing migration of the existing scalar model.

---

### Phase 1 — Listings BC Foundation
**Duration estimate: 1.5–2 cycles | Prerequisite: Phase 0 complete + D1, D2, D3 answered**

**Deliverables:**
1. Listings BC project scaffold (`src/Listings/`, `src/Listings/Listings.Api/`)
2. `Listing` aggregate (event-sourced, UUID v5 stream key)
3. State machine: Draft → ReadyForReview → Submitted → Live → Paused → Ended
4. `ListingForcedDown` event + `ListingsCascadeCompleted` integration message
5. `ProductSummaryView` local projection (reacts to ProductCatalog integration messages)
6. Recall cascade handler: `RecallCascadeHandler` consuming `product-recall` exchange
7. `ListingsActiveView` inline projection (per-SKU index of active stream IDs)
8. HTTP endpoints: Create listing, Update content, Submit for review, Approve, Pause, Resume, End
9. Integration tests: full happy path + recall cascade (using Testcontainers + RabbitMQ)
10. OWN_WEBSITE channel registered as first `Marketplace` document (if D2 = yes)

**Not in scope:** Actual marketplace API calls (D3). Variant-aware listings (D1). Compliance gates (D8 partial).

---

### Phase 2 — Marketplaces BC Foundation
**Duration estimate: 1.5–2 cycles | Prerequisite: Phase 1 complete + D3, D6 answered**

**Deliverables:**
1. Marketplaces BC project scaffold (`src/Marketplaces/`, `src/Marketplaces/Marketplaces.Api/`)
2. `Marketplace` document CRUD with `ChannelCode` as stable identity
3. `CategoryMapping` document store (internal category → marketplace category)
4. `MarketplaceAttributeSchema` versioned document (D10)
5. Marketplace API adapter stubs (Amazon, Walmart, eBay) — adapters present but not wired to real APIs
6. `ListingSubmittedToMarketplace` consumer in Marketplaces BC → calls adapter stub → publishes `MarketplaceListingActivated`
7. Integration: Listings BC consumes `MarketplaceListingActivated` → `ListingActivated` domain event
8. `AttributeSchemaPublished` event + Listings BC reaction (flag drafts requiring review)

**Not in scope:** Real marketplace API credentials and live calls (Phase 3). Variant-aware listings (still D1).

---

### Phase 3 — Variants, Compliance, Real API Calls
**Duration estimate: 2–3 cycles | Prerequisite: D1 definitively answered + D8 answered**

**Deliverables:**
1. Variant model implementation in Product Catalog (per D1 decision)
2. `ProductVariantAdded` / `ProductVariantRemoved` domain events
3. Listings BC: variant-aware listing creation (list against child SKU with parent grouping)
4. Compliance metadata: `ComplianceMetadataSet` domain event, `HazmatClassificationChanged`
5. Compliance gate on listing submission (reject if marketplace requires hazmat class and it's missing)
6. Real marketplace API calls for at least one channel (Amazon US recommended — largest market)
7. Scheduled seasonal reactivation (Wolverine scheduled messages)
8. Lot/batch targeting for recall cascade (if D7 answered affirmatively)
9. Admin UI for Listings lifecycle management (MudBlazor components)

---

### Phase 3+ — Taxonomy, Automation, Scale
**Ongoing work — not time-bounded**

- Full structured category taxonomy BC or sub-domain within Product Catalog
- Category-to-marketplace mapping UI (manage mappings without code changes)
- Vendor-product association (`VendorAssociated` / `VendorDisassociated` events)
- Vendor Portal integration: vendor submits product content → catalog manager approval workflow
- Automated marketplace attribute schema refresh (poll marketplace APIs for schema changes)
- Multi-channel listing analytics (listing performance per channel)
- A/B testing for listing content (title variants, image ordering)

---

## 8. ADR Candidates

These ADRs must be written before Phase 0 implementation begins. They capture the "why" behind decisions that will otherwise be revisited by every developer who touches the codebase.

| ADR # | Decision | Covers |
|---|---|---|
| **0022** | Product Catalog BC migration to event sourcing | Why ES now (not at launch), migration strategy, `ProductMigrated` bootstrap event pattern |
| **0023** | `catalog:` namespace prefix for UUID v5 stream IDs | Extends ADR 0016 with Product Catalog BC's namespace, preventing collision with `pricing:` streams |
| **0024** | Granular Product Catalog integration messages (retire `ProductUpdated`) | Why `ProductContentUpdated` + `ProductCategoryChanged` replace `ProductUpdated`; backward compatibility strategy |
| **0025** | Recall cascade via dedicated priority RabbitMQ exchange | Why priority queue over synchronous call; near-synchronous vs. fully-synchronous trade-off; `ListingForcedDown` vs. normal `ListingPaused` |
| **0026** | Listings BC event-sourced aggregate with UUID v5 composite key | `listing:{sku}:{channelCode}` key design; why not UUID v7; why Listing is ES and Marketplace is not |
| **0027** | Marketplace identity as document entity (not enum/aggregate/config) | D4 decision rationale; `ChannelCode` stability contract; why event sourcing is not warranted for marketplace config |
| **0028** | Category-to-marketplace mapping ownership in Marketplaces BC | D5 decision; change-rate alignment; anti-corruption layer via `CategoryMappingView` local read model in Listings BC |
| **0029** | Listings BC local `ProductSummaryView` (no HTTP to Catalog) | Why Listings BC never calls Product Catalog HTTP API at runtime; how `ProductSummaryView` is maintained via integration messages |

---

*This document should be updated when Owner decisions D1–D10 are received. Convert phase plans into cycle-specific task breakdowns in `docs/planning/cycles/` once Owner gates are cleared.*

---

## Appendix A: Current Integration Message Deprecation Plan

```
Phase 0 (retire with migration):
  Messages.Contracts.ProductCatalog.ProductUpdated  → deprecated; consumers updated to
    ProductContentUpdated + ProductCategoryChanged

Phase 0 (enrich, not retire):
  Messages.Contracts.ProductCatalog.ProductAdded    → add Status, Brand, HasDimensions fields
  Messages.Contracts.ProductCatalog.ProductDiscontinued → add IsRecall, Reason fields

Phase 2 (new):
  Messages.Contracts.Listings.*    (new namespace)
  Messages.Contracts.Marketplaces.* (new namespace)
```

Backward compatibility: Pricing BC's `ProductAddedHandler` and Inventory BC's consumer of `ProductAdded` must be updated in the same cycle as the contract enrichment. This is a coordinated change across three BCs — plan accordingly.

---

## Appendix B: Domain Event to Integration Message Mapping

```
Product Catalog Domain Events → Integration Messages (1:1 unless noted)

ProductAdded           → ProductCatalog.ProductAdded
ProductMigrated        → (no integration message — internal bootstrap only)
ProductNameChanged     → ProductCatalog.ProductContentUpdated (merged with DescriptionUpdated)
ProductDescriptionUpdated → ProductCatalog.ProductContentUpdated (merged)
ProductCategoryAssigned → ProductCatalog.ProductCategoryChanged
ProductImagesUpdated   → ProductCatalog.ProductImagesUpdated
ProductTagsUpdated     → (optional — omit in Phase 0; add if CX needs search index refresh)
ProductDimensionsSet   → ProductCatalog.ProductDimensionsChanged
ProductActivated       → ProductCatalog.ProductStatusChanged (PreviousStatus, NewStatus=Active)
ProductDiscontinued    → ProductCatalog.ProductDiscontinued (IsRecall flag drives exchange routing)
ProductSetToComingSoon → ProductCatalog.ProductStatusChanged
ProductSetToOutOfSeason → ProductCatalog.ProductStatusChanged
ProductDeleted         → ProductCatalog.ProductDeleted
ProductRestored        → ProductCatalog.ProductRestored
```

---

## Appendix C: BC Schema Assignments

```
PostgreSQL shared instance — per-BC schema isolation:
  product_catalog   — Product aggregate event streams + ProductCatalogView projection
  listings          — Listing aggregate event streams + ListingsActiveView + ProductSummaryView
  marketplaces      — Marketplace documents + CategoryMapping + MarketplaceAttributeSchema
  pricing           — (existing) ProductPrice event streams + CurrentPriceView
  inventory         — (existing) ProductInventory streams
  orders            — (existing) Order streams
```

Each BC sets `opts.DatabaseSchemaName = "{bc_name}"` in its Marten configuration. No cross-schema queries at runtime. All cross-BC data flows through integration messages.

---

---

# Part C: UX Engineer Perspective
**Author:** UX Engineer  
**Date:** 2026-03-09  
**Context:** Response to PSA Architecture Evolution Plan — Product Catalog BC Event Sourcing Migration

---

## C.1 — UX Assessment of the Event Sourcing Decision

**Verdict: Strong Yes from the UX side — with important implementation guardrails.**

### Does granular event granularity map to user-visible features?

Yes, and more directly than the PSA's rationale suggests. The three highest-value user-visible capabilities that fall out of event sourcing for free:

1. **Product Change History timeline** — Every catalog manager has asked "who changed that description and when?" after a product shipped with bad copy. Right now we have no answer. With event sourcing, the answer is built into the aggregate. This is not a nice-to-have for a pet supply retailer: compliance teams need it for hazmat and recall documentation. Merchandising teams need it for seasonal planning audits. Customer service needs it when a customer calls about a product that "used to say" something it no longer says.

2. **Precise listing reaction** — When a catalog manager updates `ProductCategoryAssigned`, the admin UI can inform them *specifically* that "3 marketplace listings may need to be re-mapped to the new category." This is only possible if the system knows *what* changed, not just *that* something changed. Granular events are the mechanism; the user-visible output is confidence — the catalog manager knows exactly what downstream work is triggered.

3. **Recall cascade visibility** — `ProductDiscontinued` as a first-class domain event enables the admin UI to show a real-time cascade summary: "Discontinuing this product will force-end 12 active listings. Do you want to proceed?" This is the difference between a scary black-box operation and a transparent, controllable workflow. Users can make informed decisions. This is a foundational safety UX requirement for a retail product.

### Does the recall cascade design work from a UX standpoint?

Yes, but only if the admin UI is designed to surface it actively. The PSA is correct that the event routing is clean architecturally. The UX risk is that the cascade happens *correctly* but *invisibly* — the catalog manager clicks "Discontinue," something happens in the background, and they have no idea if 1 listing or 47 listings were affected.

**Required UX contract:**
- Before confirming `ProductDiscontinued`: show a count of affected active listings (optimistic read from current projections — no event sourcing magic needed here, just a query)
- After confirming: show a real-time progress indicator or summary of listings transitioned (see C.6 for SignalR design)
- After completion: show a permanent audit record in Product Change History

Owned by UX: the modal design, the progress/confirmation pattern, the summary view  
Owned by PSA/Engineer: the event routing, the cascade trigger, the projection updates

### Is the event stream valuable as a Product Change History view?

Yes. This is a first-class admin feature, not a developer debug tool. See C.4 for full design. The short answer: **surface it as a tab on every product detail page, not buried in a separate log viewer.**

---

## C.2 — Event Granularity: UX Business Use Cases

| Event | User-Facing Capability | Who Sees It | UX Notes |
|---|---|---|---|
| `ProductAdded` | Confirms new product creation; seeds the change history timeline | Catalog manager who created it; their team lead | Show as "Product created by [user]" in history — not raw event name |
| `ProductMigrated` | One-time — should NOT appear in the UI change history | System only | Filter this from all user-facing history views. It is a technical bootstrap artifact, not a business event. |
| `ProductNameChanged` | Content audit trail — "was this change approved?" link to content review workflow | Merchandising team, content reviewers | Show old name → new name diff inline in history. High value for SEO and compliance audits. |
| `ProductDescriptionUpdated` | Same as above; triggers content review queue entry if description exceeds a word-count change threshold | Content team | Consider a "significant change" heuristic: >30% text change = flag for review. UX can define the threshold; engineer implements the projection rule. |
| `ProductImagesUpdated` | Image audit trail; shows thumbnail(s) of what changed | Merchandising, creative team | This event needs a UX refinement (see below) — "updated" is ambiguous. |
| `ProductTagsUpdated` | SEO/search relevance change log | SEO manager, merchandising | Low urgency; useful in batch weekly summary |
| `ProductDimensionsSet` | Shipping cost recalculation trigger; fulfillment team alert | Fulfillment ops, finance | Should trigger a soft alert to fulfillment: "Dimensions changed — verify shipping rate tier" |
| `ProductCategoryAssigned` | Triggers listing re-mapping workflow — catalog manager sees inline prompt to review affected listings | Catalog manager, marketplace team | Most operationally significant event after discontinuation. See C.3. |
| `ProductBrandSet` | Brand association audit; useful when switching vendors or during brand consolidation | Merchandising, vendor management | Low urgency unless brand change is bulk (multiple SKUs) |
| `ProductActivated` | Product becomes visible and purchasable — triggers storefront listing check | Catalog manager, storefront team | Should show as a green "went live" marker in history timeline |
| `ProductDiscontinued` | Recall/discontinuation cascade workflow — see C.1 and C.3 | Catalog manager, compliance, ops | Highest-urgency event in the system. Requires modal confirmation and real-time cascade summary. |
| `ProductSetToOutOfSeason` | Seasonal management — catalog manager can batch-set products and schedule a reactivation date | Merchandising, seasonal planning team | Admin UI SHOULD offer a "reactivate on date" field alongside this transition. The event itself doesn't need to carry the reactivation date — a scheduled command can handle it — but the UX must make it easy to set. |
| `ProductSetToComingSoon` | Pre-launch workflow — enables "notify me" subscriptions, countdown displays on storefront PDP | Merchandising, marketing, storefront | Phase 1: admin sets Coming Soon, storefront shows status label. Phase 2: "notify me when available" subscription list builds from this. Marketing team should be notified when this status is set so they can plan launch content. |
| `ProductDeleted` | Permanent removal — requires explicit confirmation, shows in history with "deleted by [user]" | Catalog manager, admin | Hard delete vs. soft delete distinction must be clear to users. `ProductDeleted` should be near-irreversible in the UI (require typing the product name to confirm). |
| `ProductRestored` | Undo of deletion — recovery workflow for accidental deletes | Admin, catalog manager | Show as a recovery event in history. Rare but critical when it's needed. |
| `ProductVariantAdded` *(Phase 2)* | Variant management — parent product gains a new color/size/configuration; storefront selector updates | Merchandising, storefront | Admin UI needs a variant editor that feels like managing a family, not creating isolated products. Each variant addition should show a preview of the updated storefront selector. |

### UX Refinement Required: `ProductImagesUpdated`

"Updated" is too coarse. The admin UI and the change history view need to distinguish:

- **Image added** (existing images retained; 1 new added) → low-risk, show "+1 image" in history
- **Image replaced** (all images swapped) → high-risk, requires content review — show "all images replaced" with thumbnail comparison in history
- **Image removed** (product now has fewer images) → medium-risk, show count change

**Recommendation to PSA:** Consider splitting `ProductImagesUpdated` into `ProductImageAdded`, `ProductImageRemoved`, and `ProductImageSetReplaced`. The UX value of distinguishing these is significant — a content reviewer who sees "all images replaced" behaves very differently from one who sees "+1 supplemental image added." If splitting the event isn't desired in Phase 1, add a `ChangeType` attribute to `ProductImagesUpdated` with values `Added | Removed | FullReplacement`.

Owned by UX: defining the distinction and the UX behavior for each case  
Owned by PSA/Engineer: event schema decision

---

## C.3 — Listings Event Granularity: UX Implications

### Validating the `EndedReason` Attribute

The `EndedReason` attribute on `ListingEnded` is the right call. Here are the five most important values and how user behavior differs for each:

| EndedReason | User-Visible Label | Admin's Immediate Action |
|---|---|---|
| `RecallForced` | "Forced Down — Product Discontinued" | Review: can this listing be re-linked to a replacement product? Or permanently close. Urgent. |
| `CategoryMismatch` | "Ended — Category Mismatch" | Re-map to new category and resubmit. Admin needs a direct "re-list" affordance on this ended listing. |
| `MarketplaceRejected` | "Ended — Rejected by [Marketplace Name]" | Review marketplace rejection reason (which may come from an external API response), correct, and resubmit. |
| `ManualEnd` | "Ended — Manually Closed" | No action required. Informational. Show who ended it and when. |
| `ProductDeleted` | "Ended — Product Removed" | No action required. Listing cannot be restored unless product is restored first. |

**Design implication:** The Listing detail page should show `EndedReason` prominently — not buried in metadata. For `RecallForced` and `MarketplaceRejected`, show an inline call-to-action card, not just a status label.

### When a Listing Goes `Live`: What Notification Does the Catalog Manager Get?

**Recommendation: In-app toast + persistent notification bell.**

- Toast: "✓ [Product Name] listing is now Live on [Marketplace]" — dismissible after 5 seconds
- Notification bell: persists the event so they can review it later if they missed the toast
- Email: only if the listing had been in review for >24 hours (i.e., it's a delayed approval they may have forgotten about)

Do NOT send email for every listing going live — at scale, a catalog manager publishing 50 SKUs in a day would receive 50 emails. Email should be reserved for delayed or unexpected state changes.

### When a Listing is Forced Down by Recall: Admin Call-to-Action

This is the highest-stakes UX moment in the Listings admin. The catalog manager should see:

```
┌─────────────────────────────────────────────────────┐
│ ⚠ LISTING FORCED DOWN — PRODUCT DISCONTINUED        │
│                                                     │
│ "Himalayan Pink Salt Dog Treats" (SKU: HPT-2204)    │
│ was discontinued at 2:47 PM today.                  │
│                                                     │
│ This listing has been removed from Amazon US.       │
│                                                     │
│ [Review Discontinuation]  [Acknowledge & Dismiss]   │
└─────────────────────────────────────────────────────┘
```

"Review Discontinuation" navigates to the product detail page showing the `ProductDiscontinued` event in Change History, with any compliance notes the catalog manager added at discontinuation time.

"Acknowledge & Dismiss" marks the notification as reviewed. The listing itself remains in `Ended` state — dismissing the notification does NOT restore the listing.

If multiple listings are force-ended simultaneously (e.g., 12 listings across marketplaces), group them into a single notification: "12 listings force-ended due to [Product Name] discontinuation — [Review All]" rather than flooding the admin with 12 individual toasts.

### Is `Paused` vs. `Ended` Clear to Users?

Currently, no — without explicit UI treatment, users will conflate these. The distinction must be encoded in visual design and copy:

| State | Color/Badge | Copy | User Mental Model |
|---|---|---|---|
| `Paused` | Yellow / ⏸ Paused | "Temporarily hidden from marketplace" | "I can easily resume this" |
| `Ended` | Gray / ✖ Ended | "Closed — requires re-listing to restore" | "This is done; significant effort to bring back" |

The Paused state should always show a prominent "Resume Listing" button. The Ended state should show "Create New Listing from This Product" (not "restore," which implies it resumes the same listing ID — which it won't on most marketplaces).

For `RecallForced` ended listings specifically, the "Create New Listing" button should be disabled until the product status is no longer `Discontinued`.

---

## C.4 — Product Change History: A New Admin Feature

### Information Architecture

**Location:** Tab on the Product Detail page, labeled "History"  
**NOT:** A separate admin section, a developer log viewer, or a modal

Rationale: Catalog managers need change history in context of the product they're working on. Navigating away to a separate history section breaks the workflow. A tab keeps them in context.

```
┌─────────────────────────────────────────────────────────────────────┐
│  [← Back to Catalog]                                                │
│                                                                     │
│  Himalayan Pink Salt Dog Treats                    SKU: HPT-2204   │
│  ─────────────────────────────────────────────────────────────────  │
│  [Overview] [Images] [Listings] [Pricing] [History]                 │
│─────────────────────────────────────────────────────────────────────│
│                                                                     │
│  PRODUCT CHANGE HISTORY                                             │
│                                                                     │
│  Filter: [All Changes ▼]   [Date Range: Last 30 days ▼]   [Export] │
│                                                                     │
│  ● Today, 2:47 PM                                                   │
│    Product Discontinued                                             │
│    Reason: Supplier discontinued ingredient                         │
│    By: Sarah M. (Merchandising)                                     │
│    → 12 listings force-ended across 3 marketplaces                 │
│                                                                     │
│  ○ Jul 8, 10:14 AM                                                  │
│    Description Updated                                              │
│    Changed ~40% of description text                                 │
│    By: Content Team (via bulk import)                               │
│    [Show diff ▼]                                                    │
│                                                                     │
│  ○ Jul 2, 3:01 PM                                                   │
│    Category Reassigned                                              │
│    Dog Treats → Natural & Organic Dog Treats                        │
│    By: Jake L. (Catalog)                                            │
│    → 4 listings flagged for re-mapping                             │
│                                                                     │
│  ○ Jun 15, 9:00 AM                                                  │
│    Images Updated (Full Replacement)                                │
│    6 images replaced with 8 new images                              │
│    By: Creative Team                                                │
│    [View image comparison ▼]                                        │
│                                                                     │
│  ○ May 3, 11:22 AM                                                  │
│    Product Created                                                  │
│    By: Jake L. (Catalog)                                            │
│─────────────────────────────────────────────────────────────────────│
│  [Load earlier history]                                             │
└─────────────────────────────────────────────────────────────────────┘
```

### Information Density

**Show "significant" events by default; allow "all events" via filter.**

Default visible events (significant):
- Status changes (Activated, Discontinued, Out of Season, Coming Soon)
- Category reassignment
- Full image replacement
- Description updates (>30% change)
- Name changes

Hidden by default (visible via "All Changes" filter):
- Tag updates
- Minor dimension adjustments
- Individual image additions

This mirrors how email clients treat important vs. promotional — the underlying events are all stored, but the UX surfaces relevance, not volume.

### Category Reassignment vs. Name Fix in History

Category reassignment should display the from → to taxonomy path and call out downstream impact ("4 listings flagged"). A name fix should show the old name struck through and the new name, with no downstream impact note unless a name change triggers any listing review requirement on the target marketplace.

Visual weight: category reassignment gets a heavier card (border highlight). Name fix gets a lighter inline entry. The design system should have two history card weights: `significant` (border, impact note) and `minor` (no border, no impact note).

### Can a User "Revert" from History?

**Short answer: No. And say so clearly.**

Offering a "revert" button from a history view implies that reverting is safe and consequence-free. For an event-sourced product catalog, reversion is actually a new command — `ProductNameReverted` is still a new event, not an undo. And reverting a `ProductCategoryAssigned` would trigger another downstream cascade.

**Recommended approach:** Instead of "Revert," offer **"Copy values from this point in time"** — a read-only snapshot view of what the product looked like at that moment, with a "Use these values" button that pre-fills the edit form. The catalog manager then reviews and explicitly saves, creating new forward-moving events. This is transparent, auditable, and doesn't create a false "undo" mental model.

Owned by UX: the "Copy from history" pattern design  
Owned by PSA/Engineer: the product-at-point-in-time query (Marten's `AggregateStreamAsync` at a specific version handles this natively)

---

## C.5 — Marketplace Document Store: UX Fit

### Does the Document Store Design Work for Marketplace Config Management?

Yes — for the current scope of CritterSupply's marketplace management needs, a document store is the right fit. Marketplace configurations change rarely (perhaps a few times a month), the number of channels is small (5-10 active at any time), and the operations are administrative rather than high-frequency transactional.

The `ChannelCode` as a stable string identifier works cleanly in the admin UI:
- It can be used as a display key in dropdowns and tables without needing to resolve an opaque GUID
- It provides human-readable identifiers in API responses and debug logs
- It maps naturally to external marketplace identifiers

### What Would Be LOST Without an Event History for Marketplace Config?

This is the meaningful UX gap. Concrete scenarios that will occur in production:

1. **Fee structure changes:** "Amazon raised their category fee last quarter — when did we update our margin settings and did we catch all affected listings?" No event history = no answer.
2. **Configuration drift:** "Our Amazon US channel has different settings than Amazon CA — when did they diverge?" No history = audit by memory.
3. **Who changed the API credentials:** A security audit question. Without history, the answer is "someone at some point."
4. **Rollback after a bad config change:** If a fee rule change causes incorrect pricing on 200 listings, you need to know *exactly* what changed to undo it.

These scenarios are real operational risks for a retail business, not edge cases.

### Recommendation: Lightweight Audit Log on Marketplace Documents

The PSA should add a lightweight `AuditLog` embedded document on each Marketplace document — an append-only list of `{Timestamp, UserId, FieldChanged, OldValue, NewValue, Note}` records. This is NOT full event sourcing — it's a pragmatic audit trail that requires no architectural shift.

At 5-10 marketplace channels with config changes a few times per month, the audit log stays small indefinitely. The UX benefit (answering "who changed what and when") is immediate.

**What the UX surface looks like:** A "Configuration History" tab on the Marketplace detail page in admin, identical in structure to the Product Change History tab but lighter in implementation.

Owned by UX: tab design and display requirements  
Owned by PSA/Engineer: `AuditLog` embedded document schema, update-on-write mechanism

### Adding a New Marketplace Channel: UX for `ChannelCode` Design

The catalog manager adding `KROGER_US` as a new channel needs a guided workflow, not a raw "create document" form:

```
┌─────────────────────────────────────────────────────┐
│  ADD NEW MARKETPLACE CHANNEL                        │
│                                                     │
│  Channel Code *                                     │
│  [KROGER_US                          ]              │
│  Use: BRAND_REGION format (e.g. AMAZON_US)          │
│                                                     │
│  Display Name *                                     │
│  [Kroger Marketplace (United States) ]              │
│                                                     │
│  Channel Type *                                     │
│  ○ Third-party Marketplace  ● Own Website           │
│  ○ Wholesale Portal                                 │
│                                                     │
│  Region *  [United States ▼]                        │
│                                                     │
│  ─────────────────────────────────────────────────  │
│  [Cancel]                        [Create Channel]   │
└─────────────────────────────────────────────────────┘
```

Key UX requirements:
- `ChannelCode` field: auto-format to UPPERCASE, replace spaces with underscores, show format hint
- Prevent duplicate `ChannelCode` — inline validation on blur (check against existing codes)
- "Display Name" is what appears in the admin UI everywhere — the `ChannelCode` is the system key; show it in a monospace font to signal "technical identifier"
- After creation, immediately navigate to the new channel's configuration page so the manager can set it up without a separate navigation step

---

## C.6 — Real-Time Admin Notifications (SignalR)

### Immediate (Real-Time Push Required)

These events require admin attention now. Use a persistent notification panel (not just a toast) that survives page navigation.

| Event | Toast Content | Action |
|---|---|---|
| `ProductDiscontinued` | "⚠ [Product Name] discontinued — [N] listings force-ended" | [Review Listings] |
| `ListingEnded` with `EndedReason = MarketplaceRejected` | "✖ [Product] listing rejected by [Marketplace] — action needed" | [View Rejection] |
| `ListingEnded` with `EndedReason = RecallForced` | "⚠ [Product] listing removed due to discontinuation" | [Review] |
| `ProductRestored` | "✓ [Product] restored — [N] listings ready to re-publish" | [Review Listings] |

**Toast design spec:**
- Position: bottom-right, stacked if multiple
- Duration: 8 seconds (longer than typical 3s because these require a decision)
- Color: Red/amber for warnings, green for positive confirmations
- If 3+ toasts would stack: collapse into "3 new notifications — [View All]" to avoid overwhelming the screen

### Background (Batch or Periodic)

These events can wait for a dashboard widget refresh or a notification bell count update. Do NOT push as toasts.

- `ProductCategoryAssigned` — notify via dashboard "Listings needing re-map" counter
- `ListingLive` — notification bell count, daily email digest (opt-in)
- `ProductActivated` — notification bell only
- `ProductImagesUpdated` — dashboard "Recent Changes" feed
- `ProductDescriptionUpdated` — dashboard "Pending content review" queue if threshold exceeded

### Silent (Log to History, No Notification)

- `ProductTagsUpdated`
- `ProductDimensionsSet`
- `ProductBrandSet`
- `ProductMigrated` (never surface this to users)
- `ProductNameChanged` (unless a compliance review workflow is triggered — then background)

---

## C.7 — UX Risk Register: Event Sourcing Specific Risks

### Risk 1: Product Change History Becomes Overwhelming
**Trigger:** An active SKU that's been in the catalog for 2 years may have hundreds of events — every seasonal status change, every image update, every tag tweak.  
**User impact:** The History tab becomes unusable noise. Catalog managers stop checking it.  
**Mitigation:** Default to "Significant events only" filter (as defined in C.4). Implement event categorization in the projection — each event gets a `Significance` attribute (`Major | Minor | System`). The UX filters on this, not on event type. This categorization must be defined collaboratively by UX and Engineering — it cannot be an afterthought.  
**Owned by:** UX (significance taxonomy), Engineer (projection attribute)

### Risk 2: Recall Cascade UI Creates Panic
**Trigger:** A catalog manager discontinues one product. Simultaneously, the notification panel fills with 12-47 forced-ended listing notifications across multiple marketplaces.  
**User impact:** Cognitive overload. The manager may not know if the system is behaving correctly or something has gone wrong. They may attempt to manually undo by restoring the product (creating a bad state).  
**Mitigation:** Before confirming `ProductDiscontinued`, show a pre-flight summary: "This will force-end 12 active listings across 3 marketplaces. This cannot be immediately undone." Post-cascade, show a single grouped summary notification — not 12 individual ones. The grouped notification expands to show the full list.  
**Owned by:** UX (modal design, grouped notification design), Engineer (pre-flight count query, grouping logic)

### Risk 3: Eventual Consistency Creates Stale History
**Trigger:** A catalog manager makes a change, immediately clicks the History tab, and does not see their change listed.  
**User impact:** They assume the save failed and repeat the action, creating duplicate events.  
**Mitigation:** After any write command, the admin UI should show an optimistic "Change saved — history updating..." indicator on the History tab. This sets correct expectations without requiring the projection to be synchronously updated. The tab refreshes automatically after a 2-3 second delay.  
**Owned by:** UX (loading state design), Engineer (optimistic UI implementation)

### Risk 4: `ProductMigrated` Event Leaks Into User-Facing Views
**Trigger:** The one-time bootstrap migration emits a `ProductMigrated` event for every existing SKU. If filtering is not implemented correctly, catalog managers will see "Product Migrated" as the first history entry for every product — a confusing and meaningless artifact.  
**Mitigation:** `ProductMigrated` must be explicitly excluded from all user-facing projections and history views from day one. This is a build-time requirement, not a post-launch cleanup. The filtering rule should be documented in the admin UI spec and enforced in the history projection.  
**Owned by:** UX (specification), Engineer (projection filter implementation)

### Risk 5: Status Label Confusion Between Product Status and Listing Status
**Trigger:** A product can be `Active` in the Product Catalog while one of its listings is `Paused` on Amazon. A catalog manager looking at the product overview may see "Active" and assume the product is selling everywhere — missing the paused listing.  
**User impact:** Invisible lost sales. Paused listings go unnoticed.  
**Mitigation:** The Product Overview page must show a Listings Summary widget: aggregate status counts across all marketplaces. "2 Live, 1 Paused, 0 Ended" — so the product-level status never implies marketplace-level health.  
**Owned by:** UX (widget design), Engineer (cross-BC read model — Product + Listings projection)

---

## C.8 — UX Recommendations: Admin UI for Event-Sourced Product Catalog

### P0 — Required Before Launch

**P0.1: Pre-flight confirmation modal for `ProductDiscontinued`**  
The single highest-stakes operation in the system. Must show affected listing count, marketplace breakdown, and require explicit confirmation before the command is submitted. No exceptions.

**P0.2: Grouped cascade notification for recall events**  
Individual per-listing toasts for a recall cascade will create a broken admin experience. Implement grouped notification logic from day one — this is easier to build correctly than to retrofit. UX to provide the grouping spec; Engineer to implement in the SignalR hub.

**P0.3: Filter `ProductMigrated` from all user-facing history views**  
Build-time requirement. If this ships without the filter, every product's history will start with a confusing "Product Migrated" entry. Document the filter requirement in the projection spec.

### P1 — Required Within First Iteration

**P1.1: Product Change History tab with significance filtering**  
Build the History tab as described in C.4. Default to significant events. Include the "All Changes" filter. This is the primary user-visible benefit of event sourcing — ship it, don't defer it.

**P1.2: Listings Summary widget on Product Overview**  
Cross-BC read model showing aggregate listing status per product. Prevents the "product is Active but listing is Paused" blind spot identified in C.7 Risk 5.

**P1.3: `Paused` vs. `Ended` visual differentiation in Listings admin**  
Implement the color/badge/copy distinctions described in C.3 before launch. Once users form a mental model around these states, retroactively changing the visual design is expensive.

### P2 — High-Value, Next Iteration

**P2.1: "Copy values from history" pattern for quasi-revert**  
As described in C.4 — not a true revert, but a "pre-fill from historical state" workflow. High value for seasonal products that cycle between states annually (Out of Season → Active → Out of Season).

**P2.2: Lightweight audit log on Marketplace documents**  
As described in C.5 — an embedded `AuditLog` on each Marketplace document, surfaced as a "Configuration History" tab in admin. Addresses the operational audit gap without requiring full event sourcing for Marketplaces.

**P2.3: `ProductSetToOutOfSeason` reactivation scheduling**  
Add a "Reactivate on date" field to the Out of Season transition workflow. The event itself doesn't carry the date — a scheduled command handles it — but the UX must make it easy to set at transition time so merchandisers don't have to remember to manually re-activate seasonal products.

---

*End of Part C — UX Engineer Perspective*  
*This section to be reviewed by PSA for cross-cutting implementation dependencies before the architecture document is finalized.*