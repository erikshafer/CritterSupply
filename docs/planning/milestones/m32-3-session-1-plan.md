# M32.3 Session 1 Plan: Product Admin Write UI

**Milestone:** M32.3 (Backoffice Phase 3B — Write Operations Depth + Cross-BC UX)
**Session:** 1
**Focus:** Product description/name updates + basic discontinuation workflow
**Date:** 2026-03-19

---

## Session Goals

Implement Product Admin write operations in Backoffice.Web using existing Product Catalog BC endpoints.

**What's Shipping:**
1. Extend ICatalogClient interface with write operation methods
2. Implement CatalogClient write methods
3. Create ProductEdit.razor page with edit form
4. Add basic product discontinuation workflow
5. Wire navigation from existing pages (Dashboard or new Products menu item)

**What's NOT Shipping (Deferred):**
- Product history tab (requires Product Catalog ES migration — future milestone)
- Discontinuation pre-flight impact counts (requires Listings/Marketplaces BCs)
- Advanced product image management
- Category/subcategory editing (Phase 1 uses simple strings)

---

## Prerequisites Verified

### ✅ Backend Endpoints Exist

**Product Catalog API endpoints already implemented:**
- `PUT /api/products/{sku}/description` — UpdateProductDescription (CopyWriter policy)
- `PUT /api/products/{sku}/display-name` — UpdateProductDisplayName (exists, needs verification)
- `PATCH /api/products/{sku}/status` — ChangeProductStatus (for discontinuation)

**Product Status Enum:**
```csharp
public enum ProductStatus
{
    Active,
    Discontinued
}
```

**Product.IsTerminal Property:**
```csharp
public bool IsTerminal => Status == ProductStatus.Discontinued || IsDeleted;
```

### ✅ Skill Files Read

- **blazor-wasm-jwt.md** — WASM patterns, named HttpClients, authorization
- **wolverine-message-handlers.md** — HTTP endpoint patterns, compound handlers
- **e2e-playwright-testing.md** — E2E testing patterns (for later)

### ✅ Existing Backoffice Structure

**Client Pattern:**
- Domain project: `src/Backoffice/Backoffice/Clients/ICatalogClient.cs`
- Implementation: `src/Backoffice/Backoffice.Api/Clients/CatalogClient.cs`
- Registration: `Program.cs` in Backoffice.Api with named HttpClient

**Current ICatalogClient:**
```csharp
public interface ICatalogClient
{
    Task<ProductDto?> GetProductAsync(string sku, CancellationToken ct = default);
}

public sealed record ProductDto(
    string Sku,
    string Name,
    string Description,
    string Category,
    string Status);
```

**WASM Pages Pattern:**
- `src/Backoffice/Backoffice.Web/Pages/*.razor`
- Uses MudBlazor components
- Session-expired pattern for 401 handling
- Optimistic UI updates for mutations

---

## Implementation Plan

### Step 1: Extend ICatalogClient Interface

**File:** `src/Backoffice/Backoffice/Clients/ICatalogClient.cs`

Add methods:
```csharp
Task<bool> UpdateProductDescriptionAsync(string sku, string description, CancellationToken ct = default);
Task<bool> UpdateProductDisplayNameAsync(string sku, string displayName, CancellationToken ct = default);
Task<bool> DiscontinueProductAsync(string sku, CancellationToken ct = default);
```

**Return Type:** `bool` (true = success, false = 404/error)

**Why bool?** Simple for WASM UI — check success, show snackbar on failure, trigger session-expired on 401.

---

### Step 2: Implement CatalogClient Write Methods

**File:** `src/Backoffice/Backoffice.Api/Clients/CatalogClient.cs`

**Pattern:**
```csharp
public async Task<bool> UpdateProductDescriptionAsync(string sku, string description, CancellationToken ct = default)
{
    var request = new { Sku = sku, Description = description };
    var response = await _httpClient.PutAsJsonAsync($"/api/products/{Uri.EscapeDataString(sku)}/description", request, ct);

    return response.IsSuccessStatusCode;
}
```

**Error Handling:** Let 401 Unauthorized bubble up (will trigger session-expired modal in WASM).

---

### Step 3: Create ProductEdit.razor Page

**File:** `src/Backoffice/Backoffice.Web/Pages/ProductEdit.razor`

**Route:** `@page "/products/{sku}/edit"`

**Authorization:** `@attribute [Authorize(Policy = "CopyWriter")]` (matches backend policy)

**Form Fields:**
- SKU (read-only display)
- Product Name (editable, max 100 chars)
- Description (editable, multiline, max 2000 chars)
- Status (display only — "Active" or "Discontinued")

