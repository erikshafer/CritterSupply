# M33.0 Session 12 Plan — Phase 3 Completion (R-5 through R-8)

**Date:** 2026-03-23
**Phase:** M33.0 Phase 3 (Returns BC Structural Refactor)
**Branch:** `claude/phase-3-session-r5-r8`

---

## Session Objectives

Complete the remaining Phase 3 items from the Returns BC structural refactor:

- **R-5**: Create `Returns.Api/Queries/` folder and explode `ReturnQueries.cs` → 2 individual query files
- **R-6**: ✅ SKIP (already completed in Session 11 - validators added to all command files)
- **R-7**: Create `Returns/Integration/` folder and move integration handler
- **R-8**: Rename `Returns/Returns/Returns/` → `Returns/Returns/ReturnProcessing/`

---

## Prerequisites (Session 11 Status)

✅ **Complete:**
- R-1: All 11 command handlers migrated to vertical slices
- R-3: Bulk files (`ReturnCommands.cs`, `ReturnValidators.cs`) deleted
- R-4: `ReturnCommandHandlers.cs` exploded (from Session 10)

✅ **Build Status:**
- 0 errors, 36 pre-existing warnings (unchanged from Session 10)
- All business logic preserved exactly

---

## Work Items

### R-5: Query Handler Vertical Slices

**Current state:** `ReturnQueries.cs` contains 2 query handlers + shared response DTO

**Target state:**
```
src/Returns/Returns.Api/
├── Queries/
│   ├── GetReturn.cs              # Query, handler, response DTO
│   └── GetReturnsForOrder.cs     # Query, handler, response DTO
```

**Pattern:**
- Each file contains: handler class with `[WolverineGet]` endpoint
- Shared response DTO (`ReturnSummaryResponse`) moves to first file (GetReturn.cs)
- Handler classes remain static (no instance state)
- Authorization attributes preserved (`[Authorize(Policy = "CustomerService")]`)

**Files:**
1. Create `src/Returns/Returns.Api/Queries/GetReturn.cs`
   - Move `GetReturnHandler` class
   - Move `ReturnSummaryResponse` record
   - Move `ReturnLineItemResponse` record (if exists)
   - Preserve `ToResponse()` helper method

2. Create `src/Returns/Returns.Api/Queries/GetReturnsForOrder.cs`
   - Move `GetReturnsForOrderHandler` class
   - Add reference to `GetReturnHandler.ToResponse()` from sibling file

3. Delete `src/Returns/Returns/Returns/ReturnQueries.cs`

---

### R-6: Validator Addition (Session 11 Completion)

**Status:** ✅ **COMPLETE** (Session 11)

Per Session 11 retrospective:
- All 11 command handlers now have validators following ADR 0039
- No additional validator work required for R-6

---

### R-7: Integration Handler Organization

**Current state:** Integration handler lives in main domain folder

**Target state:**
```
src/Returns/Returns/
├── ReturnProcessing/      # (still named Returns/ temporarily)
│   └── [all command handlers, aggregate, events]
├── Integration/
│   └── ShipmentDelivered.cs
```

**Pattern:**
- Create `Integration/` folder at `src/Returns/Returns/Integration/`
- Move `ShipmentDeliveredHandler.cs` → `ShipmentDelivered.cs`
- Update namespace from `Returns.Returns` → `Returns.Integration`
- Preserve handler logic exactly (idempotency check, document store)

**Files:**
1. Create `src/Returns/Returns/Integration/ShipmentDelivered.cs`
   - Copy handler from `ShipmentDeliveredHandler.cs`
   - Update namespace to `Returns.Integration`
   - Rename static class to `ShipmentDeliveredHandler` (consistent with handler pattern)

2. Delete `src/Returns/Returns/Returns/ShipmentDeliveredHandler.cs`

---

### R-8: Folder Rename

**Current state:** `src/Returns/Returns/Returns/` (triple nesting)

