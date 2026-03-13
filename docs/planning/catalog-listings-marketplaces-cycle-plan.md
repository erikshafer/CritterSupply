# Product Catalog · Listings · Marketplaces: Cycle-by-Cycle Execution Plan

**Document Owner:** Cross-functional — Principal Software Architect, Product Owner, UX Engineer  
**Status:** ✅ Approved — All three sign-offs obtained  
**Date:** 2026-03-12  
**Triggered by:** Planning session to convert the [Evolution Plan](catalog-listings-marketplaces-evolution-plan.md) into executable cycles  
**Source documents:**
- [`catalog-listings-marketplaces-evolution-plan.md`](catalog-listings-marketplaces-evolution-plan.md) — Architecture & integration design
- [`catalog-listings-marketplaces-discovery.md`](catalog-listings-marketplaces-discovery.md) — PO + UXE discovery analysis
- [`catalog-variant-model.md`](catalog-variant-model.md) — D1 decision: Parent/Child hierarchy
- [`catalog-listings-marketplaces-glossary.md`](catalog-listings-marketplaces-glossary.md) — Ubiquitous language
- [`CONTEXTS.md`](../../CONTEXTS.md) — Architectural source of truth

---

## Table of Contents

1. [Decision Summary (D1–D10)](#1-decision-summary-d1d10)
2. [Cycle Sequence Overview](#2-cycle-sequence-overview)
3. [Hard Dependencies Between Phases](#3-hard-dependencies-between-phases)
4. [Cycle 29: Product Catalog ES Migration (Phase 0)](#4-cycle-29-product-catalog-es-migration-phase-0)
5. [Cycles 30–31: Listings BC Foundation (Phase 1)](#5-cycles-3031-listings-bc-foundation-phase-1)
6. [Cycles 32–33: Marketplaces BC Foundation (Phase 2)](#6-cycles-3233-marketplaces-bc-foundation-phase-2)
7. [Cycles 34–35: Variants, Compliance, Real API Calls (Phase 3)](#7-cycles-3435-variants-compliance-real-api-calls-phase-3)
8. [ADR Schedule](#8-adr-schedule)
9. [Cross-BC Coordination Points](#9-cross-bc-coordination-points)
10. [Risk Register](#10-risk-register)
11. [Acceptance Criteria by Phase](#11-acceptance-criteria-by-phase)
12. [UI Deliverable Timeline](#12-ui-deliverable-timeline)

---

## 1. Decision Summary (D1–D10)

All outstanding Owner decisions from the [Evolution Plan §6](catalog-listings-marketplaces-evolution-plan.md#6-questions-for-ownererik) are now resolved. These decisions are **final** and should not be re-litigated without an explicit escalation.

| Decision | Answer | Decided By | Confidence | Notes |
|----------|--------|-----------|------------|-------|
| **D1** — Variant Model Shape | ✅ Option A: Parent/Child (`ProductFamily` + `Product` with `FamilyId`) | Owner (2026-03-09) | — | Full design in [`catalog-variant-model.md`](catalog-variant-model.md). PSA, PO, UXE all signed off. |
| **D2** — Own Website as Formal Channel | ✅ **YES** — `OWN_WEBSITE` is a `ChannelCode` | PO + PSA consensus | High | Recall cascade covers storefront automatically; unified "where is this product live?" query. Caveat: `OWN_WEBSITE` lifecycle is simpler — `Draft → Live` is near-instant, no marketplace review queue. |
| **D3** — API Call Ownership | ✅ **Marketplaces BC** owns marketplace API calls | PO + PSA consensus | High | Listings BC owns *intent* (what to list); Marketplaces BC owns *mechanism* (how to call marketplace APIs). Prevents Listings from becoming a God-BC. |
| **D4** — Marketplace Identity | ✅ Document entity in Marketplaces BC | PSA (no Owner input needed) | — | `ChannelCode` is stable natural key. Not event-sourced — config changes are infrequent. |
| **D5** — Category Mapping Ownership | ✅ Marketplaces BC | PSA (no Owner input needed) | — | Ownership follows change rate — marketplace taxonomy changes are marketplace-specific knowledge. |
| **D6** — API Credentials Storage | ✅ **Vault** (path stored on `Marketplace` document) | PO (non-negotiable) | Non-negotiable | Never credentials in Postgres. Vault stub acceptable in development, but pattern must be correct. Credential rotation without deployment is a hard requirement. |
| **D7** — Lot/Batch Tracking | ✅ **Defer to Phase 3** — Full-SKU cascade at launch | PO + PSA consensus | High | Full-SKU cascade is the correct initial response for real pet supply recalls. Lot-specific targeting is a Phase 3 optimization after Inventory BC has lot tracking wired. |
| **D8** — Compliance Metadata | ✅ **Minimum viable** (`IsHazmat` + `HazmatClass`) at Listings launch; full suite Phase 2 | PO + PSA consensus | High | `HazmatClass` must use standard UN/DOT classification values (Class 1–9), not free-text. Covers ~95% of pet supply hazmat requirements. Full compliance suite (Prop65, AgeRestriction, RestrictedStates) deferred. |
| **D9** — Seasonal Reactivation | ✅ **Manual for Phase 1**, scheduled for Phase 2 | PO + PSA consensus | High | Phase 2: `PlannedReactivationDate` fires a scheduled reminder (human confirms), not auto-reactivation. Exception: `OWN_WEBSITE` can auto-reactivate since we control the experience. |
| **D10** — Breaking Schema Changes | ✅ **WARN, don't block** | PO decision | High | `RequiresSchemaReview` flag + yellow warning badge on affected drafts. Listings can still be submitted — marketplace API is the ultimate enforcer. Standard `MarketplaceSubmissionRejected` flow handles actual rejections. |

---

## 2. Cycle Sequence Overview

```
Cycle 25: Returns BC Phase 1              ← COMPLETE (2026-03-12)
Cycle 26: Notifications BC Phase 1         ← planned
Cycle 27: Promotions BC Phase 1            ← planned
Cycle 28: Admin Portal Phase 1             ← planned
────────────────────────────────────────────────────────
Cycle 29: Product Catalog ES Migration     ← Phase 0 (can overlap w/ Cycle 28)
Cycle 30: Listings BC Foundation (Part A)  ← Phase 1a
Cycle 31: Listings BC Integration (Part B) ← Phase 1b
Cycle 32: Marketplaces BC Foundation       ← Phase 2a
Cycle 33: Marketplaces ↔ Listings Integ.   ← Phase 2b
Cycle 34: Variants + Compliance            ← Phase 3a
Cycle 35: Real API Calls + Automation      ← Phase 3b
```

**Total estimated effort:** 7 cycles (29–35)

**Priority level:** Elevated from "Low" to **Medium** (PO decision). Marketplace selling is the #1 revenue growth lever for pet supply retail and demonstrates the real value of bounded contexts + event-driven design in the reference architecture.

**Acceleration option:** Phase 0 (Cycle 29) is a backend-only migration with no UI dependency. It can run as "Cycle 28b" alongside Admin Portal, since it touches `src/Product Catalog/`, `src/Shared/Messages.Contracts/`, and `src/Pricing/` — no overlap with Admin Portal files. This would save one cycle.

---

## 3. Hard Dependencies Between Phases

```
Phase 0 ──blocks──► Phase 1 (Listings needs granular ProductCatalog events)
Phase 0 ──blocks──► Phase 2 (Marketplaces needs ProductDiscontinued w/ IsRecall)
Phase 1 ──blocks──► Phase 2 (Marketplaces consumes ListingSubmittedToMarketplace)
Phase 1 + 2 ──block──► Phase 3 (Variants build on both Listings + Marketplaces)
```

**Decision gates:**
- ✅ D1 (variant model): DECIDED — unblocks Phase 3 design
- ✅ D2 (OwnWebsite as channel): DECIDED — unblocks Phase 1 `OWN_WEBSITE` listing
- ✅ D3 (API call ownership): DECIDED — unblocks Phase 2 scope
- ✅ D6 (credentials storage): DECIDED — unblocks Phase 2 Vault pattern
- All decision gates are now clear. No remaining blockers.

---

## 4. Cycle 29: Product Catalog ES Migration (Phase 0)

**Duration:** 1–1.5 cycles  
**Risk level:** HIGH (existing 24+ integration tests must pass unchanged)  
**Prerequisite:** None (independent of Cycles 26–28)

### Phase 0-A: Event-Sourced Aggregate + Projection

**Goal:** Transform Product Catalog from Marten document store to event sourcing with identical read-path behavior.

#### Tasks

| # | Task | Files | Effort |
|---|------|-------|--------|
| 0.1 | **Domain events** — 14 sealed records covering all product mutations | `ProductCatalog/Products/Events.cs` (NEW) | 0.5 day |
| 0.2 | **Product aggregate rewrite** — `Create()` factory + `Apply()` per event; `StreamId(sku)` UUID v5 with `catalog:` namespace | `ProductCatalog/Products/Product.cs` (REWRITE) | 1 day |
| 0.3 | **ProductCatalogView projection** — `SingleStreamProjection<ProductCatalogView>` registered inline; identical shape to current `Product` record | `ProductCatalog/Products/ProductCatalogView.cs` (NEW) | 0.5 day |
| 0.4 | **Handler migration** — All CRUD handlers switch from `session.Store()` to `session.Events.Append()` | `ProductCatalog.Api/Products/*.cs` (MODIFY all 6 handlers) | 1.5 days |
| 0.5 | **Program.cs configuration** — Register inline projection; configure `product-recall` priority exchange | `ProductCatalog.Api/Program.cs` (MODIFY) | 0.5 day |
| 0.6 | **P0.3: Exclude `ProductMigrated` from history** — Projection classifies `ProductMigrated` as `Significance.System`; default history filter excludes System events | Projection design in `ProductCatalogView.cs` | 0.5 day |

**Handler migration detail** — The `UpdateProduct` handler is the most complex change. It must diff incoming request fields against current state and emit **granular** events:

```
Before: session.Store(product with { Name = ..., Category = ... })
After:  var events = new List<object>();
        if (nameChanged) events.Add(new ProductNameChanged(...));
        if (categoryChanged) events.Add(new ProductCategoryAssigned(...));
        session.Events.Append(streamId, events.ToArray());
```

### Phase 0-B: Integration Message Enrichment + Downstream Updates

**Goal:** Replace coarse-grained `ProductUpdated` with granular integration messages; update downstream consumers.

#### Tasks

| # | Task | Files | Effort |
|---|------|-------|--------|
| 0.7 | **Enriched `ProductAdded`** — Add `Status`, `Brand`, `HasDimensions` fields | `Messages.Contracts/ProductCatalog/ProductAdded.cs` (MODIFY) | 0.25 day |
| 0.8 | **New granular contracts** — `ProductContentUpdated`, `ProductCategoryChanged`, `ProductImagesUpdated`, `ProductDimensionsChanged`, `ProductStatusChanged`, `ProductDeleted`, `ProductRestored` | `Messages.Contracts/ProductCatalog/` (7 NEW files) | 0.5 day |
| 0.9 | **Enriched `ProductDiscontinued`** — Add `Reason`, `IsRecall` fields | `Messages.Contracts/ProductCatalog/ProductDiscontinued.cs` (MODIFY) | 0.25 day |
| 0.10 | **Deprecate `ProductUpdated`** — Mark `[Obsolete]`, stop publishing | `Messages.Contracts/ProductCatalog/ProductUpdated.cs` (MODIFY) | 0.1 day |
| 0.11 | **Pricing BC consumer update** — Accept enriched `ProductAdded` (new fields nullable initially for backward compat) | `Pricing/Products/ProductAddedHandler.cs` (MODIFY) | 0.25 day |
| 0.12 | **Priority exchange for recalls** — `ProductDiscontinued` where `IsRecall = true` routes to `product-recall` exchange (priority 10) | `ProductCatalog.Api/Program.cs` Wolverine config (MODIFY) | 0.5 day |

**Cross-BC coordination note:** Task 0.11 (Pricing BC update) must ship in the same cycle as the contract enrichment. New fields are added as **nullable** initially — Pricing handler ignores them. No current consumers of `ProductUpdated` exist (safe deprecation).

### Phase 0-C: Migration Job + Tests

**Goal:** Migrate existing product documents to event streams; verify all tests pass.

#### Tasks

| # | Task | Files | Effort |
|---|------|-------|--------|
| 0.13 | **Migration job** — Background Wolverine handler; triggered by `POST /api/admin/migrate-to-event-sourcing`; batch-processes documents in chunks of 100; idempotent (checks `FetchStreamStateAsync` before creating stream); emits `ProductMigrated` per existing product | `ProductCatalog.Api/Migration/MigrateToEventSourcing.cs` (NEW) | 1 day |
| 0.14 | **Migration correctness tests** — Pre-migration document snapshots == post-migration `ProductCatalogView` state (field-by-field); idempotency (run twice, assert no duplicates); post-migration `AddProduct` creates event stream, not document | `ProductCatalog.Api.IntegrationTests/MigrationTests.cs` (NEW) | 1 day |
| 0.15 | **Event sourcing behavior tests** — `UpdateProduct` with name change emits `ProductNameChanged`; with category change emits `ProductCategoryAssigned`; with both emits both events; `ChangeProductStatus` to Discontinued emits `ProductDiscontinued` | `ProductCatalog.Api.IntegrationTests/EventSourcingTests.cs` (NEW) | 1 day |
| 0.16 | **Existing test verification** — All 24+ existing integration tests pass without assertion changes (response bodies are identical because `ProductCatalogView` projects to same shape) | Run full test suite | 0.5 day |
| 0.17 | **ADR writing** — ADRs 0026 (ES migration) and 0027 (catalog: namespace prefix) | `docs/decisions/0026-*.md`, `docs/decisions/0027-*.md` (NEW) | 0.5 day |

### Phase 0 File Change Inventory

```
MODIFIED (9 files):
  src/Product Catalog/ProductCatalog/Products/Product.cs          ← full rewrite to ES aggregate
  src/Product Catalog/ProductCatalog.Api/Products/AddProduct.cs   ← ES write path
  src/Product Catalog/ProductCatalog.Api/Products/UpdateProduct.cs ← granular event emission
  src/Product Catalog/ProductCatalog.Api/Products/ChangeProductStatus.cs ← status events
  src/Product Catalog/ProductCatalog.Api/Products/GetProduct.cs   ← read from projection
  src/Product Catalog/ProductCatalog.Api/Products/ListProducts.cs ← query projection
  src/Product Catalog/ProductCatalog.Api/Products/AssignProductToVendor.cs ← ES write
  src/Product Catalog/ProductCatalog.Api/Program.cs               ← projection + exchange config
  src/Pricing/Pricing/Products/ProductAddedHandler.cs             ← accept enriched contract

NEW (14+ files):
  src/Product Catalog/ProductCatalog/Products/Events.cs           ← 14 domain events
  src/Product Catalog/ProductCatalog/Products/ProductCatalogView.cs ← SingleStreamProjection
  src/Product Catalog/ProductCatalog.Api/Migration/MigrateToEventSourcing.cs
  src/Shared/Messages.Contracts/ProductCatalog/ProductContentUpdated.cs
  src/Shared/Messages.Contracts/ProductCatalog/ProductCategoryChanged.cs
  src/Shared/Messages.Contracts/ProductCatalog/ProductImagesUpdated.cs
  src/Shared/Messages.Contracts/ProductCatalog/ProductDimensionsChanged.cs
  src/Shared/Messages.Contracts/ProductCatalog/ProductStatusChanged.cs
  src/Shared/Messages.Contracts/ProductCatalog/ProductDeleted.cs
  src/Shared/Messages.Contracts/ProductCatalog/ProductRestored.cs
  tests/.../MigrationTests.cs
  tests/.../EventSourcingTests.cs
  docs/decisions/0026-product-catalog-event-sourcing-migration.md
  docs/decisions/0027-catalog-namespace-uuid-v5-stream-ids.md

DEPRECATED (1 file):
  src/Shared/Messages.Contracts/ProductCatalog/ProductUpdated.cs  ← mark [Obsolete]
```

### Phase 0 Gate

✅ All 24+ existing Product Catalog integration tests pass without assertion changes  
✅ New migration correctness + event sourcing behavior tests pass  
✅ Full solution test suite (~895+ tests) passes  
✅ `ProductCatalogView` projects to identical state as pre-migration documents  
✅ Granular integration messages publish correctly to RabbitMQ  
✅ `ProductDiscontinued(IsRecall: true)` routes to `product-recall` priority exchange  
✅ ADRs 0026 and 0027 written

---

## 5. Cycles 30–31: Listings BC Foundation (Phase 1)

**Duration:** 1.5–2 cycles  
**Risk level:** MEDIUM  
**Prerequisites:** Phase 0 complete; D2 ✅ D3 ✅ (both answered)

### Cycle 30 — Phase 1a: Aggregate + Projections + Recall Handler

#### Project Scaffolding

```
src/
  Listings/
    Listings/                            ← domain project (regular SDK)
      Listings.csproj                    ← References: Messages.Contracts
      Listings/
        Listing.cs                       ← event-sourced aggregate
        ListingStatus.cs                 ← enum: Draft, ReadyForReview, Submitted, Live, Paused, Ended
        EndedCause.cs                    ← discriminated union: ManualEnd, ProductDiscontinued, etc.
        Events.cs                        ← 13 domain events
        ListingsActiveView.cs            ← inline projection (per-SKU index of active stream IDs)
      ProductSummary/
        ProductSummaryView.cs            ← local document (maintained by integration event handlers)
        ProductSummaryHandlers.cs        ← handlers for ProductCatalog.* integration messages
    Listings.Api/                        ← API project (Web SDK)
      Listings.Api.csproj               ← References: Listings, Messages.Contracts
      Properties/launchSettings.json     ← Port: 5246
      Program.cs                         ← Wolverine + Marten + RabbitMQ config
      appsettings.json
      Dockerfile
      Listings/
        CreateListing.cs
        UpdateListingContent.cs
        SubmitForReview.cs
        ApproveListing.cs
        PauseListing.cs
        ResumeListing.cs
        EndListing.cs
        GetListing.cs
        ListListings.cs
      Recall/
        RecallCascadeHandler.cs          ← consumes from product-recall priority exchange
```

**Port allocation:** `5246` (next available after Returns at 5245)

**Solution updates:** Add projects to `CritterSupply.slnx`; Docker Compose entry with `listings` profile; PostgreSQL database `listings` in `docker/postgres/create-databases.sh`.

#### Tasks

| # | Task | Effort |
|---|------|--------|
| 1.1 | **Project scaffolding** — `dotnet new` for both projects; solution + Docker Compose + DB script updates | 0.5 day |
| 1.2 | **Listing aggregate** — event-sourced with `Create()` + `Apply()` per event; `StreamId(sku, channelCode)` UUID v5 with `listing:` namespace; state machine invariants enforced in handlers | 1 day |
| 1.3 | **ListingsActiveView projection** — custom multi-stream projection; inline; indexed by SKU; maintains `IReadOnlyList<Guid> ActiveListingStreamIds` | 0.5 day |
| 1.4 | **ProductSummaryView** — local Marten document maintained by integration event handlers consuming from ProductCatalog; handlers for `ProductAdded`, `ProductContentUpdated`, `ProductCategoryChanged`, `ProductDiscontinued`, `ProductStatusChanged` | 1 day |
| 1.5 | **RecallCascadeHandler** — consumes `ProductDiscontinued(IsRecall)` from `product-recall` priority exchange; loads `ListingsActiveView` for SKU; appends `ListingForcedDown` to all active listing streams; publishes `ListingsCascadeCompleted` | 0.5 day |
| 1.6 | **Program.cs wiring** — Marten config (`listings` schema, inline projections, `ProductSummaryView` document registration); Wolverine config (handler discovery for both assemblies, RabbitMQ queue bindings for catalog events + recall exchange) | 0.5 day |

**Listing state machine:**
```
Draft            → ReadyForReview, Submitted (if skipReview), Ended
ReadyForReview   → Submitted (approved), Draft (rejected), Ended
Submitted        → Live (marketplace confirmed), Ended (marketplace rejected)
Live             → Paused, Ended
Paused           → Live (resumed), Ended
Ended            → (terminal — no transitions out)

ANY non-terminal → Ended (via ForcedDown from recall cascade — bypasses normal Paused)
```

**OWN_WEBSITE fast path (D2):** When `ChannelCode = OWN_WEBSITE`, the `Submitted → Live` transition is automatic — no Marketplaces BC involvement. Handled by a channel-aware policy in the submission handler.

### Cycle 31 — Phase 1b: HTTP Endpoints + Integration Tests + Admin UI Scaffold

#### Tasks

| # | Task | Effort |
|---|------|--------|
| 1.7 | **HTTP endpoints** — 9 Wolverine HTTP endpoints for full lifecycle management (Create, UpdateContent, SubmitForReview, Approve, Pause, Resume, End, GetListing, ListListings) | 1.5 days |
| 1.8 | **Integration message contracts** — `Messages.Contracts.Listings` namespace: `ListingCreated`, `ListingActivated`, `ListingPaused`, `ListingEnded`, `ListingForcedDown`, `ListingsCascadeCompleted` | 0.5 day |
| 1.9 | **Integration tests** — TestContainers PostgreSQL + RabbitMQ; test classes: `CreateListingTests`, `ListingLifecycleTests`, `RecallCascadeTests`, `ProductSummaryViewTests`, `ListingInvariantTests` | 2 days |
| 1.10 | **AdminPortal.Web scaffold** — Blazor Server app (MudBlazor 9.1.0), `<MudLayout>` shell, no authentication (infrastructure-protected); port 5244 | 0.5 day |
| 1.11 | **AdminPortal.Api scaffold** — BFF project proxying to ProductCatalog.Api + Listings.Api; port 5243 | 0.5 day |
| 1.12 | **Listings admin pages** — Dashboard (`/admin/listings`), Detail (`/admin/listings/{id}`), Create (`/admin/listings/create`); `<ListingStatusBadge>` shared component | 1.5 days |
| 1.13 | **Product Detail enhancements** — Listings Summary widget (P1.2: "2 Live · 1 Paused · 0 Ended"); Product Change History tab (P1.1: `<MudTimeline>` with significance filtering) | 1 day |
| 1.14 | **Pre-flight discontinuation modal (P0.1)** — `<MudDialog>` showing affected listing count, channel breakdown, reason field, recall checkbox; wired to real Listings data | 0.5 day |
| 1.15 | **Grouped cascade notification (P0.2)** — `<CascadeNotificationCard>` renders one grouped notification per cascade, not per-listing toasts | 0.5 day |
| 1.16 | **ADR writing** — ADRs 0028 (granular integration messages), 0029 (recall cascade priority exchange), 0030 (Listings BC composite key), 0033 (ProductSummaryView pattern) | 0.5 day |
| 1.17 | **OWN_WEBSITE seed data** — Register `OWN_WEBSITE` as the first Marketplace document (seeded in development) | 0.25 day |

### Phase 1 Gate

✅ Full listing lifecycle works end-to-end via API: Draft → ReadyForReview → Submitted → Live → Paused → Ended  
✅ `OWN_WEBSITE` listings transition `Draft → Live` automatically (no marketplace review)  
✅ Recall cascade: `ProductDiscontinued(IsRecall: true)` forces down ALL active listings for the SKU within one message processing cycle  
✅ `ListingsCascadeCompleted` integration message publishes with count of affected listings  
✅ Listing creation rejected for Discontinued/Deleted products and non-existent SKUs  
✅ `ProductSummaryView` maintained correctly by reacting to Product Catalog integration messages  
✅ Integration tests cover: happy path, recall cascade (3+ listings across 2+ channels), duplicate listing prevention, terminal product rejection  
✅ AdminPortal.Web scaffold running with Listings Dashboard, Detail, and Create pages  
✅ Pre-flight discontinuation modal shows accurate affected listing count  
✅ ADRs 0028–0030, 0033 written

---

## 6. Cycles 32–33: Marketplaces BC Foundation (Phase 2)

**Duration:** 1.5–2 cycles  
**Risk level:** MEDIUM  
**Prerequisites:** Phase 1 complete; D3 ✅ D6 ✅ (both answered)

### Cycle 32 — Phase 2a: Document Store + Category Mappings + Adapter Stubs

#### Project Scaffolding

```
src/
  Marketplaces/
    Marketplaces/                        ← domain project
      Marketplaces.csproj               ← References: Messages.Contracts
      Marketplaces/
        Marketplace.cs                   ← document entity (ChannelCode = Id)
      CategoryMappings/
        CategoryMapping.cs               ← document entity (composite key)
      AttributeSchemas/
        MarketplaceAttributeSchema.cs    ← versioned document
      Adapters/
        IMarketplaceAdapter.cs           ← adapter interface
        StubAmazonAdapter.cs
        StubWalmartAdapter.cs
        StubEbayAdapter.cs
        StubOwnWebsiteAdapter.cs         ← pass-through (no API call)
    Marketplaces.Api/                    ← API project
      Marketplaces.Api.csproj           ← References: Marketplaces, Messages.Contracts
      Properties/launchSettings.json     ← Port: 5247
      Program.cs
      appsettings.json
      Dockerfile
      Marketplaces/
        RegisterMarketplace.cs
        UpdateMarketplace.cs
        DeactivateMarketplace.cs
        GetMarketplace.cs
        ListMarketplaces.cs
      CategoryMappings/
        SetCategoryMapping.cs
        GetCategoryMapping.cs
        ListCategoryMappings.cs
      AttributeSchemas/
        PublishAttributeSchema.cs
        GetAttributeSchema.cs
```

**Port allocation:** `5247`

**Solution updates:** Add projects to `CritterSupply.slnx`; Docker Compose entry with `marketplaces` profile; PostgreSQL database `marketplaces` in `docker/postgres/create-databases.sh`.

#### Tasks

| # | Task | Effort |
|---|------|--------|
| 2.1 | **Project scaffolding** — `dotnet new` for both projects; solution + Docker Compose + DB script updates | 0.5 day |
| 2.2 | **Marketplace document CRUD** — `ChannelCode` as stable `string Id`; `IsActive`, `IsOwnWebsite`, `ApiCredentialVaultPath`, `AttributeSchemaVersion`; standard Wolverine handlers | 1 day |
| 2.3 | **CategoryMapping store** — composite `Id` = `{channelCode}:{internalCategory}`; `MarketplaceCategoryId`, `MarketplaceCategoryPath`, `LastVerifiedAt`; CRUD handlers | 1 day |
| 2.4 | **MarketplaceAttributeSchema** — versioned document; `Id` = `{channelCode}:{semver}`; required/optional attribute definitions; `IsBreakingChange` flag | 0.5 day |
| 2.5 | **Adapter stubs** — `IMarketplaceAdapter` interface with `SubmitListingAsync` and `DeactivateListingAsync`; stub implementations return success after simulated delay; adapter resolution by `ChannelCode` via DI | 1 day |
| 2.6 | **Seed data** — Register `AMAZON_US`, `WALMART_US`, `EBAY_US`, `OWN_WEBSITE` marketplace documents in development; seed category mappings for top pet supply categories | 0.5 day |
| 2.7 | **Program.cs wiring** — Marten config (`marketplaces` schema, document registrations); Wolverine + RabbitMQ; Vault pattern stub for credentials | 0.5 day |

### Cycle 33 — Phase 2b: Bidirectional Listings ↔ Marketplaces Integration

This cycle wires the first **bidirectional async messaging integration** in CritterSupply.

#### Tasks

| # | Task | Effort |
|---|------|--------|
| 2.8 | **`ListingSubmittedToMarketplace` consumer** — Marketplaces BC receives submission intent; routes to appropriate adapter by `ChannelCode`; adapter calls stub API; publishes `MarketplaceListingActivated` or `MarketplaceSubmissionRejected` back | 1 day |
| 2.9 | **Listings BC consumes `MarketplaceListingActivated`** — appends `ListingActivated` domain event to Listing stream; listing transitions to `Live` | 0.5 day |
| 2.10 | **Listings BC consumes `MarketplaceSubmissionRejected`** — appends `ListingRejected` domain event with field-level error details; listing transitions back to `Draft` for correction | 0.5 day |
| 2.11 | **`AttributeSchemaPublished` event + Listings reaction** — when Marketplaces BC publishes a new schema version with `IsBreakingChange: true`, Listings BC queries all Draft listings for that channel and flags them `RequiresSchemaReview` (D10: warn, don't block) | 0.5 day |
| 2.12 | **Submission timeout safety net** — Wolverine scheduled timeout: if a listing stays in `Submitted` for >30 minutes, fire `ListingSubmissionTimedOut` to flag for manual review; prevents orphaned listings | 0.5 day |
| 2.13 | **Integration message contracts** — `Messages.Contracts.Marketplaces`: `MarketplaceRegistered`, `MarketplaceDeactivated`, `CategoryMappingUpdated`, `AttributeSchemaPublished`, `MarketplaceListingActivated`, `MarketplaceSubmissionRejected`, `MarketplaceListingDeactivated` | 0.5 day |
| 2.14 | **Integration tests** — TestContainers; test classes: `MarketplaceCrudTests`, `CategoryMappingTests`, `AttributeSchemaTests`, `ListingSubmissionFlowTests` (end-to-end: listing submitted → adapter called → confirmation back), `SeedDataTests` | 2 days |
| 2.15 | **Admin UI — Marketplace pages** — Marketplaces list (`/admin/marketplaces`), Detail (`/admin/marketplaces/{channelCode}`) with 4 tabs (Configuration, Category Mappings, Attribute Schema, Audit Log), Add Channel (`/admin/marketplaces/create`) with guided workflow, Category Mapping Editor | 2 days |
| 2.16 | **ADR writing** — ADRs 0031 (Marketplace as document entity), 0032 (category mapping ownership) | 0.25 day |

### Phase 2 Gate

✅ `Marketplace` documents exist for AMAZON_US, WALMART_US, EBAY_US, OWN_WEBSITE  
✅ Listing submitted to a marketplace channel triggers Marketplaces BC adapter (stub)  
✅ `MarketplaceListingActivated` flows back to Listings BC; listing transitions to `Live`  
✅ `MarketplaceSubmissionRejected` flows back with field-level errors; listing transitions to Draft  
✅ `AttributeSchemaPublished(IsBreakingChange: true)` flags affected draft listings with `RequiresSchemaReview`  
✅ Category-to-marketplace mappings exist for top pet supply categories  
✅ Submission timeout fires after 30 minutes for orphaned `Submitted` listings  
✅ Admin UI: Marketplace list, detail (4 tabs), create (guided workflow), category mapping editor  
✅ Integration tests cover: end-to-end submission flow, rejection flow, schema review flagging  
✅ ADRs 0031 and 0032 written

---

## 7. Cycles 34–35: Variants, Compliance, Real API Calls (Phase 3)

**Duration:** 2–3 cycles  
**Risk level:** HIGH (new aggregate, cross-BC coordination, first real external API)  
**Prerequisites:** Phases 1 and 2 complete; D1 ✅ (answered)

### Cycle 34 — Phase 3a: ProductFamily Aggregate + Variant-Aware Listings + Compliance

#### Tasks

| # | Task | Effort |
|---|------|--------|
| 3.1 | **ProductFamily aggregate** — event-sourced; UUID v7 stream key; events: `ProductFamilyCreated`, `ProductFamilyUpdated`, `ProductVariantAdded`, `ProductVariantRemoved`, `ProductFamilyArchived`; tracks `IReadOnlyList<string> VariantSkus` | 1.5 days |
| 3.2 | **Product aggregate gains `FamilyId?`** — new events: `ProductFamilyAssigned(Guid FamilyId, VariantAttributes)`, `ProductFamilyUnassigned`; both streams updated atomically in one `SaveChangesAsync` (dual-stream write pattern per `catalog-variant-model.md`) | 1 day |
| 3.3 | **VariantAttributes value object** — `Dictionary<string, string> Values` (e.g., `{ "Size": "Large", "Color": "Red" }`); validated against category taxonomy attribute dimensions | 0.5 day |
| 3.4 | **ProductFamily HTTP endpoints** — Create family, Add variant to family, Remove variant, Get family, List families; endpoints in ProductCatalog.Api | 1 day |
| 3.5 | **Variant-aware listing creation** — `ListingDraftCreated` gains `FamilyId?` and `ParentListingId?`; listing creation for child SKU can group with sibling listings on same channel (Amazon parent/child ASIN pattern) | 1 day |
| 3.6 | **Compliance metadata** — new events: `ComplianceMetadataSet(IsHazmat, HazmatClass, ...)`, `HazmatClassificationChanged`; `HazmatClass` uses standard UN/DOT values (enum, not free-text, per PO requirement) | 0.5 day |
| 3.7 | **Compliance gate on listing submission** — if `ProductSummaryView.IsHazmat == true` and `HazmatClass` is null, reject submission with 400; marketplace-specific rules handled by adapter | 0.5 day |
| 3.8 | **Admin UI — ProductFamily pages** — Family list (`/admin/catalog/families`), Family detail (`/admin/catalog/families/{familyId}`) with variant `<MudDataGrid>`, Add Variant dialog with dynamic attribute fields | 1.5 days |
| 3.9 | **Integration tests** — ProductFamily CRUD, dual-stream variant writes, variant-aware listing creation, compliance gate validation | 1.5 days |
| 3.10 | **ADR writing** — ADR 0034 (parent/child variant model) | 0.25 day |

### Cycle 35 — Phase 3b: Real API Calls + Storefront Variants + Automation

#### Tasks

| # | Task | Effort |
|---|------|--------|
| 3.11 | **Amazon SP-API integration** — replace `StubAmazonAdapter` with real implementation; OAuth2 token management; Feeds API for listing creation; Notifications API for status callbacks; error handling + retry via Wolverine outbox | 2 days |
| 3.12 | **Storefront PDP variant picker** — `<MudToggleGroup<string>>` per variant dimension (Size, Color, Flavor); reactive price/stock updates on selection change; "Add to Cart" submits selected variant SKU; accessibility: `aria-label`, `aria-disabled`, `aria-live="polite"` | 1.5 days |
| 3.13 | **Storefront product listing page** — Product cards show variant count badge ("4 variants"); family grouping so one card per family, not per variant | 0.5 day |
| 3.14 | **Cart variant display** — Cart line items show variant attributes ("Size: Medium, Flavor: Peanut") alongside SKU | 0.5 day |
| 3.15 | **Scheduled seasonal reactivation (D9 Phase 2)** — `ProductSetToOutOfSeason` with `PlannedReactivationDate?`; Wolverine scheduled message fires on date; for `OWN_WEBSITE`: auto-reactivate; for marketplace channels: create notification for human confirmation | 1 day |
| 3.16 | **Family recall cascade** — discontinuing a `ProductFamily` forces down all child variant listings across all channels; implemented as `ProductFamilyArchived` → query all `VariantSkus` → cascade per SKU | 0.5 day |
| 3.17 | **Integration tests** — real Amazon adapter (requires test credentials or mock server); variant picker rendering; family recall cascade; seasonal reactivation scheduling | 1.5 days |

### Phase 3 Gate

✅ ProductFamily aggregate works: create family, add/remove variants, archive family  
✅ Variant-aware listings: child SKU listing groups with sibling listings under parent family  
✅ `IsHazmat` + `HazmatClass` validated on listing submission; hazmat products without classification are rejected  
✅ At least one real marketplace API integration works end-to-end (Amazon US)  
✅ Family recall cascade: discontinuing family forces down all child variant listings across all channels  
✅ Storefront PDP variant picker: select size/color → price/stock updates → "Add to Cart" submits variant SKU  
✅ Seasonal reactivation: scheduled reminder for marketplace channels, auto-reactivate for OWN_WEBSITE  
✅ Admin UI: Family list, detail, add variant dialog  

---

## 8. ADR Schedule

The evolution plan proposed ADRs 0022–0029, but those numbers are taken. Renumbered starting from 0026:

| ADR # | Title | Ships Before | Phase |
|-------|-------|-------------|-------|
| **0026** | Product Catalog BC migration to event sourcing | Cycle 29 start | Phase 0 |
| **0027** | `catalog:` namespace prefix for UUID v5 stream IDs | Cycle 29 start | Phase 0 |
| **0028** | Granular Product Catalog integration messages (retire `ProductUpdated`) | Cycle 30 start | Phase 0 |
| **0029** | Recall cascade via dedicated priority RabbitMQ exchange | Cycle 30 start | Phase 0 |
| **0030** | Listings BC event-sourced aggregate with UUID v5 composite key | Cycle 30 start | Phase 1 |
| **0031** | Marketplace identity as document entity (not enum/aggregate/config) | Cycle 32 start | Phase 2 |
| **0032** | Category-to-marketplace mapping ownership in Marketplaces BC | Cycle 32 start | Phase 2 |
| **0033** | Listings BC local `ProductSummaryView` (no HTTP to Catalog) | Cycle 30 start | Phase 1 |
| **0034** | Parent/Child variant model for ProductFamily aggregate | Cycle 34 start | Phase 3 |

**Writing rule:** ADRs must be written *before* the phase they document begins. They capture the "why" decisions that would otherwise be re-litigated by every developer touching the codebase.

---

## 9. Cross-BC Coordination Points

Four moments in the plan require coordinated changes across multiple bounded contexts in the same cycle:

### CP-1: Phase 0 — Integration Contract Enrichment (Cycle 29)

**BCs touched:** Product Catalog (publisher), Pricing (consumer), Messages.Contracts (shared)

**What ships together:**
1. `ProductAdded` enriched with `Status`, `Brand`, `HasDimensions` (nullable initially for backward compat)
2. Pricing BC's `ProductAddedHandler` updated to accept new signature
3. Product Catalog handlers start publishing enriched events

**Risk:** Missing required fields in deserialization. Wolverine's JSON deserialization ignores unknown fields but may fail on new required fields.  
**Mitigation:** Add new fields as **nullable** initially; make non-nullable in a follow-up commit.

### CP-2: Phase 1 — ProductSummaryView Population (Cycle 30)

**BCs touched:** Product Catalog (publisher), Listings (consumer), Messages.Contracts (shared)

**What ships together:**
1. Product Catalog publishes granular events to RabbitMQ
2. Listings BC has handlers that maintain `ProductSummaryView`

**Risk:** If Listings BC deploys before Product Catalog starts publishing, `ProductSummaryView` is empty; listing creation fails.  
**Mitigation:** Listings BC handles "product not found in `ProductSummaryView`" gracefully with clear error. Include a bootstrap `POST /api/admin/backfill-product-summaries` endpoint for initial population.

### CP-3: Phase 2 — Bidirectional Listings ↔ Marketplaces (Cycle 33)

**BCs touched:** Listings (publisher + consumer), Marketplaces (consumer + publisher), Messages.Contracts (shared)

**What ships together:**
1. `ListingSubmittedToMarketplace` defined + published by Listings
2. `MarketplaceListingActivated` + `MarketplaceSubmissionRejected` defined + published by Marketplaces
3. Both BCs have handlers for incoming messages

**Risk:** This is the first **bidirectional integration** in CritterSupply. If the activation message is lost, the listing stays in `Submitted` forever.  
**Mitigation:** Wolverine scheduled timeout after 30 minutes; `ListingSubmissionTimedOut` flags for manual review.

### CP-4: Phase 0 — Deprecating `ProductUpdated` (Cycle 29)

**BCs touched:** Messages.Contracts (deprecate), all potential consumers

**Current consumers of `ProductUpdated`:** None. The event is defined but no BC has a handler.  
**Action:** Mark `[Obsolete]` and stop publishing. This is the safest possible deprecation.

---

## 10. Risk Register

### Risk 1: Phase 0 Migration Breaks Existing Tests (Severity: HIGH)

**Trigger:** `ProductCatalogView` projection does not produce byte-for-byte identical JSON as current `Product` document.  
**Impact:** 24+ integration tests fail; migration cannot ship.  
**Mitigation:**
- Dedicated `ProjectionCorrectnessTest`: snapshot pre-migration documents, run migration, assert field-by-field equality
- Keep old `mt_doc_product` table alive for rollback window (don't `DROP` in Phase 0)
- Run full solution test suite (~895+ tests) as Phase 0 gate  
**Residual risk:** Medium — custom JSON converters on `Sku`, `ProductName`, `ProductDimensions` must serialize identically through projection.

### Risk 2: RecallCascadeHandler Query Performance (Severity: MEDIUM)

**Trigger:** `ListingsActiveView` projection has stale data; recall cascade misses active listings.  
**Impact:** Recalled product remains live on marketplace channels.  
**Mitigation:**
- `ListingsActiveView` is an **inline** projection — updates synchronously, no lag
- Load test with 100 listings per SKU to verify <2 second cascade
- Fallback safety net: if `ListingsActiveView` returns stale data, additionally query `session.Query<ListingView>()` as backup  
**Residual risk:** Low — inline projections are synchronous by definition.

### Risk 3: Scope Creep from Marketplace Complexity (Severity: HIGH — PO's #1 concern)

**Trigger:** Amazon/Walmart/eBay each have unique attribute schemas, submission rules, and error formats. Engineering team spends 3 cycles modeling schemas before submitting a real listing.  
**Impact:** Phase 3 balloons; no end-to-end demo.  
**Mitigation:**
- Phase 2 uses **stubs only** — no real API calls
- Phase 3 targets **ONE marketplace** (Amazon US) for real integration
- Adapter pattern isolates marketplace specifics — adding a new channel is a new adapter, not a core change  
**Residual risk:** Medium — even one marketplace (Amazon SP-API) is complex.

### Risk 4: Admin Portal Dependency Bottleneck (Severity: MEDIUM)

**Trigger:** Admin Portal (Cycle 28) is delayed; Listings BC has no management UI.  
**Impact:** Backend capability with no human-facing surface.  
**Mitigation:**
- Listings BC API endpoints are fully self-sufficient (CRUD via HTTP/Swagger)
- Phase 1 ships a lightweight `AdminPortal.Web` scaffold (Blazor Server, MudBlazor, no auth) — independent of Cycle 28's full Admin Portal scope
- Every page built in Phases 1–2 survives into the full Admin Portal shell  
**Residual risk:** Low — UXE's Option C (standalone scaffold) eliminates the bottleneck.

### Risk 5: Multi-Cycle Effort Without Intermediate Value (Severity: MEDIUM — PO concern)

**Trigger:** 7 cycles before a catalog manager can list on Amazon.  
**Impact:** Stakeholder fatigue; question whether effort is worthwhile.  
**Mitigation:** Each phase delivers independently valuable capability:
- Phase 0: Better Product Catalog with granular events (value to ALL downstream BCs)
- Phase 1: OWN_WEBSITE listings with recall cascade (value to storefront operations)
- Phase 2: Marketplace stubs with schema validation (value to catalog managers preparing content)
- Phase 3: Real Amazon integration (value to revenue)  
If any phase is cut, previous phases still provide value.

---

## 11. Acceptance Criteria by Phase

### Phase 0: Product Catalog ES Migration (PO Sign-Off)

1. ✅ All 24+ existing integration tests pass without assertion changes
2. ✅ `ProductAdded`, `ProductDiscontinued` integration messages continue to publish correctly
3. ✅ `VendorProductAssociated` integration message still fires on vendor assignment
4. ✅ New granular events exist on event stream: `ProductCategoryAssigned`, `ProductStatusChanged`, `ProductImagesUpdated`
5. ✅ No data loss during migration — every existing product document has a corresponding event stream
6. ✅ API response times within acceptable range (no regression from ES overhead)

### Phase 1: Listings BC Foundation (PO Sign-Off)

1. ✅ Create a listing for an existing product on `OWN_WEBSITE` channel
2. ✅ Listing lifecycle (Draft → Live → Paused → Ended) works end-to-end
3. ✅ Recall cascade forces down ALL active listings for a discontinued SKU within one message processing cycle
4. ✅ `ListingsCascadeCompleted` publishes with count of affected listings
5. ✅ Listing creation rejected for Discontinued/Deleted products and non-existent SKUs
6. ✅ Pre-flight confirmation modal shows accurate affected listing count before discontinuation
7. ✅ Integration tests cover: happy path, recall cascade (3+ listings, 2+ channels), duplicate prevention

### Phase 2: Marketplaces BC Foundation (PO Sign-Off)

1. ✅ Marketplace documents exist for AMAZON_US, WALMART_US, EBAY_US, OWN_WEBSITE
2. ✅ Listing submission triggers Marketplaces BC adapter (stub — real API not required)
3. ✅ `MarketplaceListingActivated` flows back and transitions listing to `Live`
4. ✅ `MarketplaceSubmissionRejected` flows back with field-level errors
5. ✅ Breaking schema changes flag affected drafts as `RequiresSchemaReview` (warn, not block)
6. ✅ Category-to-marketplace mappings exist for top pet supply categories
7. ✅ Credential management uses Vault pattern

### Phase 3: Variants + Compliance + Real APIs (PO Sign-Off)

1. ✅ ProductFamily + child Products with FamilyId implemented per D1
2. ✅ Listing can be created for child variant SKU with parent family grouping
3. ✅ `IsHazmat` + `HazmatClass` validated on listing submission
4. ✅ At least one real marketplace API integration works end-to-end (Amazon US)
5. ✅ Family recall cascade: discontinuing family forces down all child variant listings
6. ✅ Storefront PDP variant picker (select size/color → price/stock update → add to cart with variant SKU)
7. ✅ Seasonal reactivation: scheduled reminder for marketplaces, auto for OWN_WEBSITE

---

## 12. UI Deliverable Timeline

### Summary by Phase

| Phase | Admin UI | Storefront | New Components |
|-------|----------|-----------|----------------|
| **Phase 0** | None (projection design only) | None | 0 |
| **Phase 1** | AdminPortal.Web scaffold + 5 pages + 1 dialog + 3 components | None | ~9 |
| **Phase 2** | 4 pages + 2 components | None | ~6 |
| **Phase 3** | 3 pages + 1 dialog | PDP variant picker + product card badges + cart variant display | ~7 |
| **Total** | ~12 admin pages | ~3 Storefront modifications | ~22 components/pages |

### Phase-by-Phase Detail

**Phase 0 — No UI shipped:**
- `ProductMigrated` excluded from history projections (P0.3 — projection-level filter)
- `ProductChangeHistoryView` schema designed (significance classification: Major/Minor/System)

**Phase 1 — Admin Portal scaffold ships:**
- `AdminPortal.Web` (Blazor Server, MudBlazor, port 5244, no auth)
- `AdminPortal.Api` (BFF, port 5243, proxies to ProductCatalog.Api + Listings.Api)
- Listings Dashboard (`/admin/listings`) — `<MudDataGrid>` + KPI cards
- Listing Detail (`/admin/listings/{id}`) — `<MudTabs>` + `<MudStepper>` + actions
- Create Listing (`/admin/listings/create`) — `<MudAutocomplete>` + `<MudSelect>`
- Product Detail enhanced — Listings Summary widget (P1.2) + Change History tab (P1.1)
- Pre-flight Discontinuation Modal (P0.1) — `<MudDialog>` with affected listing count
- Grouped Cascade Notification (P0.2) — `<CascadeNotificationCard>`
- Shared: `<ListingStatusBadge>`, `<ProductChangeTimeline>`, `<CascadeNotificationCard>`

**Phase 2 — Marketplace admin pages added:**
- Marketplaces list (`/admin/marketplaces`)
- Marketplace Detail (`/admin/marketplaces/{channelCode}`) — 4 tabs
- Add Channel (`/admin/marketplaces/create`) — guided workflow
- Category Mapping Editor
- Shared: `<AuditLogTimeline>`, `<AttributeSchemaViewer>`

**Phase 3 — Storefront + admin variant pages:**
- ProductFamily list (`/admin/catalog/families`)
- ProductFamily Detail (`/admin/catalog/families/{familyId}`) — variant `<MudDataGrid>`
- Add Variant dialog — dynamic attribute fields
- **Storefront PDP** — `<MudToggleGroup<string>>` variant picker with accessibility
- **Storefront product cards** — variant count badge
- **Cart** — variant attributes display

### Admin Portal Migration Path

```
Phase 1 (Cycle 31)          → Phase 2 (Cycle 33)          → Cycle 28+ (Admin Portal)
───────────────────────────────────────────────────────────────────────────────────────
AdminPortal.Web scaffold      Add Marketplace pages          Wire AdminIdentity BC
AdminPortal.Api BFF           Add Category Mapping editor    JWT auth + role-based access
No auth (VPN only)            Still no auth                  Role-filtered navigation
Catalog + Listings pages      + Marketplace pages            + Dashboards + permissions
```

Zero throwaway work. Every page built in Phases 1–2 survives into the full Admin Portal. The shell (`<MudLayout>`, `<MudAppBar>`, `<MudDrawer>`, navigation) becomes the Admin Portal shell. Auth wraps around it — it doesn't replace it.

---

## Appendix A: Port Allocation Updates

| BC | Port | Status |
|----|------|--------|
| Listings.Api | 5246 | 📋 Reserved (Phase 1) |
| Marketplaces.Api | 5247 | 📋 Reserved (Phase 2) |
| AdminPortal.Api | 5243 | 📋 Reserved (Phase 1) |
| AdminPortal.Web | 5244 | 📋 Reserved (Phase 1) |

---

## Appendix B: Database Schema Additions

```
PostgreSQL shared instance — per-BC schema isolation:
  listings          ← NEW (Phase 1) — Listing event streams + ListingsActiveView + ProductSummaryView
  marketplaces      ← NEW (Phase 2) — Marketplace docs + CategoryMapping + MarketplaceAttributeSchema
  product_catalog   ← MODIFIED (Phase 0) — Product event streams + ProductCatalogView (replaces document table)
```

---

## Appendix C: Integration Message Flow (Final State)

```
ProductCatalog ──publishes──► Listings ──────────────────► Marketplaces
     │                           │                               │
     ├──publishes──► Pricing     ├─queries──► ProductSummaryView │
     │                           ├─queries──► CategoryMappingView◄┘
     ├──publishes──► Inventory   │
     │                           └─publishes──► CustomerExperience
     └──publishes──► CustomerExperience

Bidirectional:
  Listings ──ListingSubmittedToMarketplace──► Marketplaces
  Marketplaces ──MarketplaceListingActivated──► Listings
  Marketplaces ──MarketplaceSubmissionRejected──► Listings

Priority path (recall cascade):
  ProductCatalog ──[product-recall exchange, priority=10]──► Listings (RecallCascadeHandler)
```

---

*This document should be updated at the start and end of each cycle. Convert phase tasks into GitHub Issues when a cycle begins.*

*Sign-offs: PSA ✅ · PO ✅ · UXE ✅*
