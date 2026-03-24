# M33.0 Session 13 Plan — Phase 5: Backoffice Folder Restructure + Transaction Fix

**Date:** 2026-03-24
**Phase:** M33.0 Phase 5 (Backoffice Folder Restructure + Transaction Fix)
**Branch:** `claude/update-current-cycle-status`

---

## Session Context

### Phase 4 Status
According to Session 12 retrospective, Phase 4 (Vendor Portal Structural Refactor) is **COMPLETE**:
- ✅ VP-1 through VP-6: All Vendor Portal structural refactoring finished
- ✅ F-2 Phase A: Feature-level `@ignore` tags removed from E2E tests
- ✅ Build: 0 errors, 36 pre-existing warnings (unchanged)
- ✅ All 86 VendorPortal.Api.IntegrationTests passing

### Session 13 Objective
Execute Phase 5 of M33.0: Backoffice folder restructure + transaction fix following the patterns established in Returns BC and Vendor Portal.

---

## Phase 5 Work Items (from M33-M34 Proposal)

From `docs/planning/milestones/m33-m34-engineering-proposal-2026-03-21.md`:

| Item | Finding | Effort | Priority | Notes |
|------|---------|--------|----------|-------|
| **XC-3 + BO-2** | Move AcknowledgeAlert to AlertManagement/, remove manual SaveChangesAsync | S | 1 | Transaction fix + folder move atomic |
| **BO-1** | Restructure Backoffice.Api Commands/ + Queries/ → feature-named folders | M | 2 | Follow Returns/VP patterns |
| **BO-3** | Colocate Backoffice/Projections/ with capabilities | S | 3 | Move projections to feature folders |

**Total Phase 5 Estimate:** 2 sessions (4-6 hours) - This is Session 1 of 2

---

## Current Backoffice Structure

### Backoffice.Api Commands/ (8 files)
```
src/Backoffice/Backoffice.Api/Commands/
├── AcknowledgeAlertEndpoint.cs
├── AddOrderNoteEndpoint.cs
├── ApproveReturn.cs
├── CancelOrder.cs
├── DeleteOrderNoteEndpoint.cs
├── DenyReturn.cs
├── EditOrderNoteEndpoint.cs
└── Inventory/
    ├── AdjustInventoryProxy.cs
    └── ReceiveStockProxy.cs
```

### Backoffice.Api Queries/ (14 files)
```
src/Backoffice/Backoffice.Api/Queries/
├── GetAlertFeed.cs
├── GetCorrespondenceHistory.cs
├── GetCustomerServiceView.cs
├── GetDashboardMetrics.cs
├── GetDashboardSummary.cs
├── GetLowStockAlerts.cs
├── GetOrderDetailView.cs
├── GetOrderNotes.cs
├── GetProductList.cs
├── GetReturnDetails.cs
├── GetReturns.cs
├── GetStockLevel.cs
├── SearchOrders.cs
└── Inventory/
    └── GetInventoryList.cs
```

### Backoffice/Commands/ (1 file)
```
src/Backoffice/Backoffice/Commands/
└── AcknowledgeAlert.cs (command + handler with manual SaveChangesAsync)
```

### Backoffice/Projections/ (10 files)
```
src/Backoffice/Backoffice/Projections/
├── AdminDailyMetrics.cs
├── AdminDailyMetricsProjection.cs
├── AlertFeedView.cs
├── AlertFeedViewProjection.cs
├── CorrespondenceMetricsView.cs
├── CorrespondenceMetricsViewProjection.cs
├── FulfillmentPipelineView.cs
├── FulfillmentPipelineViewProjection.cs
├── ReturnMetricsView.cs
└── ReturnMetricsViewProjection.cs
```

---

## Session 13 Execution Plan

### Part 1: XC-3 + BO-2 — AcknowledgeAlert Transaction Fix
**Goal:** Move AcknowledgeAlert to AlertManagement/ folder + remove manual SaveChangesAsync

**⚠️ CRITICAL:** These MUST ship in the same commit (atomic changeset)

**Tasks:**
1. Create `src/Backoffice/Backoffice/AlertManagement/` folder
2. Move `Commands/AcknowledgeAlert.cs` → `AlertManagement/AcknowledgeAlert.cs`
3. Remove line 43: `await session.SaveChangesAsync(ct);` (Wolverine handles transactions)
4. Update namespace from `Backoffice.Commands` to `Backoffice.AlertManagement`
5. Update endpoint import in `AcknowledgeAlertEndpoint.cs`
6. Delete empty `Commands/` folder
7. Verify build succeeds
8. Run Backoffice integration tests (specifically alert acknowledgment tests)

**Expected Result:**
- `src/Backoffice/Backoffice/AlertManagement/AcknowledgeAlert.cs` exists
- No `Commands/` folder remains in `Backoffice/`
- Handler does not manually call `SaveChangesAsync()` (Wolverine auto-transaction)
- All alert acknowledgment tests pass

---

### Part 2: BO-1 — Backoffice.Api Folder Restructure
**Goal:** Restructure Commands/ + Queries/ → feature-named folders

