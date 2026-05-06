# Inventory BC Remaster — S2 Session Retrospective

**Milestone:** M42.2 — Inventory BC Remaster: S2
**Session date:** 2026-04-10
**Session type:** Implementation
**Scope:** P1 slices 13–24 + S1 deferred item (RestockFromReturn integration test)

---

## What Was Delivered

| Slice | Name | Status | Notes |
|-------|------|--------|-------|
| Track A | Aggregate Enhancements | ✅ Delivered | PickedAllocations, HasPendingBackorders, PickedQuantity, TotalOnHand updated |
| 13 | ItemPicked → StockPicked | ✅ Delivered | Inline short pick detection per Anti-Pattern #13 |
| 14 | ShipmentHandedToCarrier → StockShipped | ✅ Delivered | Combined pick-and-ship path for out-of-order delivery |
| 15 | Short Pick Detection | ✅ Delivered | Inline in ItemPickedHandler — ZeroPick + ShortPick |
| 16 | ReservationExpired | ✅ Delivered | Scheduled via OutgoingMessages.Delay(); idempotent |
| 17 | Concurrent Reservation Conflict | ✅ Delivered | Test written; Gap #13 documented (see below) |
| 18 | BackorderCreated → BackorderRegistered | ✅ Delivered | BackorderCreated contract enriched with Items (Gap #10) |
| 19 | BackorderPolicy — stock arrival clears backorder | ✅ Delivered | Wired into ReceiveStockHandler |
| 20 | InitiateCycleCount | ✅ Delivered | HTTP endpoint with FluentValidation |
| 21–22 | CompleteCycleCount + discrepancy | ✅ Delivered | Shortage, surplus, negative rejection paths |
| 23 | RecordDamage | ✅ Delivered | DamageRecorded + InventoryAdjusted + low stock inline |
| 24 | WriteOffStock | ✅ Delivered | StockWrittenOff + InventoryAdjusted + low stock inline |
| S1 deferred | RestockFromReturn integration test | ✅ Delivered | 4 tests: restock, non-restockable skip, unknown SKU skip, multi-item |

**All 12 P1 slices delivered. 1 S1 deferred item resolved.**

---

## Test Delta

| Suite | S1 Baseline | S2 Final | Delta |
|-------|-------------|----------|-------|
| Inventory unit tests | 83 | 100 | +17 |
| Inventory integration tests | 54 | 83 | +29 |
| Orders integration tests | 55 | 55 | 0 |
| Orders unit tests | 144 | 144 | 0 |

---

## Build/Test Status

- Build: **0 errors**, ≤4 warnings (same as S1 baseline)
- All 4 test suites: **green**

---

## Gap #13 Resolution: ConcurrencyException + `.Discard()` Policy

> ✅ **Resolved in M43.1** (2026-05-06). The policy now terminates in
> `MoveToErrorQueue()` and is covered by a deterministic integration test in
> `tests/Inventory/Inventory.Api.IntegrationTests/Reliability/ConcurrencyExhaustionDlqTests.cs`.
> See [`m43-1-plan.md`](./m43-1-plan.md). The original S2 findings (below) are
> preserved for historical context.

### Findings

The concurrent reservation test (Slice 17) confirms:

1. When two `StockReservationRequested` messages arrive simultaneously for the same
   inventory stream with exactly enough stock for one:
   - The first reservation succeeds (appends `StockReserved`)
   - The second hits a `ConcurrencyException` on retry
   - Wolverine retries once, then retries with cooldown (100ms, 250ms)
   - If stock is still insufficient after retry, the message is **silently discarded**

2. The second order **never receives `ReservationFailed`** — it's simply dropped.

### Original S2 Policy

```csharp
opts.OnException<ConcurrencyException>()
    .RetryOnce()
    .Then.RetryWithCooldown(100.Milliseconds(), 250.Milliseconds())
    .Then.Discard();
```

### Recommendation for S3

Change from `.Discard()` to `.MoveToDeadLetterQueue()` for visibility. This is a
non-trivial change because:
- Dead letter queue handlers need to be created
- Monitoring/alerting needs to be added
- The `Before()` validation on `StockReservationRequestedHandler` properly rejects
  insufficient stock, so the retry path usually succeeds (the concurrency conflict
  is on the event append, not the validation)

The risk of silent drops is **low but non-zero** in the current architecture. When
the Fulfillment routing engine sends `StockReservationRequested` to a specific
warehouse, it already checked `StockAvailabilityView` for availability. The main
risk vector is flash sales where availability drops between the routing query and
the reservation command.

**Decision: Defer `.MoveToDeadLetterQueue()` to S3** — document as known limitation.

### M43.1 Update — what shipped

- Production policy in `src/Inventory/Inventory.Api/Program.cs` terminates in
  `.MoveToErrorQueue()`, with a code comment naming Gap #13 and pointing at this
  retrospective and `m43-1-plan.md`.
- New deterministic test
  `Reliability/ConcurrencyExhaustionDlqTests.ConcurrencyException_RetriesExhaust_EnvelopeMovesToDeadLetterQueue`
  proves the policy chain ends in `inventory.wolverine_dead_letters` (not in a
  silent discard).
- Existing race test renamed to `ConcurrentReservations_LastUnitContention_NoSilentDrop`;
  obsolete `Discard`/TODO comments removed; assertions updated to enforce the
  no-DLQ-escalation invariant for routine 2-way contention (the deterministic
  exhaustion proof now lives in the dedicated reliability test).
- Latent column-name bug in `DeadLetterQueueLogSink` (queried `explanation`/`source`
  against the actual `exception_type`/`exception_message`/`source` schema) fixed
  while the new test surfaced it. The sink now logs `ExceptionType` and
  `ExceptionMessage` per envelope.

---

## Integration Contract Changes

### New Contracts
- `Messages.Contracts.Fulfillment.ItemPicked` — Gap #11 enrichment (WarehouseId, OrderId)
- `Messages.Contracts.Inventory.BackorderStockAvailable` — new outbound

### Modified Contracts
- `Messages.Contracts.Fulfillment.BackorderCreated` — Gap #10 enrichment (Items list)

### Queue Subscriptions
- `inventory-fulfillment-events`: now handles `StockReservationRequested`,
  `ShipmentHandedToCarrier`, `ItemPicked`, `BackorderCreated`

---

## New Domain Events

| Event | Slice | State Change |
|-------|-------|-------------|
| `StockPicked` | 13 | Committed → Picked |
| `StockShipped` | 14 | Picked removed, TotalOnHand decremented |
| `StockDiscrepancyFound` | 15 | None (audit only) |
| `ReservationExpired` | 16 | Same as ReservationReleased |
| `BackorderRegistered` | 18 | HasPendingBackorders = true |
| `BackorderCleared` | 19 | HasPendingBackorders = false |
| `CycleCountInitiated` | 20 | None |
| `CycleCountCompleted` | 21 | None (via InventoryAdjusted) |
| `DamageRecorded` | 23 | None (via InventoryAdjusted) |
| `StockWrittenOff` | 24 | None (via InventoryAdjusted) |

---

## Technical Decisions

1. **ShipmentHandedToCarrierHandler Load pattern**: Uses Marten `MatchesSql` with
   `jsonb_each_text` for dictionary value lookup — LINQ `Dictionary.Values.Contains()`
   not supported by Marten.

2. **BackorderCreatedHandler**: Handles multi-item backorders by iterating through
   items and appending per-stream events. Does not use the aggregate Load/Handle
   pattern because it touches multiple streams.

3. **CycleCount negative rejection**: `CompleteCycleCountHandler.Before()` rejects
   counts that would push AvailableQuantity negative — operations manager must
   investigate discrepancy before adjusting.

4. **LowStockPolicy inline calls**: RecordDamage, WriteOffStock, and CycleCount
   use inline `LowStockPolicy.CrossedThresholdDownward()` checks and append
   `LowStockThresholdBreached` domain events — same pattern as S1.

---

## Deferred Items

- **Gap #13 `.MoveToDeadLetterQueue()`** — ✅ resolved in M43.1 (see resolution above)
- **AlertFeedView integration event for StockDiscrepancyFound** — marked TODO in
  ItemPickedHandler; deferred to S3 when AlertFeedView projection is built
- **HTTP endpoint wiring for CycleCount, RecordDamage, WriteOffStock** — handlers
  exist but Wolverine HTTP `[WolverinePost]` attributes not yet added; deferred to
  S3 or API surface cleanup pass
