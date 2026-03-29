# M36.1 Phase 0 Reconciliation — Product Catalog ES Migration Audit

**Date:** 2026-03-29
**Author:** Principal Software Architect (automated audit)
**Purpose:** Task-by-task comparison of the execution plan's Phase 0 (17 tasks) against what M35.0 and M36.0 actually delivered. This document is institutional memory — it prevents future sessions from re-deriving what was and wasn't done.

**Source plan:** `docs/planning/catalog-listings-marketplaces-cycle-plan.md`, Section 4 ("Cycle 29: Product Catalog ES Migration"), Tasks 0.1–0.17.

---

## Reconciliation Table

| Task | Description | Status | Evidence / Notes |
|------|-------------|--------|-----------------|
| **0.1** | Domain events — 14 sealed records covering all product mutations | ⚠️ Partial | **12 of 14 delivered.** File: `src/Product Catalog/ProductCatalog/Products/ProductEvents.cs`. Events: `ProductMigrated`, `ProductCreated`, `ProductNameChanged`, `ProductDescriptionChanged`, `ProductCategoryChanged`, `ProductImagesUpdated`, `ProductDimensionsChanged`, `ProductStatusChanged`, `ProductTagsUpdated`, `ProductSoftDeleted`, `ProductRestored`, `ProductVendorAssigned`. **Missing:** Plan called for 14; actual is 12. Likely the 2 missing are `ProductBrandChanged` and a compliance-related event — neither was needed for M35.0 scope. |
| **0.2** | Product aggregate rewrite — `Create()` factory + `Apply()` per event; `StreamId(sku)` UUID v5 | ⚠️ Partial | **Aggregate exists** at `src/Product Catalog/ProductCatalog/Products/CatalogProduct.cs` with 12 `Apply()` methods. **No `Create()` factory method** — aggregate is rehydrated via Apply only. **UUID v5 not used** — stream IDs are `Guid.NewGuid()` (random v4), not deterministic `catalog:{sku}` UUID v5. SKU-to-stream mapping maintained via `ProductCatalogView` projection lookup. |
| **0.3** | `ProductCatalogView` projection — `SingleStreamProjection` registered inline | ✅ Done | File: `src/Product Catalog/ProductCatalog/Products/ProductCatalogViewProjection.cs`. Registered inline in Program.cs line 44. Handles all 12 domain events with 2 `Create` + 10 `Apply` methods. |
| **0.4** | Handler migration — all CRUD handlers use `session.Events.Append()` | ✅ Done | **14 handlers fully migrated.** All use `session.Events.StartStream()` (2 create handlers) or `session.Events.Append()` (11 mutation handlers). 3 read-only query handlers. Zero `session.Store()` calls remain in handler code. Files in `src/Product Catalog/ProductCatalog.Api/Products/`. |
| **0.5** | Program.cs configuration — register projection; configure exchanges | ⚠️ Partial | **Projection registered:** ✅ Inline (line 44). **`AutoApplyTransactions` present:** ✅ (added M36.0 Session 6). **`product-recall` priority exchange:** ❌ Not configured. Only 3 exchanges exist: `vendor-portal-product-associated`, `product-catalog-product-added`, `product-catalog-product-discontinued`. |
| **0.6** | Exclude `ProductMigrated` from history — significance classification | ❌ Not done | No significance classification system exists. `ProductMigrated` is treated identically to other events in the projection. No `Significance` enum or filter mechanism found. |
| **0.7** | Enriched `ProductAdded` — add `Status`, `Brand`, `HasDimensions` fields | ❌ Not done | Current contract: `ProductAdded(string Sku, string Name, string Category, DateTimeOffset AddedAt)`. Missing: `Status`, `Brand`, `HasDimensions`. File: `src/Shared/Messages.Contracts/ProductCatalog/ProductAdded.cs`. |
| **0.8** | New granular integration contracts — 7 new files | ❌ Not done | **None of the 7 planned contracts exist.** Searched `src/Shared/Messages.Contracts/ProductCatalog/`. Missing: `ProductContentUpdated`, `ProductCategoryChanged`, `ProductImagesUpdated`, `ProductDimensionsChanged`, `ProductStatusChanged`, `ProductDeleted`, `ProductRestored`. Only 4 integration messages exist: `ProductAdded`, `ProductDiscontinued`, `ProductUpdated`, `VendorProductAssociated`. |
| **0.9** | Enriched `ProductDiscontinued` — add `Reason`, `IsRecall` fields | ❌ Not done | Current contract: `ProductDiscontinued(string Sku, DateTimeOffset DiscontinuedAt)`. Missing: `Reason`, `IsRecall`. File: `src/Shared/Messages.Contracts/ProductCatalog/ProductDiscontinued.cs`. **This is a hard Phase 1 prerequisite** — the recall cascade routes on `IsRecall = true`. |
| **0.10** | Deprecate `ProductUpdated` — mark `[Obsolete]` | ❌ Not done | `ProductUpdated.cs` exists without `[Obsolete]` attribute. Still being published (coarse-grained). |
| **0.11** | Pricing BC consumer update — accept enriched `ProductAdded` | ❌ Not done | Blocked by 0.7 (enriched `ProductAdded` not yet published). Current Pricing handler consumes existing 4-field contract. |
| **0.12** | Priority exchange for recalls — `product-recall` exchange | ❌ Not done | No `product-recall` exchange configured in `ProductCatalog.Api/Program.cs`. No priority routing for `ProductDiscontinued` events. |
| **0.13** | Migration job — batch processor with admin endpoint | ❌ Not done | No `Migration/` directory exists. Per-product migration exists via `MigrateProductES.cs` (`POST /api/products/{sku}/migrate`), but no batch job. No `MigrateToEventSourcing.cs`. No admin endpoint for bulk migration. |
| **0.14** | Migration correctness tests — pre/post equality, idempotency | ❌ Not done | `MigrationTests.cs` does not exist in `tests/Product Catalog/ProductCatalog.Api.IntegrationTests/`. |
| **0.15** | Event sourcing behavior tests — granular event emission | ❌ Not done | `EventSourcingTests.cs` does not exist. No tests verify that `UpdateProduct` with a name change emits `ProductNameChanged` specifically. |
| **0.16** | Existing test verification — 48+ tests pass unchanged | ✅ Done | 48/48 integration tests pass. All existing test assertions work with `ProductCatalogView` shape. Confirmed in M36.0 Session 6. |
| **0.17** | ADR writing — ADRs 0030 and 0031 | ⚠️ Numbering conflict | **ADR numbers 0030 and 0031 are already taken.** ADR 0030 = "Notifications → Correspondence Rename". ADR 0031 = "Admin Portal RBAC Model". Highest ADR is **0040**. The plan's reserved range 0030–0038 is entirely occupied. M36.1 ADRs must start at **0041**. No ES migration ADR or UUID v5 namespace ADR exists under any number. |

