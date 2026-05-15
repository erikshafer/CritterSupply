# Inventory

> **Source folder:** `src/Inventory/`
> **Status:** Implemented
> **Most recent material milestone:** M47.0 — Replacement reservation for cross-product exchanges (Returns ↔ Inventory choreography)
> **Dossier depth:** S2 — full

## Purpose

Inventory tracks per-warehouse stock levels for every SKU and manages the reservation lifecycle that backs both order fulfillment and return-driven replacements. Stock moves through soft holds (reservations), commitments, picks, ships, inter-warehouse transfers, quarantine, damage write-offs, cycle counts, backorder registration, and replenishment signaling. The BC owns two event-sourced aggregates and exposes a stock-availability read model that Fulfillment's routing engine consumes on the critical checkout path.

## Aggregates

### `ProductInventory`

- **Stream ID:** UUID v5, computed deterministically from the natural key `inventory:{sku}:{warehouseId}` via `InventoryStreamId.Compute(sku, warehouseId)` (`src/Inventory/Inventory/Management/InventoryStreamId.cs:13`). This is a deliberate departure from the repository-wide UUID v7 default (see ADR 0016) — it lets any handler derive the stream id from `(sku, warehouseId)` without a lookup, which is essential for choreographed reservation messages arriving from Orders, Fulfillment, and Returns.
- **Key state:** `Sku`, `WarehouseId`, `OnHand`, `Reserved`, `QuarantinedQuantity`, open reservation set, open backorder set, low-stock threshold, replenishment policy state. Modeled as a `sealed record` decider (`src/Inventory/Inventory/Management/ProductInventory.cs:9`, 234 LOC).
- **Lifecycle stages:** `Initialized` → `Active` (receive / reserve / commit / pick / ship / transfer / quarantine / cycle-count / backorder loops). The aggregate has no terminal state; it lives for the SKU/warehouse combination.

### `InventoryTransfer`

- **Stream ID:** UUID v7 (default convention; the natural-key carve-out applies only to `ProductInventory`).
- **Key state:** `TransferId`, `Sku`, `FromWarehouseId`, `ToWarehouseId`, `RequestedQuantity`, `ShippedQuantity`, `ReceivedQuantity`, `Status` (`TransferStatus` enum). Modeled as a `sealed record` decider (`src/Inventory/Inventory/Management/InventoryTransfer.cs:8`, 49 LOC).
- **Lifecycle stages:** `Requested` → `Shipped` → `Received` (or `ShortReceived`) | `Cancelled`. Terminal once received or cancelled.

## Commands

Grouped by aggregate then by lifecycle phase. Handler files are colocated with the command record under `src/Inventory/Inventory/Management/`.

### `ProductInventory` commands (16)

- **Initialization:** `InitializeInventory` (`InitializeInventory.cs`).
- **Receipt / replenishment:** `ReceiveStock` (`ReceiveStock.cs`); `RestockFromReturn` arrives as the integration message handled by `RestockFromReturnHandler.cs` (treated as a command into `ProductInventory`).
- **Reservation:** `ReserveStock` (`ReserveStock.cs`), `CommitReservation` (`CommitReservation.cs`), `ReleaseReservation` (`ReleaseReservation.cs`), `ExpireReservation` (`ExpireReservation.cs`); cross-product variants `ReserveReplacementForExchange` (`ReserveReplacementForExchangeHandler.cs`) and `ReleaseExchangeReservation` (`ReleaseExchangeReservationHandler.cs`).
- **Pick / ship:** driven by `ItemPickedHandler.cs` and `ShipmentHandedToCarrierHandler.cs` reacting to Fulfillment integration messages (no public command record).
- **Quarantine:** `QuarantineStock` (`QuarantineStock.cs`), `ReleaseQuarantine` (`ReleaseQuarantine.cs`), `DisposeQuarantine` (`DisposeQuarantine.cs`).
- **Adjustment / write-off / damage:** `AdjustInventory` (`AdjustInventory.cs`), `WriteOffStock` (`WriteOffStock.cs`), `RecordDamage` (`RecordDamage.cs`).
- **Cycle count:** `InitiateCycleCount`, `CompleteCycleCount` (both in `CycleCount.cs:13` and `:72`).

