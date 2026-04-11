# Inventory BC Remaster — S4 Retrospective

> **Session:** M42.4 — Inventory BC Remaster: S4 (Close-Out)
> **Date:** 2026-04-11
> **Scope:** S3 carryover items + read model completeness + remaster wrap-up
> **ADR:** [0060 — Inventory BC Remaster Rationale](../../decisions/0060-inventory-bc-remaster-rationale.md)

---

## Delivery Table

| Item | Status | Notes |
|---|---|---|
| **S3 Carryover: WarehouseSkuDetailView** | ✅ | Inline `MultiStreamProjection`, all events including transfers + quarantine. |
| **S3 Carryover: StockDiscrepancyDetected integration event** | ✅ | `OutgoingMessages` pattern in `ItemPickedHandler`. |
| **S3 Carryover: DLQ log sink** | ✅ | `DeadLetterQueueLogSink` `BackgroundService` polling `wolverine_dead_letters` table. |
| **Track 2: StockAvailabilityView regression** | ✅ | Quarantine exclusion + in-transit non-double-counting tests. |
| **Track 2: AlertFeedView rebuild** | ✅ | Scripted event sequence: discrepancy → low stock → quarantine. |
| **Track 3: Slice 12 retirement** | ⛔ BLOCKED | Orders still routes `OrderPlaced` to Inventory; requires coordinated update. |
| **Track 3: ADR-0060 close-out** | ✅ | Remaster completion addendum appended to ADR. |
| **Track 3: Slices doc update** | ✅ | All P0/P1/P2 slices marked ✅; P3+ deferral reasons documented. |
| **Track 4: Slice 39 (stretch)** | ⏩ Deferred | Deferred to dedicated session — not completed in S4 time budget. |

---

## Test Delta

| Suite | S3 Count | S4 Count | Delta |
|---|---|---|---|
| Inventory Unit | 120 | 141 | +21 |
| Inventory Integration | 96 | 106 | +10 |
| Orders Unit | 144 | 144 | 0 |
| Orders Integration | 55 | 55 | 0 |
| **Total** | **415** | **446** | **+31** |

### New Test Files
- `WarehouseSkuDetailViewProjectionTests.cs` — 14 unit tests (all P0/P1/P2 Apply methods, transfer lifecycle, quarantine round-trips)
- `StockAvailabilityViewProjectionTests.cs` (additions) — 6 new tests (transfer in-transit non-double-counting, quarantine exclusion regression)
- `AlertFeedViewTests.cs` — 5 integration tests (discrepancy scripted sequence, low stock threshold, short/zero/full pick verification)
- `WarehouseSkuDetailViewTests.cs` — 5 integration tests (init, transfer lifecycle, quarantine round-trip, quarantine dispose, reserve-commit-pick-ship)

---

## Technical Decisions

### 1. WarehouseSkuDetailView as Inline MultiStreamProjection
Chosen over Async because the view is consulted on the operational path (warehouse dashboards
need current state). Identity maps to UUID v5 via `InventoryStreamId.Compute()`. Covers all
19 `ProductInventory` event types.

### 2. DeadLetterQueueLogSink approach
Background service polling the `wolverine_dead_letters` PostgreSQL table directly (via
`NpgsqlDataSource`). Avoids coupling to Wolverine internal APIs. Gracefully handles
table-not-yet-created (`PostgresException` 42P01). Production alerting is explicitly an
Operations BC handoff.

### 3. StockDiscrepancyDetected as integration event
Parallels the pattern established by `LowStockDetected`. Carries `DiscrepancyType` as `string`
(not enum) in the integration contract to avoid cross-BC enum coupling.

### 4. Slice 12 decision
Orders BC still publishes `OrderPlaced` to Inventory via local Wolverine queue. The
`OrderDecider` emits `FulfillmentRequested` to Fulfillment, but Fulfillment does not yet send
`StockReservationRequested` back to Inventory (that's Phase 2). Retirement requires:
(a) Fulfillment routes and sends `StockReservationRequested`,
(b) Orders stops routing `OrderPlaced` to Inventory.
Both changes are beyond single-BC scope.

---

## Deferred Items

| Item | Reason | Priority |
|---|---|---|
| Slice 12 — `OrderPlacedHandler` retirement | Blocked on coordinated Orders + Fulfillment update | Phase 2 |
| Slice 39 — FC capacity exposure | Not completed in S4 time budget; no external dependency | P3 |
| DLQ alerting/monitoring pipeline | Operations BC concern — log sink is the handoff point | Operations BC |
| Frontend wiring | Backoffice Blazor dashboard — separate BC session | Backoffice BC |
| P3+ slices 36–42 | See inventory-remaster-slices.md for per-slice deferral reasons | P3+ |

---

## Build Status

- **Errors:** 0
- **Inventory warnings:** 0 (solution-wide: 4, all pre-existing)
- **Warning delta:** 0 (≤ S3 baseline ✅)

---

## Remaster Summary

Total across S1–S4: **35 P0+P1+P2 slices delivered**. 2 aggregates (`ProductInventory`
remastered, `InventoryTransfer` new). 7 P3+ slices deferred. **141 unit tests, 106 integration
tests** (total: 247 Inventory tests). Orders suites unchanged at 199. Build clean with 0 errors,
0 Inventory warnings.

### Session Retrospectives

- [S1 Retrospective](inventory-remaster-s1-retrospective.md)
- [S2 Retrospective](inventory-remaster-s2-retrospective.md)
- [S3 Retrospective](inventory-remaster-s3-retrospective.md)
- [S4 Retrospective](inventory-remaster-s4-retrospective.md) *(this document)*
