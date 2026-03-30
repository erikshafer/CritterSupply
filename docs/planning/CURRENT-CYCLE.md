# Current Development Milestone

> **Note:** This file is maintained as a lightweight AI-readable summary of the active development milestone.
> It is the fallback when GitHub Issues/Projects are not directly accessible.
> **Primary tracking:** GitHub Issues + GitHub Project board (see links below)
>
> **For full GitHub-first access on this machine, you need:**
> 1. **GitHub MCP server** configured in your AI tool's MCP settings
> 2. **GitHub auth** (personal access token with `repo` + `project` scopes)
>
> With both configured, query GitHub directly: `list_issues(milestone="M31.5", state="open")`
> This works identically on any machine — MacBook, Windows PC, Linux laptop.
>
> **⚡ New:** We've migrated from "Cycle N Phase M" to **Milestone-Based Versioning** (M.N format).
> See [ADR 0032](../decisions/0032-milestone-based-planning-schema.md) and [Milestone Mapping](milestone-mapping.md) for details.

---

## 🤖 LLM Navigation Guide

**Choose your section based on your task:**

| **Your Task** | **Go To Section** | **Purpose** |
|---------------|-------------------|-------------|
| 📊 **Quick status check** | [Quick Status](#quick-status) | One-table snapshot of current state |
| 🚀 **Starting new work** | [Active Milestone](#active-milestone) | Detailed info on current milestone only |
| ✅ **Recording completion** | [Recent Completions](#recent-completions) | Add to top of list (keep last 3 milestones) |
| 📦 **Looking up old milestone** | [Milestone Archive](#milestone-archive) | Full historical record (M29.1 and earlier) |
| 🗺️ **Planning next work** | [Roadmap](#roadmap) | Next 3-4 milestones + future BCs |
| 🔗 **Finding references** | [Quick Links](#quick-links) | GitHub, docs, ADRs |

**Update Instructions:**
- **Milestone starts:** Update [Active Milestone](#active-milestone) section + Quick Status table
- **Milestone completes:** Move Active → [Recent Completions](#recent-completions), update Quick Status, add retrospective link
- **Archiving old milestones:** After 3 milestones in Recent Completions, move oldest to [Milestone Archive](#milestone-archive)
- **Roadmap changes:** Update [Roadmap](#roadmap) with new priorities (keep focused on next 3-4 only)

---

## Quick Status

| Aspect | Status |
|--------|--------|
| **Current Milestone** | M36.1 — Listings BC Foundation (Session 4 Complete — Phase 1 Gate Met) |
| **Status** | ✅ **PHASE 1 COMPLETE** — Paginated endpoint + CORS + detail page + E2E coverage; 35/35 tests passing |
| **Recent Completion** | M36.0 — Engineering Quality (2026-03-29) |
| **Previous Completion** | M35.0 — Product Expansion Begins (2026-03-27) |
| **Active BCs** | 19 total (Listings BC scaffold added) |

*Last Updated: 2026-03-30 (M36.1 Session 4 complete — Phase 1 gate met)*

---

## Active Milestone

### 📋 M36.1: Listings BC Foundation

**Status:** 🔨 **IN PROGRESS** — Session 2 complete; domain core delivered (aggregate, projections, handlers, recall cascade)
**Goal:** Deliver the Listings BC with full listing lifecycle, OWN_WEBSITE fast-path, recall cascade, ProductSummaryView anti-corruption layer, and Backoffice admin pages

**Scope:** Phase 0 cleanup + full Phase 1 (Option B — 5–7 sessions)

**Pre-Planning Input:**
- [M36.0 Milestone Closure Retrospective](./milestones/m36-0-milestone-closure-retrospective.md) — What M36.1 inherits
- [M36.1 Phase 0 Reconciliation](./milestones/m36-1-phase-0-reconciliation.md) — Task-by-task audit of Product Catalog ES migration
- [M36.1 Plan](./milestones/m36-1-plan.md) — Implementation contract with session sequence, guard rails, and DoD
- [Catalog-Listings-Marketplaces Cycle Plan](./catalog-listings-marketplaces-cycle-plan.md) — Approved execution plan (Phases 0–3)

**Key Decisions:**
- D1–D10 from execution plan remain valid
- D11 (new): Authorization for new BCs from day one — `[Authorize]` on all endpoints from first commit
- ADRs renumbered: 0041–0046 (plan's 0030–0038 range occupied)
- Backoffice.Web absorbs Phase 1 admin pages (no new scaffold)
- Listings.Api: port 5246, database `listings`

**Phase 0 Prerequisites (deliver in Session 1):**
- 7 granular Product Catalog integration contracts (Task 0.8)
- Enriched `ProductDiscontinued` with `Reason` + `IsRecall` (Task 0.9)
- `product-recall` priority exchange (Task 0.12)
- Event sourcing behavior tests (Task 0.15)

**Session Plan:**
1. Phase 0 cleanup + Listings BC scaffold
2. Listing aggregate + ProductSummaryView
3. Recall cascade + ListingsActiveView
4. Backoffice admin pages
5. Integration tests + E2E + polish

**Guard Rails:**
- GR-1: Every new BC must include `opts.Policies.AutoApplyTransactions()` before any handler is written
- GR-2: Every new BC API must have `[Authorize]` from first commit
- GR-3: No HTTP calls from Listings to Product Catalog — `ProductSummaryView` is the ACL
- GR-4: Phase 0 prerequisites confirmed green before Listings aggregate work
- GR-5: New projects added to solution file and Docker Compose immediately
- GR-6: Integration tests before UI work
- GR-7: Naming conventions per ADR 0040

**CI Baseline:**
- CI Run #808 (green on main)
- E2E Run #381 (green on main)
- CodeQL Run #369 (green on main)

**Session 1 Progress (2026-03-29):**
- Phase 0 tasks completed:
  - ✅ 7 granular integration contracts created (`Messages.Contracts/ProductCatalog/`)
  - ✅ `ProductAdded` enriched with Status, Brand, HasDimensions
  - ✅ `ProductDiscontinued` enriched with Reason, IsRecall
  - ✅ `ProductUpdated` marked `[Obsolete]`
  - ✅ 8 handlers updated to publish granular events + CreateProductES enriched
  - ✅ `product-recall` fanout exchange configured
  - ✅ 10 event sourcing behavior tests added (58/58 Product Catalog tests pass)
- Listings BC scaffold:
  - ✅ Domain project (`src/Listings/Listings/`)
  - ✅ API project (`src/Listings/Listings.Api/`) with AutoApplyTransactions, JWT auth, health endpoint
  - ✅ Integration test project with TestAuth (1/1 health check test passes)
  - ✅ Added to CritterSupply.slnx, Docker Compose (port 5246), postgres init script
  - ✅ `Messages.Contracts/Listings/` directory created
- ADRs:
  - ✅ ADR 0041 — Product Catalog ES Migration Decisions
  - ✅ ADR 0042 — `catalog:` Namespace UUID v5 Convention
- Build: 0 errors, 33 warnings (matches M36.0 baseline)
- Session 2 picks up: Listing aggregate + ProductSummaryView ACL
- Retrospective: [Session 1](./milestones/m36-1-session-1-retrospective.md)

**Session 2 Progress (2026-03-29):**
- Listing aggregate (event-sourced):
  - ✅ `Listing` aggregate with `Create()` factory and `Apply()` methods per event
  - ✅ Domain events: `ListingDraftCreated`, `ListingSubmittedForReview`, `ListingApproved`, `ListingActivated`, `ListingPaused`, `ListingResumed`, `ListingEnded`, `ListingForcedDown`, `ListingContentUpdated`
  - ✅ `ListingStatus` enum: `Draft`, `ReadyForReview`, `Submitted`, `Live`, `Paused`, `Ended`
  - ✅ `ListingStreamId` — UUID v5 deterministic stream IDs per ADR 0042 (`listing:{sku}:{channelCode}`)
  - ✅ `EndedCause` enum: `ManualEnd`, `ProductDiscontinued`, `SubmissionRejected`, `ProductDeleted`
- ProductSummaryView (anti-corruption layer):
  - ✅ Marten document keyed by SKU — stores Name, Description, Category, Status, Brand, HasDimensions, ImageUrls
  - ✅ 9 individual handler classes consuming Product Catalog integration events (one per `*Handler` class per Wolverine convention)
  - ✅ Handlers: `ProductAddedHandler`, `ProductContentUpdatedHandler`, `ProductCategoryChangedHandler`, `ProductImagesUpdatedHandler`, `ProductDimensionsChangedHandler`, `ProductStatusChangedHandler`, `ProductDeletedHandler`, `ProductRestoredHandler`, `ProductDiscontinuedSummaryHandler`
- ListingsActiveView projection:
  - ✅ Inline `MultiStreamProjection<ListingsActiveView, string>` keyed by SKU
  - ✅ Maintains per-SKU list of active listing stream IDs (incremented on `ListingDraftCreated`, decremented on `ListingEnded`/`ListingForcedDown`)
- Lifecycle command handlers:
  - ✅ `CreateListing` — validates product in `ProductSummaryView`, checks `ListingsActiveView` for duplicates, starts event stream
  - ✅ `ActivateListing` — supports `OWN_WEBSITE` fast path (`Draft → Live`) and normal `Submitted → Live`
  - ✅ `PauseListing` — validates `Live → Paused`
  - ✅ `ResumeListing` — validates `Paused → Live`
  - ✅ `EndListing` — validates non-terminal state, appends `ListingEnded` with `ManualEnd` cause
- RecallCascadeHandler:
  - ✅ Consumes `ProductDiscontinued` messages (reacts only when `IsRecall == true`)
  - ✅ Loads `ListingsActiveView` for SKU, iterates active streams
  - ✅ Idempotency: skips already-ended listings before appending `ListingForcedDown`
- Integration message contracts:
  - ✅ `Messages.Contracts.Listings`: `ListingCreated`, `ListingActivated`, `ListingEnded`, `ListingForcedDown`, `ListingsCascadeCompleted`
- Program.cs:
  - ✅ Inline snapshot for `Listing` aggregate
  - ✅ `ListingsActiveViewProjection` registered as inline projection
  - ✅ Domain assembly discovery for handler scanning
- Integration tests: 18/18 passing
  - ✅ 9 lifecycle tests (create, activate OWN_WEBSITE, pause, resume, end, invalid transitions, error cases)
  - ✅ 4 ProductSummaryView tests (add, status change, delete, content update)
  - ✅ 4 recall cascade tests (3-channel force-down, idempotency, count verification, non-recall skip)
  - ✅ 1 health check test
- Build: 0 errors, 33 warnings (matches M36.0 baseline)
- Session 3 picks up: HTTP endpoints + admin UI
- Retrospective: [Session 2](./milestones/m36-1-session-2-retrospective.md)

**Session 3 Progress (2026-03-30):**
- Remaining lifecycle handlers:
  - ✅ `SubmitListingForReview` — Draft → ReadyForReview transition, validator, no integration message (internal only)
  - ✅ `ApproveListing` — ReadyForReview → Submitted transition, publishes `ListingApproved` integration message
  - ✅ `ContentPropagationHandler` — consumes `ProductContentUpdated`, propagates to Live listings only (Draft/Paused/ReadyForReview/Submitted skipped)
- Integration message contracts:
  - ✅ `ListingApproved` added to `Messages.Contracts/Listings/ListingIntegrationMessages.cs`
- HTTP endpoints (9 total in `Listings.Api/Listings/ListingEndpoints.cs`):
  - ✅ POST `/api/listings` — CreateListing [Authorize]
  - ✅ POST `/api/listings/{id}/submit-for-review` — SubmitForReview [Authorize]
  - ✅ POST `/api/listings/{id}/approve` — ApproveListing [Authorize]
  - ✅ POST `/api/listings/{id}/activate` — ActivateListing [Authorize]
  - ✅ POST `/api/listings/{id}/pause` — PauseListing [Authorize]
  - ✅ POST `/api/listings/{id}/resume` — ResumeListing [Authorize]
  - ✅ POST `/api/listings/{id}/end` — EndListing [Authorize]
  - ✅ GET `/api/listings/{id}` — GetListing [Authorize]
  - ✅ GET `/api/listings?sku={sku}` — ListListings [Authorize]
- Backoffice Blazor components:
  - ✅ `ListingStatusBadge` shared component with data-testid per status variant
  - ✅ Listings admin page at `/admin/listings` with status filtering
  - ✅ Pre-flight discontinuation modal (P0.1) with affected listing count from API
  - ✅ NavMenu updated with Listings link under ProductManager policy
- Integration tests: 32/32 passing (18 baseline + 14 new)
  - ✅ 5 ReviewWorkflowTests (submit/approve lifecycle + invalid transitions)
  - ✅ 3 ContentPropagationTests (Live updated, Draft ignored, Paused ignored)
  - ✅ 6 ListingEndpointTests (create 201, submit 200, approve 200, get 200, list 200, auth verification)
- Build: 0 errors, 33 warnings (matches M36.0 baseline)
- Session 4 picks up: E2E stubs, listing detail page, full listing query, pre-flight modal wiring
- Retrospective: [Session 3](./milestones/m36-1-session-3-retrospective.md)

**Session 4 Progress (2026-03-30):**
- Backend completions:
  - ✅ CORS configuration in `Listings.Api/Program.cs` — `BackofficePolicy` allowing `http://localhost:5244`
  - ✅ Paginated `GET /api/listings/all?page=&pageSize=&status=` endpoint — coexists with SKU-filtered endpoint
  - ✅ `ListingsApi` named HttpClient added to `Backoffice.Web/Program.cs` (base `http://localhost:5246`)
  - ✅ `ListingsAdmin.razor` updated to use paginated endpoint with server-side filtering and pagination
  - ✅ Pre-flight discontinuation modal wired into `ProductEdit.razor` via `IDialogService`
- Listing detail page:
  - ✅ `/admin/listings/{id}` read-only detail page with all `data-testid` attributes
  - ✅ Action buttons (Approve, Pause, End) present as disabled stubs with tooltips
  - ✅ Back navigation to `/admin/listings`
  - ✅ 404 handling, session expired handling
- E2E page objects:
  - ✅ `ListingsAdminPage.cs` — navigate, filter, row count, detail navigation
  - ✅ `ListingDetailPage.cs` — navigate, read fields, check disabled buttons, back nav
- E2E feature files:
  - ✅ `ListingsAdmin.feature` — 3 executable + 2 `@wip` scenarios
  - ✅ `ListingsDetail.feature` — 1 executable + 3 `@wip` scenarios
- Integration tests: 35/35 passing (32 baseline + 3 new paginated endpoint tests)
- Build: 0 errors, 33 warnings
- **Phase 1 gate: MET** — all criteria from execution plan satisfied
- Session 5 picks up: Phase 2 planning (Marketplaces BC), E2E step definitions, action button wiring
- Retrospective: [Session 4](./milestones/m36-1-session-4-retrospective.md)

---

## Recent Completions

### ✅ M36.0: Engineering Quality (2026-03-29)

**Status:** ✅ **Complete** — 6 sessions; all 9 definition-of-done criteria met
**Goal:** Critter Stack idiom compliance, DDD naming audit, authorization hardening, integration and E2E test coverage
**CI Confirmation:** CI Run #808, E2E Run #381, CodeQL Run #369

**Key Deliverables:**
- **Track A (Session 1):** Shared `TestAuthHandler` utility. 21 pre-existing test failures eliminated across Orders, CustomerIdentity, Correspondence.
- **Track B (Sessions 2–3):** All `bus.PublishAsync()` replaced with `OutgoingMessages`. 34 `SaveChangesAsync()` calls removed. `IStartStream` applied to Payments.
- **Track C (Sessions 3–4):** Command renames (`PaymentRequested` → `RequestPayment`, etc.). ADR 0040 documented. Vertical slice splits across VP, PC, VI.
- **Track D (Sessions 4–5):** 55 endpoints protected with `[Authorize]` across 10 BCs. JWT Bearer added to Shopping + Storefront.
- **Track E (Session 6):** UI cleanup (brand, templates, dead-end buttons). VP Team Management E2E (15 scenarios). Backoffice Order Search/Detail E2E (4 scenarios).
- **Root cause fix:** `AutoApplyTransactions()` added to Product Catalog — root cause of 5 projection failures misclassified as timing.

**Inherited by M36.1:**
1. VP Team Management `@wip` scenarios (13) — deferred to M37.x
2. Returns cross-BC saga tests (6 skipped) — monitor
3. Product Catalog `SaveChangesAsync` sweep (12 calls) — address opportunistically

**Retrospectives:** [Session 1](./milestones/m36-0-session-1-retrospective.md) · [Session 2](./milestones/m36-0-session-2-retrospective.md) · [Session 3](./milestones/m36-0-session-3-retrospective.md) · [Session 4](./milestones/m36-0-session-4-retrospective.md) · [Session 5](./milestones/m36-0-session-5-retrospective.md) · [Session 6](./milestones/m36-0-session-6-retrospective.md) · [Milestone Closure](./milestones/m36-0-milestone-closure-retrospective.md)

### ✅ M35.0: Product Expansion Begins (2026-03-27)

**Status:** ✅ **Complete** — 7 sessions + documentation audit + closure session
**Goal:** Deliver deferred M34.0 product items, begin product expansion

**Session 1 Progress (2026-03-27):**
- ✅ **Housekeeping:** Updated CURRENT-CYCLE.md — moved M34.0 to Recent Completions, set M35.0 as active milestone
- ✅ **Plan:** Created [M35.0 plan](./milestones/m35-0-plan.md) documenting deferred items and Session 1 scope
- ✅ **CustomerSearch detail:** Created `GET /api/backoffice/customers/{customerId}` BFF endpoint + `CustomerDetail.razor` page at `/customers/{customerId:guid}`
- ✅ **View Details button:** Enabled previously-disabled button in `CustomerSearch.razor` with navigation to detail page
- ✅ **Integration tests:** 4 new tests (happy path, not-found, no-orders, with-addresses) — 95/95 Backoffice.Api.IntegrationTests pass

**Session 2 Progress (2026-03-27):**
- ✅ **E2E page object:** Created `CustomerDetailPage.cs` POM matching actual `data-testid` attributes
- ✅ **E2E scenarios:** Created `CustomerDetail.feature` with 8 scenarios covering search→detail navigation, customer info display, order history, addresses, back-navigation, order detail navigation, not-found, and empty search results
- ✅ **Step definitions:** Created `CustomerDetailSteps.cs` with steps for the two-page customer service flow
- ✅ **POM extension:** Extended `CustomerSearchPage.cs` with new methods using corrected locators (existing methods preserved)
- ✅ **data-testid fix:** Added `customer-search-no-results` to CustomerSearch.razor empty-results alert
- ✅ **Test bug classification:** Identified stale POM locators in existing `CustomerSearchPage.cs` (test bug — locators written for aspirational page architecture, not actual implementation)
- ✅ **Build:** 0 errors, pre-existing warnings unchanged
- ✅ **Integration tests:** 95/95 Backoffice.Api.IntegrationTests still passing

**Session 3 Progress (2026-03-27):**
- ✅ **Stale locator fix:** Consolidated `CustomerSearchPage.cs` — replaced stale locators (`customer-search-email`, `customer-search-submit`, `customer-details-card`, `order-history-table`, `return-requests-section`) with correct data-testid values. Removed 15 methods for non-existent inline-details UI.
- ✅ **CustomerService.feature rewrite:** Rewrote 10 stale scenarios → 6 matching the two-page flow (CustomerSearch → CustomerDetail). Removed 4 scenarios testing non-existent UI (return request approval/denial, inline return requests, pagination).
- ✅ **CustomerServiceSteps.cs cleanup:** Removed 26 dead step definitions targeting non-existent UI. Kept 3 shared Given steps used by both CustomerService.feature and CustomerDetail.feature.
- ✅ **Track 3 assessment:** All items remain deferred — require event modeling or Vendor Identity architectural work before implementation.
- ✅ **Build:** 0 errors, warnings reduced from 36 to 10
- ✅ **Integration tests:** 95/95 Backoffice.Api.IntegrationTests still passing
- ✅ **CI:** E2E Run #331, CI Run #760 (action_required — awaiting environment approval for PR). Main baseline: E2E Run #330 (green).

**Session 4 Progress (2026-03-27):**
- ✅ **ASIE prerequisite assessment:** Confirmed issues #254 (EF Core project structure) and #255 (CreateVendorTenant command) are already fully implemented in the codebase. Both GitHub issues are stale — work was completed but issues were never closed. No Track 3 items are blocked by Vendor Identity prerequisites.
- ✅ **EMF: Exchange v2 cross-product exchange:** Modeled 5 new domain events, 4 commands, 2 read models, 5 slices, 5 Given/When/Then scenarios. Committed 10 Gherkin scenarios to `docs/features/returns/cross-product-exchange.feature`.
- ✅ **EMF: Vendor Portal team management:** Confirmed 7 existing events/commands in VendorIdentity BC. Identified 2 new read models (TeamRosterView, PendingInvitationsView), 8 slices, 6 Given/When/Then scenarios. Committed 17 Gherkin scenarios to `docs/features/vendor-portal/team-management.feature`.
- ✅ **EMF: Product Catalog Evolution:** Modeled 11 domain events, 11 commands, 2 read models, 5 slices, 4 Given/When/Then scenarios. Committed 13 Gherkin scenarios to `docs/features/product-catalog/catalog-event-sourcing-migration.feature`.
- ✅ **Search BC:** Confirmed out of scope for M35.0 Track 3. Deferred to future milestone.
- ✅ **Session plan:** Created [m35-0-session-4-plan.md](./milestones/m35-0-session-4-plan.md) with full event models, slice tables, and implementation contract for Session 5.
- ✅ **Build:** 0 errors, 34 warnings (unchanged — no application code modified)
- ✅ **Integration tests:** 95/95 Backoffice.Api.IntegrationTests still passing
- ✅ **CI:** E2E Run #333 (green on main), CI Run #762 (green on main)

**Track 3 Clearance Status:**
- ✅ **Exchange v2 (cross-product)** — EMF cleared, ASIE cleared, **implemented in Session 5**
- ✅ **Vendor Portal Team Management** — EMF cleared, ASIE cleared, **BFF implemented in Session 6** (frontend deferred)
- ✅ **Product Catalog Evolution** — EMF cleared, ASIE cleared, **implemented in Sessions 5+6**
- ❌ **Search BC** — Deferred to future milestone (no existing design)

**Session 5 Progress (2026-03-27):**
- ✅ **Product Catalog ES migration (foundation):** 11 domain events, `CatalogProduct` aggregate, `ProductCatalogView` projection (inline), `ProductCatalogViewProjection`. Event-sourced handlers: `CreateProduct`, `ChangeProductName`, `ChangeProductStatus`, `SoftDeleteProduct`, `RestoreProduct`, `MigrateProduct`. Query handlers: `GetProductES`, `ListProductsES`. Integration event publishing for `ProductAdded` and `ProductDiscontinued`.
- ✅ **Exchange v2 (cross-product exchange):** 5 new domain events (`CrossProductExchangeRequested`, `ExchangePriceDifferenceCalculated`, `ExchangeAdditionalPaymentRequired`, `ExchangeAdditionalPaymentCaptured`, `ExchangePartialRefundIssued`). `ApproveExchange` handler updated to support price differences in both directions. RabbitMQ routing for new events. 4 new integration message contracts.
- ✅ **Product Catalog integration tests:** 41/41 passing
- ✅ **Returns unit tests:** 66/66 passing (6 new cross-product exchange tests)
- ✅ **Returns integration tests:** 30/30 passing (14 pre-existing failures due to auth — fixed in Session 6)

**Session 6 Progress (2026-03-27):**
- ✅ **Returns test fix:** Root cause — GET endpoints had `[Authorize]` but test fixture lacked auth bypass. Fixed by registering `TestAuthHandler` for both `Backoffice` and `Vendor` JWT schemes. **44/44 Returns integration tests now pass.**
- ✅ **Product Catalog ES migration (granular handlers):** 5 new event-sourced handlers — `ChangeProductDescriptionES`, `ChangeProductCategoryES`, `UpdateProductImagesES`, `ChangeProductDimensionsES`, `UpdateProductTagsES`. 3 legacy document-store handlers removed (`UpdateProduct.cs`, `UpdateProductDescription.cs`, `UpdateProductDisplayName.cs`). **48/48 integration tests pass.**
- ✅ **Vendor Portal Team Management BFF:** 2 BFF proxy endpoints (`GET /api/vendor-portal/team/roster`, `GET /api/vendor-portal/team/invitations/pending`). Local Marten read models (`TeamMember`, `TeamInvitation`). Event handlers subscribing to 7 VendorIdentity lifecycle events. RabbitMQ wiring in both VendorIdentity.Api and VendorPortal.Api. **86/86 VendorPortal tests pass.**
- ✅ **VP Team Management Blazor page:** Delivered in Closure Session
- ✅ **GitHub issues #254 and #255:** Closed in Closure Session

**Documentation Audit (2026-03-27):**
- ✅ **Audit findings:** [m35-0-audit-findings.md](./milestones/m35-0-audit-findings.md) — handler-by-handler Product Catalog migration table, Returns exchange assessment, VP team management completion status, issue #254/#255 status
- ✅ **CURRENT-CYCLE.md:** Updated with Session 5+6 progress, corrected status
- ✅ **CONTEXTS.md:** Updated Product Catalog (ES migration), Returns (cross-product exchange), Vendor Portal (team management BFF), Backoffice (moved to Implemented)
- ✅ **README.md:** Updated Backoffice status, Product Catalog description
- ✅ **Session 6 retrospective:** Created retroactively

**Planned Tracks (sequenced):**
- **Track 1:** Housekeeping — CURRENT-CYCLE.md update, M35.0 plan creation ✅
- **Track 2:** CustomerSearch detail page (deferred from M34.0) — BFF endpoint, Blazor page, integration tests ✅
- **Track 3:** Product expansion — 3 items implemented across Sessions 5+6 ✅ (VP frontend deferred)

**Session 1 Retrospective:** [m35-0-session-1-retrospective.md](./milestones/m35-0-session-1-retrospective.md)
**Session 2 Retrospective:** [m35-0-session-2-retrospective.md](./milestones/m35-0-session-2-retrospective.md)
**Session 3 Retrospective:** [m35-0-session-3-retrospective.md](./milestones/m35-0-session-3-retrospective.md)
**Session 4 Plan:** [m35-0-session-4-plan.md](./milestones/m35-0-session-4-plan.md)
**Session 4 Retrospective:** [m35-0-session-4-retrospective.md](./milestones/m35-0-session-4-retrospective.md)
**Session 5 Retrospective:** [m35-0-session-5-retrospective.md](./milestones/m35-0-session-5-retrospective.md)
**Session 6 Retrospective:** [m35-0-session-6-retrospective.md](./milestones/m35-0-session-6-retrospective.md)
**Audit Findings:** [m35-0-audit-findings.md](./milestones/m35-0-audit-findings.md)

**Closure Session Progress (2026-03-27):**
- ✅ **Product Catalog ES migration (final handler):** AssignProductToVendor migrated to event sourcing — added `ProductVendorAssigned` event, aggregate Apply method, projection handler. Created `AssignProductToVendorES.cs` with GET + POST single + POST bulk endpoints. Removed old document-store handler. **14/14 handlers now event-sourced. 48/48 integration tests pass.**
- ✅ **VP Team Management Blazor page:** Created `TeamManagement.razor` at `/team` with team roster table, pending invitations table, loading/error/empty states, admin-only access guard, data-testid attributes on all interactive elements. Added NavMenu entry (Admin-only visibility).
- ✅ **GitHub issues #254 and #255:** Closed with implementation citations
- ✅ **Milestone closure retrospective:** [m35-0-milestone-closure-retrospective.md](./milestones/m35-0-milestone-closure-retrospective.md)
- ✅ **M36.0 pre-planning findings:** [m36-0-pre-planning-quality-findings.md](./milestones/m36-0-pre-planning-quality-findings.md)
- ✅ **CURRENT-CYCLE.md:** M35.0 moved to Recent Completions, M36.0 set as active

**Milestone Closure:**
- **All planned Track 1, 2, 3 items delivered**
- **CI:** Main baseline CI Run #770 (green), E2E Run #341 (green), CodeQL Run #339 (green)
- **PR:** CI Run #772, E2E Run #343 (pending approval)

**Remaining Before Milestone Closure:** None — milestone complete.

**References:**
- [M35.0 Plan](./milestones/m35-0-plan.md)
- [M34.0 Plan](./milestones/m34-0-plan.md)

---

## Recent Completions

### M34.0: Experience Completion + Vocabulary Alignment

**Status:** ✅ **COMPLETE** — All tracks delivered; CI verified green (Run #320)
**Goal:** Restore trustworthy test signal first, then complete already-supported user experiences and align vocabulary across BC boundaries

**What Shipped:**
- ✅ **S1–S4 (Stabilization):** Backoffice E2E bootstrap fix, test baseline (118 non-E2E tests), route drift cleanup, vocabulary normalization
- ✅ **B1 (Issue #460):** Vendor Portal RBAC fix — ReadOnly users can now view change requests
- ✅ **F1 (Experience completion):** OrderDetail.razor, ReturnDetail.razor, NavMenu link enablement, Homepage link enablement
- ✅ **F2 (Vocabulary):** Return status "Requested" alignment, NavMenu vocabulary alignment, Homepage vocabulary alignment
- ✅ **CI:** E2E Run #320 green (all 6 jobs), CI Run #750 green, CodeQL Run #323 green
- ✅ **Label drift:** Resolved 9 open issues, added missing labels to `01-labels.sh`, fixed workflow trigger
- ⏳ **Deferred:** CustomerSearch detail page (needs new backend surface area → M35.0), Vendor Portal team management (not architecturally supported)

**Session 1 Retrospective:** [m34-0-session-1-retrospective.md](./milestones/m34-0-session-1-retrospective.md)

**References:**
- [M33-M34 Proposal](./milestones/m33-m34-engineering-proposal-2026-03-21.md)
- [M34.0 Plan](./milestones/m34-0-plan.md)
- [M34 RBAC Issue Draft](./milestones/m34-0-rbac-issue-draft.md)

*Completed: 2026-03-26*

---

### M33.0: Code Correction + Broken Feedback Loop Repair

**Status:** ✅ **COMPLETE** — All 15 sessions finished, all 12 exit criteria met (2026-03-25)
**Goal:** Fix broken tests, build missing projections, execute structural refactors, document canonical patterns

**What Shipped:**
- ✅ INV-3 fix: `AdjustInventoryEndpoint` pattern correction + integration message publishing
- ✅ F-8: `BackofficeTestFixture.ExecuteAndWaitAsync()` instrumentation (75 tests passing)
- ✅ 3 Marten projections: ReturnMetricsView, CorrespondenceMetricsView, FulfillmentPipelineView
- ✅ 2 Backoffice pages: Order Search, Return Management (with 10 integration tests)
- ✅ Returns BC structural refactor: R-1 through R-7 (11 command vertical slices)
- ✅ Vendor Portal structural refactor: VP-1 through VP-6 (folder flattening, handler explosion, validators)
- ✅ Backoffice folder restructure: BO-1/BO-2/BO-3 (8 feature folders, transaction fix)
- ✅ ADR 0039: Canonical validator placement convention
- ✅ CheckoutCompleted dual-payload collision fix (🔴 live risk eliminated)
- ✅ 9 Quick Wins: INV-1/2, PR-1, CO-1, PAY-1/FUL-1/ORD-1, F-9
- ✅ Backoffice Returns E2E coverage (12 Gherkin scenarios, POM, step definitions)
- ✅ Build: 0 errors, 36 pre-existing warnings (unchanged)
- ✅ All 91 Backoffice.Api.IntegrationTests passing, all 86 VendorPortal.Api.IntegrationTests passing

**Key Learnings:**
- Mixing `IMessageBus.InvokeAsync()` with manual event appending doesn't respect `Before()` validation
- Wolverine auto-transaction removes need for manual `SaveChangesAsync()` in handlers
- Vertical slice organization: Command + Handler + Validator + Events in single file (ADR 0039)
- M33.0 E2E stabilization patterns: Remove aggressive error UI checks, rely on natural timeouts

**References:**
- [M33.0 Milestone Closure Retrospective](./milestones/m33-0-milestone-closure-retrospective.md)
- [M33.0 E2E Test Efforts Retrospective](./milestones/m33-0-e2e-test-efforts-retrospective.md)
- [ADR 0039: Canonical Validator Placement](../decisions/0039-canonical-validator-placement.md)
- [All Session Retrospectives](./milestones/) (m33-0-session-*-retrospective.md files)

*Completed: 2026-03-25*

---

### M32.4: Backoffice Phase 4 — E2E Stabilization + UX Polish

**Status:** ✅ **COMPLETE** — All critical and medium priorities finished in single session (2026-03-21)
- ✅ INV-3 Fixed: `AdjustInventoryEndpoint` reverted to manual validation + explicit integration message publishing
- ✅ All 48 Inventory.Api.IntegrationTests passing
- ✅ F-8 Verified: `BackofficeTestFixture.ExecuteAndWaitAsync()` working (75 Backoffice tests passing)
- ✅ Retrospective documenting Wolverine compound handler learnings created
- **Key Learning:** Mixing `IMessageBus.InvokeAsync()` with manual event appending doesn't respect `Before()` validation

**Sessions 5+6 Completion (2026-03-22 to 2026-03-23):**
- ⚠️ **Priority 3 PARTIALLY DELIVERED:** Order Search + Return Management pages were added to Backoffice.Web, but post-mortem review found unresolved recovery work
- ✅ Created 2 new Blazor WASM pages (`/orders/search`, `/returns`)
- ✅ Updated NavMenu with role-based navigation items
- ❌ Created Backoffice.Web.UnitTests bUnit project — **all tests removed after 7 failed fix attempts**
- ❌ No replacement E2E coverage was added, leaving the new pages with **ZERO automated UI coverage**
- ⚠️ Post-mortem review found route-shape/BFF mismatch and status/discoverability inconsistencies that should be addressed before treating Priority 3 as closed
- ✅ All 51 Backoffice.Api.IntegrationTests passing (no regressions in that suite)
- ✅ Retrospective documenting bUnit limitations and Blazor WASM local DTOs created
- **See:** `docs/planning/milestones/m33-0-post-mortem-recovery-review.md`

**Session 7 Completion (2026-03-23):**
- ✅ **Priority 3 FULLY DELIVERED:** All post-mortem blocking issues resolved
- ✅ Created 2 BFF proxy endpoints at correct `/api/backoffice/*` paths (SearchOrders, GetReturns)
- ✅ Fixed frontend route mismatches in OrderSearch.razor and ReturnManagement.razor
- ✅ Fixed NavMenu authorization (operations-manager can now see Order Search + Return Management)
- ✅ Fixed return status vocabulary ("Pending" → "Requested", removed invalid status from UI)
- ✅ Added 10 comprehensive integration tests (4 OrderSearch + 6 ReturnList scenarios)
- ✅ All 91 Backoffice.Api.IntegrationTests passing (up from 51)
- ✅ Zero build errors, zero test failures
- ✅ Retrospective documenting recovery patterns and lessons learned created
- **See:** `docs/planning/milestones/m33-0-session-7-retrospective.md`

**Session 2 Completion (PREVIOUSLY UNDOCUMENTED):**
- ✅ **Priority 2 COMPLETE:** All three Marten projections built and tested
- ✅ ReturnMetricsView projection (inline, singleton, active return counts)
- ✅ CorrespondenceMetricsView projection (inline, singleton, email queue health)
- ✅ FulfillmentPipelineView projection (inline, singleton, active shipments pipeline)
- ✅ All projections registered in Program.cs with `ProjectionLifecycle.Inline`
- ✅ 14 projection integration tests passing (EventDrivenProjectionTests)
- ✅ Dashboard uses ReturnMetricsView for PendingReturns KPI
- **See:** `docs/planning/milestones/m33-0-session-2-retrospective.md`

**Session 8 Completion (2026-03-23):**
- ✅ **Phase 1 COMPLETE:** XC-1 ADR + CheckoutCompleted fix delivered
- ✅ ADR 0039 published (canonical validator placement convention)
- ✅ Shopping's `CheckoutCompleted` renamed to `CartCheckoutCompleted`
- ✅ Orders' internal `CheckoutCompleted` renamed to `OrderCreated`
- ✅ All consumers updated (zero `CheckoutCompleted` references remain)
- ✅ Build succeeds (0 errors, 36 pre-existing warnings)
- ✅ All tests passing (971+ tests across all BCs)
- ✅ Live 🔴 risk eliminated (dual-payload collision at checkout)
- ✅ Retrospective documenting Phase 1 completion created
- **See:** `docs/planning/milestones/m33-0-session-8-retrospective.md`

**Session 9 Completion (2026-03-23):**
- ✅ **Phase 2 COMPLETE:** All 9 Quick Wins items delivered in single session
- ✅ INV-1: Consolidated AdjustInventory* 4-file shatter → AdjustInventory.cs
- ✅ INV-2: Consolidated ReceiveInboundStock* split → ReceiveInboundStock.cs
- ✅ INV-3: Renamed Inventory folders (Commands/ → InventoryManagement/, Queries/ → StockQueries/)
- ✅ PR-1: Merged Pricing validator splits (SetInitialPrice + ChangePrice)
- ✅ CO-1: Exploded MessageEvents.cs → 4 individual event files
- ✅ PAY-1/FUL-1/ORD-1: Moved isolated Queries to feature-named folders
- ✅ F-9: Fixed Orders test collection attributes (3 raw string literals)
- ✅ Build: 0 errors, 36 pre-existing warnings (unchanged)
- ✅ All tests passing (no regressions)
- ✅ Retrospective documenting Phase 2 completion created
- **See:** `docs/planning/milestones/m33-0-session-9-retrospective.md`

**Session 10 Completion (2026-03-23):**
- ✅ **Phase 3 STARTED:** R-4 fully delivered, R-1 partial (3/11 commands) — 27% complete
- ✅ R-4: Exploded `ReturnCommandHandlers.cs` (387 lines) → 5 individual handler files
- ✅ R-1 (3/11): Created vertical slices for DenyReturn, SubmitInspection, RequestReturn
- ✅ ReturnValidators.cs now empty (all validators moved to vertical slice files)
- ✅ Build: 0 errors, 36 pre-existing warnings (unchanged)
- ⚠️ Pre-existing test failures (14 failures, 30 passed — auth issues, not refactoring-related)
- ✅ Session plan documenting Phase 3 scope and sequencing created
- ✅ Session retrospective documenting learnings and shared type dependencies created
- **See:** `docs/planning/milestones/m33-0-session-10-plan.md`, `docs/planning/milestones/m33-0-session-10-retrospective.md`

**Session 11 Completion (2026-03-23):**
- ✅ **Phase 3 COMPLETE (R-1 + R-3 delivered):** All 11 command handlers migrated to vertical slices
- ✅ R-1: Created 7 remaining vertical slices (ApproveReturn, ReceiveReturn, StartInspection, ExpireReturn, ApproveExchange, DenyExchange, ShipReplacementItem)
- ✅ R-3: Deleted `ReturnCommands.cs` and `ReturnValidators.cs` bulk files
- ✅ All handlers follow ADR 0039 canonical validator placement convention
- ✅ Build: 0 errors, 36 pre-existing warnings (unchanged from Session 10)
- ✅ Preserved all business logic exactly (price validation, scheduled messages, multi-event handlers)
- ✅ 8 commits total (7 vertical slices + 1 bulk file deletion)
- ✅ Session plan + retrospective documenting pattern variations and learnings created
- **See:** `docs/planning/milestones/m33-0-session-11-plan.md`, `docs/planning/milestones/m33-0-session-11-retrospective.md`

**Session 12 Completion (2026-03-23):**
- ✅ **Phase 4 COMPLETE (VP-5 + VP-6 + verification):** All Vendor Portal structural refactoring finished
- ✅ VP-5: VendorHubMessages.cs split into individual message files
- ✅ VP-6: FluentValidation validators added to all 7 VP commands
- ✅ VP-1/VP-2/VP-3/VP-4: Verified complete from prior sessions (folder flattening, handler explosion)
- ✅ F-2 Phase A: No feature-level @ignore tags in E2E files
- ✅ Build: 0 errors, 36 pre-existing warnings (unchanged)
- ✅ All 86 VendorPortal.Api.IntegrationTests passing (0% regression rate)
- ✅ Session retrospective documenting Phase 4 completion + timeout recovery pattern
- **See:** `docs/planning/milestones/m33-0-session-12-retrospective.md`

**Session 13 Completion (2026-03-24):**
- ✅ **Phase 5 COMPLETE (BO-1 + BO-2 + BO-3 + XC-3):** All Backoffice folder restructure + transaction fix delivered
- ✅ XC-3 + BO-2: AcknowledgeAlert transaction fix (removed manual `SaveChangesAsync()` — Wolverine auto-transaction)
- ✅ BO-1: Restructured Backoffice.Api folders (23 endpoint files → 8 feature-named folders)
- ✅ BO-3: Colocated projections with features (10 projection files → 2 feature folders)
- ✅ Namespace migration: All `Backoffice.Projections.*` → `Backoffice.DashboardReporting.*` or `Backoffice.AlertManagement.*`
- ✅ Test fixes: Updated 3 integration tests to manually commit after calling handler directly
- ✅ Build: 0 errors, 36 pre-existing warnings (unchanged)
- ✅ All 91 Backoffice.Api.IntegrationTests passing (0% regression rate)
- ✅ Session retrospective documenting transaction pattern learnings + namespace migration strategy
- **See:** `docs/planning/milestones/m33-0-session-13-retrospective.md`

**Session 14 Completion (2026-03-25):**
- ✅ **Phase 6 COMPLETE (VERIFICATION ONLY):** All deliverables already existed from previous sessions
- ✅ 3 Marten projections: ReturnMetricsView, CorrespondenceMetricsView, FulfillmentPipelineView (Session 2)
- ✅ 2 pages: Order Search (`/orders/search`), Return Management (`/returns`) (Sessions 5+6)
- ✅ 2 BFF endpoints: SearchOrders, GetReturns at `/api/backoffice/*` (Session 7)
- ✅ NavMenu authorization aligned with page access (Session 7)
- ✅ Return status vocabulary fixed (Requested, not Pending) (Session 7)
- ✅ 10 integration tests (4 OrderSearch + 6 ReturnList) (Session 7)
- ✅ All 91 Backoffice.Api.IntegrationTests passing (unchanged)
- ⚠️ bUnit infrastructure exists but no actual tests (deferred per Session 5 Option A)
- ❌ Detail navigation deferred (not blocking CS workflows)
- ❌ Broader search deferred (GUID search sufficient for MVP)
- **See:** `docs/planning/milestones/m33-0-session-14-phase-6-retrospective.md`

**Session 15 Completion (2026-03-25):**
- ✅ **Phase 7 COMPLETE (OPTIONAL HARDENING):** Returns E2E coverage + Blazor WASM routing patterns documented
- ✅ 12 Gherkin scenarios in ReturnManagement.feature (navigation, filtering, authorization, session expiry)
- ✅ ReturnManagementPage POM with semantic timeout constants (WasmHydrationTimeoutMs, MudSelectListboxTimeoutMs, ApiCallTimeoutMs)
- ✅ ReturnManagementSteps binding Gherkin to POM (Given/When/Then for all 12 scenarios)
- ✅ Added 4 missing data-testid attributes to ReturnManagement.razor (page-heading, return-row-{id}, return-status, returns-loading)
- ✅ 121-line section added to e2e-playwright-testing.md documenting Blazor WASM client-side navigation patterns
- ✅ Zero build errors (Backoffice.Web + Backoffice.E2ETests compile successfully)
- ⚠️ E2E tests require Docker for execution (TestContainers dependency — deferred to CI workflow)
- 📋 **Follow-Up:** Add backoffice-e2e job to `.github/workflows/e2e.yml` (not blocking M33.0 closure)
- **See:** `docs/planning/milestones/m33-0-session-15-phase-7-retrospective.md`

**Remaining Planned Priorities:**
- ✅ **Phase 7:** Returns E2E coverage + Blazor WASM routing patterns (COMPLETE)
- 📋 **Milestone Closure:** Review all phases, ensure all exit criteria met (next session)

**References:**
- M33-M34 Proposal: `docs/planning/milestones/m33-m34-engineering-proposal-2026-03-21.md`
- Session 1 Plan: `docs/planning/milestones/m33-0-session-1-plan.md`
- Session 1 Retrospective: `docs/planning/milestones/m33-0-session-1-retrospective.md`
- Session 2 Retrospective: `docs/planning/milestones/m33-0-session-2-retrospective.md`
- Session 5 Status: `docs/planning/milestones/m33-0-session-5-status.md`
- Session 6 Status: `docs/planning/milestones/m33-0-session-6-status.md`
- Sessions 5+6 Retrospective: `docs/planning/milestones/m33-0-session-5-retrospective.md` (combined)
- Post-Mortem Recovery Review: `docs/planning/milestones/m33-0-post-mortem-recovery-review.md`
- Session 7 Plan: `docs/planning/milestones/m33-0-session-7-plan.md`
- Session 7 Retrospective: `docs/planning/milestones/m33-0-session-7-retrospective.md`
- Session 8 Plan: `docs/planning/milestones/m33-0-session-8-plan.md`
- Session 8 Retrospective: `docs/planning/milestones/m33-0-session-8-retrospective.md`
- Session 9 Plan: `docs/planning/milestones/m33-0-session-9-plan.md`
- Session 9 Retrospective: `docs/planning/milestones/m33-0-session-9-retrospective.md`
- Session 10 Plan: `docs/planning/milestones/m33-0-session-10-plan.md`
- Session 10 Retrospective: `docs/planning/milestones/m33-0-session-10-retrospective.md`
- Session 11 Plan: `docs/planning/milestones/m33-0-session-11-plan.md`
- Session 11 Retrospective: `docs/planning/milestones/m33-0-session-11-retrospective.md`
- Session 12 Retrospective: `docs/planning/milestones/m33-0-session-12-retrospective.md`
- Session 13 Plan: `docs/planning/milestones/m33-0-session-13-phase-5-plan.md`
- Session 13 Retrospective: `docs/planning/milestones/m33-0-session-13-retrospective.md`
- Session 14 Retrospective: `docs/planning/milestones/m33-0-session-14-phase-6-retrospective.md`
- Session 15 Retrospective: `docs/planning/milestones/m33-0-session-15-phase-7-retrospective.md`
- ADR 0039: `docs/decisions/0039-canonical-validator-placement.md`
- M32.4 Retrospective: `docs/planning/milestones/m32-4-session-1-retrospective.md`

---

## Recent Completions

### M32.4: Backoffice Phase 4 — E2E Stabilization + UX Polish

**Status:** ✅ **COMPLETE** — All critical and medium priorities finished in single session (2026-03-21)
**Goal:** Fix E2E test fixture issue, stabilize test suite, audit DateTimeOffset precision

**What Shipped:**
- ✅ **Priority 1 (CRITICAL):** Fixed E2E test fixture issue — Blazor WASM now publishes automatically before tests
- ✅ **Priority 2 (MEDIUM):** Automated Blazor WASM publish — MSBuild target runs before VSTest execution
- ✅ **Priority 3 (MEDIUM):** DateTimeOffset precision audit — all EF Core tests already use correct tolerance patterns
- 📄 **Documentation:** Comprehensive audit document (192 lines) documenting findings and patterns

**Key Technical Win:**
- Single MSBuild target solution addressed both Priority 1 (blocking issue) AND Priority 2 (automation), collapsing 2 planned sessions into 1

**Audit Results:**
- BackofficeIdentity (EF Core): ✅ Uses `TimeSpan.FromMilliseconds(1)` tolerance correctly
- VendorIdentity (EF Core): ✅ Uses `ShouldBeInRange()` (built-in tolerance)
- Customer Identity (EF Core): ✅ No DateTimeOffset assertions
- All Marten BCs: ✅ Not affected (Marten preserves full precision)
- **Conclusion:** No code fixes required

**Session Efficiency:** Completed 3 priorities in ~2.5 hours (planned 4-6 hours for Priority 1 alone)

**Deferred (Optional):**
- Priority 4 (LOW): GET /api/backoffice-identity/users/{userId} endpoint
- Priority 5 (LOW): Table sorting in UserList.razor

**References:**
- [M32.4 Plan](./milestones/m32-4-plan.md)
- [Session 1 Retrospective](./milestones/m32-4-session-1-retrospective.md)
- [DateTimeOffset Audit](./milestones/m32-4-datetime-offset-audit.md)

*Completed: 2026-03-21*

---

### M32.3: Backoffice Phase 3B — Write Operations Depth

**Status:** ✅ **COMPLETE** — All 10 sessions finished (10 Blazor pages, 34 E2E scenarios, 6 integration tests)
**Goal:** Implement write operations depth for Product Admin, Pricing Admin, Warehouse Admin, User Management

**What Shipped:**
- **10 Blazor WASM Pages:** ProductList, ProductEdit, PriceEdit, InventoryList, InventoryEdit, UserList, UserCreate, UserEdit
- **4 Client Interfaces Extended:** ICatalogClient, IPricingClient, IInventoryClient, IBackofficeIdentityClient (15 methods added)
- **34 E2E Scenarios Created:** ProductAdmin (6), PricingAdmin (6), WarehouseAdmin (10), UserManagement (12)
- **6 Integration Tests Passing:** BackofficeIdentity password reset (security-critical refresh token invalidation verified)
- **14 Backend Endpoints Utilized:** Product Catalog (4), Pricing (1), Inventory (4), BackofficeIdentity (5)
- **Build Status:** 0 errors across all projects (10 sessions)
- **Test Coverage:** ~85% (integration + E2E combined)

**Key Technical Wins:**
- Blazor WASM local DTO pattern (cannot reference backend projects)
- Two-click confirmation pattern for destructive actions
- Wolverine direct implementation pattern (mixed parameter source fix)
- Hidden message divs for E2E assertions
- ScenarioContext dynamic URL replacement for E2E tests
- EF Core DateTimeOffset precision tolerance pattern

**Production Readiness:** ✅ READY (with documented E2E fixture gap for M32.4)

**E2E Test Status:**
- 22/34 scenarios passing (ProductAdmin, PricingAdmin, WarehouseAdmin)
- 12/34 scenarios blocked by E2E fixture issue (UserManagement — environmental, not code defect)

**Integration Test Status:**
- 6/6 passing (BackofficeIdentity password reset endpoint)

**Session Summary:**
1. ✅ Session 1: Product Admin write UI
2. ✅ Session 2: Product List UI + API routing audit
3. ✅ Session 3: Product Admin E2E tests + Pricing Admin write UI
4. ✅ Session 4: Warehouse Admin write UI
5. ✅ Session 5: Pricing Admin E2E tests
6. ✅ Session 6: Warehouse Admin E2E tests
7. ✅ Session 7: User Management write UI
8. ❌ Session 8: SKIPPED (no CSV/Excel exports needed)
9. ✅ Session 9: User Management E2E tests + integration tests
10. ✅ Session 10: Integration test stabilization + E2E investigation
11. ✅ Session 11: Milestone wrap-up + M32.4 planning + Wolverine pattern documentation

**Deferred to M32.4:**
- E2E fixture investigation (Blazor WASM app not loading in test context, 4-6 hours)
- DateTimeOffset precision audit across all EF Core tests
- GET /api/backoffice-identity/users/{userId} endpoint (performance optimization)
- Table sorting in UserList.razor (UX enhancement)
- Enhanced error messages (400 vs 500 vs 503 specificity)

**References:**
- [M32.3 Retrospective](./milestones/m32-3-retrospective.md)
- [Session 1 Retrospective](./milestones/m32-3-session-1-retrospective.md)
- [Session 2 Retrospective](./milestones/m32-3-session-2-retrospective.md)
- [Session 4 Retrospective](./milestones/m32-3-session-4-retrospective.md)
- [Session 5 Retrospective](./milestones/m32-3-session-5-retrospective.md)
- [Session 6 Retrospective](./milestones/m32-3-session-6-retrospective.md)
- [Session 7 Retrospective](./milestones/m32-3-session-7-retrospective.md)
- [Session 9 Retrospective](./milestones/m32-3-session-9-retrospective.md)
- [Session 10 Retrospective](./milestones/m32-3-session-10-retrospective.md)
- [Session 11 Plan](./milestones/m32-3-session-11-plan.md)
- [Session 11 Retrospective](./milestones/m32-3-session-11-retrospective.md) (pending)

*Completed: 2026-03-21*

---

### M32.2: Backoffice Phase 3A — Stabilization + UX Hardening

**Status:** ✅ **COMPLETE** — All 3 sessions finished (all P0 + P1 + P2 items)
**Goal:** Execute narrow M32.2 scope (stabilization + UX hardening) and defer heavier write-ops/UI depth to M32.3

**Current findings (2026-03-18):**
- ✅ UX audit backlog has been converted to copy/paste issue drafts:
  - `docs/planning/ux-audit-discovery-2026-03-18.md` → "Drop-in backlog entries"
- ✅ M32.1 retrospective already recommends:
  - M32.2 focus on E2E stabilization
  - Write-operations UI deferred to M32.3+
- ✅ No existing `m32.2*` / `m32.3*` milestone plan files found under `docs/planning/milestones/`
- ✅ No existing GitHub Issues currently assigned to milestone `M32.2` or `M32.3`

**Session 1 Progress (2026-03-19):**
- ✅ P0-1: Fixed Alerts.razor authorization role mismatch (warehouse-manager → warehouse-clerk)
- ✅ P1-1: Gated dead-end navigation in CustomerSearch.razor ("View Details" button disabled with tooltip)
- ✅ Verified build succeeds with both fixes (0 errors)
- ✅ Stored memories for future sessions
- **Retrospective:** `docs/planning/milestones/m32.2-session-1-retrospective.md`

**Session 2 Progress (2026-03-19):**
- ✅ P0-2: Alert acknowledgment UX (Alerts.razor) — Acknowledge button, optimistic UI, 409 handling
- ✅ P0-3: Session-expired recovery UX — SessionExpiredModal, returnUrl redirect, standardized 401 handling
- ✅ P0-4: Network/conflict/retry state standardization — Applied session-expired pattern to Dashboard + CustomerSearch
- ✅ All 3 P0 items completed with zero rework (199 lines added, 10 removed, 9 files changed)
- ✅ Stored memory: event-based SessionExpiredService pattern for Blazor WASM 401 handling
- **Retrospective:** `docs/planning/milestones/m32.2-session-2-retrospective.md`

**Session 3 Progress (2026-03-19):**
- ✅ P1-2: Data freshness indicators — Backend `QueriedAt` timestamps + relative time display ("2 minutes ago")
- ✅ P2-9: Operator terminology consistency pass — Dashboard, Alerts, CustomerSearch reviewed (zero issues found)
- ✅ P2-10: Empty-state UX guidance — All pages already have appropriate empty states
- ✅ All remaining P1 and P2 items completed with minimal changes (43 lines added, 5 removed, 3 files changed)
- **Retrospective:** `docs/planning/milestones/m32.2-session-3-retro.md`

**Final Backlog Status:**

**M32.2 (stabilization + UX hardening) — ALL COMPLETE:**
- ✅ P0-1: Alerts authorization role mismatch (COMPLETED Session 1)
- ✅ P0-2: Alert acknowledgment UX (COMPLETED Session 2)
- ✅ P0-3: Session-expired recovery UX (COMPLETED Session 2)
- ✅ P0-4: Network/conflict/retry state standardization (COMPLETED Session 2)
- ✅ P1-1: Dead-end route gating/replacement (COMPLETED Session 1)
- ✅ P1-2: Data freshness indicators (COMPLETED Session 3)
- ✅ P2-9: Operator terminology consistency pass (COMPLETED Session 3)
- ✅ P2-10: Empty-state UX guidance (COMPLETED Session 3)

**M32.3 (write-ops/UI depth + cross-BC dependencies) — DEFERRED:**
- P1-3: Product history tab with significance filtering (event-sourcing dependent)
- P1-4: Discontinuation pre-flight impact + grouped notification UX (Listings/Marketplaces dependency)
- Existing deferred Phase 3 items: Promotions management UI, CSV/Excel exports, bulk operations pattern, returns analytics dashboard, audit log viewer

**Decision record:**
- ✅ **Option A selected:** Keep M32.2 narrow (stability + UX hardening), push heavier write-ops/UI depth to M32.3

**Completion Summary:**
- **Duration:** 3 sessions (~6 hours)
- **Deliverables:** 8 UX improvements (4 P0, 2 P1, 2 P2)
- **Build Status:** 0 errors, 0 warnings
- **Key Achievement:** M32.2 Backoffice MVP stabilization functionally complete. Only E2E testing remains before milestone closure.

---

### M32.1 Historical Detail (to be condensed after M32.2 kickoff)

### 🚀 M32.1: Backoffice Phase 2 — Write Operations

**Status:** 🚀 **IN PROGRESS** — Sessions 1-10 completed, E2E test infrastructure built, all 32 tests timeout at ~30 seconds
**Duration Estimate:** 3-4 cycles (12-18 sessions)
**Current Phase:** Diagnosing E2E test failures (Session 11) — Playwright tracing and WASM hydration investigation

**What's Shipping:**
- **Phase 2 Prerequisite (Sessions 1-3):** Domain BC endpoint gaps closed (Product Catalog write, Pricing write, Inventory write, Payments query)
- **Blazor WASM Frontend (Sessions 4-8):** Backoffice.Web with JWT auth, role-based navigation, dashboard UI, CS workflows UI
- **Write Operations UI (Sessions 9-12):** Product admin, pricing admin, warehouse admin, user management
- **E2E Testing (Sessions 13-15):** Playwright tests for critical workflows
- **Documentation (Session 16):** Retrospectives, skills updates, gap register closure

**Phase 2 Approach:**
1. **Sessions 1-3:** ✅ Close 9 endpoint gaps in domain BCs (prerequisite for write operations)
2. **Sessions 4-8:** ✅ Build Blazor WASM frontend shell with JWT auth and read-only views
3. **Sessions 9-10:** ✅ Build E2E test infrastructure + run first test execution (all 32 tests failing)
4. **Session 11:** 🔄 Enable Playwright tracing, diagnose WASM hydration or appsettings.json injection
5. **Sessions 12-15:** Add write operations UI (product, pricing, inventory, users)
6. **Session 16:** Documentation and retrospective

**Key Decisions:**
- Session 1 will write **4 ADRs documenting M32.0 decisions** (0034-0037: BFF Architecture, SignalR Hub, Projections Strategy, OrderNote Ownership)
- Blazor WASM follows Vendor Portal pattern (in-memory JWT, background token refresh, SignalR with JWT Bearer)
- E2E tests use real Kestrel servers (not TestServer) for SignalR testing
- Gap closure first (Sessions 1-3) prevents mid-cycle blockers

**Session 1 Goals:** ✅ COMPLETED
- ✅ Write ADRs 0034-0037 (M32.0 architectural decisions)
- ✅ Close Product Catalog admin write endpoint gaps (update description, update display name, delete product)
- ✅ Add multi-issuer JWT to Product Catalog BC (Backoffice scheme)
- ✅ 10+ integration tests for Product Catalog write endpoints

**Session 2 Goals:** ✅ COMPLETED (with deferred tests)
- ✅ Close Pricing BC write endpoint gaps (set base price, schedule price change, cancel schedule)
- ✅ Add multi-issuer JWT to Pricing BC (Backoffice scheme)
- ✅ Implement floor/ceiling constraint enforcement
- ⚠️ Integration tests (deferred to Session 4 due to timeout)

**Session 3 Goals:** ✅ COMPLETED
- ✅ Close Inventory BC write endpoints (adjust inventory, receive inbound stock)
- ✅ Close Payments BC query endpoint (list payments for order)
- ✅ Update Gap Register (9 Phase 2 blockers → 1 blocker)
- ✅ Session 2 and Session 3 retrospectives completed

**Session 4 Goals:** ✅ COMPLETED
- ✅ Fix Pricing BC integration tests (25 tests, all passing)
- ✅ Add authorization bypass pattern to test fixtures
- ✅ Fix missing Apply method for ProductRegistered event
- ✅ Session 4 retrospective completed

**Session 5 Goals:** ✅ COMPLETED
- ✅ Fix Inventory BC integration tests (48 tests, all passing)
- ✅ Fix Payments BC integration tests (24 tests, all passing)
- ✅ Add AdjustInventoryRequestValidator for HTTP endpoint validation
- ✅ Multi-policy authorization bypass (CustomerService + FinanceClerk)
- ✅ Session 5 retrospective completed

**Session 6 Goals:** ✅ COMPLETED
- ✅ Begin Blazor WASM scaffolding (Backoffice.Web project)
- ✅ Basic project structure following Vendor Portal pattern
- ✅ JWT authentication infrastructure (in-memory token storage)
- ✅ Login page + authentication state provider
- ✅ Stub navigation shell (AppBar, Drawer, role-based menu)
- ✅ TokenRefreshService for background token refresh
- ✅ 17 files created, project builds successfully (0 errors)

**Session 7 Goals:** ✅ COMPLETED
- ✅ Create Customer Search page (CS role — highest-frequency workflow)
- ✅ Create Executive Dashboard page (Executive role — KPI metrics)
- ✅ Create Operations Alert Feed page (OperationsManager role)
- ✅ Wire SignalR hub connection (BackofficeHubService)
- ✅ Create typed HTTP client interfaces (stub-backed for rapid iteration)
- ✅ Test role-based navigation visibility

**Session 8 Goals:** ✅ COMPLETED
- ✅ Replace GetDashboardSummary stub with real AdminDailyMetrics projection query
- ✅ Remove duplicate stub endpoints (GetOperationsAlerts, SearchCustomers already had real implementations)
- ✅ Fix SignalRNotificationTests to match BackofficeEvent discriminated union signatures
- ✅ All 75 Backoffice integration tests passing

**Session 9 Goals:** ✅ COMPLETED (split into 9a and 9b due to context limit)
- ✅ Create E2E test infrastructure (Backoffice.E2ETests project)
- ✅ 3-server WASM E2E fixture (BackofficeIdentity.Api + Backoffice.Api + Backoffice.Web)
- ✅ 3 BDD feature files (Authentication, CustomerService, OperationsAlerts) with 32 scenarios
- ✅ Page Object Models (LoginPage, DashboardPage, CustomerSearchPage, OperationsAlertsPage)
- ✅ Playwright v1.51.0 configuration with browser downloads
- ✅ Fix compilation errors (appsettings.json injection, WasmStaticFileHost, test hooks)
- ✅ Project builds successfully (0 errors, 6 nullable warnings)

**Session 10 Goals:** ✅ COMPLETED
- ✅ Start infrastructure (Postgres, RabbitMQ, Jaeger) via Docker Compose
- ✅ Run E2E tests for first time (`dotnet test Backoffice.E2ETests`)
- ✅ Document test failures: All 32 scenarios timeout at ~30 seconds
- ✅ Root cause analysis: Likely Blazor WASM hydration failure or appsettings.json injection failure
- ✅ Write comprehensive Session 10 retrospective with diagnostic strategy
- ✅ Update CURRENT-CYCLE.md

**Session 11 Goals:** ✅ COMPLETED
- ✅ Enable Playwright tracing to capture browser console logs, network traffic, screenshots
- ✅ Run first E2E test to generate trace files (all traces captured successfully)
- ✅ Add trace-on-failure logic (saves `.zip` files to `playwright-traces/`)
- ✅ Diagnose WASM hydration issue: discovered critical wwwroot path bug
- ✅ Fix: `FindWasmRoot()` was returning `bin/.../wwwroot` (has `_framework` but missing `index.html`)
- ⚠️ Partial success: wwwroot path fixed, but 404 errors still present (requires publish output)
- ✅ Write comprehensive Session 11 retrospective with root cause analysis
- ✅ Update CURRENT-CYCLE.md

**Session 12 Goals:** ✅ COMPLETED
- ✅ View Playwright traces from Session 11 (via logging, not viewer due to time)
- ✅ Fix middleware ordering: `UseStaticFiles` BEFORE `MapGet` route handlers
- ✅ Diagnose root cause of 404s: `index.html` missing from `bin/.../wwwroot` (only in publish output)
- ✅ Fix `FindWasmRoot()` to prefer publish output directory (`bin/.../publish/wwwroot`)
- ✅ Run `dotnet publish` to create complete wwwroot with `index.html` + `_framework`
- ✅ All 404 errors fixed — WASM files now serve correctly (200 OK)
- ⚠️ Discovered new issue: Authorization policies not registered (`CustomerService`, `Executive`, etc.)
- ✅ Write Session 12 retrospective documenting fixes and new issue
- ✅ Update CURRENT-CYCLE.md

**Session 13 Goals:** ✅ COMPLETED (with caveats)
- ✅ Register authorization policies in `Backoffice.Web/Program.cs` (7 policies added)
- ✅ Add `data-testid` attributes to `Login.razor` (5 test-ids added)
- ✅ Fix JWT role claims to use kebab-case (created `ToRoleString()` extension)
- ✅ Update post-login navigation to `/dashboard`
- ⚠️ Dashboard navigation still failing — test times out at URL check
- ✅ Write Session 13 retrospective documenting major fixes + ongoing issue

**Session 14 Goals:** ✅ COMPLETED (with test failures)
- ✅ Fix `LoginHandler` Line 133 to use `ToRoleString()` for consistency
- ✅ Align Dashboard.razor test-ids with DashboardPage.cs expectations (17 changes)
- ✅ Add `realtime-connected` and `realtime-disconnected` test-id indicators
- ✅ Add nested `kpi-value` test-ids to all KPI cards
- ❌ Run full authentication feature suite (tests failed — needs debugging in Session 15)
- ✅ Write comprehensive Session 14 retrospective
- ✅ Update CURRENT-CYCLE.md

**Session 15 Goals:** ✅ COMPLETED (with deferred E2E fixes)
- ✅ Investigate E2E test failures (identified 4 root causes: WASM hydration, navigation, KPI rendering, SignalR connection timeouts)
- ✅ Resolve Active Customers KPI mismatch (removed from DashboardPage.cs - not in M32.1 scope)
- ⚠️ Run full authentication test suite (deferred to Session 16 - requires timeout fixes)
- ✅ Document test-id conventions in e2e-playwright-testing.md (comprehensive naming guide added)
- ✅ Write Session 15 retrospective
- ✅ Update CURRENT-CYCLE.md

**Session 16 Goals:** (Next — Milestone Completion)
- **PRIMARY GOAL:** Get at least 1 authentication E2E scenario passing (smoke test validation)
- Run single authentication scenario with Playwright tracing enabled
- Fix dashboard navigation timing (add explicit auth state + MudBlazor hydration checks)
- Reduce LoginPage timeout from 30s to 15s
- Write Session 16 retrospective
- Write M32.1 milestone retrospective
- Update CURRENT-CYCLE.md (move M32.1 to Recent Completions)
- Update E2E test documentation with timeout tuning guidance
- **Note:** Full 32-test suite stabilization can be deferred to M32.2 if needed

**References:**
- [M32.1 Plan](./milestones/m32-1-backoffice-phase-2-plan.md)
- [M32.1 Triage and Completion Plan](./milestones/m32.1-triage-and-completion-plan.md) ⭐ **NEW**
- [M32.0 Retrospective](./milestones/m32-0-retrospective.md)
- [Session 1 Retrospective](./milestones/m32-1-session-1-retrospective.md)
- [Session 2 Retrospective](./milestones/m32-1-session-2-retrospective.md)
- [Session 3 Retrospective](./milestones/m32-1-session-3-retrospective.md)
- [Session 4 Retrospective](./milestones/m32-1-session-4-retrospective.md)
- [Session 5 Retrospective](./milestones/m32-1-session-5-retrospective.md)
- [Session 6 Retrospective](./milestones/m32-1-session-6-retrospective.md)
- [Session 7 Retrospective](./milestones/m32-1-session-7-retrospective.md)
- [Session 8 Retrospective](./milestones/m32-1-session-8-retrospective.md)
- [Session 9 Retrospective](./milestones/m32-1-session-9-retrospective.md)
- [Session 10 Retrospective](./milestones/m32-1-session-10-retrospective.md)
- [Session 11 Retrospective](./milestones/m32-1-session-11-retrospective.md)
- [Session 12 Retrospective](./milestones/m32-1-session-12-retrospective.md)
- [Session 13 Retrospective](./milestones/m32-1-session-13-retrospective.md)
- [Session 14 Retrospective](./milestones/m32-1-session-14-retrospective.md)
- [Session 15 Retrospective](./milestones/m32.1-session-15-retrospective.md)
- [UX Audit Discovery (includes M32.2/M32.3 issue drafts)](./ux-audit-discovery-2026-03-18.md) ⭐ **NEW**
- [Backoffice Event Modeling](./backoffice-event-modeling-revised.md)
- [Backoffice Frontend Design](./backoffice-frontend-design.md)
- [Frontend Design Alignment Analysis](./backoffice-frontend-design-alignment-analysis.md)
- [Integration Gap Register](./backoffice-integration-gap-register.md)
- [ADR 0034: Backoffice BFF Architecture](../decisions/0034-backoffice-bff-architecture.md)
- [ADR 0035: Backoffice SignalR Hub Design](../decisions/0035-backoffice-signalr-hub-design.md)
- [ADR 0036: BFF-Owned Projections Strategy](../decisions/0036-bff-projections-strategy.md)
- [ADR 0037: OrderNote Aggregate Ownership](../decisions/0037-ordernote-aggregate-ownership.md)

**Deferred to Phase 3:**
- Promotions management UI
- CSV/Excel exports
- Bulk operations pattern
- Returns analytics dashboard
- Audit log viewer

---

## Recent Completions

> **Contains:** Last 3 completed milestones for quick reference.
> **Archive Policy:** After 3 milestones accumulate, move oldest to [Milestone Archive](#milestone-archive).

### ✅ M32.0: Backoffice Phase 1 — Read-Only Dashboards (2026-03-16)

**What shipped:**
- Backoffice BFF (Backend-for-Frontend) for internal operations portal
- CS agent workflows: customer search, order lookup, return management, correspondence history, order notes
- Executive dashboard with 5 real-time KPIs (order count, revenue, AOV, payment failure rate)
- Operations alert feed with SignalR push notifications
- Warehouse clerk tools: stock visibility, low-stock alerts, alert acknowledgment
- BFF-owned Marten projections (AdminDailyMetrics, AlertFeedView)
- OrderNote aggregate (BFF-owned internal CS comments)
- 75 integration tests (Alba + TestContainers) — all passing
- 14+ RabbitMQ event subscriptions from 7 domain BCs

**Key Technical Wins:**
- BFF pattern consistency (3rd successful implementation: Storefront, Vendor Portal, Backoffice)
- Multi-issuer JWT integration (domain BCs accept tokens from 2+ identity providers)
- BFF-owned projections for real-time dashboards (alternative to Analytics BC)
- Integration testing pattern for multi-BC BFFs (stub client fixture design)
- OrderNote aggregate ownership decision (ADR 0037 — operational metadata belongs in BFF)

**Key Decisions:**
- Pre-wired SignalR configuration accelerated Session 8 (3h → 2h)
- Inline projections require explicit `SaveChangesAsync()` before querying
- Role-based SignalR groups scale better than user-specific groups for internal portals
- Stub clients must mirror real BC API design (separate list vs detail storage)

**Build Status:** 0 errors, 7 pre-existing warnings (OrderNoteTests nullable false positives)

**Duration:** 11 sessions (~28 hours) — within estimate (26-32 hours)

**References:**
- [Milestone Plan](./milestones/m32-0-backoffice-phase-1-plan.md)
- [Milestone Retrospective](./milestones/m32-0-retrospective.md)
- [Session 11 Retrospective](./milestones/m32-0-session-11-retrospective.md)
- [ADR 0031: Backoffice RBAC Model](../decisions/0031-admin-portal-rbac-model.md)
- [ADR 0032: Multi-Issuer JWT Strategy](../decisions/0032-multi-issuer-jwt-strategy.md)
- [ADR 0033: Backoffice Rename](../decisions/0033-admin-portal-to-backoffice-rename.md)

**ADRs to Write (Phase 2):**
- ADR 0034: Backoffice BFF Architecture
- ADR 0035: Backoffice SignalR Hub Design
- ADR 0036: BFF-Owned Projections Strategy
- ADR 0037: OrderNote Aggregate Ownership

**Deferred to M32.1 (Phase 2):**
- 9 endpoint gaps (Product Catalog write, Pricing write, Inventory write, Payments order query)
- Blazor WASM frontend (Backoffice.Web)
- Write operations (product admin, pricing adjustments, inventory adjustments)
- E2E tests (Playwright)

*Completed: 2026-03-16*

---

### ✅ M31.5: Backoffice Prerequisites (2026-03-16)

**What shipped:**
- 8 Phase 0.5 blocking gaps closed across 5 sessions
- GetCustomerByEmail endpoint (Customer Identity BC)
- Inventory BC HTTP query endpoints (GetStockLevel, GetLowStock)
- Fulfillment BC GetShipmentsForOrder endpoint
- Multi-issuer JWT configuration (5 domain BCs: Orders, Payments, Inventory, Fulfillment, Correspondence)
- Endpoint authorization with `[Authorize]` attributes (17 endpoints across 7 BCs)
- 38 fully defined endpoints ready for Backoffice Phase 1

**Key Decisions:**
- Multi-issuer JWT uses named schemes (`"Backoffice"`, `"Vendor"`)
- Policy-based authorization aligned with ADR 0031 roles
- Product Catalog policy already named "VendorAdmin" (no rename needed)
- GetAddressSnapshot deliberately left unprotected (BC-to-BC integration)

**Build Status:** 0 errors, 7 pre-existing warnings (Correspondence BC unused variables)

**References:**
- [Milestone Plan](./milestones/m31-5-backoffice-prerequisites.md)
- [Session 5 Retrospective](./milestones/m31-5-session-5-retrospective.md)
- [Integration Gap Register](./backoffice-integration-gap-register.md) (updated)
- [ADR 0032: Multi-Issuer JWT Strategy](../decisions/0032-multi-issuer-jwt-strategy.md)

*Completed: 2026-03-16*

---

### ✅ M31.0: Correspondence BC Extended (2026-03-15)

**What shipped:**
- 5 new integration handlers: ShipmentDeliveredHandler, ShipmentDeliveryFailedHandler, ReturnDeniedHandler, ReturnExpiredHandler, RefundCompletedHandler
- SMS channel infrastructure: ISmsProvider interface, StubSmsProvider with fake Twilio SID generation
- RabbitMQ Payments BC queue added (correspondence-payments-events)
- All 4 BC integration queues configured: Orders, Fulfillment, Returns, Payments
- 8 total handlers (4 from M28.0 + 4 new from M31.0)

**Key Decisions:**
- Pure choreography pattern scales well (no sagas needed)
- Defer template system and Customer Identity queries to Phase 3+
- Inline HTML templates in handlers for now

**Build Status:** 0 errors, 7 expected warnings (TODO placeholders)

**References:**
- [Retrospective](./cycles/m31-0-retrospective.md)
- CONTEXTS.md updated with M31.0 integration matrix

*Completed: 2026-03-15*

---

### ✅ M30.1: Shopping BC Coupon Integration (2026-03-15)

**What shipped:**
- ApplyCouponToCart + RemoveCouponFromCart command handlers
- Real PromotionsClient integration (ValidateCoupon + CalculateDiscount HTTP calls)
- GetCart enrichment with discount information
- Dual handler pattern (command handler + HTTP endpoint handler classes)
- 11 integration tests covering valid/invalid coupons, empty/terminal carts, discount calculations

**Key Patterns:**
- Wolverine Railway Programming with async external service calls requires separate handler classes
- Alba test fixture DI replacement: RemoveAll + AddSingleton pattern for stub injection
- Single coupon per cart (stacking deferred to M30.3+)

**Skills Refresh:**
- Propagated M30.1 learnings to `wolverine-message-handlers.md` (Railway Programming with async validation)

**References:**
- [Retrospective](./cycles/m30-1-shopping-bc-coupon-retrospective.md)
- CONTEXTS.md updated with Shopping ↔ Promotions bidirectional integration

*Completed: 2026-03-15*

---

### ✅ M30.0: Promotions BC Redemption (2026-03-15)

**What shipped:**
- RedeemCoupon, RevokeCoupon, RecordPromotionRedemption command handlers
- GenerateCouponBatch fan-out pattern (PREFIX-XXXX format)
- CalculateDiscount query with stub CartView
- RecordPromotionRedemptionHandler choreography integration with Orders BC
- ExpireCoupon scheduled message (promotion end date expiry)
- 29 integration tests across lifecycle, validation, redemption, discount calculation

**Key Patterns:**
- Handlers manually loading aggregates must use `session.Events.Append()` (not tuple returns)
- Draft promotions can issue coupons (enables batch generation before activation)
- **Banker's Rounding:** `Math.Round(6.825, 2)` → 6.82 (even), not 6.83 — affects discount calculations

**Skills Refresh:**
- Updated `wolverine-message-handlers.md` (anti-pattern #8)
- Updated `modern-csharp-coding-standards.md` (banker's rounding)
- Updated `critterstack-testing-patterns.md` (fan-out timing)

**Deferred:**
- Full Shopping BC integration (completed in M30.1)
- Pricing BC floor price enforcement (future)

**References:**
- [Retrospective](./milestones/m30-0-retrospective.md)
- CONTEXTS.md updated with M30.0 implementation status

*Completed: 2026-03-15*

---

## Milestone Archive

> **Contains:** Completed milestones older than the last 3 (M29.1 and earlier).
> **Purpose:** Historical reference without cluttering recent work context.

<details>
<summary><strong>M29.1: Promotions BC Core — MVP (2026-03-14 to 2026-03-15)</strong></summary>

**What shipped:**
- Event-sourced Promotion aggregate (UUID v7) with 6 domain events
- Event-sourced Coupon aggregate (UUID v5 from code) with 4 domain events
- Command handlers: CreatePromotion, ActivatePromotion, IssueCoupon
- CouponLookupView projection (case-insensitive coupon validation)
- ValidateCoupon query endpoint with business rules
- Marten snapshot projections (Promotion + Coupon)
- 11 integration tests (all passing)
- Port 5250 allocated

**Pattern Discoveries:**
- IStartStream return type for event stream creation
- Snapshot projection requirement for queryability

**Deferred to M30.0:** Redemption tracking, batch generation, Shopping/Pricing integration

[Retrospective](./cycles/cycle-29-phase-2-retrospective-notes.md)

</details>

<details>
<summary><strong>M29.0: Backoffice Identity BC (2026-03-14)</strong></summary>

**What shipped:**
- ADR 0031: RBAC model (7 roles, policy-based authorization)
- EF Core entity model: AdminUser, AdminRole, AdminUserStatus, BackofficeIdentityDbContext
- Authentication handlers: Login, RefreshToken, Logout (JWT + refresh token rotation)
- User management handlers: CreateAdminUser, GetAdminUsers, ChangeAdminUserRole, DeactivateAdminUser
- JWT token generation with 7 authorization policies
- API endpoints: 3 auth + 4 user management (Wolverine HTTP)
- Infrastructure: Docker Compose, Aspire, database, port 5249

[Retrospective](./cycles/cycle-29-admin-identity-phase-1-retrospective.md)

</details>

<details>
<summary><strong>M28.0: Correspondence BC Core (2026-03-13 to 2026-03-14)</strong></summary>

**What shipped:**
- Message aggregate (event-sourced) — 4 domain events, retry lifecycle
- Provider interfaces (IEmailProvider, StubEmailProvider)
- OrderPlacedHandler — email order confirmations
- SendMessage handler — exponential backoff retry (5min, 30min, 2hr)
- MessageListView projection (inline)
- HTTP query endpoints (GetMessagesForCustomer, GetMessageDetails)
- 12 unit tests + 5 integration tests

[Retrospective](./cycles/cycle-28-correspondence-bc-phase-1-retrospective.md)

</details>

<details>
<summary><strong>M25.2: Returns BC Exchanges (2026-03-13)</strong></summary>

**What shipped:**
- Exchange workflow (UC-11) — ReturnType enum, ExchangeRequest, 5 exchange domain events, 3 command handlers
- 6 integration messages for exchange workflow
- CE SignalR handlers — 7 handlers, ReturnStatusChanged discriminated union event
- Sequential returns — IsReturnInProgress → ActiveReturnIds saga refactor
- Anticorruption layer — EnumTranslations static class
- Cross-BC smoke tests (3-host Alba fixture)

[Plan](./cycles/cycle-27-returns-bc-phase-3.md) | [Retrospective](./cycles/cycle-27-returns-bc-phase-3-retrospective.md)

</details>

<details>
<summary><strong>M25.1: Returns BC Mixed Inspection (2026-03-12 to 2026-03-13)</strong></summary>

**What shipped:**
- ReturnCompleted expanded with per-item disposition
- 5 new integration events (ReturnApproved, ReturnRejected, ReturnExpired, ReturnReceived, ReturnedItem)
- Mixed inspection three-way logic
- GetReturnsForOrder query (Marten inline snapshots)
- RabbitMQ dual-queue routing + Fulfillment queue wiring fix
- ~99 total return-related tests

[Plan](./cycles/cycle-26-returns-bc-phase-2.md) | [Retrospective](./cycles/cycle-26-returns-bc-phase-2-retrospective.md)

</details>

<details>
<summary><strong>M25.0: Returns BC Core (2026-03-12)</strong></summary>

**What shipped:**
- Event-sourced Return aggregate (10 lifecycle states, 9 domain events)
- 6 command handlers + 7 API endpoints (port 5245)
- ReturnEligibilityWindow from Fulfillment.ShipmentDelivered
- Auto-approval logic + restocking fee calculation
- 48 unit tests + 5 integration tests

[Plan & Retrospective](./cycles/cycle-25-returns-bc-phase-1.md)

</details>

<details>
<summary><strong>Cycle 24: Fulfillment Integrity + Returns Prerequisites (2026-03-12)</strong></summary>

**What shipped:**
- RabbitMQ transport wired in Fulfillment.Api
- RecordDeliveryFailure endpoint + ShipmentDeliveryFailed cascade
- UUID v5 idempotent shipment creation
- SharedShippingAddress with dual JSON annotations
- Orders saga return handlers + IsReturnInProgress guard
- GET /api/orders/{orderId}/returnable-items endpoint

[Plan](./cycles/cycle-24-fulfillment-integrity-returns-prerequisites.md)

</details>

<details>
<summary><strong>Cycle 23: Vendor Portal E2E Testing (2026-03-11)</strong></summary>

**What shipped:**
- 3-server E2E fixture (VendorIdentity.Api + VendorPortal.Api + WASM static host)
- 12 BDD scenarios (P0 + P1a) across 3 feature files
- Page Object Models for Login, Dashboard, Change Requests, Submit, Settings
- SignalR hub message injection testing

[Plan](./cycles/cycle-23-vendor-portal-e2e-testing.md) | [Skills Update](../skills/e2e-playwright-testing.md)

</details>

<details>
<summary><strong>Cycle 22: Vendor Portal + Vendor Identity Phase 1 (2026-03-08 to 2026-03-10)</strong></summary>

**What shipped (6 phases):**
- Phase 1: JWT Auth (VendorIdentity.Api, EF Core, token lifecycle)
- Phase 2: Vendor Portal API (analytics, alerts, dashboard, multi-tenant)
- Phase 3: Blazor WASM Frontend (SignalR hub, in-memory JWT, live updates)
- Phase 4: Change Request Workflow (7-state machine, Catalog BC integration)
- Phase 5: Saved Views + VendorAccount (notification preferences, saved dashboard views)
- Phase 6: Full Identity Lifecycle + Admin Tools (8 admin endpoints, compensation handler)
- 143 integration tests (100% pass rate)

[Event Modeling](vendor-portal-event-modeling.md) | [Retrospective](./cycles/cycle-22-retrospective.md) | [Milestone](https://github.com/erikshafer/CritterSupply/milestone/16)

</details>

<details>
<summary><strong>Cycle 21: Pricing BC Phase 1 (2026-03-07 to 2026-03-08)</strong></summary>

**What shipped:**
- ProductPrice event-sourced aggregate (UUID v5 deterministic stream ID)
- Money value object (140 unit tests)
- CurrentPriceView inline projection (zero-lag queries)
- Shopping BC security fix (server-authoritative pricing)
- 5 ADRs written
- 151 Pricing tests + 56 Shopping tests

[Plan](pricing-event-modeling.md) | [Retrospective](./cycles/cycle-21-retrospective.md) | [Milestone](https://github.com/erikshafer/CritterSupply/milestone/15) (closed)

</details>

<details>
<summary><strong>Cycle 20: Automated Browser Testing (2026-03-04 to 2026-03-07)</strong></summary>

**What shipped:**
- Playwright + Reqnroll E2E testing infrastructure
- Real Kestrel servers (not TestServer) for SignalR testing
- Page Object Model with data-testid selectors
- MudBlazor component interaction patterns
- Playwright tracing for CI failure diagnosis

[Plan](./cycles/cycle-20-automated-browser-testing.md) | [Retrospective](./cycles/cycle-20-retrospective.md) | [Milestone](https://github.com/erikshafer/CritterSupply/milestone/2)

</details>

<details>
<summary><strong>Cycle 19.5: Complete Checkout Workflow (2026-03-04)</strong></summary>

**What shipped:**
- Wired checkout stepper to backend APIs
- Checkout initialization + CheckoutId persistence
- Error handling with MudSnackbar toasts
- End-to-end manual testing

[Milestone](https://github.com/erikshafer/CritterSupply/milestone/13)

</details>

<details>
<summary><strong>Cycle 19: Authentication & Authorization (2026-02-25 to 2026-02-26)</strong></summary>

**What shipped:**
- Cookie-based authentication (ASP.NET Core middleware)
- Login/Logout pages with MudBlazor
- Protected routes (Cart, Checkout)
- AppBar authentication UI
- Cart persistence via browser localStorage
- Swagger UI + seed data for ProductCatalog.Api

[Plan](./cycles/cycle-19-authentication-authorization.md) | [Retrospective](./cycles/cycle-19-retrospective.md)

</details>

*Archive Last Updated: 2026-03-15*

---

## Roadmap

> **Contains:** Next 3-4 milestones + future BCs (high-level only).
> **Purpose:** Forward-looking planning without excessive detail.

### Next 3-4 Milestones

> ⚠️ **Updated 2026-03-29:** M36.0 complete. M36.1 planning complete — Listings BC foundation.

- **M36.0 (complete):** Engineering Quality
  - ✅ 21 pre-existing test failures eliminated (TestAuthHandler utility)
  - ✅ 55 endpoints protected with `[Authorize]` across 10 BCs
  - ✅ 34 `SaveChangesAsync()` calls removed, `bus.PublishAsync()` violations fixed
  - ✅ `AutoApplyTransactions` root cause fix for Product Catalog
  - See [M36.0 Milestone Closure Retrospective](milestones/m36-0-milestone-closure-retrospective.md)

- **M36.1 (active — planning):** Listings BC Foundation
  - Phase 0 cleanup: granular Product Catalog integration messages
  - Listings BC: event-sourced listing aggregate, OWN_WEBSITE fast-path, recall cascade
  - ProductSummaryView anti-corruption layer
  - Backoffice admin pages (extend existing Backoffice.Web)
  - Port 5246, database `listings`, ADRs 0041–0046
  - See [M36.1 Plan](milestones/m36-1-plan.md)

- **M37.x (planned):** Marketplaces BC Foundation (Phase 2)
  - Marketplace document entity, category mapping, adapter pattern
  - Bidirectional async messaging with Listings BC
  - Vault credential storage interface
  - See [Catalog-Listings-Marketplaces Cycle Plan](catalog-listings-marketplaces-cycle-plan.md)

- **M38.x (planned):** Variants + Compliance + Real API Calls (Phase 3)
  - ProductFamily aggregate, variant-aware listings
  - Full compliance metadata, real marketplace API calls

### Future BCs (Priority Roadmap — Post M36.1)

> Product Catalog ES migration complete. Listings BC foundation in progress (M36.1).

**High Priority (Active in M36.1+):**
- 🟢 **Listings BC** — M36.1 active; event-sourced listing aggregate, recall cascade, OWN_WEBSITE
- 🟡 **Marketplaces BC** — M37.x planned; marketplace adapters, category mapping
- 🟡 **Product Variants** — M38.x planned; ProductFamily aggregate, variant-aware listings

**Medium Priority:**
- 🟡 **Search BC** — Full-text product search, faceted navigation
- 🟡 **Recommendations BC** — Personalized product recommendations

**Lower Priority (Strategic/Retention):**
- 🔵 **Analytics BC** — Business intelligence, reporting, dashboards
- 🔵 **Store Credit BC** — Gift cards, store credit issuance
- 🔵 **Loyalty BC** — Rewards program, points accumulation
- 🔵 **Operations Dashboard** — Developer/SRE event stream visualization (React + SignalR)

See [CONTEXTS.md — Future Considerations](../../CONTEXTS.md) for full specifications.

*Roadmap Last Updated: 2026-03-27 (M35.0 complete; M36.0 is engineering quality)*

---

## Quick Links

- [CONTEXTS.md](../../CONTEXTS.md) — Architectural source of truth *(always read first)*
- [GitHub Issues](https://github.com/erikshafer/CritterSupply/issues) — Issue tracking
- [GitHub Project Board](https://github.com/users/erikshafer/projects/9) — Kanban board
- [Historical Cycles](./cycles/) — Markdown retrospectives
- [Milestone Mapping](./milestone-mapping.md) — Legacy "Cycle N" → "M.N" translation
- [Migration Plan](./GITHUB-MIGRATION-PLAN.md) — How we got here
- [ADR 0011](../decisions/0011-github-projects-issues-migration.md) — Why we made this change
- [ADR 0032](../decisions/0032-milestone-based-planning-schema.md) — Milestone-based planning schema

---

*Document Last Updated: 2026-03-29*
*Active Milestone: M36.0 — Engineering Quality (complete — ready for closure)*
*Update Policy: At milestone start, milestone end, and significant task changes*
