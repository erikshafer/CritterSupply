# Inventory BC Remaster — S3 Retrospective

> **Session:** M42.3 — Inventory BC Remaster: S3
> **Date:** 2026-04-11
> **Scope:** P2 slices 25–35 (Transfers, Quarantine, Replenishment, Dashboards) + S2 carryover items
> **ADR:** [0060 — Inventory BC Remaster Rationale](../../decisions/0060-inventory-bc-remaster-rationale.md)

---

## Delivery Table

| Item | Status | Notes |
|---|---|---|
| **S2 Carryover: Gap #13 — DLQ for ConcurrencyException** | ✅ | Replaced `.Discard()` with `.MoveToErrorQueue()`. Wolverine API uses `MoveToErrorQueue` (not `MoveToDeadLetterQueue`). |
| **S2 Carryover: AlertFeedView projection** | ✅ | `AlertFeedViewProjection` (async EventProjection) reacts to `StockDiscrepancyFound` + `LowStockThresholdBreached`. Uses `JasperFx.Events.IEvent<T>` for transform methods. |
| **S2 Carryover: HTTP endpoint wiring** | ✅ | `CycleCountEndpoints`, `RecordDamageEndpoint`, `WriteOffStockEndpoint`. Pass-through pattern — handler logic stays in domain project. |
| **Slice 25 — RequestTransfer** | ✅ | New `InventoryTransfer` aggregate with `Guid.CreateVersion7()`. Multi-stream: `TransferRequested` on transfer stream + `StockTransferredOut` on source `ProductInventory`. |
| **Slice 26 — ShipTransfer** | ✅ | Status guard: must be `Requested`. `TransferShipped` event. |
| **Slice 27 — ReceiveTransfer** | ✅ | Multi-stream: `TransferReceived` on transfer + `StockTransferredIn` on destination `ProductInventory`. |
| **Slice 28 — CancelTransfer** | ✅ | Status guard: pre-ship only. Compensation via `InventoryAdjusted` (positive) on source. |
| **Slice 29 — Short transfer receipt** | ✅ | `TransferShortReceived` + `StockDiscrepancyFound` (DiscrepancyType.ShortTransfer) on destination stream. Surfaces in `AlertFeedView`. |
| **Slice 30 — ReplenishmentPolicy** | ✅ | `ReplenishmentPolicy.ShouldTrigger(available, hasBackorders)`. Inline in handlers that decrement stock (transfers, quarantine). |
| **Slice 31 — NetworkInventorySummaryView** | ✅ | Async `MultiStreamProjection<NetworkInventorySummaryView, string>` keyed by SKU. Tracks available/reserved/quarantined per warehouse. |
| **Slice 32 — BackorderImpactView** | ✅ | Async `MultiStreamProjection<BackorderImpactView, string>` keyed by SKU. Reacts to `BackorderRegistered`/`BackorderCleared`. |
| **Slice 33 — QuarantineStock** | ✅ | `StockQuarantined` + negative `InventoryAdjusted`. New `QuarantinedQuantity` field on `ProductInventory`. |
| **Slice 34 — ReleaseQuarantine** | ✅ | `QuarantineReleased` + positive `InventoryAdjusted`. Round-trip confirmed: available fully restored. |
| **Slice 35 — DisposeQuarantine** | ✅ | `QuarantineDisposed` + `StockWrittenOff`. No resurrection path. Requires `OperationsManager` policy. |

---

## Test Delta

| Suite | S2 Count | S3 Count | Delta |
|---|---|---|---|
| Inventory Unit | 100 | 120 | +20 |
| Inventory Integration | 83 | 96 | +13 |
| Orders Unit | 144 | 144 | 0 |
| Orders Integration | 55 | 55 | 0 |
| **Total** | **382** | **415** | **+33** |

