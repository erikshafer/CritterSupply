# M33.0 Session 3 Plan — Add Order Search + Return Management Pages to Backoffice

**Date:** 2026-03-22
**Status:** 🚀 IN PROGRESS
**Session Goal:** Implement Priority 3 from M33 proposal - Order Search and Return Management pages with bUnit tests

---

## Context

**Priority 3 from M33-M34 Proposal (Phase 6 - Backoffice Completion):**
- Build Order Search page at `/orders/search`: search by order number (primary), customer email, customer name; results table with status; single-click navigation to Order Detail; role-gated to CustomerService / OperationsManager / SystemAdmin
- Build Return Management page at `/returns`: active return queue defaulting to Pending stage; filterable by stage; count badge matching `PendingReturns` tile; single-click to return detail; role-gated to CustomerService / OperationsManager / SystemAdmin
- Create `Backoffice.Web.UnitTests` bUnit project; initial coverage: new Order Search page, new Return Management page, role-gated NavMenu, Dashboard KPI tile rendering

**Current State:**
- Sessions 1-2 completed ✅: INV-3 fixed, F-8 fixture instrumented, 3 projections built (ReturnMetricsView, CorrespondenceMetricsView, FulfillmentPipelineView)
- `ReturnMetricsView` projection is live and integrated with dashboard
- Existing pages provide patterns: `CustomerSearch.razor`, `ProductList.razor`, `InventoryList.razor`
- `IOrdersClient` and `IReturnsClient` interfaces exist in `Backoffice/Clients/`
- Existing backend queries: `GetOrderDetailView.cs`, `GetReturnDetails.cs` in Backoffice.Api

---

## Prerequisites Check

### Existing Infrastructure
- ✅ `IOrdersClient` interface exists (`GetOrdersAsync(Guid customerId)`, `GetOrderAsync(Guid orderId)`)
- ✅ `IReturnsClient` interface exists (`GetReturnsAsync(Guid? orderId)`, `GetReturnAsync(Guid returnId)`)
- ✅ `ReturnMetricsView` projection exists (provides pending return count)
- ❓ Need to verify: Does Orders.Api have a search endpoint? (Likely needs to be added)
- ❓ Need to verify: Does `IOrdersClient.GetOrdersAsync()` support search by order number, email, name?

### Pattern References
- `CustomerSearch.razor` — search input + results table + role-gating pattern
- `ProductList.razor` — list view with filtering
- `InventoryList.razor` — warehouse-specific list view
- `Alerts.razor` — filterable list with status badges

---

## Session Plan

### Step 1: Research & Gap Analysis (Measure Twice)
- [ ] Read `CustomerSearch.razor` to understand search + results table pattern
- [ ] Read `ProductList.razor` to understand MudTable patterns
- [ ] Check if Orders.Api has search endpoint (by order number, email, name)
  - If missing: Need to add `SearchOrdersEndpoint.cs` in Orders.Api
- [ ] Check if Returns.Api has list/filter endpoint (by status)
  - If missing: May need to extend `GetReturnsAsync()` with status filter
- [ ] Verify `IOrdersClient` and `IReturnsClient` have all needed methods
- [ ] Check `NavMenu.razor` to understand how to add new nav items
- [ ] Document findings: what exists vs what needs to be built

### Step 2: Add Backend Endpoints (if needed)
*Only if Step 1 identifies missing endpoints*

**Scenario A: Orders search endpoint missing**
- [ ] Create `SearchOrdersEndpoint.cs` in Orders.Api
- [ ] Add query parameters: orderNumber, customerEmail, customerName, limit
- [ ] Use Marten compiled queries for efficient search
- [ ] Add integration test verifying search works
- [ ] Update `IOrdersClient` to include `SearchOrdersAsync()` method
- [ ] Update `OrdersClient.cs` implementation

**Scenario B: Returns filter endpoint needs enhancement**
- [ ] Check if `GetReturnsAsync()` supports status filter
- [ ] If not, add status parameter to Returns.Api endpoint
- [ ] Update `IReturnsClient` interface if needed
- [ ] Add integration test verifying filtering works

