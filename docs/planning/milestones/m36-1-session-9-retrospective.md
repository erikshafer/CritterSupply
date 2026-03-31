# M36.1 Session 9 Retrospective — ADRs + E2E Coverage + Phase 2 Gate Closure

**Session Date:** 2026-03-31
**Status:** ✅ Complete
**CI Run:** PR build — 0 errors, 33 warnings (all pre-existing)
**Integration Tests at Session Start:** 62 (35 Listings + 27 Marketplaces — 0 failures)
**Integration Tests at Session Close:** 62 (35 Listings + 27 Marketplaces — 0 failures)

---

## What Was Delivered

### Stream A: ADRs 0048 and 0049

#### ADR 0048 — Marketplace Document Entity Design

Previously committed during Session 8 preliminary work. Verified complete — 5 architectural decisions documented:

1. Marten document store (not event sourcing) for configuration-level data
2. `Id` as natural key (channel code string)
3. Sealed class with mutable operational fields
4. OWN_WEBSITE excluded from Marketplaces BC
5. Idempotent seed data with dual-invocation support

**File:** `docs/decisions/0048-marketplace-document-entity-design.md`

#### ADR 0049 — Category Mapping Ownership

Authored this session. 4 architectural decisions + alternatives considered:

1. Category mappings owned by Marketplaces BC (not Product Catalog or Listings)
2. Composite key format: `{ChannelCode}:{InternalCategory}` — enables single `LoadAsync` calls
3. `InternalCategory` aligned with Product Catalog `Category` field — known coupling risk documented
4. 18 seed mappings (6 categories × 3 channels) with idempotency guard

Alternatives rejected: mappings in Product Catalog (leaks channel knowledge), mappings in Listings (wrong ownership), shared CategoryTaxonomy BC (premature abstraction).

**File:** `docs/decisions/0049-category-mapping-ownership.md`

### Stream B: E2E Coverage — Marketplace Admin Pages

#### data-testid Audit

**MarketplacesList.razor** — Added 12 new `data-testid` attributes:
- Page container: `marketplaces-page`
- Breadcrumbs: `marketplaces-breadcrumbs`
- Title: `marketplaces-title`
- Loading: `marketplaces-loading`
- Error alert: `marketplaces-error`
- Per-row: `marketplace-display-name-{id}`, `marketplace-status-{id}`, `marketplace-active-chip-{id}`, `marketplace-inactive-chip-{id}`, `marketplace-created-{id}`
- No records: `marketplaces-no-records`
- Pre-existing: `marketplaces-table`, `marketplace-row-{id}` (unchanged)

**CategoryMappingsList.razor** — Added 8 new `data-testid` attributes:
- Page container: `category-mappings-page`
- Breadcrumbs: `category-mappings-breadcrumbs`
- Title: `category-mappings-title`
- Loading: `category-mappings-loading`
- Error alert: `category-mappings-error`
- Per-row: `mapping-category-{id}`, `mapping-marketplace-id-{id}`
- No records: `category-mappings-no-records`
- Pre-existing: `category-mappings-table`, `mapping-row-{id}`, `channel-filter` (unchanged)

#### StubMarketplacesApiHost

Created `StubMarketplacesApiHost` in `E2ETestFixture.cs`, following the established `StubListingsApiHost` pattern:

- Serves `GET /api/marketplaces` — returns all seeded marketplaces, ordered by Id
- Serves `GET /api/category-mappings?channelCode=` — returns mappings with optional channel filter
- `SeedMarketplace()` and `SeedCategoryMapping()` for per-scenario data setup
- `Clear()` for between-scenario cleanup
- CORS fully open for test origins

#### E2E Infrastructure Updates

- `WasmStaticFileHost` constructor updated to accept `marketplacesApiUrl` parameter
- Dynamic `appsettings.json` now includes `MarketplacesApiUrl` (previously missing — marketplace pages used default `localhost:5247`)
- `E2ETestFixture.InitializeAsync()` starts StubMarketplacesApiHost (step 5, before WASM host)
- `E2ETestFixture.DisposeAsync()` disposes StubMarketplacesApiHost
- `E2ETestFixture.ClearAllStubs()` clears marketplace stub data
- `wwwroot/appsettings.json` updated to include `ListingsApiUrl` (was missing for consistency)

#### Page Objects

- **`MarketplacesListPage.cs`** — Navigate, wait for load, row count, row visibility, display name, status chip text, table visibility
- **`CategoryMappingsListPage.cs`** — Navigate, wait for load, row count, channel filter interaction (MudSelect pattern), breadcrumb visibility and text, table visibility

Both follow the established POM pattern: constructor accepts `(IPage, string baseUrl)`, uses `GetByTestId()` locators, includes timeout constants for WASM bootstrap and API calls.

#### Feature File and Step Definitions

**`MarketplacesAdmin.feature`** — 6 scenarios across both pages:

| # | Scenario | Page |
|---|----------|------|
| 1 | Admin sees 3 seeded channels with correct display names | Marketplaces List |
| 2 | Each row shows correct Active status chip | Marketplaces List |
| 3 | Unauthenticated request redirects to login | Marketplaces List |
| 4 | Admin sees all 18 seeded category mappings | Category Mappings |
| 5 | Filter by AMAZON_US returns exactly 6 rows | Category Mappings |
| 6 | Breadcrumb trail shows Home → Marketplaces → Category Mappings | Category Mappings |

**`MarketplacesAdminSteps.cs`** — 13 step definitions:
- 2 Given steps (seed marketplace data, navigate to category mappings)
- 3 When steps (navigate to marketplaces, navigate to category mappings, filter by channel)
- 8 Then steps (table visible, row count, display name, status chip, login redirect, mapping count, breadcrumb)

