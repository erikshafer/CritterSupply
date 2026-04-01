# ADR 0050: Marketplaces ProductSummaryView Anti-Corruption Layer

**Status:** ✅ Accepted

**Date:** 2026-04-01

**Context:**

During M36.1 Phase 2 (Session 7), the `ListingApprovedHandler` in the Marketplaces BC was implemented with a deliberate tradeoff: the `ListingApproved` integration message from the Listings BC carried enriched product data fields (`ProductName`, `Category`, `Price`) directly in the message payload. This allowed the handler to build marketplace submissions without any local product state, accelerating Phase 2 delivery.

This tradeoff was documented in the Session 7 retrospective and later captured in ADR 0049's risk section as a known coupling issue. The Marketplaces BC was directly dependent on the Listings BC's message enrichment for product data — meaning:

1. **Coupling to Listings BC message shape:** If the Listings BC changed or removed the enriched fields, the Marketplaces BC would silently receive null/default values with no local fallback.
2. **Category taxonomy coupling (ADR 0049 risk):** The `Category` field passed through two BC boundaries (Product Catalog → Listings → Marketplaces) as a raw string. If Product Catalog renamed a category, the Marketplaces BC would silently fail category mapping lookups with no visibility into the root cause.
3. **No independent data verification:** The Marketplaces BC could not verify product status (e.g., whether a product was discontinued) before submitting to external channels.

M37.0 Session 1 resolves this debt by introducing a local `ProductSummaryView` anti-corruption layer (ACL) in the Marketplaces BC.

**Decisions:**

### 1. Marketplaces BC Maintains Its Own ProductSummaryView ACL

A new `ProductSummaryView` Marten document in the Marketplaces domain project (`src/Marketplaces/Marketplaces/Products/ProductSummaryView.cs`) stores product data needed for listing submissions. The document is keyed by SKU (string Id) and maintained exclusively by integration event handlers — never by HTTP calls or direct Listings BC data.

```csharp
public sealed class ProductSummaryView
{
    public string Id { get; set; } = null!;          // SKU
    public string ProductName { get; set; } = null!;
    public string? Category { get; set; }
    public decimal? BasePrice { get; set; }
    public ProductSummaryStatus Status { get; set; }
}
```

**Rationale:** Each BC owns its own anti-corruption layer. The Listings BC already has its own `ProductSummaryView` with a broader field set (Description, Brand, HasDimensions, ImageUrls) suited to its listing lifecycle needs. The Marketplaces BC needs only the fields required for marketplace adapter submissions: product name, category (for mapping lookups), base price, and status. Separate views ensure each BC can evolve its data needs independently.

### 2. Subscribe to Four Product Catalog Integration Events

The Marketplaces BC subscribes to a targeted subset of Product Catalog events:

| Event | Handler | Field Updated |
|-------|---------|---------------|
| `ProductAdded` | `ProductAddedHandler` | Creates view with ProductName, Category, Status |
| `ProductContentUpdated` | `ProductContentUpdatedHandler` | Updates ProductName |
| `ProductCategoryChanged` | `ProductCategoryChangedHandler` | Updates Category |
| `ProductStatusChanged` | `ProductStatusChangedHandler` | Updates Status |

**Events not subscribed to (and why):**

- `ProductImagesUpdated` — Marketplaces BC does not submit images via the adapter interface; images are not part of `ListingSubmission`.
- `ProductDimensionsChanged` — Shipping dimensions are not used in the current adapter submission flow.
- `ProductDeleted` / `ProductRestored` — The Marketplaces BC relies on `ProductStatusChanged` for lifecycle transitions. If a product is deleted and then restored, the status change events cover the transition. Future work may add explicit deletion handling if needed.
- `ProductDiscontinued` — Handled by the `ProductStatusChanged` event when status transitions to "Discontinued". The recall cascade concern is owned by the Listings BC, not Marketplaces.

This is intentionally fewer than the Listings BC's 9 subscriptions — each BC subscribes to exactly what it needs.

### 3. RabbitMQ Queue: `marketplaces-product-catalog-events`

A dedicated queue (`marketplaces-product-catalog-events`) is configured in `Marketplaces.Api/Program.cs` alongside the existing `marketplaces-listings-events` queue. This follows the naming convention established by the Listings BC (`listings-product-catalog-events`).