### Step 3: Build Order Search Page
- [ ] Create `/Orders/OrderSearch.razor` in Backoffice.Web/Pages
- [ ] Implement search UI:
  - Search input for order number (primary)
  - Optional filters: customer email, customer name
  - "Search" button
  - Results table: Order Number, Customer, Placed At, Status, Total Amount
  - "View Details" button → navigate to `/orders/{orderId}` (existing page?)
- [ ] Add authorization: `@attribute [Authorize(Roles = "customer-service,operations-manager,system-admin")]`
- [ ] Handle loading states, empty states, error states
- [ ] Add data-testid attributes for E2E testing
- [ ] Follow session-expired pattern (check 401 responses)

### Step 4: Build Return Management Page
- [ ] Create `/Returns/ReturnManagement.razor` in Backoffice.Web/Pages
- [ ] Implement list UI:
  - Status filter dropdown (default: Pending)
  - Results table: Return ID, Order ID, Requested At, Status, Return Type
  - Count badge showing total filtered returns (should match `PendingReturns` dashboard tile when filter=Pending)
  - "View Details" button → navigate to `/returns/{returnId}` (existing page?)
- [ ] Add authorization: `@attribute [Authorize(Roles = "customer-service,operations-manager,system-admin")]`
- [ ] Handle loading states, empty states, error states
- [ ] Add data-testid attributes for E2E testing
- [ ] Follow session-expired pattern (check 401 responses)

### Step 5: Update Navigation
- [ ] Add "Order Search" nav item to `NavMenu.razor` (CustomerService role)
- [ ] Add "Return Management" nav item to `NavMenu.razor` (CustomerService role)
- [ ] Verify nav items appear/hide based on user role

### Step 6: Create Backoffice.Web.UnitTests Project
- [ ] Create `tests/Backoffice/Backoffice.Web.UnitTests/` directory
- [ ] Copy bUnit project structure from `Storefront.Web.UnitTests` (template)
- [ ] Add project references:
  - Backoffice.Web project reference
  - bUnit NuGet package
  - MudBlazor.UnitTests package (for MudBlazor component testing)
  - Shouldly (assertions)
- [ ] Create `BunitTestBase.cs` shared base class (MudServices, AuthorizationCore setup)
- [ ] Add to solution files (.sln and .slnx)

### Step 7: Write bUnit Tests
- [ ] Create `OrderSearchTests.cs`:
  - Test: Search form renders correctly
  - Test: Search input validation
  - Test: Results table displays search results
  - Test: "View Details" button navigates correctly
  - Test: Empty state when no results
  - Test: Loading state during search
- [ ] Create `ReturnManagementTests.cs`:
  - Test: Return list renders correctly
  - Test: Status filter dropdown works
  - Test: Results table displays returns
  - Test: Count badge matches filtered count
  - Test: "View Details" button navigates correctly
  - Test: Empty state when no returns
- [ ] Create `NavMenuTests.cs`:
  - Test: Order Search nav item visible for CustomerService role
  - Test: Return Management nav item visible for CustomerService role
  - Test: Nav items hidden for non-authorized roles
- [ ] Create `DashboardKpiTests.cs`:
  - Test: PendingReturns tile renders with live count
  - Test: LowStockAlerts tile renders with live count

### Step 8: Verify All Tests Pass
- [ ] Run `dotnet test tests/Backoffice/Backoffice.Web.UnitTests` (new bUnit tests)
- [ ] Run `dotnet test tests/Backoffice/Backoffice.Api.IntegrationTests` (should still be ~75+ tests passing)
- [ ] Verify no regressions in other BCs
- [ ] Build entire solution (`dotnet build`) with 0 errors

### Step 9: Manual Verification
- [ ] Start infrastructure (Postgres, RabbitMQ)
- [ ] Run Backoffice.Web locally
- [ ] Log in as CustomerService user
- [ ] Navigate to Order Search page
- [ ] Perform search and verify results display
- [ ] Navigate to Return Management page
- [ ] Filter by status and verify results display
- [ ] Verify count badge matches dashboard `PendingReturns` tile

