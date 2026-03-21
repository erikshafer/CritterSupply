# M33.0 Session 1 Plan — Code Correction + Broken Feedback Loop Repair

**Date:** 2026-03-21
**Status:** ✅ COMPLETE
**Session Goal:** Fix INV-3 and F-8, create session planning infrastructure

---

## Session Context

This is a continuation/recovery session. The previous M33.0 attempt (commits 641fc93 and 3b413f4) introduced bugs in the `AdjustInventoryEndpoint` while attempting to fix INV-3. The endpoint now dispatches through Wolverine but validation failures aren't being handled correctly, causing 4 test failures.

**Previous session discoveries:**
- INV-3 fix approach: Endpoint should dispatch via `IMessageBus.InvokeAsync()` to ensure integration messages go through Wolverine's transactional outbox
- Handler returns `OutgoingMessages` with `InventoryAdjusted` and `LowStockDetected` integration messages
- F-8 completed: `BackofficeTestFixture` now has `ExecuteAndWaitAsync()` and `TrackedHttpCall()` methods
- **Issue:** Validation in `Before()` method doesn't prevent `Handle()` from running when dispatching via `IMessageBus.InvokeAsync()`

---

## Troubleshooting Analysis

The core issue is that Wolverine's compound handler workflow (Load → Before → Validate → Handle) doesn't work as expected when using `IMessageBus.InvokeAsync()` with manual `session.Events.Append()`.

**Two possible fixes:**
1. **Option A (Simpler):** Revert endpoint to manual event appending + validation, but add explicit integration message publishing via `IMessageBus.PublishAsync()`
2. **Option B (Cleaner):** Fix the handler to use Wolverine's aggregate workflow properly (remove manual Load/Append, use `[WriteAggregate]`)

**Decision:** Go with Option A for now. It's lower risk and achieves the INV-3 goal (integration messages reach RabbitMQ).

---

## Session Plan

### Phase 1: Fix INV-3 (Revert + Simpler Approach) - ✅ COMPLETE
- [x] Revert `AdjustInventoryEndpoint` to original manual approach (validation + event appending)
- [x] Keep the integration message publishing logic in the handler
- [x] Add explicit `IMessageBus.PublishAsync()` calls in the endpoint to publish integration messages
- [x] Verify all 48 Inventory tests pass

### Phase 2: Document Learnings - ✅ COMPLETE
- [x] Create `m33-0-session-1-retrospective.md` documenting:
  - Why the IMessageBus.InvokeAsync() approach didn't work
  - The simpler fix that was chosen
  - Lessons learned about Wolverine compound handlers vs manual event appending
- [x] Update CURRENT-CYCLE.md with M33.0 status

### Phase 3: Verify F-8 Still Works - ✅ COMPLETE
- [x] Run Backoffice tests to verify F-8 fix (ExecuteAndWaitAsync) is still working
- [x] Document that F-8 is complete and independent of INV-3 fix approach

---

## Exit Criteria

- ✅ All 48 Inventory.Api.IntegrationTests passing
- ✅ `AdjustInventory` handler publishes `InventoryAdjusted` integration message
- ✅ `AdjustInventory` handler detects low-stock threshold crossing and publishes `LowStockDetected`
- ✅ Integration messages go through Wolverine's transactional outbox (not bypassed)
- ✅ Retrospective document created
- ✅ CURRENT-CYCLE.md updated

---

## Notes

- The previous approach (dispatching through IMessageBus) was architecturally cleaner but introduced complexity
- The simpler approach (manual appending + explicit publishing) achieves the same goal with less risk
- Future improvement: Consider moving to full Wolverine aggregate workflow when we have time for proper testing