---

## Summary by Status

| Status | Count | Tasks |
|--------|-------|-------|
| ✅ Done | 3 | 0.3, 0.4, 0.16 |
| ⚠️ Partial | 3 | 0.1, 0.2, 0.5 |
| ❌ Not done | 10 | 0.6, 0.7, 0.8, 0.9, 0.10, 0.11, 0.12, 0.13, 0.14, 0.15 |
| ⚠️ Numbering conflict | 1 | 0.17 |

**3 of 17 tasks fully complete. 3 partially complete. 11 not done or conflicted.**

---

## Partial Task Details

### 0.1 — Domain Events (12 of 14)

The plan specified 14 sealed records. M35.0 delivered 12. The delta is likely:
- `ProductBrandChanged` — not implemented because brand is set at creation and no handler exists to change it independently
- A compliance-related event (e.g., `ProductComplianceUpdated`) — deferred because compliance metadata (`IsHazmat`, `HazmatClass`) is Phase 2 scope

**Disposition:** The 12 existing events cover all current handler mutations. Additional events should be added when their corresponding handlers are implemented (brand change, compliance update). Not a Phase 1 blocker.

### 0.2 — Aggregate Design (Apply-only, no Create factory, no UUID v5)

Two deviations from plan:

1. **No `Create()` factory method.** The aggregate uses `Apply(ProductCreated)` and `Apply(ProductMigrated)` for initial state. This is a valid Marten pattern — `Create` methods on projections serve the same purpose. The plan's `Create()` factory was aspirational; the current pattern works correctly.

2. **No UUID v5 stream IDs.** Streams use `Guid.NewGuid()` instead of deterministic `catalog:{sku}` UUID v5. The plan's UUID v5 convention (ADR 0016) would enable idempotent stream creation and SKU-based stream lookups without projection queries. Current pattern works but requires projection lookup to map SKU → stream ID.

**Disposition:** UUID v5 migration is desirable but not a Phase 1 blocker. SKU → stream ID mapping works via `ProductCatalogView` projection. Defer UUID v5 to a follow-up task. Record the decision in the plan.

