# M33.0 Session 9 Plan: Phase 2 Quick Wins Batch

**Session:** 9
**Date:** 2026-03-23
**Status:** 🚀 ACTIVE
**Type:** Phase 2 — Quick Wins Batch (Structural Refactors)

---

## Overview

Session 9 targets **Phase 2: Quick Wins Batch** from the M33-M34 Engineering Proposal. All items in this phase are:
- **Independent** — No dependencies between items
- **Small effort** — S (small) complexity per item
- **Structural only** — File moves, consolidations, splits (no logic changes)
- **ADR-compliant** — Follow ADR 0039 validator placement pattern (established in Session 8)

**Phase 1 Prerequisites Met:**
- ✅ ADR 0039 published (canonical validator placement)
- ✅ CheckoutCompleted collision fixed
- ✅ Build: 0 errors, all tests passing

**Parallelizable:** All items can be implemented in any order or in parallel.

---

## Session 9 Scope

### In Scope (All Phase 2 Items)

| Item | Finding | Effort | Description |
|------|---------|--------|-------------|
| **INV-1** | Inventory `AdjustInventory*` shatter | S | Consolidate 4 files → `AdjustInventory.cs` (command + validator + handler) |
| **INV-2** | Inventory `ReceiveInboundStock*` shatter | S | Consolidate split files → `ReceiveInboundStock.cs` |
| **INV-3** | Inventory folder rename | S | Rename `Commands/` + `Queries/` → feature-named folders |
| **PR-1** | Pricing three-way split | S | Merge `SetInitialPrice` + `ChangePrice` splits → one file per command |
| **CO-1** | Correspondence bulk events | S | Explode `MessageEvents.cs` → 4 individual event files |
| **PAY-1** | Payments isolated Queries | S | Move `Queries/GetPaymentsForOrder.cs` → `OrderPayments/` |
| **FUL-1** | Fulfillment isolated Queries | S | Move `Queries/GetShipmentsForOrder.cs` → `OrderFulfillment/` |
| **ORD-1** | Orders isolated Queries | S | Move `Queries/GetReturnableItems.cs` → `Returns/` |
| **F-9** | Orders raw string literals | S | Fix 3 collection attributes → `[Collection(IntegrationTestCollection.Name)]` |

**Total:** 9 items, all Size S

---

## Out of Scope

**Explicitly NOT in Session 9:**
- Phase 3: Returns BC full structural refactor (R-1 through R-7) — **Sessions 10-13**
- Phase 4: Vendor Portal structural refactor (VP-1 through VP-6) — **Sessions 13-15**
- Phase 5: Backoffice folder restructure (BO-1/BO-2/BO-3) — **Sessions 15-16**
- Phase 6: Missing projections + missing pages — **Sessions 16-19**

**Rationale:** Phase 2 items are quick, independent wins that set up folder conventions before larger refactors. Keeping session scope tight ensures high confidence and rapid iteration.

---

## Implementation Strategy

### Approach: Sequential Micro-Batches

Rather than trying to parallelize all 9 items, implement in **sequential micro-batches** by BC:

**Batch 1: Inventory BC (INV-1 + INV-2 + INV-3)**
1. Consolidate `AdjustInventory*` 4-file shatter → `AdjustInventory.cs`
2. Consolidate `ReceiveInboundStock*` split → `ReceiveInboundStock.cs`
3. Rename `Commands/` → `InventoryManagement/`, `Queries/` → `StockQueries/`
4. Build + test verification
5. Commit: "INV-1/2/3: Consolidate Inventory validators + rename folders"

**Batch 2: Pricing BC (PR-1)**
1. Merge `SetInitialPrice` split → `SetInitialPrice.cs`
2. Merge `ChangePrice` split → `ChangePrice.cs`
3. Build + test verification
4. Commit: "PR-1: Merge Pricing validator splits"

**Batch 3: Correspondence BC (CO-1)**
1. Explode `MessageEvents.cs` → 4 individual event files
2. Build + test verification
3. Commit: "CO-1: Explode Correspondence bulk events file"

**Batch 4: Isolated Queries (PAY-1 + FUL-1 + ORD-1)**
1. Move `Payments/Queries/GetPaymentsForOrder.cs` → `Payments/OrderPayments/`
2. Move `Fulfillment/Queries/GetShipmentsForOrder.cs` → `Fulfillment/OrderFulfillment/`
3. Move `Orders/Queries/GetReturnableItems.cs` → `Orders/Returns/`
4. Build + test verification
5. Commit: "PAY-1/FUL-1/ORD-1: Move isolated Queries to feature-named folders"

**Batch 5: Test Fix (F-9)**
1. Fix 3 raw string literals in Orders.Api.IntegrationTests
2. Build + test verification
3. Commit: "F-9: Fix Orders test collection attributes"

**Benefits:**
- Smaller commits (easier to review, easier to revert)
- Per-BC verification (isolated blast radius)
- Clear narrative (git history tells story of refactor)

