# M33.0 Session 5 Plan — Complete Order Search + Return Management Pages

**Date:** 2026-03-22
**Status:** 🚀 ACTIVE
**Session Goal:** Complete Priority 3 from M33 - finish frontend implementation started in Session 3

---

## Context

**Session 3 Progress:**
- ✅ Backend complete: SearchOrdersEndpoint, Returns status filter, client interfaces updated
- ❌ Frontend incomplete: Order Search page, Return Management page, NavMenu, bUnit tests not built

**This Session (Session 5):**
- Build Order Search page (`/Orders/OrderSearch.razor`)
- Build Return Management page (`/Returns/ReturnManagement.razor`)
- Update NavMenu with new navigation items
- Create `Backoffice.Web.UnitTests` bUnit project
- Write comprehensive bUnit tests
- Verify all tests pass
- Manual verification
- Create retrospective

---

## Session Plan

### Step 1: Update Client Implementations (15 min)
- [ ] Update `OrdersClient.cs` to implement `SearchOrdersAsync()` method
- [ ] Update `ReturnsClient.cs` to pass `status` query parameter in `GetReturnsAsync()`
- [ ] Commit: "M33.0 Session 5: Implement OrdersClient and ReturnsClient methods"

### Step 2: Build Order Search Page (45 min)
- [ ] Create `/Orders/OrderSearch.razor` in Backoffice.Web/Pages
- [ ] Search input for Order ID (GUID format)
- [ ] Results table with Order Number, Customer, Placed At, Status, Total
- [ ] Role authorization: `customer-service,operations-manager,system-admin`
- [ ] Session-expired error handling pattern
- [ ] Empty state + loading state
- [ ] data-testid attributes for E2E
- [ ] Commit: "M33.0 Session 5: Add Order Search page"

### Step 3: Build Return Management Page (45 min)
- [ ] Create `/Returns/ReturnManagement.razor` in Backoffice.Web/Pages
- [ ] Status filter dropdown (default: Pending)
- [ ] Results table: Return ID, Order ID, Requested At, Status, Return Type
- [ ] Count badge for filtered results
- [ ] Role authorization: `customer-service,operations-manager,system-admin`
- [ ] Session-expired error handling
- [ ] Empty state + loading state
- [ ] data-testid attributes for E2E
- [ ] Commit: "M33.0 Session 5: Add Return Management page"

### Step 4: Update Navigation (10 min)
- [ ] Add "Order Search" nav item to NavMenu.razor
- [ ] Add "Return Management" nav item to NavMenu.razor
- [ ] Role-gate both to CustomerService role
- [ ] Commit: "M33.0 Session 5: Add nav items for Order Search and Return Management"

### Step 5: Build & Verify Compilation (5 min)
- [ ] Run `dotnet build` (verify 0 errors)
- [ ] Run existing Backoffice tests to ensure no regressions
- [ ] Commit if any fixes needed

### Step 6: Create bUnit Test Project (30 min)
- [ ] Create `tests/Backoffice/Backoffice.Web.UnitTests/` directory
- [ ] Add project file with bUnit, MudBlazor.UnitTests, Shouldly packages
- [ ] Create `BunitTestBase.cs` shared base class
- [ ] Add to .sln and .slnx files
- [ ] Commit: "M33.0 Session 5: Create Backoffice.Web.UnitTests bUnit project"

### Step 7: Write bUnit Tests (60 min)
- [ ] Create `OrderSearchTests.cs` (5-6 tests)
- [ ] Create `ReturnManagementTests.cs` (6-7 tests)
- [ ] Create `NavMenuTests.cs` (2 tests for new nav items)
- [ ] Commit: "M33.0 Session 5: Add bUnit tests for new pages"

### Step 8: Run All Tests (10 min)
- [ ] Run `dotnet test tests/Backoffice/Backoffice.Web.UnitTests`
- [ ] Run `dotnet test tests/Backoffice/Backoffice.Api.IntegrationTests`
- [ ] Verify all tests pass (or fix failures)
- [ ] Commit any test fixes

### Step 9: Manual Verification (20 min)
- [ ] Start infrastructure containers
- [ ] Run Backoffice.Web locally
- [ ] Log in as CustomerService user
- [ ] Test Order Search page functionality
- [ ] Test Return Management page functionality
- [ ] Verify nav items appear correctly
- [ ] Document any issues found

### Step 10: Documentation (30 min)
- [ ] Create `m33-0-session-5-retrospective.md`
- [ ] Update `CURRENT-CYCLE.md` with Session 5 completion
- [ ] Document patterns, decisions, and any deviations
- [ ] Commit: "M33.0 Session 5: Session retrospective and doc updates"

---

## Exit Criteria

- ✅ `OrdersClient.SearchOrdersAsync()` implemented
- ✅ `ReturnsClient.GetReturnsAsync()` accepts status parameter
- ✅ Order Search page at `/orders/search` (fully functional)
- ✅ Return Management page at `/returns` (fully functional)
- ✅ Both pages role-gated correctly
- ✅ NavMenu updated with new items
- ✅ `Backoffice.Web.UnitTests` bUnit project created
- ✅ bUnit tests for new pages (10+ tests total)
- ✅ All tests passing (bUnit + integration)
- ✅ Build: 0 errors
- ✅ Manual verification successful
- ✅ Retrospective created

---

## Key Patterns to Follow

**From CustomerSearch.razor:**
- Search form with loading states
- Results table with MudTable
- Session-expired error handling (check 401 before IsSuccessStatusCode)

**From Alerts.razor:**
- Status filter dropdown
- Filterable list view
- Count badges

**bUnit Setup (from VendorPortal pattern):**
```csharp
public class BunitTestBase : TestContextWrapper
{
    protected BunitTestBase()
    {
        Services.AddMudServices();
        Services.AddAuthorizationCore();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }
}
```

---

## Estimated Time

- Client implementations: 15 min
- Order Search page: 45 min
- Return Management page: 45 min
- NavMenu updates: 10 min
- Build & verify: 5 min
- bUnit project setup: 30 min
- bUnit tests: 60 min
- Run tests & fix: 10 min
- Manual verification: 20 min
- Documentation: 30 min

**Total: ~4.5 hours**

---

*Session Start: 2026-03-22*
