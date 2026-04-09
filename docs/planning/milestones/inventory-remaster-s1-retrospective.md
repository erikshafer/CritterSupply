# Inventory BC Remaster — S1 Retrospective

**Milestone:** M42.1 — Inventory BC Remaster: S1
**Session type:** Implementation (P0 Foundation, Slices 1–12)
**Date:** 2026-04-09
**ADR:** `docs/decisions/0060-inventory-bc-remaster-rationale.md`

---

## What Was Delivered

| Slice | Description | Status | Test Delta |
|-------|-------------|--------|------------|
| 1 | UUID v5 Stream IDs (`InventoryStreamId.Compute()`) | ✅ Done | +8 unit |
| 2 | `StockAvailabilityView` inline multi-stream projection | ✅ Done | +9 unit |
| 3 | `GET /api/inventory/availability/{sku}` endpoint | ✅ Done | +4 integration |
| 4 | `StockReservationRequested` handler (Fulfillment → Inventory) | ✅ Done | +2 integration |
| 5 | Reservation failure path (Before() 409 guard) | ✅ Done | covered by Slice 4 tests |
| 6 | `ReservationCommitted` enriched with Sku/WarehouseId | ✅ Done | existing tests updated |
| 7 | `ReservationReleased` enriched with Sku/WarehouseId | ✅ Done | existing tests updated |
| 8 | `StockReceived` structured payload (SupplierId, PurchaseOrderId) | ✅ Done | existing tests updated |
| 9 | `RestockFromReturnHandler` (Returns → Inventory) | ✅ Done | — |
| 10 | `InventoryAdjusted` enriched with Sku/WarehouseId | ✅ Done | existing tests updated |
| 11 | `LowStockThresholdBreached` + `LowStockPolicy` | ✅ Done | +7 unit |
| 12 | `OrderPlacedHandler` migration comment (dual-publish bridge) | ✅ Done | 0 changes |

**All 12 P0 slices delivered.**

---

## Build/Test Status

| Suite | Baseline | Final | Delta |
|-------|----------|-------|-------|
| Build errors | 0 | 0 | — |
| Build warnings | 11 | 4 | -7 (reduced) |
| Inventory unit tests | 54 | 83 | +29 |
| Inventory integration tests | 48 | 54 | +6 |
| Orders integration tests | 55 | 55 | 0 |
| Orders unit tests | 144 | 144 | 0 |

---

## New Files Created

- `src/Inventory/Inventory/Management/InventoryStreamId.cs` — UUID v5 deterministic stream IDs
- `src/Inventory/Inventory/Management/StockAvailabilityView.cs` — Multi-warehouse view model
- `src/Inventory/Inventory/Management/StockAvailabilityViewProjection.cs` — Inline multi-stream projection
- `src/Inventory/Inventory/Management/StockReservationRequestedHandler.cs` — Fulfillment-initiated reservation
- `src/Inventory/Inventory/Management/RestockFromReturnHandler.cs` — Returns → Inventory restock
- `src/Inventory/Inventory/Management/LowStockPolicy.cs` — Threshold crossing detection
- `src/Inventory/Inventory/Management/LowStockThresholdBreached.cs` — Domain event
- `src/Inventory/Inventory.Api/StockQueries/GetStockAvailability.cs` — Availability query endpoint
- `src/Shared/Messages.Contracts/Fulfillment/StockReservationRequested.cs` — Integration contract
- `tests/Inventory/Inventory.UnitTests/Management/InventoryStreamIdTests.cs`
- `tests/Inventory/Inventory.UnitTests/Management/LowStockPolicyTests.cs`
- `tests/Inventory/Inventory.UnitTests/Management/StockAvailabilityViewProjectionTests.cs`
- `tests/Inventory/Inventory.Api.IntegrationTests/StockQueries/StockAvailabilityEndpointTests.cs`
- `tests/Inventory/Inventory.Api.IntegrationTests/Management/StockReservationRequestedTests.cs`