### `InventoryTransfer` commands (4)

- **Request:** `RequestTransfer` (`RequestTransfer.cs`).
- **Ship:** `ShipTransfer` (`ShipTransfer.cs`).
- **Receive:** `ReceiveTransfer` (`ReceiveTransfer.cs`).
- **Cancel:** `CancelTransfer` (`CancelTransfer.cs`).

**Command total: 20** explicit command records + handler-driven pick/ship side effects. Reconciliation: the M48.0 S1 retrospective recorded **21 commands**; the difference of −1 reflects the fact that pick and ship are not modeled as standalone command records but as integration-message handlers (`ItemPickedHandler`, `ShipmentHandedToCarrierHandler`). Counting either pick or ship as a command brings the total back to S1's 21; both handlers do mutate `ProductInventory` and could legitimately be counted.

## Domain events

Grouped by aggregate then by lifecycle phase. All event records live under `src/Inventory/Inventory/Management/`.

### `ProductInventory` events (24)

- **Initialization (1):** `InventoryInitialized`.
- **Receipt / replenishment (2):** `StockReceived`, `StockRestocked`.
- **Reservation (4):** `StockReserved`, `ReservationCommitted`, `ReservationReleased`, `ReservationExpired`.
- **Pick / ship (2):** `StockPicked`, `StockShipped`.
- **Transfer side-effects on stock (2):** `StockTransferredOut`, `StockTransferredIn`. (Emitted on `ProductInventory` streams when `InventoryTransfer` ships and receives respectively.)
- **Quarantine (3):** `StockQuarantined`, `QuarantineReleased`, `QuarantineDisposed`.
- **Adjustment / write-off / damage (3):** `InventoryAdjusted`, `StockWrittenOff`, `DamageRecorded`.
- **Cycle count (3):** `CycleCountInitiated`, `CycleCountCompleted`, `StockDiscrepancyFound`.
- **Backorder (2):** `BackorderRegistered`, `BackorderCleared`.
- **Replenishment signaling (2):** `LowStockThresholdBreached`, `ReplenishmentTriggered`.

### `InventoryTransfer` events (5)

- **Request (1):** `TransferRequested`.
- **Ship (1):** `TransferShipped`.
- **Receive (2):** `TransferReceived`, `TransferShortReceived`.
- **Cancel (1):** `TransferCancelled`.

**Event total: 29.** Reconciliation: the M48.0 S1 retrospective and the prior stub recorded **27 events**; the actual count from the source tree is 29 (a deviation of **+2**). The two events not separately enumerated in the S1 tally are `StockTransferredIn` and `StockTransferredOut` — the cross-aggregate stock side-effects that the S1 row appears to have folded into the transfer phase. They are real, distinct event records (`StockTransferredIn.cs`, `StockTransferredOut.cs`) emitted on the `ProductInventory` stream rather than on `InventoryTransfer`, which is why they read as duplicates of `TransferShipped`/`TransferReceived` at a glance.

## Projections

Registered in `src/Inventory/Inventory.Api/Program.cs` (lines 44–70). Three lifecycle classes are in use; the **three async projections** are a deliberate M42.3 carve-out for read paths off the critical checkout flow.

