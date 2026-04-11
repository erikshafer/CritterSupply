# Inventory BC Remaster — Slice Table

> **Session Date:** 2026-04-08
> **ADR:** [0060 — Inventory BC Remaster Rationale](../decisions/0060-inventory-bc-remaster-rationale.md)
> **Retrospective:** [Inventory Remaster Event Modeling Retrospective](milestones/inventory-remaster-event-modeling-retrospective.md)
> **Aggregates:** `ProductInventory` (remastered), `InventoryTransfer` (new)

---

## Aggregate Reference

| Aggregate | Stream ID Pattern | Key Events |
|---|---|---|
| `ProductInventory` | UUID v5 from `inventory:{SKU}:{WarehouseId}` | InventoryInitialized → StockReserved → ReservationCommitted → StockPicked → StockShipped |
| `InventoryTransfer` | `Guid.CreateVersion7()` | TransferRequested → TransferShipped → TransferReceived |

---

## P0 — Foundation (Slices 1–12)

These slices establish the core remastered model. All P0 slices must be implemented before P1 begins.

| # | Slice Name | Command / Trigger | Events | View / Read Model | Aggregate | BC | Priority |
|---|---|---|---|---|---|---|---|
| 1 | Initialize inventory (UUID v5) | `InitializeInventory` | `InventoryInitialized` | `WarehouseSkuDetailView` | ProductInventory | Inventory | P0 ✅ |
| 2 | Stock availability projection | *(projection setup)* | All ProductInventory events | `StockAvailabilityView` | ProductInventory | Inventory | P0 ✅ |
| 3 | Stock availability HTTP query | `GET /api/inventory/availability/{sku}` | — | `StockAvailabilityView` (read) | — | Inventory | P0 ✅ |
| 4 | Routing-informed reservation | `StockReservationRequested` (from Fulfillment) | `StockReserved` | `StockAvailabilityView` updated | ProductInventory | Inventory | P0 ✅ |
| 5 | Reservation failure (insufficient stock) | `StockReservationRequested` (insufficient qty) | — | — (publishes `ReservationFailed`) | ProductInventory | Inventory | P0 ✅ |
| 6 | Reservation commit | `ReservationCommitRequested` (from Orders) | `ReservationCommitted` | `WarehouseSkuDetailView` updated | ProductInventory | Inventory | P0 ✅ |
| 7 | Reservation release | `ReservationReleaseRequested` (from Orders) | `ReservationReleased` | `WarehouseSkuDetailView`, `StockAvailabilityView` | ProductInventory | Inventory | P0 ✅ |
| 8 | Receive stock (supplier PO) | `ReceiveStock` (HTTP) | `StockReceived` | `WarehouseSkuDetailView`, `StockAvailabilityView` | ProductInventory | Inventory | P0 ✅ |
| 9 | Restock from return | `RestockFromReturn` (integration from Returns) | `StockRestocked` | `WarehouseSkuDetailView`, `StockAvailabilityView` | ProductInventory | Inventory | P0 ✅ |
| 10 | Manual adjustment | `AdjustInventory` (HTTP) | `InventoryAdjusted` | `WarehouseSkuDetailView` | ProductInventory | Inventory | P0 ✅ |
| 11 | Low-stock threshold detection | *(inline policy in adjustment/shipment handlers)* | `LowStockThresholdBreached` | `LowStockAlertView` (publishes `LowStockDetected`) | ProductInventory | Inventory | P0 ✅ |
| 12 | OrderPlacedHandler dual-publish bridge | `OrderPlaced` (from Orders) | `StockReserved` | — (retained as dual-publish bridge — retirement blocked on coordinated Orders+Fulfillment update; see S4 retro) | ProductInventory | Inventory | P0 ✅ |

---

## P1 — Failure Modes and Physical Operations (Slices 13–24)

