# M32.3 Session 6 Plan: Warehouse Admin E2E Tests

**Date:** 2026-03-20
**Milestone:** M32.3 — Backoffice Phase 3B: Write Operations Depth
**Session:** 6 of 10
**Goal:** Create comprehensive E2E test coverage for Warehouse Admin workflows (10 scenarios)

---

## Context

Session 5 completed Pricing Admin E2E tests (6 scenarios). Session 4 completed Warehouse Admin write UI (InventoryList.razor + InventoryEdit.razor). Session 6 focuses exclusively on Warehouse Admin E2E tests.

**Key Lesson from Session 5 (I1):** Read UI code BEFORE writing Page Objects. Session 5 had to refactor test IDs because Page Objects were written before reading actual DOM structure.

**Estimated Time:** ~5-6 hours (10 scenarios × ~30 min/scenario per Session 5 I2)

---

## Pre-Implementation Analysis

### UI Code Review (Completed Before Writing)

**InventoryList.razor:**
- Route: `/inventory`
- Auth: `warehouse-clerk,system-admin`
- MudTable with client-side search by SKU
- Color-coded status chips (Out of Stock / Low Stock / In Stock)
- Row click navigates to `/inventory/{sku}/edit`
- Session-expired handling via 401 check
- **No existing data-testid attributes** — must add all

**InventoryEdit.razor:**
- Route: `/inventory/{sku}/edit`
- Auth: `warehouse-clerk,system-admin`
- Three KPI cards: Available, Reserved, Total
- Dual-form layout: Adjust Inventory + Receive Inbound Stock
- Adjust: quantity, reason dropdown (4 options), adjustedBy (read-only)
- Receive: quantity (min 1), source (freeform text)
- Success/error message via MudAlert
- **No existing data-testid attributes** — must add all

### Bug Found: URL Mismatch (Blocker)
- InventoryEdit.razor calls `GET /api/inventory/{sku}`
- But GetStockLevel.cs endpoint is at `GET /api/backoffice/inventory/{sku}`
- Fix: Change GetStockLevel route to `/api/inventory/{sku}` for consistency with other inventory endpoints

---

## Execution Plan

### Phase 1: Fix URL Mismatch Bug
- Change `GetStockLevel.cs` route from `/api/backoffice/inventory/{sku}` to `/api/inventory/{sku}`

### Phase 2: Add data-testid Attributes to UI Pages
- InventoryList.razor: search input, table, rows, status chips
- InventoryEdit.razor: KPI cards, form sections, inputs, buttons, hidden message divs

### Phase 3: Create WarehouseAdmin.feature (10 Scenarios)
1. Warehouse Clerk can browse inventory list
2. Warehouse Clerk can filter inventory by SKU
3. Warehouse Clerk can navigate to edit page
4. Warehouse Clerk can adjust inventory (cycle count)
5. Warehouse Clerk can adjust inventory (damage — negative)
6. Warehouse Clerk can receive inbound stock
7. Adjustment requires reason selection
8. Receive quantity must be greater than zero
9. Session expired redirects to login during inventory edit
10. SystemAdmin can access warehouse admin pages

### Phase 4: Create Page Object Models
- InventoryListPage.cs
- InventoryEditPage.cs

### Phase 5: Create WarehouseAdminSteps.cs
- Step definitions binding Gherkin to Page Objects
- Reuse existing AuthenticationSteps and SessionExpirySteps patterns

### Phase 6: Build Verification
- `dotnet build` — target 0 errors

### Phase 7: Documentation
- Update CURRENT-CYCLE.md with Session 6 progress
- Create Session 6 retrospective

---

## Risks

### R1: URL Mismatch Bug
**Risk:** GetStockLevel uses `/api/backoffice/inventory/{sku}` but InventoryEdit.razor calls `/api/inventory/{sku}`.
**Mitigation:** Fix route before writing E2E tests.

### R2: Missing Step Definitions
**Risk:** PricingAdmin.feature uses `Given the Backoffice system is running` and `stub catalog client has product` steps that have no step definition bindings.
**Observation:** These may be dead code/planned-but-unimplemented. WarehouseAdmin.feature should use `Given the Backoffice application is running` (from AuthenticationSteps.cs) instead.

### R3: E2E Test Flakiness
**Risk:** Tests may have timing issues.
**Mitigation:** Use established timeout patterns (500-1000ms after state changes).

---

## Success Criteria

- [ ] WarehouseAdmin.feature with 10 scenarios
- [ ] InventoryListPage.cs Page Object Model
- [ ] InventoryEditPage.cs Page Object Model
- [ ] WarehouseAdminSteps.cs step definitions
- [ ] Both UI pages updated with data-testid attributes
- [ ] URL mismatch bug fixed
- [ ] Build: 0 errors
- [ ] CURRENT-CYCLE.md updated
- [ ] Session 6 retrospective created
