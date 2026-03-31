# M36.1 Milestone Closure Retrospective

**Status:** ✅ Complete
**Date:** 2026-03-31
**Sessions:** 10 (Sessions 1–9 implementation; Session 10 closure)
**Duration:** 2026-03-29 → 2026-03-31

---

## 1. Goal Statement and Outcome

**Goal:** Deliver the Listings BC and Marketplaces BC as production-ready foundations within the CritterSupply reference architecture — establishing event-sourced listing lifecycle management, an anti-corruption layer against Product Catalog, marketplace channel configuration, stub adapter infrastructure for future real integrations, and Backoffice admin UI coverage with full E2E test support.

**Outcome: M36.1 is complete.** Both phase gates passed:

- **Phase 1 gate (Session 4):** 12/12 criteria met. Listings BC fully operational with event-sourced aggregate, ProductSummaryView ACL, recall cascade, review workflow, HTTP endpoints, Backoffice admin pages, and 35 integration tests passing.
- **Phase 2 gate (Session 9):** 16/16 criteria met. Marketplaces BC fully operational with Marketplace and CategoryMapping documents, CRUD handlers, 3 stub adapters, ListingApproved consumer, IVaultClient pattern, Backoffice admin pages, 27 integration tests passing, 6 E2E scenarios written, and ADRs 0048–0049 authored.

Final build state: 0 errors, 33 warnings (all pre-existing). 62 integration tests passing (35 Listings + 27 Marketplaces). 6 marketplace E2E scenarios authored.

---

## 2. What Was Delivered — By Phase

### Phase 1: Listings BC Foundation (Sessions 1–5)

**Scope:** A new event-sourced bounded context managing the listing lifecycle — from draft creation through review, activation, pause, resume, and end — with cross-BC integration for product catalog changes and product recall cascades.

**Key deliverables:**

- **Listing aggregate** — Event-sourced state machine with 8 domain events covering the full lifecycle (Draft → ReadyForReview → Submitted → Live → Paused → Ended). Stream IDs use UUID v5 deterministic keys from `listing:{sku}:{channelCode}`.
- **ProductSummaryView ACL** — Anti-corruption layer consuming 9 granular Product Catalog integration events (ProductAdded, ProductContentUpdated, ProductCategoryChanged, ProductImagesUpdated, ProductDimensionsChanged, ProductStatusChanged, ProductDeleted, ProductRestored, ProductDiscontinued) into a Marten document keyed by SKU. Decouples Listings from Product Catalog internal status values via a ProductSummaryStatus enum.
- **Recall cascade handler** — Consumes ProductDiscontinued (IsRecall=true), force-downs all active listings for the affected SKU, publishes ListingsCascadeCompleted.
- **Review workflow** — SubmitListingForReview (Draft → ReadyForReview) and ApproveListing (ReadyForReview → Submitted, publishes enriched ListingApproved).
- **Content propagation** — Consumes ProductContentUpdated and appends ListingContentUpdated only to Live listings, avoiding redundant events for Draft/Paused states.
- **9 HTTP endpoints** — Full CRUD plus lifecycle actions, all with [Authorize].
- **Backoffice admin UI** — Listings table with status filter and pagination, listing detail page (read-only), ListingStatusBadge component, PreflightDiscontinuationModal integrated into Product Edit page.
- **E2E infrastructure** — StubListingsApiHost, WasmStaticFileHost dynamic appsettings injection, ListingsAdminPage and ListingDetailPage page objects, ListingsAdmin.feature and ListingsDetail.feature files with step definitions.
- **Phase 0 prerequisites** — 7 new granular integration contracts replacing the deprecated ProductUpdated message, product-recall priority exchange, 10 new Product Catalog behavior tests.

**Gate:** 12/12 criteria met at Session 4. 35 integration tests passing.

### Phase 2: Marketplaces BC Foundation (Sessions 6–9)

**Scope:** A new document-store bounded context managing marketplace channel configurations and category mappings, with stub adapter infrastructure for future real-marketplace integrations (Amazon, Walmart, eBay) and a consumer for the ListingApproved event from the Listings BC.

**Key deliverables:**

