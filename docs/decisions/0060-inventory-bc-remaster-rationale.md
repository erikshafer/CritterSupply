# ADR 0060 — Inventory BC Remaster Rationale

**Status:** Accepted
**Date:** 2026-04-08
**Milestone:** Inventory BC Remaster — Event Modeling Session (M42.0)

---

## Context

The Inventory BC was built as one of CritterSupply's early bounded contexts. It shipped with a
minimal, functional event model: 7 domain events (`InventoryInitialized`, `StockReserved`,
`ReservationCommitted`, `ReservationReleased`, `StockReceived`, `StockRestocked`,
`InventoryAdjusted`), 7 `Apply()` methods, no failure modes, and a single hardcoded warehouse
(`WH-01`). This was appropriate for establishing the Orders saga integration, but it does not
model the actual complexity of warehouse inventory management.

The Fulfillment BC Remaster (M41.0, ADR 0059) surfaced 9 concrete gaps in the Inventory BC, rated
by severity. Two are 🔴 Critical (hardcoded WH-01 and no multi-warehouse allocation), four are
🟡 Medium, and two are 🟠 Low. The remastered Fulfillment BC's routing engine requires Inventory
to expose per-warehouse stock availability — a capability that does not exist today.

### Key Problems

1. **Hardcoded `WH-01`** — `OrderPlacedHandler.cs` contains `const string defaultWarehouse = "WH-01"`,
   making multi-warehouse routing impossible. The Fulfillment routing engine cannot query or use
   per-warehouse stock data.

2. **No routing integration surface** — Inventory has no HTTP endpoint or projection that provides
   cross-warehouse availability for a SKU. The routing engine needs this to make informed decisions.

3. **Missing physical operation tracking** — No concept of stock being physically picked from bins
   or handed to a carrier. `TotalOnHand` never decrements in the current model.

4. **`StockReceived` vs `StockRestocked` ambiguity** — Both `Apply()` methods are identical.
   Whether these are semantically distinct was unresolved.

5. **MD5 stream IDs** — `CombinedGuid()` uses MD5, violating ADR 0016 (UUID v5 standard).

6. **No failure modes** — No reservation expiry, no short pick detection, no cycle count workflows,
   no backorder tracking, no inter-warehouse transfers.

## Decision

We conducted a full five-phase Event Modeling workshop to produce a complete domain model for the
Inventory BC. This is a **remaster** — same domain purpose, same BC boundaries, but designed from
first principles with proper depth.

### Key Architectural Decisions Made During the Session

#### 1. Routing Integration — Fulfillment-Initiated Reservation (Option A)

**The new flow:**

1. Orders publishes `OrderPlaced` → triggers Payments only (Inventory no longer subscribes)
2. Orders publishes `FulfillmentRequested` → Fulfillment
3. Fulfillment's routing engine queries Inventory via `GET /api/inventory/availability/{sku}`
4. Fulfillment selects a warehouse → publishes `FulfillmentCenterAssigned`
5. Fulfillment sends `StockReservationRequested` → Inventory (carries WarehouseId)
6. Inventory reserves at the designated warehouse → publishes `ReservationConfirmed`
7. Orders saga receives `ReservationConfirmed` (unchanged contract)

**`OrderPlacedHandler` is retired.** It does not survive in any form post-migration.

**Migration strategy (Dual-Publish per bc-remaster.md):**
- Phase 1 (Inventory remaster S1): Add `StockReservationRequested` handler + `StockAvailabilityView`
  projection + HTTP endpoint. Keep `OrderPlacedHandler` alive as dual-publish bridge.
- Phase 2 (coordinated Orders + Fulfillment session): Modify Orders saga to send
  `FulfillmentRequested` before reservation. Fulfillment routes and sends
  `StockReservationRequested`. Remove `OrderPlacedHandler`.

**Rationale:** Option B (Inventory waits for `FulfillmentCenterAssigned`) creates temporal coupling —
Inventory would need to park the `OrderPlaced` message until routing completes, which is saga
territory. Option A keeps each BC stateless and reactive.