### Step 10: Documentation
- [ ] Update session plan with actual decisions made
- [ ] Create `m33-0-session-3-retrospective.md`
- [ ] Update `CURRENT-CYCLE.md` with session 3 progress
- [ ] Document any patterns discovered (e.g., search endpoint pattern, bUnit setup)

---

## Exit Criteria

- ✅ Order Search page implemented at `/orders/search`
- ✅ Return Management page implemented at `/returns`
- ✅ Both pages role-gated to CustomerService / OperationsManager / SystemAdmin
- ✅ NavMenu updated with new nav items
- ✅ `Backoffice.Web.UnitTests` bUnit project created
- ✅ bUnit tests written for:
  - Order Search page
  - Return Management page
  - NavMenu role-gated nav items
  - Dashboard KPI tiles
- ✅ All bUnit tests passing
- ✅ All Backoffice integration tests still passing
- ✅ Build: 0 errors, no new warnings
- ✅ Manual verification: pages work end-to-end
- ✅ Retrospective documenting patterns and decisions

---

## Key Decisions to Make

1. **Search endpoint location:** Add to Orders.Api or use Backoffice.Api as proxy?
   - **Recommendation:** Add to Orders.Api (domain BC owns data), Backoffice.Api proxies if needed
2. **Search implementation:** Marten compiled queries vs LINQ queries?
   - **Recommendation:** Marten compiled queries for better performance
3. **Return count consistency:** Should `/returns` page count match `PendingReturns` dashboard tile?
   - **Recommendation:** Yes, both should query `ReturnMetricsView.ActiveCount` when filter=Pending
4. **bUnit test coverage depth:** How many tests per page?
   - **Recommendation:** 4-6 tests per page (happy path, empty state, loading, error, role-gating)

---

## Risks & Mitigation

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Orders.Api search endpoint missing | High | High | Add endpoint in this session (Scenario A) |
| Returns.Api filter not working | Medium | Medium | Extend endpoint if needed (Scenario B) |
| bUnit setup differs from Storefront pattern | Low | Medium | Copy exact template from Storefront.Web.UnitTests |
| MudBlazor component mocking issues | Medium | Low | Use MudBlazor.UnitTests package (handles JSInterop) |
| Count badge mismatch with dashboard | Low | High | Both should query same `ReturnMetricsView` projection |

---

## Notes

- **Measure Twice, Cut Once:** Read existing pages (CustomerSearch, ProductList) before writing new pages
- **Follow Established Patterns:** Use same auth, error handling, session-expired patterns as existing pages
- **Test Thoroughly:** bUnit tests prove role-gating and component rendering work correctly
- **Commit Often:** One commit per major step (backend endpoints, Order Search page, Return Management page, bUnit project, tests)
- **BFF Pattern:** Backoffice.Api acts as BFF — domain BCs (Orders, Returns) own data, Backoffice proxies

---

## Pattern References (from existing code)

**CustomerSearch.razor patterns to copy:**
- Search input with "Search" button
- Results table with action buttons
- Role-gating with `@attribute [Authorize(Roles = "...")]`
- Session-expired error handling
- Empty state messaging

**ProductList.razor patterns to copy:**
- MudTable for results display
- Loading state with spinner
- Action column with buttons

**Auth pattern (from all pages):**
```csharp
// Check 401 before checking IsSuccessStatusCode
if (response.StatusCode == HttpStatusCode.Unauthorized)
{
    SessionExpiredService.TriggerSessionExpired();
    return;
}
```

**bUnit setup pattern (from Storefront.Web.UnitTests):**
```csharp
public class BunitTestBase : TestContextWrapper
{
    protected BunitTestBase()
    {
        Services.AddMudServices();
        Services.AddAuthorizationCore();
        JSInterop.Mode = JSRuntimeMode.Loose; // For MudBlazor components
    }
}
```

---

*Session Start Time: 2026-03-22 01:25 UTC*
