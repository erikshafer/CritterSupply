# Inventory

> **Source folder:** `src/Inventory/`
> **Status:** Implemented
> **Most recent material milestone:** M47.0 — Replacement reservation for cross-product exchanges (Returns ↔ Inventory choreography)
> **Stub depth:** S1 — to be deepened in S2

## Purpose

Inventory tracks per-warehouse stock levels and manages the reservation lifecycle that backs both order fulfillment and return-driven replacements. Stock moves through soft holds (reservations), commits, picks, ships, transfers between warehouses, quarantine, damage write-offs, cycle counts, and backorder registration. Inventory exposes a stock-availability read model that Fulfillment's routing engine queries when deciding where to fulfill a shipment from.

## Top-level structure

### Aggregates

- `ProductInventory` — stream ID: UUID v5 from `inventory:{sku}:{warehouseId}` (`InventoryStreamId.Compute`).
- `InventoryTransfer` — stream ID: UUID v7.

### Commands

- `InitializeInventory`
- `ReceiveStock`, `RestockFromReturn` (handler), `AdjustInventory`, `WriteOffStock`
- `ReserveStock`, `CommitReservation`, `ReleaseReservation`, `ExpireReservation`
- `QuarantineStock`, `ReleaseQuarantine`, `DisposeQuarantine`, `RecordDamage`
- `InitiateCycleCount`, `CompleteCycleCount`
- `RequestTransfer`, `ShipTransfer`, `ReceiveTransfer`, `CancelTransfer`
- `ReserveReplacementForExchange` (cross-product exchange, M47.0)
- `ReleaseExchangeReservation`

### Domain events

- `InventoryInitialized`
- `StockReceived`, `StockRestocked`, `StockShipped`, `StockPicked`
- `InventoryAdjusted`, `StockWrittenOff`, `DamageRecorded`
- `StockReserved`, `ReservationCommitted`, `ReservationReleased`, `ReservationExpired`
- `StockQuarantined`, `QuarantineReleased`, `QuarantineDisposed`
- `CycleCountInitiated`, `CycleCountCompleted`, `StockDiscrepancyFound`
- `TransferRequested`, `TransferShipped`, `TransferReceived`, `TransferShortReceived`, `TransferCancelled`
- `StockTransferredOut`, `StockTransferredIn`
- `BackorderRegistered`, `BackorderCleared`
- `LowStockThresholdBreached`, `ReplenishmentTriggered`

### Projections

- `ProductInventory` snapshot — inline, keyed by stream id.
- `InventoryTransfer` snapshot — inline, keyed by stream id.
- `StockAvailabilityView` — inline.
- `WarehouseSkuDetailView` — inline.
- `FulfillmentCenterCapacityView` — inline.
- `AlertFeedView` — async.
- `NetworkInventorySummaryView` — async.
- `BackorderImpactView` — async.

### Integration events

- `Inventory.ReservationConfirmed` — publishes
- `Inventory.ReservationFailed` — publishes
- `Inventory.ReservationCommitted` — publishes
- `Inventory.ReservationReleased` — publishes
- `Inventory.BackorderStockAvailable` — publishes
- `Inventory.InventoryAdjusted` — publishes
- `Inventory.LowStockDetected` — publishes
- `Inventory.StockReplenished` — publishes
- `Inventory.StockDiscrepancyDetected` — publishes
- `Inventory.ReplacementReserved` — publishes
- `Inventory.ReplacementReservationFailed` — publishes
- `Inventory.ReleaseExchangeReservation` — bidirectional helper contract
- `Inventory.ReserveReplacementForExchange` — subscribes
- `Orders.ReservationCommitRequested` / `ReservationReleaseRequested` — subscribes
- `Fulfillment.StockReservationRequested` / `ItemPicked` / `ShipmentHandedToCarrier` / `BackorderCreated` — subscribes

### HTTP / API surface (one line)

`Inventory.Api` exposes stock query endpoints (including the `StockAvailabilityView` consumed by Fulfillment routing) and inventory-management commands consumed by Backoffice.

### Frontend surface (if applicable)

Not applicable — no frontend in this BC.

### Identity / auth posture (if applicable)

JWT Bearer (Backoffice + Vendor schemes registered in `Inventory.Api/Program.cs`).

## Prior event modeling

- `docs/planning/inventory-remaster-slices.md`
- `docs/planning/inventory-remaster-phase-3-storyboarding.md`

## ADRs

- ADR 0016 — UUID v5 for Natural-Key Stream IDs
- ADR 0060 — Inventory BC Remaster Rationale
- ADR 0061 — Cross-Product Exchange Replacement Reservation

## Source citations (S1 stub)

- `src/Inventory/`
- `src/Shared/Messages.Contracts/Inventory/`
- `src/Inventory/Inventory.Api/Program.cs` (projection registrations)
- `CONTEXTS.md` (section: `Inventory`)
- `docs/decisions/0016-uuid-v5-for-natural-key-stream-ids.md`
- `docs/decisions/0060-inventory-bc-remaster-rationale.md`
- `docs/decisions/0061-cross-product-exchange-replacement-reservation.md`
- `docs/planning/inventory-remaster-slices.md`
- `docs/planning/inventory-remaster-phase-3-storyboarding.md`
