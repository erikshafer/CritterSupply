# M36.1 — Session 5 Retrospective

**Date:** 2026-03-30
**Duration:** Single session
**Build state:** 0 errors, 33 warnings (unchanged from Session 4 baseline)
**Integration tests:** 35/35 (unchanged — no new tests this session)
**CI Run:** #836 (dotnet.yml), #412 (e2e.yml) — both pending approval

---

## Session Deliverables

### 1. Phase 2 Planning Document — `m36-1-phase-2-plan.md`

**Location:** `docs/planning/milestones/m36-1-phase-2-plan.md`

**Key planning decisions:**

| Decision | Outcome | Rationale |
|----------|---------|-----------|
| M36.1 Phase 2 scope | Phase 2a + stub `ListingApproved` consumer | Proves end-to-end message flow without full bidirectional complexity |
| Session sequence | 3 sessions (6–8) | Session 6: scaffold + CRUD, Session 7: category mappings + adapters + consumer, Session 8: admin UI + E2E + ADRs |
| OWN_WEBSITE in Marketplaces BC | **No** — PO decision | OWN_WEBSITE is Listings BC's internal fast-path, not an external marketplace |
| Seed marketplaces | AMAZON_US, WALMART_US, EBAY_US (3 channels) | Covers the three dominant US pet supply marketplace channels |
| Seed categories | Dogs, Cats, Birds, Reptiles, Fish & Aquatics, Small Animals (6 categories) | Matches existing Product Catalog categories + 2 natural expansions |
| Category mapping count | 18 (6 categories × 3 marketplaces) | Enough for meaningful admin UI display |
| Vault stub pattern | `IVaultClient` interface + `DevVaultClient` stub | Reads from `appsettings.Development.json`; production safety guard throws if `DevVaultClient` is registered |
| ADR numbers | 0048 (marketplace document entity), 0049 (category mapping ownership) | ADR 0043 already taken; 0044–0047 reserved for Phase 1 backlog |

**Deviations from execution plan:**

1. **OWN_WEBSITE excluded from seed data** — The execution plan (Section 6, Task 2.6)
   lists OWN_WEBSITE as one of four seed documents. PO decided OWN_WEBSITE is not an
   external marketplace and should not be in the Marketplaces BC domain. This is the
   correct boundary — Listings BC owns the OWN_WEBSITE fast-path.

2. **ADR numbers corrected** — The execution plan allocated ADRs 0035–0036 for Phase 2.
   Those numbers are occupied by Backoffice ADRs. M36.1 plan allocated 0041–0046 for
   Phase 1, but 0043 was taken by `storefront-web-technology-options.md`. Phase 2 ADRs
   use 0048–0049.

3. **Bidirectional integration scoped down** — The execution plan's Phase 2b
   (Listings BC consuming `MarketplaceListingActivated`/`MarketplaceSubmissionRejected`)
   is deferred to M37.x. M36.1 delivers the Marketplaces → adapter → publish direction
   only.

**Port/database/Docker Compose assignments confirmed:**

| Resource | Value | Status |
|----------|-------|--------|
| Port | 5247 | Verified free — not in `docker-compose.yml` or any `launchSettings.json` |
| Database | `marketplaces` | Verified free — not in `docker/postgres/create-databases.sh` |
| Docker Compose profile | `marketplaces` | To be added in Session 6 |
| Marten schema | `marketplaces` | To be configured in Session 6 |

### 2. E2E Step Definitions — ListingsAdmin (3 executable scenarios)

**File:** `tests/Backoffice/Backoffice.E2ETests/StepDefinitions/ListingsAdminSteps.cs`

**Steps implemented:**

| Step | Method | Page Object |
|------|--------|-------------|
| `[Given("test listings exist in the Listings service")]` | Seeds 2 well-known listings into `StubListingsApiHost` | — |
| `[Given("I am on the listings admin page")]` | `ListingsAdminPage.NavigateAsync()` | `ListingsAdminPage` |
| `[Given("I can see a listing with a known ID")]` | `ListingsAdminPage.GetRowCountAsync()` + stores ID | `ListingsAdminPage` |
| `[When("I navigate to the listings admin page")]` | `ListingsAdminPage.NavigateAsync()` | `ListingsAdminPage` |
| `[When("I filter listings by status {string}")]` | `ListingsAdminPage.FilterByStatusAsync()` | `ListingsAdminPage` |
| `[When("I click on the listing row")]` | `ListingsAdminPage.ClickViewListingAsync()` | `ListingsAdminPage` |
| `[Then("I should see the listings table")]` | Verifies `listings-table` data-testid visible | `ListingsAdminPage` |
| `[Then("I should see at least one listing row")]` | `ListingsAdminPage.GetRowCountAsync()` > 0 | `ListingsAdminPage` |
| `[Then("I should see only listings with status {string}")]` | Verifies all status badges match | — (direct `data-testid` locators) |
| `[Then("I should be on the listing detail page")]` | `ListingDetailPage.IsOnDetailPage()` | `ListingDetailPage` |

### 3. E2E Step Definitions — ListingsDetail (1 executable scenario)

**File:** `tests/Backoffice/Backoffice.E2ETests/StepDefinitions/ListingsDetailSteps.cs`

**Steps implemented:**