### 4. ListingApprovedHandler Queries Local View — Zero Message Payload Reads

After the ACL is in place, `ListingApprovedHandler` loads the `ProductSummaryView` by SKU and sources all product data from it:

- `ProductName` → `productSummary.ProductName`
- `Category` → `productSummary.Category` (used for category mapping lookup)
- `Price` → `productSummary.BasePrice`

The handler reads only `ListingId`, `Sku`, and `ChannelCode` from the `ListingApproved` message — these are listing identifiers, not product data. If the `ProductSummaryView` is not found, the handler publishes `MarketplaceSubmissionRejected` with a clear reason.

The `ListingApproved` message contract is **not modified** in this session. The enriched fields remain on the record for backward compatibility and for any other consumers. Removing them is a separate decision for a future ADR if desired.

### 5. Known Gap: BasePrice Not Populated from Product Catalog Events

The `ProductAdded` event from Product Catalog does not carry a price field — pricing is owned by the Pricing BC. The `BasePrice` field on `ProductSummaryView` is currently `null` for all products created via the `ProductAdded` handler.

The `ListingApprovedHandler` falls back to `0m` when `BasePrice` is null (`productSummary.BasePrice ?? 0m`), which matches the previous behavior (`message.Price ?? 0m`).

**Future resolution options:**
- Subscribe to a Pricing BC event (e.g., `BasePriceSet`) when Pricing BC publishes one
- Accept that the `ListingApproved` message's `Price` field (from Listings BC) remains the primary price source until a Pricing BC integration is built
- Add a Marketplaces API endpoint for manual price override per SKU

This gap is acceptable for the current stub adapter phase. Real production adapters (M37.0 Session 2+) will need to resolve pricing before going live.

**Alternatives Considered:**

1. **Reuse Listings BC ProductSummaryView directly** — Rejected. This would create a runtime dependency on the Listings BC's database, violating bounded context isolation. Each BC must own its data.

2. **Subscribe to all 9 Product Catalog events (matching Listings BC)** — Rejected. Subscribing to events the Marketplaces BC doesn't need adds handler maintenance burden and queue message volume with no benefit. The four selected events cover all fields used by `ListingApprovedHandler`.

3. **Keep using message enrichment and defer the ACL** — Rejected. The coupling risk identified in ADR 0049 grows with each new adapter. Phase 3 production adapters should build on a clean foundation, not inherit M36.1 shortcuts.

4. **Query Listings BC via HTTP for product data** — Rejected. This creates a synchronous runtime dependency between BCs. If Listings BC is unavailable, Marketplaces BC cannot process submissions. The asynchronous ACL pattern is more resilient.

**Consequences:**

- Category taxonomy changes in Product Catalog are now propagated to Marketplaces BC via `ProductCategoryChanged`, resolving the silent-break risk documented in ADR 0049.
- `ListingApprovedHandler` has zero coupling to the Listings BC's message enrichment — it reads all product data from local state.
- The `ProductSummaryView` must be populated before a `ListingApproved` message can be processed successfully. If event ordering causes `ListingApproved` to arrive before `ProductAdded`, the handler publishes a clear rejection. This is expected to be rare in practice since products are created in the catalog long before listings are submitted.
- Adding new product data fields to marketplace submissions requires only updating the `ProductSummaryView` and its handlers — no changes to the Listings BC or the `ListingApproved` message contract.
- Pricing data has a known gap (see Decision 5) that must be resolved before production adapter go-live.

**References:**

- `src/Marketplaces/Marketplaces/Products/ProductSummaryView.cs` — ACL document
- `src/Marketplaces/Marketplaces/Products/ProductSummaryHandlers.cs` — Event handlers
- `src/Marketplaces/Marketplaces.Api/Listings/ListingApprovedHandler.cs` — Updated handler
- `src/Marketplaces/Marketplaces.Api/Program.cs` — RabbitMQ queue + Marten schema registration
- `docs/decisions/0049-category-mapping-ownership.md` — Category coupling risk that motivated this ACL
- `src/Listings/Listings/ProductSummary/ProductSummaryView.cs` — Listings BC's separate ACL (for comparison)
