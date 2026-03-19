# M32.3 Session 2 Plan: Product Search UI + E2E Tests + Pricing Admin Write UI

**Date:** 2026-03-19
**Milestone:** M32.3 — Backoffice Phase 3B: Write Operations Depth
**Status:** 🚀 **ACTIVE**

---

## Executive Summary

M32.3 Session 2 completes the **Product Admin workflow** by adding product search/list UI (resolving DEMO-001 hardcoded SKU navigation), adds E2E test coverage for ProductEdit page, reviews API endpoint routing consistency, and begins **Pricing Admin write UI** with base price setting.

**Key Goals:**
1. Product search/list page enables discovering product SKUs
2. E2E tests verify ProductEdit role permissions and workflows
3. API endpoint routing audit (Product Catalog consistency)
4. Pricing Admin write UI (set base price)

**Strategic Context:** Session 1 built ProductEdit.razor but hardcoded navigation to `/products/DEMO-001/edit`. Session 2 completes the discovery-to-edit workflow and adds test coverage before expanding to Pricing Admin.

---

## Session 2 Goals

### Primary Goals (Must Complete)

#### 1. Product Search/List UI

**Problem:** Index.razor navigation links to `/products/DEMO-001/edit` — users can't discover product SKUs to edit.

**Solution:** Create `/products` list page with search capability.

**Implementation:**
- **Route:** `/products` (list view)
- **Authorization:** `[Authorize(Roles = "copy-writer,product-manager,system-admin")]`
- **Features:**
  - Product list with SKU, display name, description preview, status
  - Search by SKU or display name (client-side filtering for MVP)
  - "Edit" button per row navigating to `/products/{sku}/edit`
  - MudTable with pagination (25 items per page)
  - Active/Discontinued status badge
- **Data Source:** `GET /api/catalog/products` (existing endpoint)

**Acceptance Criteria:**
- ✅ Products list page exists at `/products`
- ✅ Index.razor links updated to `/products` (not hardcoded DEMO-001)
- ✅ ProductManager and CopyWriter can browse products and navigate to edit
- ✅ Build succeeds with 0 errors

**Estimated Effort:** 1 hour

---

#### 2. API Endpoint Routing Audit

**Problem:** Product Catalog BC may have inconsistent routing (`/api/catalog/products` vs `/api/products`).

**Investigation:**
- Review `src/Product Catalog/ProductCatalog.Api/` for all HTTP endpoints
- Check if routes use `/api/catalog/products` or `/api/products` prefix
- Verify consistency across GET, PUT, PATCH endpoints

**Decision Criteria:**
- **Option A:** Keep mixed routes (backend flexibility)
- **Option B:** Standardize on `/api/catalog/products` (BC namespace clarity)
- **Option C:** Standardize on `/api/products` (simplicity)

**Action Items:**
- Document current routing patterns
- If inconsistent, decide on standard and update endpoints OR accept as-is with documentation
- Update CatalogClient calls if routes change

**Acceptance Criteria:**
- ✅ All Product Catalog routes documented
- ✅ Routing decision recorded (accept as-is OR standardize)
- ✅ If changed: CatalogClient updated, tests passing

**Estimated Effort:** 30 minutes

---

#### 3. E2E Tests for ProductEdit Page

**Problem:** ProductEdit.razor has no E2E test coverage for role permissions, edit workflow, discontinuation.

**Implementation:**
- **Feature File:** `tests/Backoffice/Backoffice.E2ETests/Features/ProductAdmin.feature`
- **Scenarios:**
  1. ProductManager can edit display name and description
  2. CopyWriter can edit description only (display name disabled)
  3. Discontinue product requires two clicks
  4. Session-expired handling on save failure (401)
  5. Navigation from products list to edit page

**Page Object Model:**
- **ProductListPage.cs** — Navigate to list, search, click edit
- **ProductEditPage.cs** — Edit fields, save, discontinue, verify disabled fields

**Stub Coordination:**
- StubCatalogClient already functional from Session 1
- No new stubs needed

**Acceptance Criteria:**
- ✅ 5+ E2E scenarios for ProductEdit page
- ✅ Role-based field permissions verified
- ✅ Two-click discontinuation verified
- ✅ All scenarios passing

**Estimated Effort:** 1.5 hours

---

### Secondary Goals (If Time Permits)

#### 4. Pricing Admin Write UI (Set Base Price)

**Problem:** Pricing Admin link on Index.razor says "Coming in future sessions".

**Solution:** Create `/pricing/{sku}` page for setting base price.

**Implementation:**
- **Route:** `/pricing/{sku}` (or `/products/{sku}/pricing`)
- **Authorization:** `[Authorize(Roles = "pricing-manager,system-admin")]`
- **Features:**
  - Display current base price + list price + effective date
  - Form: Set new base price (currency input)
  - Floor/ceiling constraint display (read-only info from backend)
  - Save button → `POST /api/pricing/base-price`
  - Session-expired handling

**Endpoints:**
- `GET /api/pricing/products/{sku}` — Get current price
- `POST /api/pricing/base-price` — Set base price (existing from M32.1 Session 2)