| Projection | Lifecycle | Key | Source events | Served by |
|---|---|---|---|---|
| `ProductInventory` snapshot | Inline (`Snapshot<ProductInventory>(SnapshotLifecycle.Inline)`, line 44) | Stream id (UUID v5) | All `ProductInventory` events | Aggregate loads in handlers |
| `InventoryTransfer` snapshot | Inline (`Snapshot<InventoryTransfer>(SnapshotLifecycle.Inline)`, line 47) | Stream id | All `InventoryTransfer` events | Transfer endpoint handlers |
| `StockAvailabilityView` | **Inline** (line 52, "critical checkout path") | `(Sku, WarehouseId)` | `StockReceived`, `StockReserved`, `Reservation{Committed,Released,Expired}`, `StockPicked`, `StockShipped`, `StockTransferredIn/Out`, `StockQuarantined`, `QuarantineReleased`, `InventoryAdjusted`, `StockWrittenOff`, `DamageRecorded` | `GetStockAvailability` (`/api/inventory/availability/{sku}`); consumed by Fulfillment routing |
| `WarehouseSkuDetailView` | **Inline** (line 66) | `(Sku, WarehouseId)` | Mirrors `ProductInventory` aggregate state with quantity-bucket breakdown | Backoffice detail queries |
| `FulfillmentCenterCapacityView` | **Inline** (line 70) | `WarehouseId` | Receipt/ship/transfer/write-off events | `GetFcCapacity` (`/api/inventory/fc-capacity/{warehouseId}`); routing engine |
| `AlertFeedView` | **Async** (line 56) | Alert id | `LowStockThresholdBreached`, `StockDiscrepancyFound`, `BackorderRegistered`, damage/quarantine events | Alert feed surface |
| `NetworkInventorySummaryView` | **Async** (line 59) | `Sku` (network-wide rollup) | All stock-quantity events across warehouses | Backoffice network view |
| `BackorderImpactView` | **Async** (line 62) | `Sku` | `BackorderRegistered`, `BackorderCleared`, `StockReceived`, `StockTransferredIn` | Backorder dashboards |

The async daemon is registered in solo mode (`AddAsyncDaemon(DaemonMode.Solo)`, line 72).

## Integration events

All integration contracts live under `src/Shared/Messages.Contracts/Inventory/`. Grouped by direction.

### Published by Inventory (outbound)

- `Inventory.ReservationConfirmed` — fields: `OrderId`, `ReservationId`, `Sku`, `WarehouseId`, `Quantity`. Consumed by Orders.
- `Inventory.ReservationFailed` — fields: `OrderId`, `Sku`, `Reason`. Consumed by Orders.
- `Inventory.ReservationCommitted` — fields: `OrderId`, `ReservationId`, `Sku`, `Quantity`. Consumed by Orders / Fulfillment.
- `Inventory.ReservationReleased` — fields: `OrderId`, `ReservationId`, `Sku`, `Quantity`.
- `Inventory.BackorderStockAvailable` — fields: `Sku`, `WarehouseId`, `AvailableQuantity`. Consumed by Fulfillment / Notifications.
- `Inventory.InventoryAdjusted` — fields: `Sku`, `WarehouseId`, `Delta`, `Reason`. Consumed by Backoffice / analytics.
- `Inventory.LowStockDetected` — fields: `Sku`, `WarehouseId`, `OnHand`, `Threshold`. Consumed by Vendor / Backoffice alerts.
- `Inventory.StockReplenished` — fields: `Sku`, `WarehouseId`, `Quantity`, `Source`.
- `Inventory.StockDiscrepancyDetected` — fields: `Sku`, `WarehouseId`, `Expected`, `Counted`, `DiscrepancyType`.
- `Inventory.ReplacementReserved` — fields: `ExchangeId`, `OriginalOrderId`, `Sku`, `WarehouseId`, `Quantity`. Consumed by Returns. **(ADR 0061, M47.0/S1 — Returns ↔ Inventory edge.)**
- `Inventory.ReplacementReservationFailed` — fields: `ExchangeId`, `OriginalOrderId`, `Sku`, `Reason`. Consumed by Returns. **(ADR 0061.)**

### Subscribed by Inventory (inbound)

- `Inventory.ReserveReplacementForExchange` — from Returns. Fields: `ExchangeId`, `OriginalOrderId`, `Sku`, `Quantity`, `PreferredWarehouseId?`. **Returns ↔ Inventory edge per ADR 0061.** Handled by `ReserveReplacementForExchangeHandler.cs`.
- `Inventory.ReleaseExchangeReservation` — from Returns (compensation when an exchange is cancelled or downgraded to a refund). Handled by `ReleaseExchangeReservationHandler.cs`.
- `Orders.ReservationCommitRequested` — from Orders saga. Handled by `ReservationCommitRequestedHandler.cs`.
- `Orders.ReservationReleaseRequested` — from Orders saga. Handled by `ReservationReleaseRequestedHandler.cs`.
- `Fulfillment.StockReservationRequested` — from Fulfillment routing. Handled by `StockReservationRequestedHandler.cs`.
- `Fulfillment.ItemPicked` — from Fulfillment. Handled by `ItemPickedHandler.cs`.
- `Fulfillment.ShipmentHandedToCarrier` — from Fulfillment. Handled by `ShipmentHandedToCarrierHandler.cs`.
- `Fulfillment.BackorderCreated` — from Fulfillment. Handled by `BackorderCreatedHandler.cs`.
- `Returns.RestockFromReturn` — from Returns (standard return restock, distinct from the exchange path). Handled by `RestockFromReturnHandler.cs`.

