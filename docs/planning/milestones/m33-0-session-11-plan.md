# M33.0 Session 11 Plan: Phase 3 — Returns BC Full Structural Refactor (Part 2)

**Session:** 11
**Date:** 2026-03-23
**Type:** Phase 3 Implementation — Continuing Returns BC Structural Refactoring
**Parent Milestone:** M33.0 — Code Correction + Broken Feedback Loop Repair

---

## Session Context

**Previous Session (10) Completion:**
- ✅ R-4: `ReturnCommandHandlers.cs` exploded (5 handlers extracted)
- ✅ R-1 (3/11): Created vertical slices for `DenyReturn`, `SubmitInspection`, `RequestReturn`
- ✅ `ReturnValidators.cs` now empty (only using statement + namespace)
- ✅ Build: 0 errors, 0 warnings

**Current State:**
- `ReturnCommands.cs`: 8 commands remaining (ApproveReturn, ReceiveReturn, StartInspection, ExpireReturn, ApproveExchange, DenyExchange, ShipReplacementItem + 1 already moved)
- `ReturnValidators.cs`: Empty (3 lines — using + namespace)
- Individual handler files: 8 files (ApproveReturnHandler.cs, ReceiveReturnHandler.cs, StartInspectionHandler.cs, ExpireReturnHandler.cs, ApproveExchangeHandler.cs, DenyExchangeHandler.cs, ShipReplacementItemHandler.cs, ShipmentDeliveredHandler.cs)
- Individual vertical slice files: 3 files (DenyReturn.cs, SubmitInspection.cs, RequestReturn.cs)

---

## Goals for Session 11

### Primary Goals

**1. R-1 (Complete): Create vertical slices for remaining 7 commands (8 total - 1 already moved)**

Commands to convert to vertical slices:
1. ✅ DenyReturn (already done in Session 10)
2. ✅ SubmitInspection (already done in Session 10)
3. ✅ RequestReturn (already done in Session 10)
4. 📋 ApproveReturn (no validator → add per ADR 0039)
5. 📋 ReceiveReturn (no validator → add per ADR 0039)
6. 📋 StartInspection (no validator → add per ADR 0039)
7. 📋 ExpireReturn (no validator → add per ADR 0039)
8. 📋 ApproveExchange (no validator → add per ADR 0039)
9. 📋 DenyExchange (has validator → reuse DenyReturn pattern)
10. 📋 ShipReplacementItem (no validator → add per ADR 0039)
11. 📋 ShipmentDelivered (integration handler — leave as-is for now)

**Strategy for commands without validators:**
- Follow ADR 0039 pattern: add minimal validators with basic guards
- ApproveReturn: Validate ReturnId is not empty
- ReceiveReturn: Validate ReturnId is not empty
- StartInspection: Validate ReturnId is not empty, InspectorId is not empty
- ExpireReturn: Validate ReturnId is not empty
- ApproveExchange: Validate ReturnId is not empty
- DenyExchange: Validate ReturnId is not empty, Reason is not empty, Message is not empty
- ShipReplacementItem: Validate ReturnId is not empty, ShipmentId is not empty, TrackingNumber is not empty

**2. R-3 (Complete): Delete `ReturnValidators.cs`**
- Already empty (3 lines)
- Safe to delete after R-1 completes

### Secondary Goals (Time Permitting)

**3. R-2 Preparation: Analyze ReturnEvents.cs structure**
- Map all events to their new filenames
- Identify rename targets (from Session 10 retrospective and M33 proposal):
  - `ReturnDenied` (verify usage)
  - Document shared type dependencies (`InspectionLineResult`)

### Defer to Session 12

- R-2 + UXE execution (atomic commit with event renames)
- R-5 (ReturnQueries.cs explosion)
- R-6 (Add validators to high-risk commands — overlaps with this session)

---

## Execution Sequence

### Commit 1: ApproveReturn vertical slice
1. Create `ApproveReturn.cs` with command + validator + handler
2. Remove `ApproveReturn` from `ReturnCommands.cs`
3. Delete `ApproveReturnHandler.cs`
4. Build and verify (0 errors)

### Commit 2: ReceiveReturn vertical slice
1. Create `ReceiveReturn.cs` with command + validator + handler
2. Remove `ReceiveReturn` from `ReturnCommands.cs`
3. Delete `ReceiveReturnHandler.cs`
4. Build and verify (0 errors)

### Commit 3: StartInspection vertical slice
1. Create `StartInspection.cs` with command + validator + handler
2. Remove `StartInspection` from `ReturnCommands.cs`
3. Delete `StartInspectionHandler.cs`
4. Build and verify (0 errors)

### Commit 4: ExpireReturn vertical slice
1. Create `ExpireReturn.cs` with command + validator + handler
2. Remove `ExpireReturn` from `ReturnCommands.cs`
3. Delete `ExpireReturnHandler.cs`
4. Build and verify (0 errors)

### Commit 5: ApproveExchange vertical slice
1. Create `ApproveExchange.cs` with command + validator + handler
2. Remove `ApproveExchange` from `ReturnCommands.cs`
3. Delete `ApproveExchangeHandler.cs`
4. Build and verify (0 errors)

### Commit 6: DenyExchange vertical slice
1. Create `DenyExchange.cs` with command + validator + handler
2. Remove `DenyExchange` from `ReturnCommands.cs`
3. Delete `DenyExchangeHandler.cs`
4. Build and verify (0 errors)

### Commit 7: ShipReplacementItem vertical slice
1. Create `ShipReplacementItem.cs` with command + validator + handler
2. Remove `ShipReplacementItem` from `ReturnCommands.cs`
3. Delete `ShipReplacementItemHandler.cs`
4. Build and verify (0 errors)

### Commit 8: Delete ReturnCommands.cs and ReturnValidators.cs (R-3 Complete)
1. Verify `ReturnCommands.cs` is empty (or only contains comments/namespace)
2. Delete `ReturnCommands.cs`
3. Delete `ReturnValidators.cs`
4. Build and verify (0 errors)

---

## Success Criteria

1. ✅ All 7 remaining commands converted to vertical slice files
2. ✅ Each vertical slice file contains: command + validator (per ADR 0039) + handler
3. ✅ `ReturnCommands.cs` deleted
4. ✅ `ReturnValidators.cs` deleted
5. ✅ Build succeeds with 0 errors, 0 warnings
6. ✅ R-1 and R-3 marked as complete in Phase 3 progress tracking

---

## Risk Mitigation

**Risk 1: Missing using statements**
- Mitigation: Copy all using statements from handler files to new vertical slice files
- Verification: Build after each commit

**Risk 2: Shared types between commands**
- Discovery from Session 10: `InspectionLineResult` is shared (already in SubmitInspection.cs)
- Mitigation: Leave shared types in ReturnEvents.cs for now (handle in R-2)

**Risk 3: Pre-existing test failures**
- From Session 10: 14 test failures due to auth issues (not refactoring-related)
- Mitigation: Focus on build success (0 errors) as primary validation signal

---

## References

- **ADR 0039:** `docs/decisions/0039-canonical-validator-placement.md`
- **Session 10 Retrospective:** `docs/planning/milestones/m33-0-session-10-retrospective.md`
- **M33+M34 Proposal:** `docs/planning/milestones/m33-m34-engineering-proposal-2026-03-21.md`
- **CURRENT-CYCLE:** `docs/planning/CURRENT-CYCLE.md`

---

*Plan Created: 2026-03-23*
*Ready for Execution*