### 0.5 — Program.cs (projection ✅, AutoApplyTransactions ✅, product-recall ❌)

Projection and transaction policy are correctly configured. The `product-recall` priority exchange is Phase 1 prerequisite — it must be added before the Listings BC recall cascade handler can consume priority events.

---

## Phase 1 Prerequisites Assessment

The Listings BC requires these capabilities from Product Catalog before it can begin:

### Hard Prerequisites (must exist before Listings aggregate work)

| Prerequisite | Plan Task | Status | Action Required |
|-------------|-----------|--------|-----------------|
| Granular integration messages published | 0.8 | ❌ | Create 7 new contracts in `Messages.Contracts/ProductCatalog/`. Update handlers to publish granular events instead of coarse `ProductUpdated`. |
| `ProductDiscontinued` with `IsRecall` field | 0.9 | ❌ | Enrich contract with `Reason` (string) and `IsRecall` (bool). Update `ChangeProductStatusES` handler to populate fields. |
| `product-recall` priority exchange | 0.12 | ❌ | Configure RabbitMQ exchange in `ProductCatalog.Api/Program.cs` with priority routing for `ProductDiscontinued` where `IsRecall = true`. |
| `ProductSummaryView` design confirmed | N/A (Listings BC) | ✅ Ready | Design documented in glossary and evolution plan. No Product Catalog work needed — Listings BC creates and maintains this projection locally. |

### Soft Prerequisites (desirable but not blocking)

| Prerequisite | Plan Task | Status | Disposition |
|-------------|-----------|--------|-------------|
| Enriched `ProductAdded` | 0.7 | ❌ | Can be delivered as part of Phase 1 Session 1 alongside 0.8/0.9. |
| Deprecate `ProductUpdated` | 0.10 | ❌ | Mark `[Obsolete]` and stop publishing once granular events exist. Can be done alongside 0.8. |
| Pricing consumer update | 0.11 | ❌ | New fields nullable for backward compat. Can happen any time after 0.7. |
| Event significance classification | 0.6 | ❌ | UX enhancement for Product Change History tab. Phase 1 P1 priority, not P0. |
| Migration batch job | 0.13 | ❌ | Only needed if pre-existing products must be migrated. Per-product endpoint exists. Defer unless data migration is required for Listings BC testing. |
| Migration tests | 0.14 | ❌ | Valuable for migration correctness but not blocking Listings aggregate work. |
| Event sourcing behavior tests | 0.15 | ❌ | Should exist before granular event publishing (0.8) to verify correct events emitted. Deliver in same session as 0.8. |
| UUID v5 stream IDs | 0.2 (partial) | ⚠️ | Current `Guid.NewGuid()` works. UUID v5 is desirable for idempotency but not blocking. |
| ES migration ADR | 0.17 | ❌ | Should document the decisions made during M35.0. Deliver as part of M36.1 planning session. |

---

## ADR Numbering Resolution

The execution plan reserved ADRs 0030–0038 for M36.1 work. All of these numbers are taken:

| Number | Current ADR |
|--------|------------|
| 0030 | Notifications → Correspondence Rename |
| 0031 | Admin Portal RBAC Model |
| 0032 | Milestone-Based Planning Schema / Multi-Issuer JWT (3 variants) |
| 0033 | Admin Portal → Backoffice Rename |
| 0034 | Backoffice BFF Architecture |
| 0035 | Backoffice SignalR Hub Design |
| 0036 | BFF Projections Strategy |
| 0037 | OrderNote Aggregate Ownership |
| 0038 | Identity & Access Management Evaluation |
| 0039 | Canonical Validator Placement Convention |
| 0040 | `*Requested` Integration Event Convention |

**Resolution:** M36.1 ADRs start at **0041**. Renumbered schedule:

| Plan Reference | New Number | Topic |
|---------------|------------|-------|
| ADR 0030 (plan) | **ADR 0041** | Product Catalog ES Migration Decisions |
| ADR 0031 (plan) | **ADR 0042** | `catalog:` Namespace UUID v5 Stream Key Convention |
| ADR 0032 (plan) | **ADR 0043** | Listings BC Aggregate Design |
| ADR 0033 (plan) | **ADR 0044** | `ProductSummaryView` Anti-Corruption Layer Pattern |
| ADR 0034 (plan) | **ADR 0045** | Recall Cascade Priority Exchange Design |
| ADR 0037 (plan) | **ADR 0046** | Listings State Machine Transitions |
| ADR 0038 (plan) | Deferred | Marketplaces BC Adapter Pattern (Phase 2) |