#### 2. `StockAvailabilityView` — Inline Multi-Stream Projection

The routing engine needs real-time stock availability. The `StockAvailabilityView` is a Marten
`MultiStreamProjection<StockAvailabilityView, string>` keyed by SKU string, aggregating across
all `ProductInventory` streams for the same SKU at different warehouses.

**Why inline (not async)?** The routing engine is on the critical checkout path. Stale availability
data leads to double-booking. The inline projection updates within the same transaction as the
event append.

**Shape:**
```
StockAvailabilityView {
    Sku: string
    Warehouses: [{ WarehouseId, AvailableQuantity }]
    TotalAvailable: int (computed)
}
```

#### 3. `StockReceived` vs `StockRestocked` — Keep Separate

**Product Owner tiebreaker:** These are different operational events with different paperwork,
actors, and audit trails.

| Event | Trigger | Key Payload | Audit Purpose |
|---|---|---|---|
| `StockReceived` | Supplier PO delivery | SupplierId, PurchaseOrderId | PO variance, supplier compliance |
| `StockRestocked` | Return inspection passes | ReturnId | Return rate analytics |
| `TransferReceived` (new) | Inter-warehouse transfer arrives | TransferId, FromWarehouseId | Transfer reconciliation |

All three add to `AvailableQuantity`, but carry different traceability data. `StockReceived.Source`
(the current freeform string) is replaced with structured `SupplierId` since the transfer case
now has its own event.

#### 4. `ItemPicked` Integration — Inventory Receives Physical Facts

Inventory subscribes to two Fulfillment integration events for physical operations:

- **`ItemPicked`** → Inventory appends `StockPicked` — moves from CommittedAllocations to
  PickedAllocations. Item is physically off the shelf but still inside the warehouse. TotalOnHand
  unchanged.
- **`ShipmentHandedToCarrier`** → Inventory appends `StockShipped` — removes from
  PickedAllocations. Item has physically left the building. TotalOnHand decremented.

**New aggregate state bucket:** `PickedAllocations` dictionary tracks items between physical pick
and carrier handoff. `TotalOnHand = Available + Reserved + Committed + Picked`.

**Authority model:** Inventory is the system of record for quantity. Fulfillment reports physical
facts (pick, ship). If `ItemPicked` reports a quantity that diverges from committed (short pick),
Inventory appends `StockDiscrepancyFound`.

#### 5. `InventoryTransfer` — New Aggregate

Inter-warehouse transfers are modeled as a separate `InventoryTransfer` aggregate with a three-step
lifecycle: `TransferRequested` → `TransferShipped` → `TransferReceived`.

**Why a separate aggregate?** A transfer affects two `ProductInventory` streams (source and
destination) — it cannot be a single event on one aggregate. The `InventoryTransfer` aggregate
coordinates the lifecycle, and inline `bus.InvokeAsync()` calls trigger `StockTransferredOut` on
the source and `TransferReceived` on the destination (per bc-remaster.md Inline Policy
Invocation Pattern — Wolverine cannot cascade from `session.Events.Append()`).

**In-transit stock:** Between `TransferShipped` and `TransferReceived`, stock is not available at
either location. The source `ProductInventory` debits on `StockTransferredOut`; the destination
credits on `TransferReceived`.

#### 6. Backorder Notification — Option A (Inventory Subscribes)

Inventory subscribes to `BackorderCreated` from Fulfillment. When stock arrives for a backordered
SKU, Inventory publishes `BackorderStockAvailable` to Fulfillment, enabling automatic re-routing.

**Contract enrichment required:** `BackorderCreated` must carry SKU information (currently only has
OrderId, ShipmentId, Reason). Adding `Items: IReadOnlyList<BackorderedItem>` is a breaking change
coordinated with the Fulfillment implementation milestone.

#### 7. UUID v5 Stream IDs — Clean Slate Migration