- **Marketplace document entity** — Marten document with ChannelCode as natural key (Id), operational fields for display, activation state, vault credential paths, and OWN_WEBSITE exclusion (PO decision: internal fast-path only, not a marketplace).
- **CategoryMapping document** — Composite key `{ChannelCode}:{InternalCategory}`, 18 seed mappings (6 categories × 3 channels: AMAZON_US, WALMART_US, EBAY_US).
- **CRUD handlers** — RegisterMarketplace (idempotent by ChannelCode), UpdateMarketplace, DeactivateMarketplace, GetMarketplace, ListMarketplaces, SetCategoryMapping (upsert), GetCategoryMapping, ListCategoryMappings (with channel filter).
- **IMarketplaceAdapter interface + 3 stubs** — SubmitListingAsync, CheckSubmissionStatusAsync, DeactivateListingAsync. StubAmazonAdapter, StubWalmartAdapter, StubEbayAdapter with realistic ExternalSubmissionId prefixes.
- **IVaultClient + DevVaultClient** — Configuration-backed stub with production safety guard (throws in non-Development environments).
- **ListingApproved consumer** — Validates channel/category/marketplace/adapter, calls adapter, publishes MarketplaceListingActivated or MarketplaceSubmissionRejected. Guard rails: OWN_WEBSITE skipped, missing mappings rejected, inactive marketplaces rejected.
- **Integration message publishing** — RegisterMarketplace publishes MarketplaceRegistered (new registrations only), DeactivateMarketplace publishes MarketplaceDeactivated (active→inactive transitions only).
- **Backoffice admin UI** — MarketplacesList page (channel code, display name, active badge), CategoryMappingsList page (with channel filter MudSelect), nav menu entries under ProductManager policy.
- **E2E coverage** — StubMarketplacesApiHost, MarketplacesListPage and CategoryMappingsListPage page objects, MarketplacesAdmin.feature with 6 scenarios and 13 step definitions.
- **Cross-cutting test fixes (Session 8)** — TestAuthHandler now checks for Authorization header (centralized fix across 8 fixtures), reseed-on-verify pattern for seed data tests.

**Gate:** 14/16 at Session 8 (E2E + ADRs deferred), 16/16 at Session 9. 27 integration tests passing.

---

## 3. Architectural Decisions Made

| ADR | Title | Decision |
|-----|-------|----------|
| 0040 | `*Requested` Integration Event Convention | Integration events that request action from another BC use the `*Requested` suffix to distinguish from informational events. |
| 0041 | Product Catalog ES Migration Decisions | Event-sourced Product Catalog uses inline ProjectionLifecycle for ProductCatalogView, deterministic UUID v5 stream IDs, and retains the legacy document store model for migration support. |
| 0042 | `catalog:` Namespace UUID v5 Convention | Marten stream IDs for Product Catalog use UUID v5 with `catalog:` namespace prefix for deterministic, collision-resistant identifiers. |
| 0048 | Marketplace Document Entity Design | Marketplaces uses Marten document store (not event sourcing) for configuration data. Id = ChannelCode as natural key. Sealed class with mutable operational fields. OWN_WEBSITE excluded. |
| 0049 | Category Mapping Ownership | Category mappings owned by Marketplaces BC with composite key `{ChannelCode}:{InternalCategory}`. Coupling risk to Product Catalog category taxonomy documented with `ProductSummaryView` ACL mitigation planned for M37.x. |

**Note:** ADRs 0044–0047 were reserved during Phase 1 planning but not used. ADR 0043 (Storefront Web Technology Options) was authored outside M36.1.

---

## 4. Key Lessons Learned

**1. Shared test utilities need explicit behavior documentation or targeted tests.**

The `TestAuthHandler` unconditional authentication bug — where it always returned success regardless of whether an Authorization header was present — existed since M36.0's auth hardening track and was not caught until Session 8. This meant `[Authorize]` endpoints appeared to work in tests when they would correctly reject unauthenticated requests in production. The fix was straightforward (check for header presence), but the bug survived across an entire milestone boundary because test utilities are rarely re-examined once established. Future milestones should treat shared test infrastructure changes with the same verification rigor as production code.

**2. ADRs for decisions already made should be written within the session they were decided, not deferred.**

