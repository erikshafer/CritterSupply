# M36.0 Session 2 Retrospective: Track B — Critter Stack Idiom Compliance

**Date:** 2026-03-28
**Focus:** Track B items B-1 through B-4 (Critical + High severity Critter Stack violations)
**Outcome:** All 4 items completed, full solution builds cleanly (0 errors)

---

## Track B Items Completed

### B-1: Payments — `AuthorizePayment.cs` + `PaymentRequested.cs` (🔴 Critical)

**Violation:** Both handlers called `session.Events.StartStream<Payment>()` manually, bypassing
Wolverine's transactional middleware. Events were not enrolled in the transactional outbox.

**Fix:** Replaced manual `session.Events.StartStream()` with `MartenOps.StartStream<Payment>()`
returning `(IStartStream, OutgoingMessages)` tuples. Removed `IDocumentSession session` parameter
since Wolverine now manages persistence via the `IStartStream` return type.

**Matched plan:** Yes — the plan correctly identified both files as having the same root cause
(CS-1 and CS-2). Both handlers had identical patterns: create `PaymentInitiated` event, call
gateway, then `session.Events.StartStream<Payment>()` with two events on each code path (success
and failure). The fix was straightforward since neither handler needed `IDocumentSession` for
anything other than `StartStream`.

**Key observation:** Both handlers have branching logic (success vs failure) that starts the
same stream with different second events. Each branch returns its own `IStartStream` — Wolverine
handles whichever path executes. The `MartenOps.StartStream<Payment>(id, event1, event2)` API
accepts multiple events naturally.

**Reference pattern used:** `CheckoutInitiatedHandler.cs` (Orders BC) — canonical `IStartStream`
return pattern with `using Wolverine.Marten;`.

**Build result:** 0 errors, 0 new warnings.

---

### B-2: Returns — `RequestReturn.cs` (🟡 High)

**Violation:** Handler called `bus.PublishAsync()` for 4 integration events (ExchangeRequested,
ReturnRequested ×2, ReturnApproved), bypassing the transactional outbox.

**Fix:** Replaced all 4 `bus.PublishAsync()` calls with `OutgoingMessages` collection. Changed
return type from `Task<RequestReturnResponse>` to `Task<(RequestReturnResponse, OutgoingMessages)>`.

**`bus.ScheduleAsync()` retained:** Yes, correctly retained. The `IMessageBus` injection remains
solely for `await bus.ScheduleAsync(new ExpireReturn(returnId), shipByDeadline)` — the expiration
timer scheduling. This is the only justified `IMessageBus` use in this handler, as documented
in the plan. `ScheduleAsync` requires `IMessageBus` because it needs delayed delivery semantics
that `OutgoingMessages` does not support.

**Matched plan:** Yes — the plan correctly identified 4 `bus.PublishAsync()` calls. The handler
has three code paths (Exchange → 1 message, Auto-approve → 2 messages, Review → 1 message),
totaling 4 integration messages across all paths. All are now in `OutgoingMessages`.

**Note:** The handler still uses `session.Events.StartStream<Return>()` and `session.Events.Append()`
manually. This is NOT a violation — the handler loads `ReturnEligibilityWindow` via
`session.LoadAsync<>()`, which requires manual session operations. Per Guard Rail #2, tuple
returns for `IStartStream` only work when Wolverine manages the session. Since this handler
needs the session for document loading, the manual `StartStream` + `Append` pattern is correct.

**Build result:** 0 errors, pre-existing warnings unchanged.

---

### B-3: Inventory — `AdjustInventory.cs` (🟡 High)

**Violation:** Two violations in one handler: (1) `bus.PublishAsync()` for InventoryAdjusted and
LowStockDetected integration messages, and (2) manual `SaveChangesAsync()`.

**Fix:**
1. Replaced `bus.PublishAsync()` calls with `OutgoingMessages` collection
2. Removed `await session.SaveChangesAsync(ct)` — Wolverine's auto-transaction handles persistence
   (`IntegrateWithWolverine()` + `AutoApplyTransactions()` confirmed in `Inventory.Api/Program.cs`)
3. Removed the post-save reload pattern (`session.LoadAsync<ProductInventory>()` after save)
4. Compute `newQuantity = previousQuantity + request.AdjustmentQuantity` mathematically
5. Changed return type from `Task<IResult>` to `Task<(IResult, OutgoingMessages)>`
6. Removed `IMessageBus bus` parameter

