# M33.0 Session 10 Plan: Phase 3 — Returns BC Full Structural Refactor (Part 1)

**Session:** 10
**Date:** 2026-03-23
**Type:** Phase 3 — Returns BC Full Structural Refactor (Sessions 10-13)
**Estimate:** 4-6 sessions total (this is session 1 of 4-6)

---

## Context

Phase 2 (Quick Wins) completed in Session 9 with all 9 items delivered. Now beginning Phase 3 (Returns BC Full Structural Refactor) with 8 items (R-1 through R-7 + F-7).

**Critical Constraint:** R-2 (ReturnEvents.cs explosion) MUST be combined with UXE event renames in a single atomic commit to avoid event deserialization breakage.

---

## Phase 3 Scope (Sessions 10-13)

From M33-M34 Engineering Proposal, Phase 3 includes:

| Item | Finding | Effort | Status |
|------|---------|--------|--------|
| **R-4** | Explode `ReturnCommandHandlers.cs` (387 lines, 5 handlers) → 5 files | M | Session 10 |
| **R-1** | Explode `ReturnCommands.cs` (11 commands) → 11 files with validators + handlers | M | Session 10-11 |
| **R-2** | Explode `ReturnEvents.cs` + UXE renames (ATOMIC) | M | Session 11-12 |
| **R-3** | Dissolve `ReturnValidators.cs` bulk file | S | Session 11 |
| **R-5** | Explode `ReturnQueries.cs` → 2 files | S | Session 12 |
| **R-6** | Add validators to 4 high-risk commands | S | Session 12 |
| **F-7** | Add `TrackedHttpCall()` to Returns.Api.IntegrationTests | S | Session 13 |
| **R-7** | Rename `Returns/Returns/Returns/` → `Returns/Returns/ReturnProcessing/` | S | Session 13 |

---

## Session 10 Goals

### Primary Goal: Start with R-4 (Highest Merge Conflict Risk)

**R-4: Explode `ReturnCommandHandlers.cs` (387 lines, 5 handlers) → 5 files**

The proposal explicitly calls out: *"Start here — highest merge-conflict risk file in the codebase"*

Current file contains 5 handlers:
1. `ApproveReturnHandler` (64 lines)
2. `DenyReturnHandler` (~50 lines)
3. `ReceiveReturnHandler` (~50 lines)
4. `StartInspectionHandler` (~50 lines)
5. `SubmitInspectionHandler` (~170 lines)

**Strategy:**
- Extract each handler to its own file
- Keep handlers separate from commands initially (R-1 will merge them later)
- Verify all tests pass after each extraction
- Commit frequently

### Secondary Goal: Begin R-1 (Command Explosion)

**R-1: Explode `ReturnCommands.cs` (11 commands) → 11 files**

Current file contains 11 command records:
1. `RequestReturn` + `RequestReturnItem` + `RequestReturnExchangeRequest`
2. `ApproveReturn`
3. `DenyReturn`
4. `ReceiveReturn`
5. `StartInspection`
6. `SubmitInspection` + `InspectionLineResult`
7. `ExpireReturn`
8. `ApproveExchange`
9. `DenyExchange`
10. `ShipReplacementItem`

**Strategy (for Session 10):**
- Start with commands that already have validators (easy wins)
- Follow ADR 0039 pattern: command + validator + handler in single file
- Target commands for Session 10:
  1. `RequestReturn` (already has `RequestReturnHandler.cs` separate file)
  2. `DenyReturn` (already has validator in `ReturnValidators.cs`)
  3. `SubmitInspection` (already has validator in `ReturnValidators.cs`)

**Defer to Session 11:**
- Commands that don't have validators yet (R-6 will add them)
- Commands with complex dependencies

---

## Session 10 Sequencing

### Batch 1: R-4 Handler Extractions (5 commits)

1. Extract `ApproveReturnHandler` → `ApproveReturnHandler.cs`
2. Extract `DenyReturnHandler` → `DenyReturnHandler.cs`
3. Extract `ReceiveReturnHandler` → `ReceiveReturnHandler.cs`
4. Extract `StartInspectionHandler` → `StartInspectionHandler.cs`
5. Extract `SubmitInspectionHandler` → `SubmitInspectionHandler.cs`
6. Delete `ReturnCommandHandlers.cs`

**Verification:** Build + test after each extraction

### Batch 2: R-1 Command + Validator + Handler Colocation (3 commands)

Start with the 3 easiest commands that already have validators:

1. **`DenyReturn.cs`:**
   - Move command from `ReturnCommands.cs`
   - Move validator from `ReturnValidators.cs`
   - Move handler from `DenyReturnHandler.cs` (just created)
   - Result: Single file with command + validator + handler

2. **`SubmitInspection.cs`:**
   - Move command + `InspectionLineResult` from `ReturnCommands.cs`
   - Move validator from `ReturnValidators.cs`
   - Move handler from `SubmitInspectionHandler.cs` (just created)
   - Result: Single file with command + validator + handler

3. **`RequestReturn.cs`:**
   - Move command + related types from `ReturnCommands.cs`
   - Move validator from `ReturnValidators.cs`
   - Handler already in separate `RequestReturnHandler.cs` — merge it
   - Result: Single file with command + validator + handler

**Verification:** Build + test after each command

**Defer to Session 11:**
- Remaining 8 commands (will require adding validators per R-6)

---

## Success Criteria

1. ✅ `ReturnCommandHandlers.cs` deleted (5 handlers extracted)
2. ✅ 3 commands moved to vertical slice files (command + validator + handler)
3. ✅ Build: 0 errors
4. ✅ Tests: All passing
5. ✅ Commits: Frequent (8-10 expected)

---

## UXE Event Renames (Planned for Session 11-12)

**Critical:** These renames MUST be atomic with R-2 (ReturnEvents.cs explosion).

From M33-M34 proposal:
- `ReturnRequested` → `ReturnInitiated`
- `ReturnDenied` → `ReturnFailedInspection` ⚠️ **WAIT** — this conflicts with existing usage
- `ExchangeRejected` → `ExchangeFailedInspection`
- `InspectionMixed` → `InspectionPartiallyPassed`

**Note:** Will verify `ReturnDenied` usage before renaming in Session 11-12. Current code shows `ReturnDenied` is used for CS agent denial (not inspection failure), so rename target needs clarification.

---

## Risk Mitigation

1. **Merge Conflicts:** Starting with R-4 first (per proposal) minimizes risk to other Phase 3 items
2. **Test Stability:** Verifying tests after each extraction prevents cascading failures
3. **Atomic Commits:** Small commits allow easy rollback if needed
4. **UXE Rename Coordination:** Deferring R-2 + renames to Session 11-12 gives time to verify rename targets

---

## References

- **M33+M34 Proposal:** `docs/planning/milestones/m33-m34-engineering-proposal-2026-03-21.md`
- **ADR 0039:** `docs/decisions/0039-canonical-validator-placement.md`
- **Session 9 Retrospective:** `docs/planning/milestones/m33-0-session-9-retrospective.md`
- **CURRENT-CYCLE.md:** `docs/planning/CURRENT-CYCLE.md`

---

*Plan Created: 2026-03-23*
*Status: Ready to Execute*