| # | Slice Name | Command / Trigger | Events | View / Read Model | Aggregate | BC | Priority |
|---|---|---|---|---|---|---|---|
| 13 | Physical pick tracking | `ItemPicked` (from Fulfillment) | `StockPicked` | `WarehouseSkuDetailView` (Picked bucket) | ProductInventory | Inventory | P1 ✅ |
| 14 | Physical ship tracking | `ShipmentHandedToCarrier` (from Fulfillment) | `StockShipped` | `WarehouseSkuDetailView` (TotalOnHand decremented) | ProductInventory | Inventory | P1 ✅ |
| 15 | Short pick detection | `ItemPicked` (qty < committed) | `StockPicked`, `StockDiscrepancyFound` | `AlertFeedView` | ProductInventory | Inventory | P1 ✅ |
| 16 | Reservation expiry | *(scheduled timeout message)* | `ReservationExpired` | `StockAvailabilityView` (available restored) | ProductInventory | Inventory | P1 ✅ |
| 17 | Concurrent reservation conflict | `StockReservationRequested` (ConcurrencyException) | — (retry → `ReservationFailed` if still insufficient) | — | ProductInventory | Inventory | P1 ✅ |
| 18 | Backorder registration | `BackorderCreated` (from Fulfillment) | `BackorderRegistered` | `BackorderImpactView` | ProductInventory | Inventory | P1 ✅ |
| 19 | Backorder stock available | `StockReceived` / `TransferReceived` (with pending backorders) | `BackorderCleared` | — (publishes `BackorderStockAvailable`) | ProductInventory | Inventory | P1 ✅ |
| 20 | Cycle count initiation | `InitiateCycleCount` (HTTP) | `CycleCountInitiated` | `CycleCountView` | ProductInventory | Inventory | P1 ✅ |
| 21 | Cycle count completion (no discrepancy) | `CompleteCycleCount` (HTTP) | `CycleCountCompleted` | `CycleCountView` | ProductInventory | Inventory | P1 ✅ |
| 22 | Cycle count discrepancy | `CompleteCycleCount` (physical ≠ system) | `CycleCountCompleted`, `StockDiscrepancyFound`, `InventoryAdjusted` | `AlertFeedView`, `WarehouseSkuDetailView` | ProductInventory | Inventory | P1 ✅ |
| 23 | Damage recorded | `RecordDamage` (HTTP) | `DamageRecorded`, `InventoryAdjusted` | `WarehouseSkuDetailView` | ProductInventory | Inventory | P1 ✅ |
| 24 | Stock write-off | `WriteOffStock` (HTTP) | `StockWrittenOff`, `InventoryAdjusted` | `WarehouseSkuDetailView` | ProductInventory | Inventory | P1 ✅ |

---

## P2 — Transfers, Compensation, and Advanced (Slices 25–35)

| # | Slice Name | Command / Trigger | Events | View / Read Model | Aggregate | BC | Priority |
|---|---|---|---|---|---|---|---|
| 25 | Request inter-warehouse transfer | `RequestTransfer` (HTTP) | `TransferRequested`, `StockTransferredOut` (source) | `WarehouseSkuDetailView` (both FCs) | InventoryTransfer + ProductInventory | Inventory | P2 ✅ |
| 26 | Ship transfer | `ShipTransfer` (HTTP) | `TransferShipped` | `WarehouseSkuDetailView` (in-transit) | InventoryTransfer | Inventory | P2 ✅ |
| 27 | Receive transfer | `ReceiveTransfer` (HTTP) | `TransferReceived` (transfer), `TransferReceived` (destination ProductInventory) | `WarehouseSkuDetailView`, `StockAvailabilityView` | InventoryTransfer + ProductInventory | Inventory | P2 ✅ |
| 28 | Cancel transfer (pre-ship) | `CancelTransfer` (HTTP) | `TransferCancelled`, compensation: `StockTransferredOut` reversed | `WarehouseSkuDetailView` (source restored) | InventoryTransfer + ProductInventory | Inventory | P2 ✅ |
| 29 | Short transfer receipt | `ReceiveTransfer` (qty < shipped) | `TransferShortReceived`, `StockDiscrepancyFound` | `AlertFeedView` | InventoryTransfer + ProductInventory | Inventory | P2 ✅ |
| 30 | Replenishment trigger | *(inline policy on low-stock + backorder)* | `ReplenishmentTriggered` | `LowStockAlertView` | ProductInventory | Inventory | P2 ✅ |
| 31 | Network inventory summary | *(async projection)* | All ProductInventory events | `NetworkInventorySummaryView` | — | Inventory | P2 ✅ |
| 32 | Backorder impact dashboard | *(async projection)* | `BackorderRegistered`, `BackorderCleared` + StockAvailabilityView | `BackorderImpactView` | — | Inventory | P2 ✅ |
| 33 | Stock quarantine | `QuarantineStock` (HTTP) | `StockQuarantined`, `InventoryAdjusted` (negative) | `WarehouseSkuDetailView` | ProductInventory | Inventory | P2 ✅ |
| 34 | Quarantine release (resalable) | `ReleaseQuarantine` (HTTP) | `QuarantineReleased`, `InventoryAdjusted` (positive) | `WarehouseSkuDetailView` | ProductInventory | Inventory | P2 ✅ |
| 35 | Quarantine disposal | `DisposeQuarantine` (HTTP) | `QuarantineDisposed`, `StockWrittenOff` | `WarehouseSkuDetailView` | ProductInventory | Inventory | P2 ✅ |