ADRs 0048 and 0049 were first identified as deliverables in the Phase 2 plan (Session 5), discussed substantively during Sessions 6–7 when the entities were designed, but not written until Session 9 — a 3-session deferral. This created risk of institutional knowledge loss and forced the Session 9 author to reconstruct rationale from session retrospectives rather than from fresh context. The deferral also blocked Phase 2 gate closure for two sessions. Write ADRs in the session where the decision is made, even if the implementation continues.

**3. Deliberate tradeoffs must be captured in ADRs or planning documents, not just retrospectives.**

The `ListingApproved` message enrichment shortcut — carrying `ProductName`, `Category`, and `Price` directly in the integration message rather than having Marketplaces query its own `ProductSummaryView` — was a deliberate tradeoff documented only in the Session 7 retrospective. Retrospectives are per-session artifacts that future agents may not read. The tradeoff was eventually captured in ADR 0049's risk section, but only because it was explicitly called out in Session 9. Tradeoffs that create future debt should be recorded in a durable location (ADR, planning doc, or debt table) at the time the decision is made.

**4. New API integrations into Backoffice.Web require a dynamic appsettings checklist item.**

The `WasmStaticFileHost` dynamic appsettings gap — where `MarketplacesApiUrl` was missing from the E2E test fixture's appsettings override — would have silently allowed E2E tests to run against a non-existent default URL. It was caught only because E2E tests were explicitly added in Session 9. Whenever a new named HttpClient is added to Backoffice.Web (or any Blazor WASM frontend), the corresponding E2E test fixture must be updated to inject the test URL. This should be a standard checklist item alongside CORS configuration and nav menu updates.

**5. The reseed-on-verify pattern is more resilient than relying on test execution order.**

Seed data tests failed non-deterministically in Session 7 because another test class called `CleanAllDocumentsAsync` in its disposal, wiping seed data before the seed verification tests ran. The fix — a dedicated `SeedDataTests` class that calls `ReseedAsync()` in `InitializeAsync()` — makes tests order-independent. Any BC with seed data should follow this pattern from the start rather than discovering it after failures.

---

## 5. Known Technical Debt and Deferred Items

| Item | Documented In | Target |
|------|--------------|--------|
| `ListingApproved` message enrichment — replace direct field carrying with `ProductSummaryView` ACL query in Marketplaces BC | Session 7 retro, ADR 0049 risk section | M37.x |
| E2E CI execution — MarketplacesAdmin.feature and ListingsAdmin.feature missing `@shard-X` tags; scenarios not discovered by CI shard runners | Session 9 retro, Session 10 CI verification | M37.x Session 1 |
| Category taxonomy coupling — silent break if Product Catalog renames categories without Marketplaces BC awareness | ADR 0049 risk section | M37.x |
| Phase 3 production adapters — real Amazon/Walmart/eBay API implementations replacing stubs | Phase 2 plan (Phase 3 section) | M37.x |
| Product Catalog `*ES` naming cleanup — vestigial ES suffix removed from 13 files and 5 classes in Session 10 | M36.0 Session 6 retro, Session 10 | ✅ Resolved in Session 10 |
| Redundant `SaveChangesAsync()` calls in Product Catalog — 2 remaining calls in UpdateProductTags and MigrateProduct removed in Session 10 | M36.0 Session 6 retro | ✅ Resolved in Session 10 |
| Listings admin action buttons — approve/pause/end buttons are disabled stubs on the detail page | Session 4 retro | M37.x or later |
| ListingsDetail.feature `@wip` scenarios — 3 scenarios tagged @wip (action button flows) | Session 4 retro | M37.x or later |
| Bidirectional marketplace feedback — Listings BC consuming MarketplaceListingActivated / MarketplaceSubmissionRejected | Phase 2 plan deferred scope | M37.x |

---

## 6. What M37.x Inherits

### Codebase State

Two new bounded contexts are fully operational:

- **Listings BC** (`src/Listings/`) — Event-sourced aggregate with full lifecycle, ProductSummaryView ACL, recall cascade, 9 HTTP endpoints, Backoffice admin pages. Port 5246, database schema `listings`.
- **Marketplaces BC** (`src/Marketplaces/`) — Document-store configuration entities, 3 stub adapters, ListingApproved consumer, IVaultClient pattern, Backoffice admin pages. Port 5247, database schema `marketplaces`.