## Sagas / orchestration

Inventory **is not** a saga orchestrator. Its handlers are stateless reactions to commands and integration messages; the only stateful artifacts are the two event-sourced aggregates.

Inventory **participates in** the following sagas as a choreography partner:

- **Order saga** (Orders BC) — Inventory holds, commits, and releases reservations on demand.
- **Returns cross-product exchange saga** (Returns BC, ADR 0061 / M47.0) — Inventory reserves a replacement SKU and either confirms (`ReplacementReserved`) or fails (`ReplacementReservationFailed`); compensation arrives via `ReleaseExchangeReservation`.

## HTTP / API surface

`Inventory.Api` exposes only Wolverine HTTP endpoints (`[WolverineGet]` / `[WolverinePost]`); there are no MVC controllers. JWT Bearer authentication with Backoffice and Vendor schemes is configured in `src/Inventory/Inventory.Api/Program.cs`.

### Stock queries (`StockQueries/`)

- `GET /api/inventory` → `GetAllInventory.cs:19`.
- `GET /api/inventory/{sku}` → `GetStockLevel.cs:25`.
- `GET /api/inventory/availability/{sku}` → `GetStockAvailability.cs:26` (consumed by Fulfillment routing).
- `GET /api/inventory/low-stock` → `GetLowStock.cs:33`.
- `GET /api/inventory/fc-capacity/{warehouseId}` → `GetFcCapacity.cs:31`.

### Inventory management commands (`InventoryManagement/`)

- `POST /api/inventory/{sku}/receive` → `ReceiveInboundStock.cs:54`.
- `POST /api/inventory/{sku}/adjust` → `AdjustInventory.cs:64`.
- `POST /api/inventory/write-off-stock` → `WriteOffStockEndpoint.cs:14`.
- `POST /api/inventory/record-damage` → `RecordDamageEndpoint.cs:13`.
- `POST /api/inventory/quarantine`, `/quarantine/release`, `/quarantine/dispose` → `QuarantineEndpoints.cs:13,17,21`.
- `POST /api/inventory/initiate-cycle-count`, `/complete-cycle-count` → `CycleCountEndpoints.cs:14,18`.
- `POST /api/inventory/transfers/request|ship|receive|cancel` → `TransferEndpoints.cs:13,17,21,25`.

## Frontend surface

Not applicable — no frontend in this BC. Inventory is consumed via API by Backoffice, Vendor, and Fulfillment surfaces.

## Identity / auth posture

JWT Bearer authentication. Both `Backoffice` and `Vendor` JWT schemes are registered in `Inventory.Api/Program.cs`. Endpoints inherit policy via Wolverine HTTP attributes.

## Tests as behavioral evidence

### Gherkin features (`docs/features/inventory/`, 53 scenarios across 4 files)

- `reservation-lifecycle.feature` — 13 scenarios.
- `stock-lifecycle.feature` — 13 scenarios.
- `failure-modes.feature` — 19 scenarios.
- `routing-integration.feature` — 8 scenarios.

### Unit tests (`tests/Inventory/Inventory.UnitTests/Management/`)

Decider/apply coverage: `ProductInventoryApplyTests`, `ProductInventoryS3ApplyTests`, `ProductInventoryCreateTests`, `ProductInventoryScenarioTests`, `InventoryTransferTests`, `InventoryStreamIdTests` (UUID v5 determinism), policy tests (`ReplenishmentPolicyTests`, `LowStockPolicyTests`), projection tests (`StockAvailabilityViewProjectionTests`, `WarehouseSkuDetailViewProjectionTests`, `FulfillmentCenterCapacityViewProjectionTests`).