---

## P3+ — Deferred (Slices 36–42)

These slices require dedicated sub-sessions or external system integration.

| # | Slice Name | Reason for Deferral | BC | Priority |
|---|---|---|---|---|
| 36 | Bin-level tracking (Gap #7) | Deferred — requires WMS hardware integration (RF scanners, bin management) | Inventory | P3 |
| 37 | Configurable per-SKU thresholds | Deferred — requires admin UI workflow + threshold management UI | Inventory | P3 |
| 38 | Demand forecasting | Deferred — requires ML/statistical modeling for reorder point calculation | Inventory | P3 |
| 39 | FC capacity data exposure (Gap #9) | Deferred — not completed in S4 time budget; no external dependency | Inventory | P3 |
| 40 | Lot/batch tracking | Deferred — requires regulatory/compliance scoping (regulated pet food/medication) | Inventory | P3+ |
| 41 | Expiration date tracking | Deferred — requires regulatory/compliance scoping (perishable pet food FEFO) | Inventory | P3+ |
| 42 | Vendor returns (defective stock to supplier) | Deferred — requires regulatory/compliance scoping (outbound return-to-supplier lifecycle) | Inventory | P3+ |

---

## Summary

| Priority | Slices | Description |
|---|---|---|
| **P0** | 1–12 | Foundation: UUID v5, availability projection, routing integration, receipts, adjustments |
| **P1** | 13–24 | Failure modes: physical pick/ship, short picks, expiry, backorders, cycle counts, damage |
| **P2** | 25–35 | Transfers, quarantine, replenishment, dashboards |
| **P3+** | 36–42 | Bin-level, lot tracking, forecasting, vendor returns |
| **Total** | **42** | **12 P0 + 12 P1 + 11 P2 + 7 P3** |

---

## Integration Contract Changes

### New Contracts

| Contract | Direction | Purpose |
|---|---|---|
| `StockReservationRequested` | Fulfillment → Inventory | Routing-informed reservation (replaces OrderPlaced flow) |
| `ItemPicked` (enriched) | Fulfillment → Inventory | Physical bin reconciliation |
| `BackorderStockAvailable` | Inventory → Fulfillment | Re-routing trigger for backordered shipments |
| `StockDiscrepancyDetected` | Inventory → Operations / Vendor Portal | Alerting on pick discrepancies |

### Existing Contracts with New Subscriptions

| Contract | Direction | Purpose |
|---|---|---|
| `ShipmentHandedToCarrier` | Fulfillment → Inventory | TotalOnHand decrement on custody transfer |
| `BackorderCreated` (enriched with SKU data) | Fulfillment → Inventory | Backorder tracking and replenishment trigger |

### Retired Contracts

| Contract | Retirement Phase | Reason |
|---|---|---|
| `OrderPlaced` subscription in Inventory | Phase 2 (coordinated Orders update) | Replaced by `StockReservationRequested`; WH-01 hardcode eliminated |

### Unchanged Contracts

| Contract | Direction | Notes |
|---|---|---|
| `ReservationConfirmed` | Inventory → Orders | Unchanged |
| `ReservationFailed` | Inventory → Orders | Unchanged |
| `ReservationCommitted` | Inventory → Orders | Unchanged |
| `ReservationReleased` | Inventory → Orders | Unchanged |
| `ReservationCommitRequested` | Orders → Inventory | Unchanged |
| `ReservationReleaseRequested` | Orders → Inventory | Unchanged |
| `InventoryAdjusted` | Inventory → Vendor Portal | Unchanged |
| `LowStockDetected` | Inventory → Vendor Portal / Backoffice | Unchanged |
| `StockReplenished` | Inventory → Vendor Portal | Unchanged |