**Target Structure:**
```
src/Backoffice/Backoffice.Api/
├── AlertManagement/
│   └── AcknowledgeAlertEndpoint.cs
├── OrderManagement/
│   ├── CancelOrder.cs
│   ├── SearchOrders.cs
│   └── GetOrderDetailView.cs
├── OrderNotes/
│   ├── AddOrderNoteEndpoint.cs
│   ├── EditOrderNoteEndpoint.cs
│   ├── DeleteOrderNoteEndpoint.cs
│   └── GetOrderNotes.cs
├── ReturnManagement/
│   ├── ApproveReturn.cs
│   ├── DenyReturn.cs
│   ├── GetReturns.cs
│   └── GetReturnDetails.cs
├── CustomerService/
│   ├── GetCustomerServiceView.cs
│   └── GetCorrespondenceHistory.cs
├── DashboardReporting/
│   ├── GetDashboardSummary.cs
│   └── GetDashboardMetrics.cs
├── WarehouseOperations/
│   ├── GetAlertFeed.cs
│   ├── GetLowStockAlerts.cs
│   ├── GetStockLevel.cs
│   ├── GetInventoryList.cs
│   ├── AdjustInventoryProxy.cs
│   └── ReceiveStockProxy.cs
└── ProductCatalog/
    └── GetProductList.cs
```

**Tasks:**
1. Create all feature-named folders
2. Move each file to its appropriate folder (one commit per capability group)
3. Update namespace declarations (no need — .Api endpoints can keep existing namespaces)
4. Delete empty `Commands/` and `Queries/` folders
5. Verify build succeeds after each move
6. Run integration tests after all moves

**Commit Strategy (7 commits):**
1. AlertManagement/ move (already done in Part 1)
2. OrderManagement/ move
3. OrderNotes/ move
4. ReturnManagement/ move
5. CustomerService/ move
6. DashboardReporting/ move
7. WarehouseOperations/ + ProductCatalog/ move (combined)

---

### Part 3: BO-3 — Projection Colocation
**Goal:** Move projections from Projections/ to feature-named folders

**Target Structure:**
```
src/Backoffice/Backoffice/
├── AlertManagement/
│   ├── AcknowledgeAlert.cs
│   ├── AlertFeedView.cs
│   └── AlertFeedViewProjection.cs
├── DashboardReporting/
│   ├── AdminDailyMetrics.cs
│   ├── AdminDailyMetricsProjection.cs
│   ├── CorrespondenceMetricsView.cs
│   ├── CorrespondenceMetricsViewProjection.cs
│   ├── FulfillmentPipelineView.cs
│   ├── FulfillmentPipelineViewProjection.cs
│   ├── ReturnMetricsView.cs
│   └── ReturnMetricsViewProjection.cs
```

**Tasks:**
1. Create `DashboardReporting/` folder in `Backoffice/`
2. Move `AdminDailyMetrics*` files to `DashboardReporting/`
3. Move `CorrespondenceMetricsView*` files to `DashboardReporting/`
4. Move `FulfillmentPipelineView*` files to `DashboardReporting/`
5. Move `ReturnMetricsView*` files to `DashboardReporting/`
6. Move `AlertFeedView*` files to `AlertManagement/`
7. Update namespace declarations: `Backoffice.Projections` → `Backoffice.DashboardReporting` or `Backoffice.AlertManagement`
8. Delete empty `Projections/` folder
9. Verify build succeeds
10. Run integration tests (specifically EventDrivenProjectionTests)

**Commit Strategy (3 commits):**
1. Move dashboard projections to DashboardReporting/
2. Move alert projections to AlertManagement/
3. Delete empty Projections/ folder

---

## Exit Criteria

Phase 5 Session 1 is complete when ALL of the following are true:

1. ✅ XC-3: AcknowledgeAlert handler no longer manually calls SaveChangesAsync()
2. ✅ BO-2: AcknowledgeAlert moved to AlertManagement/ folder
3. ✅ BO-1: All Backoffice.Api files reorganized into feature-named folders
4. ✅ BO-3: All projections colocated with their capabilities
5. ✅ No Commands/ or Queries/ folders remain in Backoffice.Api/
6. ✅ No Projections/ folder remains in Backoffice/
7. ✅ Build: 0 errors, 36 warnings (unchanged from Session 12)
8. ✅ All previously-passing tests still pass (91 Backoffice.Api.IntegrationTests)
9. ✅ Session retrospective created
10. ✅ CURRENT-CYCLE.md updated

---

## Risk Mitigation

### Known Risks
1. **Namespace changes may break imports:**
   - Mitigation: Update all using statements after each move
   - Use compiler errors as guide

2. **Projection namespace changes may break Program.cs registration:**
   - Mitigation: Check Program.cs after projection moves
   - Look for explicit type references in projection registration

3. **Test fixture may cache old namespace locations:**
   - Mitigation: Run `dotnet clean` before testing
   - Rebuild after all moves

### Pre-existing Issues to Document
- 36 warnings baseline (nullable warnings, unused variables)
- These are infrastructure issues, not refactoring-related

---

## Session Duration Estimate

**Total:** 2-3 hours (Session 1 of Phase 5)

**Breakdown:**
- Part 1 (XC-3 + BO-2): 20 minutes
- Part 2 (BO-1): 60 minutes (7 commits)
- Part 3 (BO-3): 40 minutes (3 commits)
- Testing + Documentation: 40 minutes

---

## Success Indicators

- All Phase 5 work items complete (or Session 1 portion complete)
- Build succeeds with 0 errors
- Pre-existing warning count unchanged (36 warnings)
- Backoffice follows same vertical slice patterns as Returns BC and Vendor Portal
- All integration tests passing
- Session retrospective documents learnings and patterns

---

## References

- **M33.0 Proposal:** `docs/planning/milestones/m33-m34-engineering-proposal-2026-03-21.md`
- **ADR 0039:** `docs/decisions/0039-canonical-validator-placement.md`
- **Vertical Slice Skill:** `docs/skills/vertical-slice-organization.md`
- **Session 12 Retrospective:** `docs/planning/milestones/m33-0-session-12-retrospective.md`
