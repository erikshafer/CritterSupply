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
| **Track 4: Slice 39 (stretch)** | ✅ | `FulfillmentCenterCapacityView` inline multi-stream projection + `GET /api/inventory/fc-capacity/{warehouseId}` endpoint. |

---

## Test Delta

| Suite | S3 Count | S4 Count | Delta |
|---|---|---|---|
| Inventory Unit | 120 | 151 | +31 |
| Inventory Integration | 96 | 109 | +13 |
| Orders Unit | 144 | 144 | 0 |
| Orders Integration | 55 | 55 | 0 |
| **Total** | **415** | **459** | **+44** |

### New Test Files
- `WarehouseSkuDetailViewProjectionTests.cs` — 14 unit tests (all P0/P1/P2 Apply methods, transfer lifecycle, quarantine round-trips)
- `StockAvailabilityViewProjectionTests.cs` (additions) — 6 new tests (transfer in-transit non-double-counting, quarantine exclusion regression)
- `FulfillmentCenterCapacityViewProjectionTests.cs` — 10 unit tests (multi-SKU accumulation, reserve/pick/ship/transfer/quarantine buckets)
- `AlertFeedViewTests.cs` — 5 integration tests (discrepancy scripted sequence, low stock threshold, short/zero/full pick verification)
- `WarehouseSkuDetailViewTests.cs` — 5 integration tests (init, transfer lifecycle, quarantine round-trip, quarantine dispose, reserve-commit-pick-ship)
- `FcCapacityTests.cs` — 3 integration tests (unknown warehouse, multi-SKU aggregation, reservation reflection)

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

### 5. FulfillmentCenterCapacityView as Inline MultiStreamProjection
Keyed by warehouse ID (string), aggregates capacity across all SKUs. Inline chosen (same
rationale as `StockAvailabilityView`) because the routing engine is on the critical checkout
path and stale capacity data leads to overloaded warehouses. The view covers 15 event types
across all `ProductInventory` operations.

---

## Deferred Items

| Item | Reason | Priority |
|---|---|---|
| Slice 12 — `OrderPlacedHandler` retirement | Blocked on coordinated Orders + Fulfillment update | Phase 2 |
| DLQ alerting/monitoring pipeline | Operations BC concern — log sink is the handoff point | Operations BC |
| Frontend wiring | Backoffice Blazor dashboard — separate BC session | Backoffice BC |
| P3+ slices 36–38, 40–42 | See inventory-remaster-slices.md for per-slice deferral reasons | P3+ |

---

## Build Status

- **Errors:** 0
- **Inventory warnings:** 0 (solution-wide: 4, all pre-existing)
- **Warning delta:** 0 (≤ S3 baseline ✅)

---

## Remaster Summary

Total across S1–S4: **36 P0+P1+P2+P3 slices delivered** (35 P0–P2 + Slice 39 stretch). 2 aggregates (`ProductInventory`
remastered, `InventoryTransfer` new). 6 P3+ slices deferred. **151 unit tests, 109 integration
tests** (total: 260 Inventory tests). Orders suites unchanged at 199. Build clean with 0 errors,
0 Inventory warnings.

### Session Retrospectives

- [S1 Retrospective](inventory-remaster-s1-retrospective.md)
- [S2 Retrospective](inventory-remaster-s2-retrospective.md)
- [S3 Retrospective](inventory-remaster-s3-retrospective.md)
- [S4 Retrospective](inventory-remaster-s4-retrospective.md) *(this document)*