## Modified Files

- `src/Inventory/Inventory/Management/ProductInventory.cs` — `Create()` uses UUID v5; `CombinedGuid()` marked `[Obsolete]`
- `src/Inventory/Inventory/Management/StockReserved.cs` — Added Sku, WarehouseId fields
- `src/Inventory/Inventory/Management/ReservationCommitted.cs` — Added Sku, WarehouseId fields
- `src/Inventory/Inventory/Management/ReservationReleased.cs` — Added Sku, WarehouseId fields
- `src/Inventory/Inventory/Management/StockReceived.cs` — Replaced freeform Source with structured SupplierId, PurchaseOrderId
- `src/Inventory/Inventory/Management/StockRestocked.cs` — Added Sku, WarehouseId fields
- `src/Inventory/Inventory/Management/InventoryAdjusted.cs` — Added Sku, WarehouseId fields
- `src/Inventory/Inventory/Management/AdjustInventory.cs` — Handler returns OutgoingMessages, LowStockPolicy integrated
- `src/Inventory/Inventory/Management/ReceiveStock.cs` — Updated for structured StockReceived, publishes StockReplenished
- `src/Inventory/Inventory/Management/ReserveStock.cs` — Uses InventoryStreamId.Compute()
- `src/Inventory/Inventory/Management/CommitReservation.cs` — Populates Sku/WarehouseId from aggregate
- `src/Inventory/Inventory/Management/ReleaseReservation.cs` — Populates Sku/WarehouseId from aggregate
- `src/Inventory/Inventory/Management/ReservationCommitRequestedHandler.cs` — Populates Sku/WarehouseId
- `src/Inventory/Inventory/Management/ReservationReleaseRequestedHandler.cs` — Populates Sku/WarehouseId
- `src/Inventory/Inventory/Management/OrderPlacedHandler.cs` — Migration bridge comment added
- `src/Inventory/Inventory/Management/InitializeInventory.cs` — Uses InventoryStreamId.Compute()
- `src/Inventory/Inventory.Api/Program.cs` — StockAvailabilityViewProjection registered Inline; RabbitMQ listener added
- `src/Inventory/Inventory.Api/InventoryManagement/AdjustInventory.cs` — LowStockPolicy + LowStockThresholdBreached
- `src/Inventory/Inventory.Api/InventoryManagement/ReceiveInboundStock.cs` — Updated for new StockReceived signature
- `src/Inventory/Inventory.Api/StockQueries/GetStockLevel.cs` — Uses InventoryStreamId.Compute()

---

## Deferred Items

1. **RestockFromReturn integration test** — Handler is implemented but integration test requires Returns BC fixtures not yet available. Test coverage is deferred to S2 when cross-BC test infrastructure is in place.
2. **Concurrent reservation test** — Per QAE note, the `.Discard()` policy on ConcurrencyException may silently drop second reservation. Behavior documented but not changed per session scope.
3. **Per-SKU configurable low stock thresholds** — LowStockPolicy uses hardcoded threshold of 10. Configurable thresholds are a P2 item (Slice 35).

---

## Technical Notes

- **UUID v5 vs MD5 migration**: `CombinedGuid()` is marked `[Obsolete]` but NOT deleted. Existing tests still test it for backward compatibility. New code exclusively uses `InventoryStreamId.Compute()`.
- **StockAvailabilityView identity**: Uses SKU (string) as document identity, not Guid. This is intentional — the routing engine queries by SKU.
- **Inline projection choice**: `StockAvailabilityViewProjection` is Inline (not Async) because the routing engine is on the critical checkout path. Stale data leads to double-booking.
- **RabbitMQ listener**: `inventory-fulfillment-events` queue added for `StockReservationRequested`. Uses durable inbox for reliable delivery.
- **Dual-publish bridge preserved**: `OrderPlacedHandler` runs alongside `StockReservationRequestedHandler` during Phase 1. Both flows coexist. Retirement is in S4.