### Stream C: Phase 2 Gate Closure

**Gate check against `m36-1-phase-2-plan.md` § 10:**

| # | Criterion | Status | Delivered In |
|---|-----------|--------|-------------|
| 1 | Marketplace documents seeded | ✅ | Session 6 |
| 2 | Marketplace CRUD works | ✅ | Session 6 |
| 3 | Deactivated marketplace rejects submissions | ✅ | Session 7 |
| 4 | CategoryMapping CRUD works | ✅ | Session 7 |
| 5 | Missing category mapping rejects submission | ✅ | Session 7 |
| 6 | IMarketplaceAdapter with 3 stub implementations | ✅ | Session 7 |
| 7 | ListingApproved consumed and routed | ✅ | Session 7 |
| 8 | IVaultClient / DevVaultClient implemented | ✅ | Session 7 |
| 9 | [Authorize] on all endpoints | ✅ | Session 6 |
| 10 | AutoApplyTransactions configured | ✅ | Session 6 |
| 11 | Solution + Docker + DB updated | ✅ | Session 6 |
| 12 | Marketplace admin list page | ✅ | Session 8 |
| 13 | Integration tests ≥22 | ✅ (27) | Sessions 6–8 |
| 14 | E2E page objects + feature files | ✅ | **Session 9** |
| 15 | ADRs 0048 and 0049 written | ✅ | **Session 9** |
| 16 | CI green | ✅ | Sessions 6–9 |

**Gate Result: PASS — 16/16 criteria met.**

---

## Test Summary

| Suite | Start | End | Change |
|-------|-------|-----|--------|
| Marketplaces (integration) | 27 (0 failing) | 27 (0 failing) | No change |
| Listings (integration) | 35 (0 failing) | 35 (0 failing) | No change |
| E2E (feature scenarios) | 0 marketplace | 6 marketplace | +6 scenarios (new feature) |
| **Total Integration** | **62** | **62** | **0 failures** |

---

## Files Changed

| File | Change | Stream |
|------|--------|--------|
| `docs/decisions/0049-category-mapping-ownership.md` | **New** — ADR 0049 | A |
| `src/Backoffice/Backoffice.Web/Pages/Marketplaces/MarketplacesList.razor` | Added 12 data-testid attributes | B |
| `src/Backoffice/Backoffice.Web/Pages/Marketplaces/CategoryMappingsList.razor` | Added 8 data-testid attributes | B |
| `src/Backoffice/Backoffice.Web/wwwroot/appsettings.json` | Added ListingsApiUrl | B |
| `tests/Backoffice/Backoffice.E2ETests/E2ETestFixture.cs` | StubMarketplacesApiHost + WasmStaticFileHost MarketplacesApiUrl | B |
| `tests/Backoffice/Backoffice.E2ETests/Pages/MarketplacesListPage.cs` | **New** — page object | B |
| `tests/Backoffice/Backoffice.E2ETests/Pages/CategoryMappingsListPage.cs` | **New** — page object | B |
| `tests/Backoffice/Backoffice.E2ETests/Features/MarketplacesAdmin.feature` | **New** — 6 scenarios | B |
| `tests/Backoffice/Backoffice.E2ETests/StepDefinitions/MarketplacesAdminSteps.cs` | **New** — 13 step definitions | B |
| `docs/planning/CURRENT-CYCLE.md` | Session 9 progress + Phase 2 PASS | C |

---

## What Went Well

1. **ADRs finally committed** — After two deferrals (Session 7 → 8 → 9), both ADRs are documented. The three-session delay was acceptable for implementation velocity but should not recur for future architectural decisions.
2. **StubMarketplacesApiHost follows established pattern** — Modelled after `StubListingsApiHost`, making the code consistent and the E2E infrastructure predictable for future BCs.
3. **data-testid coverage is now comprehensive** — Both marketplace pages have full attribute coverage for every meaningful element, not just tables and rows.
4. **Phase 2 gate passes cleanly** — All 16 criteria met across 4 sessions. The Marketplaces BC is architecturally sound and ready for Phase 3 (M37.x production adapters).

## What Could Be Better

1. **Missing MarketplacesApiUrl in WasmStaticFileHost** was discovered during this session — the E2E host had been serving a dynamic `appsettings.json` without MarketplacesApiUrl since Session 5. This means marketplace pages in E2E tests would have used the default `localhost:5247` (which doesn't exist). The fix was straightforward, but this gap should have been caught in Session 8 when the pages were created.
2. **ListingsApiUrl was missing from the real `appsettings.json`** — Added for consistency. The WASM app used a fallback default, so this wasn't a runtime issue, but it violated the principle that configuration should be explicit.

## Risks and Follow-ups

1. **Category taxonomy coupling** — Documented in ADR 0049. If Product Catalog renames internal categories, Marketplaces category mappings break silently. Mitigation planned for M37.x (ProductSummaryView ACL in Marketplaces BC).
2. **E2E tests are not executed in CI** — The 6 marketplace scenarios compile and have step definitions, but E2E test execution requires infrastructure (TestContainers + Playwright) that may not be available in all CI environments. Verify in the next CI run.
3. **Phase 3 readiness** — The Marketplaces BC now has: entity model, CRUD, seed data, adapter interface, ListingApproved consumer, admin UI, integration tests, E2E page objects, and architectural documentation. Phase 3 (M37.x) can proceed to production adapter implementations (real Amazon/Walmart/eBay integrations).

## Phase 2 Gate: PASS (16/16)

All criteria from `docs/planning/milestones/m36-1-phase-2-plan.md` § 10 are satisfied. The Marketplaces BC Phase 2 is complete.