### New Test Files
- `InventoryTransferTests.cs` — 5 unit tests (Create, Ship, Receive, ShortReceive, Cancel)
- `ProductInventoryS3ApplyTests.cs` — 9 unit tests (transfer Apply, quarantine Apply, round-trips)
- `ReplenishmentPolicyTests.cs` — 7 theory cases (threshold + backorder matrix)
- `TransferFlowTests.cs` — 7 integration tests (request, full lifecycle, cancel pre/post-ship, short receipt, ship guard)
- `QuarantineFlowTests.cs` — 6 integration tests (quarantine, release, dispose, guards, round-trip)

---

## Technical Decisions

### 1. `StockTransferredIn` vs reusing `StockReceived`
Created a new `StockTransferredIn` event rather than reusing `StockReceived` because:
- `StockReceived` carries `SupplierId + PurchaseOrderId?` provenance fields that don't apply to transfers
- `StockTransferredIn` carries `TransferId` for provenance back to the InventoryTransfer aggregate
- Distinct event types allow finer-grained projection filtering

### 2. Compensation via `InventoryAdjusted` (CancelTransfer)
`CancelTransfer` compensates the `StockTransferredOut` by appending a positive `InventoryAdjusted` event, not a reverse `StockTransferredIn`. This preserves the audit trail: the adjustment clearly records "Transfer cancellation: {reason}" and is distinguishable from inbound stock.

### 3. `QuarantinedQuantity` on ProductInventory
Added `QuarantinedQuantity` as a first-class field on the aggregate. Quarantine reduces `AvailableQuantity` via `InventoryAdjusted` and tracks the quarantined bucket separately. This means `TotalOnHand` does not include quarantined stock (it was already removed from available). If we later need quarantine in TotalOnHand, we'd adjust the computed property.

### 4. `MoveToErrorQueue()` not `MoveToDeadLetterQueue()`
Wolverine's API uses `MoveToErrorQueue()` (via `IFailureActions`), not `MoveToDeadLetterQueue()`. The dead letter envelope storage is internal to Wolverine's durable inbox/outbox. Future DLQ alerting/monitoring is deferred — TODO noted for Operations BC concern.

### 5. EventProjection for AlertFeedView
Used Marten's `EventProjection` (event-per-document) rather than a multi-stream projection. Each alert event produces exactly one `AlertFeedView` row. This is cleaner than creating/updating a document per SKU, since alerts are append-only by nature.

---

## S2 Carryover Resolution

| Gap # | Item | Resolution |
|---|---|---|
| #13 | Dead-letter for ConcurrencyException | `.Discard()` → `.MoveToErrorQueue()`. DLQ alerting deferred to Operations BC. |
| Slice 15+22 | AlertFeedView projection | `AlertFeedViewProjection` (async) for `StockDiscrepancyFound` + `LowStockThresholdBreached`. |
| S2 | HTTP endpoint wiring | `CycleCountEndpoints`, `RecordDamageEndpoint`, `WriteOffStockEndpoint` wired with `[Authorize]`. |

---

## Deferred Items (S4+)

| Item | Reason | Priority |
|---|---|---|
| DLQ alerting/monitoring infrastructure | Beyond log sink scope — Operations BC concern | S4+ |
| `WarehouseSkuDetailView` projection for transfers/quarantine | Read model updates for transfer in-transit and quarantine status | S4 |
| `ItemPickedHandler` TODO removal (StockDiscrepancyFound integration event) | Handler publishes domain event; integration event publication requires OutgoingMessages pattern update | S4 |
| Frontend (Backoffice Blazor) wiring for dashboard projections | Backend projection + read endpoint only this session | S4+ |
| P3+ slices 36–42 | Bin-level tracking, configurable thresholds, forecasting, lot/batch, expiration, vendor returns | P3+ |

---

## Build Status

- **Errors:** 0
- **Inventory warnings:** 0 (solution-wide: 4, all pre-existing in Correspondence/Backoffice)
- **Warning delta:** 0 (≤ S2 baseline ✅)