### Integration tests (`tests/Inventory/Inventory.Api.IntegrationTests/`, ~100 `[Fact]`/`[Theory]` cases across 19 suites)

Endpoint and flow coverage: `Commands/AdjustInventoryEndpointTests` (5), `Commands/ReceiveInboundStockEndpointTests` (7), `StockQueries/StockAvailabilityEndpointTests` (4), and the `Management/` suite (`AdjustInventoryEndpointTests`, `AlertFeedViewTests`, `BackorderTrackingTests`, `CommitReleaseFlowTests`, `FcCapacityTests`, `InvariantValidationTests`, `InventoryQueryTests`, `PhysicalOperationsTests`, `PhysicalPickShipTests`, `QuarantineFlowTests`, `ReceiveInboundStockEndpointTests`, `ReservationExpiryTests`, `ReservationFlowTests`, `ReserveReplacementForExchangeTests`, `RestockFromReturnTests`, `StockReservationRequestedTests`, `TransferFlowTests`, `WarehouseSkuDetailViewTests`).

The **M43.1 reliability suite** (`tests/Inventory/Inventory.Api.IntegrationTests/Reliability/ConcurrencyExhaustionDlqTests.cs`) is canonical evidence for the `ConcurrencyException` → DLQ behavior cited in ADR 0060 (per BC-remaster reliability work).

## ADRs

- **ADR 0016 — UUID v5 for Natural-Key Stream IDs.** Justifies the `(sku, warehouseId)` deterministic stream id used by `ProductInventory`.
- **ADR 0060 — Inventory BC Remaster Rationale.** Central rationale for the BC's current shape: two-aggregate split, async-projection carve-out, ConcurrencyException → DLQ contract, async daemon configuration.
- **ADR 0061 — Cross-Product Exchange Replacement Reservation.** Establishes the Returns ↔ Inventory edge and the `ReserveReplacementForExchange` / `ReplacementReserved` / `ReplacementReservationFailed` / `ReleaseExchangeReservation` contracts (M47.0/S1).

## Prior event modeling

- `docs/planning/inventory-remaster-slices.md` — vertical-slice decomposition of the Inventory remaster (per-slice command/event/handler triples driving the M42.x–M43.x cycles).
- `docs/planning/inventory-remaster-phase-3-storyboarding.md` — phase-3 storyboarding for reservation/commit/transfer/quarantine flows that fed the integration-test design.

## Source citations (S2 full)

- `src/Inventory/Inventory/Management/ProductInventory.cs` (aggregate, 234 LOC)
- `src/Inventory/Inventory/Management/InventoryTransfer.cs` (aggregate, 49 LOC)
- `src/Inventory/Inventory/Management/InventoryStreamId.cs:13` (UUID v5 stream id)
- All command/event/handler files under `src/Inventory/Inventory/Management/` (referenced inline above)
- `src/Inventory/Inventory.Api/Program.cs:44-72` (projection registrations and async daemon)
- `src/Inventory/Inventory.Api/InventoryManagement/` (Wolverine HTTP endpoints)
- `src/Inventory/Inventory.Api/StockQueries/` (Wolverine HTTP query endpoints)
- `src/Inventory/Inventory.Api/DeadLetterQueueLogSink.cs` (DLQ wiring referenced by ADR 0060)
- `src/Shared/Messages.Contracts/Inventory/` (13 integration message contracts)
- `tests/Inventory/Inventory.UnitTests/Management/` (decider, projection, policy, stream-id tests)
- `tests/Inventory/Inventory.Api.IntegrationTests/` (endpoint, flow, and reliability suites)
- `docs/features/inventory/` (4 Gherkin files, 53 scenarios)
- `CONTEXTS.md` (section: `Inventory`)
- `docs/decisions/0016-uuid-v5-for-natural-key-stream-ids.md`
- `docs/decisions/0060-inventory-bc-remaster-rationale.md`
- `docs/decisions/0061-cross-product-exchange-replacement-reservation.md`
- `docs/planning/inventory-remaster-slices.md`
- `docs/planning/inventory-remaster-phase-3-storyboarding.md`