`CombinedGuid()` (MD5) is replaced by UUID v5 via `InventoryStreamId.Compute(sku, warehouseId)`.
Uses the URL namespace UUID with `inventory:{SKU}:{WarehouseId}` input, matching the pattern
established by Listings BC (`ListingStreamId.cs`).

**Migration:** Clean slate is acceptable for CritterSupply. Seed data recreates all inventory
with UUID v5 stream IDs.

## Rationale

A "remaster" approach was chosen over incremental feature addition because:

1. **The routing integration is a cross-cutting concern.** The hardcoded WH-01 cannot be fixed
   incrementally — it requires redesigning the reservation flow with Fulfillment involvement.

2. **The existing event model was too thin.** 7 events with no failure modes cannot support the
   warehouse operational complexity that Fulfillment's remastered routing engine depends on.

3. **The Fulfillment remaster surfaced 9 concrete gaps.** These gaps are interconnected — the
   routing fix (Gap #1) requires the availability projection (Gap #2), which requires accurate
   stock tracking (Gap #6), which requires physical pick/ship events (implicit in Gap #6).

4. **No feature files existed.** Unlike Fulfillment (which had 3 feature files as domain input),
   Inventory had zero. The remaster session creates the domain specification from first principles.

## Consequences

### Positive

- Complete event model with 42 slices (12 P0, 12 P1, 11 P2, 7 P3) covering stock lifecycle,
  reservations, routing integration, failure modes, transfers, and quarantine
- `StockAvailabilityView` unlocks Fulfillment's routing engine (per ADR 0059 Q5)
- Physical pick/ship tracking enables bin reconciliation with Fulfillment
- Feature files created from scratch — `docs/features/inventory/` now has 4 feature files
- Backorder tracking enables automated replenishment trigger
- UUID v5 stream IDs align with ADR 0016

### Negative

- Implementation milestone will be larger than typical feature work (~42 slices across 4+ sessions)
- Orders saga coordination (Phase 2 of routing migration) requires a separate session
- `BackorderCreated` contract enrichment is a breaking change with Fulfillment
- `StockAvailabilityView` as inline multi-stream projection adds query load to the event append path

### Neutral

- Inter-warehouse transfers (P2) can be implemented independently of routing fix (P0)
- Bin-level tracking (P3) and lot tracking (P3+) are cleanly deferred
- Existing integration contracts to Orders (ReservationConfirmed, etc.) are unchanged
- `OrderPlacedHandler` retirement is phased — no big-bang migration required

## Alternatives Considered

### 1. Fix Only the WH-01 Hardcode

Pass `WarehouseId` through from Orders without redesigning the flow.

**Rejected because:** Orders doesn't know the warehouse at reservation time. The routing decision
happens in Fulfillment, which runs after reservation in the current flow. The sequencing is
fundamentally wrong — not just a parameter change.

### 2. Inventory Owns the Routing Decision

Inventory picks the warehouse based on its own stock data when `OrderPlaced` arrives.

**Rejected because:** Routing depends on warehouse proximity to shipping address, capacity,
SLA constraints, and 3PL considerations — none of which are Inventory's domain. ADR 0059
established that routing lives in Fulfillment. Inventory provides data; Fulfillment decides.

### 3. Single `StockIncreased` Event for All Receipt Types

Merge `StockReceived`, `StockRestocked`, and `TransferReceived` into one event.

**Rejected because:** Audit trail requirements differ. PO variance tracking (supplier compliance),
return rate analytics, and transfer reconciliation each need distinct traceability fields. The
Product Owner confirmed warehouse clerks treat these as different operational processes with
different paperwork.

### 4. Keep MD5 Stream IDs

Continue using `CombinedGuid()` with MD5 since it works and existing data uses it.

**Rejected because:** ADR 0016 established UUID v5 as the standard. MD5 has known collision risks
and is deprecated for cryptographic use. The Listings BC pattern provides a reference
implementation. Clean slate migration is acceptable for CritterSupply.