Both BCs are registered in the solution file, Docker Compose, and the Aspire AppHost. All integration message contracts are in `src/Shared/Messages.Contracts/`.

### Test Baseline

- **62 integration tests** (35 Listings + 27 Marketplaces) — all passing, 0 failures
- **6 E2E scenarios** (MarketplacesAdmin.feature) — authored and locally verified; **not yet executing in CI** (missing `@shard-X` tag)
- **4+ E2E scenarios** (ListingsAdmin.feature, ListingsDetail.feature) — authored; also missing shard tags
- **Build:** 0 errors, 33 warnings (all pre-existing)

### Open Debt Items

See Section 5 above. The highest-priority items for M37.x Session 1:

1. **Add `@shard-3` tags** to MarketplacesAdmin.feature and ListingsAdmin.feature/ListingsDetail.feature to enable CI execution
2. **Replace `ListingApproved` message enrichment** with Marketplaces-local `ProductSummaryView` ACL query (eliminates coupling to Listings message payload)
3. **Address category taxonomy coupling** (ADR 0049) — Marketplaces BC should not silently break if Product Catalog renames categories

### Phase 3 Requirements

Phase 3 (production adapter implementations) requires:

- Real API client implementations for Amazon SP-API, Walmart Marketplace API, and eBay Sell API, replacing the 3 stub adapters
- Production `IVaultClient` implementation (replacing DevVaultClient) for secure credential storage
- Submission status polling or webhook infrastructure for asynchronous marketplace review queues
- Rate limiting, retry policies, and circuit breaker patterns for external API calls
- The `ProductSummaryView` ACL in Marketplaces BC (debt item above) should be resolved before production adapters are built, to ensure listing submissions use locally-cached product data

### Next ADR Number

**0050** — the next available ADR number.

---

## ES Naming Audit (Product Catalog Cleanup)

**Context:** When Product Catalog was migrated to event sourcing in M35.0, handler files were given an `ES` suffix to distinguish them from the original document-store implementations during migration. The originals have since been removed. Session 10 performed the cleanup.

**Files found (13):**

| Original File | Renamed To | Handler Class Change |
|---|---|---|
| CreateProductES.cs | CreateProduct.cs | (class was already `CreateProductHandler` — no change) |
| GetProductES.cs | GetProduct.cs | `GetProductESHandler` → `GetProductHandler` |
| ListProductsES.cs | ListProducts.cs | `ListProductsESHandler` → `ListProductsHandler` |
| ChangeProductNameES.cs | ChangeProductName.cs | (class was already `ChangeProductNameHandler` — no change) |
| ChangeProductDescriptionES.cs | ChangeProductDescription.cs | (class was already `ChangeProductDescriptionHandler` — no change) |
| ChangeProductCategoryES.cs | ChangeProductCategory.cs | (class was already `ChangeProductCategoryHandler` — no change) |
| ChangeProductDimensionsES.cs | ChangeProductDimensions.cs | (class was already `ChangeProductDimensionsHandler` — no change) |
| ChangeProductStatusES.cs | ChangeProductStatus.cs | `ChangeProductStatusESHandler` → `ChangeProductStatusHandler` |
| UpdateProductImagesES.cs | UpdateProductImages.cs | (class was already `UpdateProductImagesHandler` — no change) |
| UpdateProductTagsES.cs | UpdateProductTags.cs | (class was already `UpdateProductTagsHandler` — no change) |
| SoftDeleteProductES.cs | SoftDeleteProduct.cs | `SoftDeleteProductESHandler` → `SoftDeleteProductHandler` |
| RestoreProductES.cs | RestoreProduct.cs | `RestoreProductESHandler` → `RestoreProductHandler` |
| MigrateProductES.cs | MigrateProduct.cs | (class was already `MigrateProductHandler` — no change) |

**SaveChangesAsync cleanup:** 2 explicit `SaveChangesAsync()` calls found and removed (UpdateProductTags, MigrateProduct). Both are redundant with Wolverine's `AutoApplyTransactions()` policy configured in Program.cs.

**Test reference updated:** Comment in `ListProductsTests.cs` updated from `ListProductsESHandler` to `ListProductsHandler`.

**Verification:** Solution builds with 0 errors. All 62 integration tests pass. No remaining `ES`-suffixed class or file names in Product Catalog source.