---

## Exit Criteria

Session 9 is complete when **all** of these are true:

1. ✅ **INV-1:** `AdjustInventory*` 4-file shatter consolidated → `AdjustInventory.cs`
2. ✅ **INV-2:** `ReceiveInboundStock*` split consolidated → `ReceiveInboundStock.cs`
3. ✅ **INV-3:** Inventory `Commands/` + `Queries/` folders renamed to feature-named folders
4. ✅ **PR-1:** Pricing `SetInitialPrice` + `ChangePrice` splits merged to one file per command
5. ✅ **CO-1:** Correspondence `MessageEvents.cs` exploded → 4 individual event files
6. ✅ **PAY-1:** Payments `Queries/GetPaymentsForOrder.cs` moved to feature folder
7. ✅ **FUL-1:** Fulfillment `Queries/GetShipmentsForOrder.cs` moved to feature folder
8. ✅ **ORD-1:** Orders `Queries/GetReturnableItems.cs` moved to feature folder
9. ✅ **F-9:** Orders test collection attributes fixed (3 raw string literals)
10. ✅ **Build:** 0 errors (36 pre-existing warnings acceptable)
11. ✅ **Tests:** All previously passing tests still pass (no regressions)
12. ✅ **Verification:** All affected BCs build and test independently
13. ✅ **Documentation:** Session 9 retrospective written
14. ✅ **CURRENT-CYCLE.md:** Updated to reflect Phase 2 completion

---

## Verification Steps (Per Batch)

After each batch:
1. Run `dotnet build` from repo root (verify 0 errors)
2. Run `dotnet test` for affected BC (verify no regressions)
3. Search for old patterns with `rg` (verify cleanup complete)
4. Commit changes with descriptive message

Final verification (after all batches):
1. Run `dotnet build` from repo root
2. Run `dotnet test` from repo root (full test suite)
3. Review git diff (ensure only structural changes, no logic)

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| **File move breaks namespaces** | Verify `namespace` declarations match new folder structure |
| **Test references break** | Run affected BC test suite after each batch |
| **Merge conflicts with other work** | Keep batches small, commit frequently |
| **Validator placement diverges from ADR 0039** | Reference ADR during implementation, verify colocation |

---

## Expected Outcomes

**Before Session 9:**
- Inventory BC: 4-file command shatter (ADR 0039 anti-pattern)
- Pricing BC: 3-way validator splits (ADR 0039 anti-pattern)
- Correspondence BC: Bulk events file (merge conflict risk)
- Multiple BCs: Isolated Queries folder (technical layer anti-pattern)
- Orders tests: Raw string collection literals (typo risk)

**After Session 9:**
- ✅ All commands follow ADR 0039 (command + validator + handler in one file)
- ✅ All events in individual files (one per event)
- ✅ All queries in feature-named folders (not technical layer)
- ✅ All tests use typed collection attributes
- ✅ Phase 2 complete (9/9 items)

---

## Session Estimate

**Total Effort:** ~3-4 hours
- Batch 1 (Inventory): 60-90 minutes
- Batch 2 (Pricing): 30 minutes
- Batch 3 (Correspondence): 20 minutes
- Batch 4 (Isolated Queries): 30 minutes
- Batch 5 (Test Fix): 10 minutes
- Final verification: 15 minutes
- Retrospective: 30 minutes

---

## References

- **M33+M34 Engineering Proposal:** `docs/planning/milestones/m33-m34-engineering-proposal-2026-03-21.md` (Phase 2, lines 77-87)
- **ADR 0039:** `docs/decisions/0039-canonical-validator-placement.md` (validator placement canonical pattern)
- **Session 8 Retrospective:** `docs/planning/milestones/m33-0-session-8-retrospective.md` (Phase 1 completion)
- **Codebase Audit 2026-03-21:** `docs/audits/CODEBASE-AUDIT-2026-03-21.md` (INV-1, INV-2, PR-1, CO-1 findings)
- **CURRENT-CYCLE.md:** `docs/planning/CURRENT-CYCLE.md` (Phase 2 tracking)

---

## Next Steps After Session 9

**Phase 3 Preview (Returns BC Full Structural Refactor):**
- R-4: Explode `ReturnCommandHandlers.cs` (387 lines, 5 handlers)
- R-1: Explode `ReturnCommands.cs` (11 commands) → 11 files
- R-2: Explode `ReturnEvents.cs` + UXE renames (ONE PR)
- R-3: Dissolve `ReturnValidators.cs` bulk file
- R-5: Explode `ReturnQueries.cs` → 2 files
- R-6: Add validators to 4 high-risk commands
- F-7: Add `TrackedHttpCall()` to Returns.Api.IntegrationTests
- R-7: Rename `Returns/Returns/Returns/` folder

**Estimate:** 4-6 sessions (R-2 + UXE renames must be atomic)

---

*Session 9 Plan Created: 2026-03-23*
*Status: Ready for execution*