**Client Extensions:**
- Extend `IPricingClient` interface (or create if not exists)
- Implement `PricingClient` in Backoffice.Api

**Acceptance Criteria:**
- ✅ Pricing admin page exists at `/pricing/{sku}`
- ✅ PricingManager can set base price
- ✅ Floor/ceiling constraints displayed
- ✅ Index.razor link updated

**Estimated Effort:** 1.5 hours (DEFERRED if time is tight)

---

## Technical Approach

### 1. Product List Page Pattern

**Following:** Existing Backoffice.Web page patterns (Dashboard, CustomerSearch, Alerts)

**Key Components:**
- MudTable with Pagination
- MudTextField for search input
- MudChip for status badges (Active=Success, Discontinued=Default)
- HttpClient direct call to `/api/catalog/products`
- Local `ProductListItemDto` record

**Example Code:**
```razor
@page "/products"
@attribute [Authorize(Roles = "copy-writer,product-manager,system-admin")]
@inject IHttpClientFactory HttpClientFactory
@inject NavigationManager Navigation

<PageTitle>Products — Backoffice</PageTitle>

<MudText Typo="Typo.h4">Product Catalog</MudText>

<MudTable Items="@_filteredProducts" Hover="true">
    <ColGroup>
        <col style="width: 150px;" /> <!-- SKU -->
        <col style="width: 200px;" /> <!-- Display Name -->
        <col /> <!-- Description -->
        <col style="width: 120px;" /> <!-- Status -->
        <col style="width: 100px;" /> <!-- Actions -->
    </ColGroup>
    <HeaderContent>
        <MudTh>SKU</MudTh>
        <MudTh>Display Name</MudTh>
        <MudTh>Description</MudTh>
        <MudTh>Status</MudTh>
        <MudTh>Actions</MudTh>
    </HeaderContent>
    <RowTemplate>
        <MudTd>@context.Sku</MudTd>
        <MudTd>@context.DisplayName</MudTd>
        <MudTd>@context.Description.Substring(0, Math.Min(100, context.Description.Length))...</MudTd>
        <MudTd>
            <MudChip Size="Size.Small" Color="@(context.IsActive ? Color.Success : Color.Default)">
                @(context.IsActive ? "Active" : "Discontinued")
            </MudChip>
        </MudTd>
        <MudTd>
            <MudButton Size="Size.Small" Variant="Variant.Text" Color="Color.Primary"
                       Href="@($"/products/{context.Sku}/edit")">
                Edit
            </MudButton>
        </MudTd>
    </RowTemplate>
</MudTable>

@code {
    private List<ProductListItemDto> _products = new();
    private List<ProductListItemDto> _filteredProducts = new();
    private string _searchText = string.Empty;

    record ProductListItemDto(string Sku, string DisplayName, string Description, bool IsActive);

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("BackofficeApi");
        _products = await client.GetFromJsonAsync<List<ProductListItemDto>>("/api/catalog/products") ?? new();
        _filteredProducts = _products;
    }
}
```

---

### 2. E2E Test Pattern

**Following:** `tests/Backoffice/Backoffice.E2ETests/Features/Authentication.feature` pattern

**Feature File Structure:**
```gherkin
Feature: Product Administration
  As a ProductManager or CopyWriter
  I want to manage product details via Backoffice
  So that I can keep catalog data accurate

  Background:
    Given the Backoffice system is running
    And test products exist in the catalog

  Scenario: ProductManager edits product display name
    Given I am logged in as "ProductManager"
    When I navigate to the products list
    And I search for product "DEMO-001"
    And I click Edit for "DEMO-001"
    And I change the display name to "Updated Product Name"
    And I click Save
    Then the product display name is updated
    And I see a success message

  Scenario: CopyWriter can only edit description
    Given I am logged in as "CopyWriter"
    When I navigate to product "DEMO-001" edit page
    Then the display name field is disabled
    And the description field is enabled
    When I update the description
    And I click Save
    Then the description is updated

  Scenario: Two-click discontinuation workflow
    Given I am logged in as "ProductManager"
    And I am on product "DEMO-001" edit page
    When I click Discontinue Product
    Then I see a warning message
    When I click Discontinue Product again
    Then the product is discontinued
```

---

### 3. Pricing Admin Page Pattern (If Time)

**Following:** ProductEdit.razor pattern (role-based fields, session-expired handling, optimistic UI)

**Key Differences:**
- Simpler form (single currency input for base price)
- Display read-only floor/ceiling constraints
- No discontinuation workflow

---

## Skills to Reference

1. **`docs/skills/blazor-wasm-jwt.md`** — Named HttpClient, local DTOs, authorization patterns
2. **`docs/skills/e2e-playwright-testing.md`** — Page Object Model, Reqnroll patterns, stub coordination
3. **`docs/skills/bunit-component-testing.md`** — If unit testing Blazor components (not E2E)
4. **`docs/skills/wolverine-message-handlers.md`** — If reviewing endpoint routing patterns

---

## Risks & Mitigations

### R1: Product Catalog Endpoint May Not Return Full List