**Matched plan:** Yes — the plan correctly identified both violations. The `SaveChangesAsync()` +
reload pattern was a symptom of the same root cause: manual persistence management instead of
letting Wolverine handle it.

**Design decision:** Computing `newQuantity` mathematically instead of reloading after save is
more correct — it's a pure calculation that doesn't depend on eventual consistency of the inline
snapshot projection. The previous pattern (save → reload → read snapshot) was fragile.

**Verified:** `IntegrateWithWolverine()` is configured at line ~52 of `Inventory.Api/Program.cs`,
and `AutoApplyTransactions()` at line ~70. Both prerequisites confirmed before removing
`SaveChangesAsync()`.

**Build result:** 0 errors, 0 new warnings.

---

### B-4: Orders — `CancelOrderEndpoint.cs` (🟡 High)

**Violation:** Endpoint called `bus.PublishAsync(new CancelOrder(orderId, request.Reason))`
instead of returning the command as a cascading message.

**Fix:** Replaced `bus.PublishAsync()` with `OutgoingMessages` containing the `CancelOrder`
command. Changed return type from `Task<IResult>` to `Task<(IResult, OutgoingMessages)>`.
Removed `IMessageBus bus` parameter.

**Matched plan:** Yes — the plan correctly identified this as bypassing the command pipeline.
The `CancelOrder` command is handled by the `Order` saga's `Handle(CancelOrder)` method. When
dispatched via `bus.PublishAsync()`, any `Before()` validators on the saga handler are skipped.
By returning the command via `OutgoingMessages`, Wolverine routes it through the full middleware
pipeline.

**Build result:** 0 errors, 0 new warnings.

---

## Patterns Observed

### Consistent Anti-Pattern: `bus.PublishAsync()` in HTTP Endpoints

B-2, B-3, and B-4 all had the same anti-pattern: HTTP endpoints injecting `IMessageBus` to
call `bus.PublishAsync()` for integration messages or cascading commands. The fix in all three
cases was identical: replace with `OutgoingMessages` tuple return.

This pattern should be audited across remaining BCs in Session 3 or later. Any HTTP endpoint
(identified by `[WolverinePost]`, `[WolverinePut]`, `[WolverineDelete]`) that injects
`IMessageBus` for `PublishAsync()` is a candidate for the same fix.

### `IMessageBus` Still Justified For: `ScheduleAsync()`

B-2 confirmed that `bus.ScheduleAsync()` is the one legitimate `IMessageBus` use case in
handlers. Delayed delivery requires `IMessageBus` because `OutgoingMessages` does not support
scheduling. This should be documented in the integration messaging skill file.

### Guard Rail #2 Held: No Tuple Returns With Manual Loads

B-2's `RequestReturnHandler` correctly keeps `session.Events.StartStream()` and
`session.Events.Append()` manual because it loads documents via `session.LoadAsync<>()`.
Guard Rail #2 was correctly applied — no attempt to force tuple returns in handlers that
manually manage the session.

---

## Session 3 Pickup

1. **B-5, B-6, B-7:** Remove redundant `SaveChangesAsync()` calls across Vendor Portal, Pricing,
   and Product Catalog (the broader sweep deferred from this session per the plan)
2. **Audit remaining BCs** for `bus.PublishAsync()` in HTTP endpoints — the pattern found in
   B-2/B-3/B-4 may exist elsewhere
3. **Consider documenting** the `ScheduleAsync()` exception in the integration messaging skill file

---

## Test Counts

**Note:** Integration tests require Docker containers (Postgres, RabbitMQ via TestContainers)
which are not available in this CI environment. Build verification confirms all changes compile
cleanly with zero errors across the full solution (33 pre-existing warnings, all from E2E test
files unrelated to Track B changes).

**Full solution build:** 0 errors, 33 warnings (all pre-existing in E2E test files)

---

## Files Modified

| Item | Files Changed |
|------|---------------|
| B-1 | `src/Payments/Payments/Processing/AuthorizePayment.cs`, `src/Payments/Payments/Processing/PaymentRequested.cs` |
| B-2 | `src/Returns/Returns/ReturnProcessing/RequestReturn.cs` |
| B-3 | `src/Inventory/Inventory.Api/InventoryManagement/AdjustInventory.cs` |
| B-4 | `src/Orders/Orders.Api/Placement/CancelOrderEndpoint.cs` |