**Target state:** `src/Returns/Returns/ReturnProcessing/`

**Why "ReturnProcessing"?**
- Distinguishes from "Returns" bounded context name
- Aligns with feature-named folder convention
- Matches capability (return request → approval → inspection → completion)

**Pattern:**
- Use `git mv` for folder rename (preserves history)
- Update namespace declarations in all affected files
- Update using directives in dependent files (tests, API project)
- Verify build after rename

**Files affected:**
- All 11 command vertical slices
- Return aggregate (`Return.cs`)
- Return events (`ReturnEvents.cs`)
- Enums (ReturnStatus, ReturnReason, ReturnType, etc.)
- Value objects (ReturnEligibilityWindow, etc.)

**Steps:**
1. Execute: `git mv "src/Returns/Returns/Returns" "src/Returns/Returns/ReturnProcessing"`
2. Update namespace in all moved files: `namespace Returns.Returns` → `namespace Returns.ReturnProcessing`
3. Update `Returns.Api/Program.cs` if any explicit namespace references exist
4. Update test project using directives
5. Rebuild solution
6. Run Returns integration tests

---

## Sequencing

**Order matters:**
1. R-5 (query handlers) — creates new folder, safe to do first
2. R-7 (integration handlers) — creates new folder, independent of R-5
3. R-8 (folder rename) — MUST be last (affects all files in domain folder)

**Rationale:**
- R-5 and R-7 are independent file moves/creations
- R-8 is a bulk namespace change affecting all domain files
- Doing R-8 last minimizes merge conflicts and simplifies commit history

---

## Exit Criteria

### Build Status
- 0 compilation errors
- 36 pre-existing warnings (unchanged from Session 11)
- No new warnings introduced

### Test Status
- All Returns BC integration tests passing (existing suite)
- No test failures related to namespace changes
- Test discovery succeeds for all test projects

### File Organization
- ✅ Query handlers in `Returns.Api/Queries/`
- ✅ Integration handlers in `Returns/Integration/`
- ✅ Domain logic in `Returns/ReturnProcessing/`
- ✅ No triple-nested `Returns/Returns/Returns/` folder remaining

### Documentation
- Session retrospective documenting patterns and learnings
- CURRENT-CYCLE.md updated with Phase 3 completion status

---

## Risk Mitigation

### Namespace Change Risk (R-8)
**Risk:** Bulk namespace rename could break test discovery or integration message routing

**Mitigation:**
- Build after every namespace change
- Run integration tests immediately after R-8
- Keep R-8 as single atomic commit (easier to revert if issues)

### Merge Conflict Risk
**Risk:** Active development in Returns BC could cause conflicts

**Mitigation:**
- Small, focused commits for R-5 and R-7
- Complete session within 2-3 hours
- Phase 3 work has been coordinated (no parallel Returns BC development)

---

## Success Metrics

- **Commits:** 4-5 (R-5, R-7, R-8 each as 1-2 commits)
- **Files Moved:** ~23 files (2 queries + 1 integration handler + 20 domain files)
- **Build Time:** <2 minutes
- **Test Time:** <30 seconds (Returns integration tests only)
- **Session Duration:** 1.5-2 hours

---

## Notes

**ADR 0039 Compliance:**
- R-5 query handlers follow same single-file pattern as command handlers
- No validators needed for queries (read-only operations)

**Integration Pattern:**
- `ShipmentDeliveredHandler` is the only integration handler in Returns BC
- Future integration handlers (e.g., `StockAvailabilityConfirmedHandler` from proposal R-8) will follow same `Integration/` folder pattern

**Phase 3 Completion:**
- This session completes all 8 Phase 3 items (R-1 through R-8)
- Returns BC will be in full vertical slice conformance after this session
- Next phase (Phase 4) targets Vendor Portal structural refactor (VP-1 through VP-6)

---

*Plan Created: 2026-03-23*