| Step | Method | Page Object |
|------|--------|-------------|
| `[Then("I should see the listing SKU")]` | `ListingDetailPage.GetSkuAsync()` | `ListingDetailPage` |
| `[Then("I should see the listing channel")]` | `ListingDetailPage.GetChannelAsync()` | `ListingDetailPage` |
| `[Then("I should see the listing status badge")]` | `ListingDetailPage.GetStatusAsync()` | `ListingDetailPage` |
| `[Then("I should see the listing product name")]` | `ListingDetailPage.GetProductNameAsync()` | `ListingDetailPage` |
| `[Then("I should see the listing created at timestamp")]` | `ListingDetailPage.GetCreatedAtAsync()` | `ListingDetailPage` |
| `[Then("the approve button should be disabled")]` | `ListingDetailPage.IsApproveButtonDisabledAsync()` | `ListingDetailPage` |
| `[Then("the pause button should be disabled")]` | `ListingDetailPage.IsPauseButtonDisabledAsync()` | `ListingDetailPage` |
| `[Then("the end listing button should be disabled")]` | `ListingDetailPage.IsEndButtonDisabledAsync()` | `ListingDetailPage` |

### 4. E2E Test Infrastructure — StubListingsApiHost

**Location:** `tests/Backoffice/Backoffice.E2ETests/E2ETestFixture.cs`

**What:** A lightweight stub Listings API host that serves mock listing data for E2E
scenarios. The Backoffice.Web (Blazor WASM) pages make browser-initiated HTTP calls to
Listings.Api — this stub replaces the real Listings.Api in E2E tests.

**Architecture:**
```
Playwright Browser → Backoffice.Web (WASM) → StubListingsApiHost (mock)
                                            ↓
                                     GET /api/listings/all → seeded listings
                                     GET /api/listings/{id} → single listing
```

**Key design decisions:**

1. **Standalone mini server** — Not hosted within Backoffice.Api or WasmStaticFileHost.
   Follows the existing pattern of separate Kestrel hosts per API.

2. **In-memory seed data** — Listings are seeded via `SeedListing()` in step
   definitions, not from a database. Cleared between scenarios via `ClearAllStubs()`.

3. **WasmStaticFileHost updated** — Now accepts `listingsApiUrl` parameter and includes
   `ListingsApiUrl` in the intercepted `appsettings.json`. This ensures the WASM app's
   `ListingsApi` named HttpClient points to the stub.

4. **Well-known test data** — `WellKnownTestData.Listings` provides deterministic
   listing IDs and SKUs for scenario assertions.

**Approach for Session 6 extension:** The `StubListingsApiHost` pattern can be replicated
for Marketplaces.Api E2E tests by adding a `StubMarketplacesApiHost` with similar
seed/clear/serve mechanics.

---

## E2E Scenario Counts

| Feature | Executable | @wip | Total |
|---------|-----------|------|-------|
| ListingsAdmin.feature | 3 (step defs written) | 2 | 5 |
| ListingsDetail.feature | 1 (step defs written) | 3 | 4 |
| **Total** | **4** | **5** | **9** |

**Remaining @wip scenarios and reasons:**
- `ListingsAdmin: Admin creates a new listing` — Blocked: listing create form not yet built
- `ListingsAdmin: Admin ends a listing` — Blocked: action buttons not wired in table
- `ListingsDetail: Admin approves a listing` — Blocked: approve button disabled stub
- `ListingsDetail: Admin pauses a listing` — Blocked: pause button disabled stub
- `ListingsDetail: Admin ends a listing` — Blocked: end button disabled stub

---

## Integration Test Counts

| Point | Count | Details |
|-------|-------|---------|
| Session start | 35/35 | Baseline from Session 4 |
| Session close | 35/35 | No new tests — E2E step definitions only |

---

## Build State

- **Errors:** 0
- **Warnings:** 33 (unchanged from Session 4 baseline)

---

## CI Status

- **CI Run #836** (`dotnet.yml`) — pending approval (first-time contributor workflow)
- **E2E Run #412** (`e2e.yml`) — pending approval

Both runs were triggered by the PR commits. The `action_required` status indicates
the workflow requires maintainer approval before executing, which is expected for
automated PR commits.

---

## What Session 6 Should Pick Up

Phase 2 implementation begins with the Marketplaces BC scaffold (Session 6 items from
the Phase 2 plan):

1. **Marketplaces.Api project scaffold** — `dotnet new` for both projects, solution
   and Docker Compose updates, DB script update
2. **`Marketplace` document entity** — `ChannelCode` as stable `string Id`
3. **Marketplace CRUD handlers** — Register, Update, Deactivate, Get, List
4. **Seed data** — AMAZON_US, WALMART_US, EBAY_US
5. **Integration tests** — MarketplaceCrudTests (minimum 10 tests)
6. **Guard rails** — `AutoApplyTransactions`, `[Authorize]`, solution + Docker updates
   in scaffold commit

**Pre-flight for Session 6:**
- Read `docs/planning/milestones/m36-1-phase-2-plan.md` (this session's output)
- Read `docs/skills/adding-new-bounded-context.md` for scaffolding checklist
- Read `docs/skills/marten-document-store.md` for document entity patterns
- Run `docker compose down -v && docker compose --profile infrastructure up -d`
  to reset Postgres volumes (new `marketplaces` database must be created)
