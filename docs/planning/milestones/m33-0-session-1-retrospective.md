# M33.0 Session 1 Retrospective — INV-3 Fix + Learnings

**Date:** 2026-03-21
**Session Type:** Bug fix + recovery from previous session's regression
**Items Completed:** INV-3 (AdjustInventoryEndpoint bypass), F-8 verification
**Test Results:** ✅ All 48 Inventory tests passing, ✅ All 75 Backoffice tests passing

---

## What We Accomplished

### INV-3: AdjustInventoryEndpoint Integration Message Publishing

**Problem:** The `AdjustInventory` endpoint was bypassing Wolverine's transactional outbox, causing integration messages (`InventoryAdjusted`, `LowStockDetected`) to not reach RabbitMQ reliably.

**Previous Session's Attempt (Failed):**
- Changed `AdjustInventoryHandler.Handle()` to return `OutgoingMessages` with integration messages
- Changed endpoint to dispatch via `IMessageBus.InvokeAsync()` expecting compound handler workflow (Load → Before → Handle) to work properly
- **Result:** 4 test failures - validation in `Before()` method didn't prevent `Handle()` from executing, causing events to be appended even when validation failed

**This Session's Fix (Successful):**
- Reverted to simpler pattern: endpoint does all validation, event appending, and integration message publishing directly
- Endpoint loads aggregate, validates manually, appends domain event via `session.Events.Append()`, saves changes, then publishes integration messages via `IMessageBus.PublishAsync()`
- Handler simplified back to `void Handle()` signature (matches `ReceiveStock` pattern)
- Made `AdjustInventoryHandler.LowStockThreshold` public const and added `CrossedLowStockThreshold()` helper for endpoint use

**Commit:** `524a913` - "M33.0: Fix INV-3 - Revert to manual validation and explicit integration message publishing"

### F-8: BackofficeTestFixture ExecuteAndWaitAsync

**Status:** Already complete from previous session (commit `3b413f4`), verified working via 75 passing Backoffice tests.

---

## Key Learnings

### 1. Wolverine Compound Handler Lifecycle Doesn't Work as Expected with Manual Event Appending

**What We Learned:**
When using the compound handler pattern (Load → Before → Validate → Handle), if the handler manually appends events via `session.Events.Append()` AND returns `OutgoingMessages`, the `Before()` validation doesn't properly prevent `Handle()` from executing when dispatching via `IMessageBus.InvokeAsync()`.

**Why This Happens:**
- The compound handler workflow expects handlers to return events or `IStartStream`, not manually append events
- When mixing manual event appending with `IMessageBus.InvokeAsync()`, Wolverine's internal handler execution flow doesn't respect the `ProblemDetails` return from `Before()`
- This is likely because the handler is being invoked in a way that bypasses the normal HTTP endpoint error handling

**Pattern to Follow Instead:**
For HTTP endpoints that need validation AND integration message publishing:
1. Do validation in the endpoint itself (before dispatching to handler)
2. Append domain event directly in endpoint via `session.Events.Append()` + `SaveChangesAsync()`
3. Publish integration messages explicitly via `IMessageBus.PublishAsync()`

**Alternatively (for pure command handlers):**
Use the full Wolverine aggregate workflow:
- Remove manual `Load()` and `session.Events.Append()`
- Use `[WriteAggregate]` attribute or return `IStartStream` / event objects
- Let Wolverine manage the aggregate loading and event appending

### 2. Simpler is Often Better

**Observation:**
The "architecturally cleaner" approach (dispatching through `IMessageBus.InvokeAsync()` with `OutgoingMessages` return) introduced complexity and broke validation. The simpler approach (manual validation + explicit publishing) achieves the same goal with less risk.

**When to Choose Simplicity:**
- When the "clever" approach requires mixing multiple Wolverine patterns (compound handlers + manual event appending + message publishing)
- When tests are failing and the root cause isn't immediately obvious
- When the HTTP endpoint already has access to everything it needs (session, message bus, aggregate)

**When the Compound Handler Pattern Works Well:**
- Pure command handlers that return events (no HTTP concerns)
- Handlers that use `[WriteAggregate]` or return `IStartStream` (let Wolverine manage event appending)
- Handlers that don't need complex validation logic (rely on FluentValidation only)

### 3. Follow Existing Patterns in the Codebase

**Key Pattern Discovery:**
The `ReceiveStock` handler uses the exact pattern that works:
- `Load()` method loads aggregate
- `Before()` method validates (returns `ProblemDetails` or `WolverineContinue.NoProblems`)
- `Handle()` method returns `void` and appends events via `session.Events.Append()`
- **No integration messages published** - purely domain event appending

**For Integration Messages:**
When a command needs to publish integration messages, the endpoint should handle it explicitly rather than relying on the handler to return `OutgoingMessages`. This keeps the handler pure (domain events only) and the endpoint responsible for cross-BC communication.

### 4. "Old (wrong) approach" / "My fix" Notes Were Clues

**Observation:**
The user mentioned seeing "Old (wrong) approach" and "My fix" notes appearing multiple times in the previous session, indicating troubleshooting attempts that didn't fully work.

**What This Tells Us:**
- The previous session tried multiple approaches to fix the validation issue
- None of the attempted fixes fully resolved the problem (4 tests still failing)
- This session's complete revert to the simpler pattern was the correct fix

**Lesson:**
When troubleshooting leads to multiple "fix attempt" iterations without success, consider whether the fundamental approach is wrong rather than continuing to iterate on a broken pattern.

---

## Technical Debt / Follow-Up Items

### None Identified

The current implementation is clean, all tests pass, and integration messages reach RabbitMQ via the transactional outbox. No follow-up work needed for INV-3 or F-8.

### Future Consideration (Not Blocking)

If we find ourselves needing this pattern (endpoint validation + handler event appending + integration messages) in other BCs, we could:
1. Document this pattern in a skill file (`docs/skills/http-endpoints-with-integration-messages.md`)
2. Create a reusable helper pattern (but only if we see this pattern recurring 3+ times)

For now, the current implementation is sufficient.

---

## Exit Criteria Met

- ✅ All 48 Inventory.Api.IntegrationTests passing
- ✅ `AdjustInventory` handler publishes `InventoryAdjusted` integration message
- ✅ `AdjustInventory` handler detects low-stock threshold crossing and publishes `LowStockDetected`
- ✅ Integration messages go through Wolverine's transactional outbox (not bypassed)
- ✅ All 75 Backoffice tests passing (F-8 verified working)
- ✅ Retrospective document created
- ⏳ CURRENT-CYCLE.md update pending

---

## Summary

This session successfully fixed INV-3 by reverting to a simpler, more reliable pattern. The key insight is that mixing Wolverine's compound handler workflow with manual event appending and integration message publishing doesn't work as expected. The correct pattern for HTTP endpoints that need all three capabilities is to handle validation, event appending, and integration message publishing directly in the endpoint rather than trying to orchestrate it through handler return types.

F-8 was already complete from the previous session and continues to work correctly.

**Next Steps:** Update CURRENT-CYCLE.md to reflect M33.0 Phase 1 completion.