**Actions:**
- "Save Changes" button → calls UpdateProductDescription + UpdateProductDisplayName
- "Discontinue Product" button → confirmation modal → calls DiscontinueProduct
- "Cancel" link → navigate back to product list (or dashboard)

**UX Requirements (from M32.2 patterns):**
- Optimistic UI updates (disable buttons during save)
- Session-expired handling (401 → SessionExpiredService.TriggerSessionExpired())
- Error snackbar on 404/400 (not blocking modal)
- Success snackbar on completion

**Data Loading:**
```csharp
protected override async Task OnInitializedAsync()
{
    var product = await CatalogClient.GetProductAsync(Sku);
    if (product is null)
    {
        Snackbar.Add("Product not found", Severity.Error);
        Navigation.NavigateTo("/dashboard");
        return;
    }

    _name = product.Name;
    _description = product.Description;
    _status = product.Status;
}
```

---

### Step 4: Add Navigation to ProductEdit

**Option A: Add to NavMenu.razor (Products Admin menu item)**
- Create new menu item: "Products" → `/products`
- Placeholder products list page → click "Edit" → navigate to `/products/{sku}/edit`

**Option B: Quick win — Add SKU lookup modal to Dashboard**
- "Edit Product" button on Dashboard
- Modal: SKU input → Navigate to `/products/{sku}/edit`

**Decision:** Start with Option B (quick win). Products list page can be added in Session 2 or later.

---

### Step 5: Discontinuation Workflow

**UI Flow:**
1. User clicks "Discontinue Product" button on ProductEdit page
2. MudDialog confirmation modal appears:
   - Title: "Discontinue Product?"
   - Message: "This product will no longer be available for sale. This action cannot be undone."
   - Actions: "Cancel" (secondary) | "Discontinue" (error color)
3. On confirm → call `CatalogClient.DiscontinueProductAsync(sku)`
4. On success → navigate back to dashboard with success snackbar
5. On 401 → trigger session-expired modal
6. On error → show error snackbar, stay on page

**No pre-flight impact counts yet** — deferred to M32.3 Session 4+ (requires Listings/Marketplaces BCs).

---

## Acceptance Criteria

### Step 1-2: Client Extension
- [ ] ICatalogClient interface has 3 new methods
- [ ] CatalogClient implements all 3 methods
- [ ] Methods call correct Product Catalog API endpoints
- [ ] Build succeeds (0 errors)

### Step 3: ProductEdit Page
- [ ] ProductEdit.razor loads product data on mount
- [ ] Edit form displays SKU (read-only), name, description
- [ ] "Save Changes" button calls UpdateDescription + UpdateDisplayName
- [ ] Optimistic UI updates (buttons disabled during save)
- [ ] Session-expired handling (401 → modal)
- [ ] Error handling (404/400 → snackbar)
- [ ] Success feedback (snackbar + optionally navigate away)

### Step 4: Navigation
- [ ] Dashboard has "Edit Product" button or modal
- [ ] Modal accepts SKU input and navigates to `/products/{sku}/edit`

### Step 5: Discontinuation
- [ ] "Discontinue Product" button shows confirmation modal
- [ ] Modal has clear warning message
- [ ] Confirmation calls DiscontinueProductAsync
- [ ] Success → navigate to dashboard with snackbar
- [ ] Error → show snackbar, stay on page

### Overall
- [ ] Build: 0 errors, 0 new warnings
- [ ] All error states handled (401, 404, 400, network failure)
- [ ] Session-expired pattern applied consistently
- [ ] MudBlazor components follow Dashboard.razor style

---

## Estimated Effort

**3-4 hours** (Step 1-5 including testing)

---

## References

- **M32.2 Session 2 Retrospective:** `docs/planning/milestones/m32.2-session-2-retrospective.md` (session-expired pattern)
- **Blazor WASM JWT Skill:** `docs/skills/blazor-wasm-jwt.md`
- **Product Catalog UpdateProductDescription:** `src/Product Catalog/ProductCatalog.Api/Products/UpdateProductDescription.cs`
- **Product Model:** `src/Product Catalog/ProductCatalog/Products/Product.cs`
- **Dashboard Example:** `src/Backoffice/Backoffice.Web/Pages/Dashboard.razor`

---

## Next Session Preview (Session 2)

**Focus:** Pricing Admin Write UI (set base price, schedule price change, cancel schedule)

**Prerequisites:**
- Verify Pricing BC endpoints exist
- Read `src/Pricing/Pricing.Api/Pricing/SetBasePriceEndpoint.cs`
- Extend Backoffice client for Pricing BC operations

---

*Plan Created: 2026-03-19*
*Session Start: TBD*
