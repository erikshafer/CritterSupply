# M33.0 Session 7: Priority 3 Recovery — BFF Route Fixes + Test Coverage

**Date:** 2026-03-23
**Status:** 📋 Planning Complete — Ready for Implementation
**Context:** Post-mortem recovery from Sessions 5/6 partial delivery

---

## Purpose

This session addresses the immediate recovery work identified in `m33-0-post-mortem-recovery-review.md`. The post-mortem found that Priority 3 (Order Search + Return Management pages) was **partially delivered**, not complete. This session fixes the critical issues blocking these pages from working correctly.

---

## What Must Ship

### MUST (Blocking Issues from Post-Mortem)

1. ✅ **Fix BFF Route Mismatch (PSA Critical #1)**
   - **Issue:** `OrderSearch.razor` calls `/api/orders/search` via "BackofficeApi" client
   - **Issue:** `ReturnManagement.razor` calls `/api/returns` via "BackofficeApi" client
   - **But:** Backoffice.Api only exposes `/api/backoffice/*` routes
   - **Fix:** Create two new BFF proxy endpoints:
     - `/api/backoffice/orders/search` → calls `IOrdersClient.SearchOrdersAsync()`
     - `/api/backoffice/returns` → calls `IReturnsClient.GetReturnsAsync()`
   - **Update:** Both Blazor pages to use correct `/api/backoffice/*` paths

2. ✅ **Fix NavMenu Role Visibility (PSA Critical #2)**
   - **Issue:** NavMenu links inside `<AuthorizeView Policy="CustomerService">`
   - **Issue:** Pages allow `customer-service,operations-manager,system-admin`
   - **Problem:** `operations-manager` can access routes but cannot see nav links
   - **Fix:** Wrap each nav link in correct authorization check matching page attributes

3. ✅ **Fix Return Status Vocabulary (PSA Critical #3)**
   - **Issue:** Page defaults to filter `status=Pending`
   - **Fact:** Returns BC has no `Pending` status (uses `Requested`, `Approved`, `Denied`, etc.)
   - **Result:** Invalid filter silently returns all returns (unfiltered)
   - **Fix:** Change default to `Requested` (the actual "pending approval" state)
   - **Fix:** Remove "Pending" from dropdown, or map it to `Requested` in the query

### SHOULD (Test Coverage — Deferred if Time Runs Out)

4. ✅ **Integration Tests for New BFF Endpoints**
   - Test `/api/backoffice/orders/search?query={guid}`
   - Test `/api/backoffice/returns?status=Requested`
   - Verify authorization (401 for unauthenticated, 403 for wrong role)
   - Verify stub client integration

5. ⚠️ **E2E Test Coverage (Deferred to Next Session)**
   - **Rationale:** E2E tests require working endpoints first
   - **Plan:** Add Playwright scenarios in session 8 after BFF fixes are verified

---

## Sequencing

**Must be sequential:**
1. Create BFF endpoints in Backoffice.Api (backend first)
2. Update Blazor pages to use correct paths (frontend depends on backend)
3. Fix NavMenu role visibility (independent)
4. Fix return status vocabulary (independent)
5. Add integration tests (verifies steps 1-4)

**Can be parallelized:**
- NavMenu fix (step 3) and return status fix (step 4) are independent

---

## Implementation Checklist

### Phase 1: BFF Endpoints (Backend)

- [ ] Create `src/Backoffice/Backoffice.Api/Queries/SearchOrders.cs`
  - `[WolverineGet("/api/backoffice/orders/search")]`
  - Query parameter: `string query`
  - Calls `IOrdersClient.SearchOrdersAsync(query)`
  - Returns `OrderSearchResponse` (same DTO shape as `OrderSearch.razor`)
  - Authorization: `[Authorize(Policy = "CustomerService")]`

- [ ] Create `src/Backoffice/Backoffice.Api/Queries/GetReturns.cs`
  - `[WolverineGet("/api/backoffice/returns")]`
  - Query parameter: `string? status` (optional)
  - Calls `IReturnsClient.GetReturnsAsync(status)`
  - Returns `IReadOnlyList<ReturnSummaryDto>`
  - Authorization: `[Authorize(Policy = "CustomerService")]`

### Phase 2: Frontend Updates

- [ ] Update `src/Backoffice/Backoffice.Web/Pages/Orders/OrderSearch.razor`
  - Change line 164: `/api/orders/search` → `/api/backoffice/orders/search`

- [ ] Update `src/Backoffice/Backoffice.Web/Pages/Returns/ReturnManagement.razor`
  - Change line 193: `/api/returns` → `/api/backoffice/returns`
  - Change line 167: Default status `"Pending"` → `"Requested"`
  - Change line 34: Remove `<MudSelectItem Value="@("Pending")">Pending</MudSelectItem>`
  - Update comment line 21: "Active return queue" → "Active return queue — defaults to Requested (awaiting approval)"

### Phase 3: NavMenu Role Visibility

- [ ] Update `src/Backoffice/Backoffice.Web/Layout/NavMenu.razor`
  - Current: Both links inside `<AuthorizeView Policy="CustomerService">`
  - Fix: Wrap **each link** in authorization matching page attributes
  - Order Search link: `<AuthorizeView Roles="customer-service,operations-manager,system-admin">`
  - Return Management link: `<AuthorizeView Roles="customer-service,operations-manager,system-admin">`
  - **Rationale:** Pages use `[Authorize(Roles = "...")]`, not policies

### Phase 4: Integration Tests

- [ ] Create `tests/Backoffice/Backoffice.Api.IntegrationTests/Orders/OrderSearchTests.cs`
  - Test: Search with valid GUID returns results
  - Test: Search with invalid GUID returns empty results
  - Test: Unauthenticated request returns 401
  - Test: Wrong role (e.g., WarehouseClerk) returns 403

- [ ] Create `tests/Backoffice/Backoffice.Api.IntegrationTests/Returns/ReturnListTests.cs`
  - Test: Get returns with no filter returns all
  - Test: Get returns with status=Requested returns filtered list
  - Test: Get returns with status=Completed returns filtered list
  - Test: Unauthenticated request returns 401
  - Test: Wrong role returns 403

### Phase 5: Build & Verification

- [ ] Run `dotnet build` (expect 0 errors, 0 new warnings)
- [ ] Run `dotnet test tests/Backoffice/Backoffice.Api.IntegrationTests` (expect all green)
- [ ] Manual verification:
  - Start infrastructure: `docker-compose --profile infrastructure up -d`
  - Run BackofficeIdentity.Api: `dotnet run --project "src/Backoffice Identity/BackofficeIdentity.Api"`
  - Run Backoffice.Api: `dotnet run --project "src/Backoffice/Backoffice.Api"`
  - Run Backoffice.Web: `dotnet run --project "src/Backoffice/Backoffice.Web"`
  - Login as customer-service user
  - Navigate to `/orders/search`, enter a GUID, verify search works
  - Navigate to `/returns`, verify list loads with Requested filter
  - Verify no console errors in browser dev tools

---

## Expected Outcomes

### Must Have (Session Success Criteria)

1. ✅ Both pages **work end-to-end** (no 404 errors, no silent failures)
2. ✅ NavMenu links visible to all authorized roles
3. ✅ Return Management defaults to a **valid** Returns BC status
4. ✅ All existing tests still pass (no regressions)
5. ✅ New integration tests pass (BFF endpoints verified)

### Nice to Have (Defer if Time Runs Out)

- E2E test coverage (session 8)
- Detail navigation implementation (future)
- Broader Order Search (customer email/name) (future)

---

## Exit Criteria

Session 7 is complete when **all** of these are true:

1. ✅ Build succeeds: `dotnet build` (0 errors)
2. ✅ All tests pass: `dotnet test` (75 Backoffice.Api.IntegrationTests + new tests)
3. ✅ Manual verification complete (checklist above)
4. ✅ Post-mortem blocking issues resolved (BFF routes, NavMenu, status vocabulary)
5. ✅ Session retrospective written: `m33-0-session-7-retrospective.md`

---

## References

- [M33.0 Post-Mortem Review](./m33-0-post-mortem-recovery-review.md) — Source of truth for what needs fixing
- [M33.0 Session 6 Status](./m33-0-session-6-status.md) — Previous session context
- [M33+M34 Engineering Proposal](./m33-m34-engineering-proposal-2026-03-21.md) — Original Priority 3 scope
- [CURRENT-CYCLE.md](../CURRENT-CYCLE.md) — Milestone tracking

---

## Notes for Implementation

### BFF Pattern Reminder

Backoffice.Api is a **Backend-for-Frontend (BFF)**. All endpoints follow the pattern:
- `/api/backoffice/{capability}/{action}` (e.g., `/api/backoffice/orders/search`)
- BFF composes data from multiple domain BCs via HTTP client interfaces
- BFF does NOT directly access domain BC databases

### Authorization Pattern

Backoffice uses **policy-based authorization**:
- Policies defined in `Backoffice.Web/Program.cs` and `Backoffice.Api/Program.cs`
- `CustomerService` policy = roles `customer-service` OR `system-admin`
- `OperationsManager` policy = roles `operations-manager` OR `system-admin`
- For NavMenu, use `Roles` attribute (not `Policy`) when multiple roles need visibility

### Testing Pattern

Backoffice.Api.IntegrationTests uses **stub clients**:
- `BackofficeTestFixture` injects stub implementations of `IOrdersClient`, `IReturnsClient`, etc.
- Tests verify BFF logic + authorization, not domain BC behavior
- Domain BC behavior is tested in BC-specific integration test projects

---

**Session Start:** 2026-03-23 (after plan approval)
**Estimated Duration:** 3-4 hours
**Priority:** CRITICAL (blocks M33.0 completion)