**Risk:** `GET /api/catalog/products` might paginate or filter results.

**Mitigation:**
- Read endpoint implementation before UI work
- If paginated: Add query params to client call
- If missing: Create stub list for MVP, file issue for backend

**Status:** INVESTIGATE FIRST

---

### R2: E2E Test Infrastructure May Need Adjustments

**Risk:** ProductEdit stubs may not handle all test scenarios.

**Mitigation:**
- Review `StubCatalogClient.cs` for product list support
- Add in-memory product seeding if needed
- Use existing E2E fixture patterns from Authentication.feature

**Status:** LOW RISK (stubs functional from Session 1)

---

### R3: Pricing Admin May Require New Client Interface

**Risk:** IPricingClient may not exist yet.

**Mitigation:**
- Check if `Backoffice/Clients/IPricingClient.cs` exists
- If missing: Create interface + implementation + stubs (same pattern as ICatalogClient)
- Pricing BC endpoints confirmed available from M32.1 Session 2

**Status:** MEDIUM RISK (new client required)

---

## Session Workflow

### Phase 1: Product List UI (1 hour)

1. Read `src/Product Catalog/ProductCatalog.Api/` endpoints (routing audit)
2. Create `Backoffice.Web/Pages/Products/ProductList.razor`
3. Update `Index.razor` navigation links (remove DEMO-001 hardcoding)
4. Test manually with `dotnet run` (verify product list loads, navigation works)
5. Commit: "M32.3 Session 2: Product list page + navigation fixes"

---

### Phase 2: E2E Tests (1.5 hours)

1. Read `tests/Backoffice/Backoffice.E2ETests/Features/Authentication.feature` (pattern reference)
2. Create `Features/ProductAdmin.feature` with 5+ scenarios
3. Create `PageObjects/ProductListPage.cs` and update `ProductEditPage.cs` (if not exists)
4. Update `StubCatalogClient.cs` for product list seeding
5. Run E2E tests: `dotnet test Backoffice.E2ETests --filter "Category=ProductAdmin"`
6. Fix failures iteratively (test-ids, timing, stub data)
7. Commit: "M32.3 Session 2: E2E tests for Product Admin workflow"

---

### Phase 3: Pricing Admin (Optional, 1.5 hours)

1. Check if `Backoffice/Clients/IPricingClient.cs` exists
2. If missing: Create interface + implementation + stubs
3. Create `Backoffice.Web/Pages/Pricing/SetBasePrice.razor`
4. Update `Index.razor` Pricing Admin link
5. Test manually
6. Commit: "M32.3 Session 2: Pricing Admin base price UI"

---

### Phase 4: Documentation (30 minutes)

1. Update `CURRENT-CYCLE.md` with Session 2 progress
2. Write `m32-3-session-2-retrospective.md`
3. Store memories for future sessions
4. Commit: "M32.3 Session 2: Documentation and retrospective"

---

## Success Criteria

### Phase 1 Complete When:
- ✅ ProductList.razor exists at `/products` route
- ✅ Index.razor navigation updated (no DEMO-001 hardcoding)
- ✅ ProductManager/CopyWriter can browse products and navigate to edit
- ✅ Build succeeds with 0 errors

### Phase 2 Complete When:
- ✅ ProductAdmin.feature file with 5+ scenarios
- ✅ All E2E scenarios passing (green)
- ✅ Role-based permissions verified via tests
- ✅ Two-click discontinuation verified

### Phase 3 Complete When (Optional):
- ✅ Pricing admin page exists
- ✅ PricingManager can set base price
- ✅ Index.razor link updated

### Session Complete When:
- ✅ All primary goals achieved (Product List + E2E Tests + API Routing Audit)
- ✅ Build: 0 errors, 0 warnings
- ✅ Retrospective written
- ✅ CURRENT-CYCLE.md updated

---

## Deferred to Future Sessions

- **Pricing Admin Schedule Price Change** — Deferred to Session 3
- **Pricing Admin Cancel Schedule** — Deferred to Session 3
- **Warehouse Admin Write UI** — Deferred to Session 3
- **User Management Write UI** — Deferred to Session 4
- **CSV/Excel Exports** — Deferred to Session 5
- **Bulk Operations** — Deferred to Session 6

---

## References

- **M32.3 Session 1 Plan:** `docs/planning/milestones/m32-3-session-1-plan.md`
- **M32.3 Session 1 Retrospective:** `docs/planning/milestones/m32-3-session-1-retrospective.md`
- **M32.2 Retrospectives:** Session 1, 2, 3 (session-expired pattern, auth fixes)
- **UX Audit:** `docs/planning/ux-audit-discovery-2026-03-18.md`
- **Skills:**
  - `docs/skills/blazor-wasm-jwt.md` (WASM patterns)
  - `docs/skills/e2e-playwright-testing.md` (E2E patterns)
  - `docs/skills/wolverine-message-handlers.md` (endpoint patterns)

---

*Plan Created: 2026-03-19*
*Session Start: 2026-03-19*
*Estimated Duration: 3-4 hours*