---

## Port and Infrastructure Audit

### Port Availability

| Port | Plan Allocation | Current Status |
|------|----------------|----------------|
| 5243 | AdminPortal.Api (plan) | ✅ **Already in use** — `Backoffice.Api` BFF |
| 5244 | AdminPortal.Web (plan) | ✅ **Already in use** — `Backoffice.Web` |
| 5246 | Listings.Api | ❌ **Available** |
| 5247 | Marketplaces.Api | ❌ **Available** |

**Resolution:** The plan's "AdminPortal.Api" is the existing `Backoffice.Api` (port 5243). No new admin BFF scaffold is needed — Phase 1 admin pages extend `Backoffice.Web` directly (see UXE finding below). Listings.Api gets port **5246**. Marketplaces.Api gets port **5247** (Phase 2).

### Database Names

Current databases in `docker/postgres/create-databases.sh` (13 total): `orders`, `payments`, `inventory`, `fulfillment`, `customeridentity`, `shopping`, `productcatalog`, `storefront`, `pricing`, `vendoridentity`, `vendorportal`, `returns`, `backofficeidentity`.

**New databases needed:**
- `listings` — for Listings BC (Phase 1)
- `marketplaces` — for Marketplaces BC (Phase 2)

Neither name conflicts with existing databases.

### Docker Compose

No Listings or Marketplaces services exist in `docker-compose.yml`. New service entries will be added when the BC API projects are created.

---

## Backoffice.Web Assessment (UXE Finding)

The execution plan (written 2026-03-12) describes building a new `Backoffice.Web` scaffold as part of Phase 1. Since then, M32–M36.0 delivered a fully functional Backoffice.Web:

- ✅ MudBlazor shell (`MudLayout`, `MudAppBar`, `MudDrawer`)
- ✅ Role-based navigation (7 roles, 7 authorization policies)
- ✅ 13 E2E feature files in `tests/Backoffice/Backoffice.E2ETests/`
- ✅ Product Admin page already exists (`Pages/ProductAdmin.razor`)
- ✅ `Backoffice.Api` BFF (port 5243) with SignalR hub and BFF projections

**Conclusion:** Phase 1 admin pages (Listings Dashboard, Listings Detail, Create Listing, Pre-flight Modal) should **extend** the existing `Backoffice.Web` — not create a new scaffold. The Product Admin page can be enhanced with the Listings Summary widget directly.

---

## Vault Infrastructure Assessment (ASIE Finding)

No Vault client infrastructure exists in the codebase. No `IVaultClient` interface, no HashiCorp Vault packages, no credential store abstractions.

**Disposition:** Vault is a Phase 2 requirement (D6 — Marketplaces BC credential storage). For Phase 1, no vault integration is needed. Phase 2 should define `IVaultClient` as an interface with a stub implementation for development and a real implementation path for production.

---

## Listing State Machine Assessment (EMF Finding)

The planned state machine (`Draft → ReadyForReview → Submitted → Live → Paused → Ended`) has these considerations:

1. **Missing state for marketplace rejection:** When a marketplace rejects a submission, the plan routes `Submitted → Draft` (back to editing). This is correct — rejection is a transition, not a state. The rejection reason should be stored as event data on the `ListingSubmissionRejected` event.

2. **`OWN_WEBSITE` fast path:** `Draft → Live` is a routing rule, not a separate state. When `ChannelCode = OWN_WEBSITE`, the `SubmitListing` command handler auto-transitions through `Submitted → Live` without waiting for external confirmation. This is documented in the glossary under "OWN_WEBSITE."

3. **Recall forced-down path:** Any non-terminal state → `Ended` with `EndedCause.ProductRecalled`. This is a forced transition triggered by the recall cascade handler, bypassing normal state guards.

**Assessment:** The state machine is complete for Phase 1. No missing states identified.

---

## Integration Message Naming Review (EMF Finding)

The plan's proposed Listings integration messages were reviewed against the `*Requested` convention (ADR 0040):

- `ListingSubmittedToMarketplace` — ✅ Past-tense, event-style name. Correct for integration event.
- `ListingsCascadeCompleted` — ✅ Past-tense. Correct.
- `MarketplaceListingActivated` — ✅ Past-tense. Correct.
- `MarketplaceSubmissionRejected` — ✅ Past-tense. Correct.

No naming conflicts with established conventions.
