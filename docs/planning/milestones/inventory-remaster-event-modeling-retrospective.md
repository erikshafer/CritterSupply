# Inventory BC Remaster — Event Modeling Session Retrospective

**Session Date:** 2026-04-08
**Session Type:** Event Modeling Workshop — Full Five-Phase Process
**Facilitator:** @event-modeling-facilitator
**Participants:** @product-owner, @principal-architect, @qa-engineer, @ux-engineer
**Duration:** Full session
**ADR Produced:** [ADR 0060 — Inventory BC Remaster Rationale](../../decisions/0060-inventory-bc-remaster-rationale.md)

---

## Session Summary

This session conducted a complete Event Modeling remaster of the Inventory BC. Unlike the
Fulfillment Remaster (which had 3 existing feature files as domain input), Inventory had zero
feature files — this session established the Inventory domain model from first principles, using
the 9-gap Inventory Gap Register from the Fulfillment Remaster as the structured charter.

### Quantitative Outcomes

| Metric | Value |
|---|---|
| Candidate events (Phase 1) | 55+ |
| Events after deduplication | 48 (7 removed as duplicates or Fulfillment-owned) |
| Swim lanes in timeline | 3 (Stock Lifecycle, Reservation Lifecycle, Routing/Query) |
| Read models identified | 9 |
| Total slices (Phase 4) | 42 |
| P0 slices (foundation) | 12 |
| P1 slices (failure modes) | 12 |
| P2 slices (transfers/advanced) | 11 |
| P3+ slices (deferred) | 7 |
| Scenarios written (Phase 5) | 55 (all P0 + P1 slices covered, P2 quarantine sampled) |
| Aggregates in remastered model | 2 (`ProductInventory` remastered + `InventoryTransfer` new) |
| Integration contract changes identified | 3 new events, 2 enriched, 1 retirement |
| Feature files created | 4 (from scratch — `docs/features/inventory/`) |

---

## Mandatory Open Questions — Answers

### Q1: Routing Integration — What Is the Exact New Flow?

**Answer: Fulfillment-initiated reservation (Option A).**

The current `OrderPlacedHandler` is retired. The new flow:

1. Orders → `FulfillmentRequested` → Fulfillment (early, before reservation)
2. Fulfillment routing engine queries `GET /api/inventory/availability/{sku}`
3. Fulfillment selects warehouse → `FulfillmentCenterAssigned`
4. Fulfillment sends `StockReservationRequested` → Inventory (with WarehouseId)
5. Inventory reserves at designated warehouse → `ReservationConfirmed` → Orders

**`OrderPlacedHandler` does not survive** post-migration. During Phase 1 it remains as a
dual-publish bridge. Phase 2 (coordinated Orders + Fulfillment session) removes it.

The **`StockAvailabilityView`** is an inline multi-stream Marten projection keyed by SKU,
aggregating per-warehouse `AvailableQuantity`. Inline (not async) because the routing engine
is on the critical checkout path — stale data leads to double-booking.

**Dissent:** The Product Owner asked whether early `FulfillmentRequested` (before payment) creates
risk of routing work for orders that won't pay. The Principal Architect noted this is
acceptable — routing is cheap (a query), and the alternative (reserving at a hardcoded warehouse
then re-routing) is architecturally unsound. Fulfillment can optimistically route; if payment
fails, the reservation is released.

### Q2: `StockReceived` vs `StockRestocked`

**Answer: Keep separate. They are different domain facts.**

The Product Owner provided the tiebreaker, grounded in warehouse operational reality:

| Event | Actor | Paperwork | Trigger | Audit |
|---|---|---|---|---|
| `StockReceived` | Receiving clerk | PO, BOL, packing slip | Supplier delivery truck | PO variance, supplier compliance |
| `StockRestocked` | Returns inspector | Return inspection form | Return passes QA | Return rate analytics |
| `TransferReceived` (new) | Receiving clerk | Transfer manifest | Inter-warehouse truck | Transfer reconciliation |

All three add to `AvailableQuantity`, but they carry different traceability data. The
current `StockReceived.Source` (freeform string) is replaced with `SupplierId` since
transfers and returns now have their own events.

**@product-owner quote:** "Three different people with three different forms walk up to that dock.
The inventory system must know which kind of receipt it was."

### Q3: `ItemPicked` — Does Inventory Receive It?

**Answer: Yes. Inventory receives both `ItemPicked` and `ShipmentHandedToCarrier`.**

These create two new domain events on `ProductInventory`:

| Integration Event | Inventory Domain Event | State Change |
|---|---|---|
| `ItemPicked` | `StockPicked` | Committed → Picked (bin reconciliation) |
| `ShipmentHandedToCarrier` | `StockShipped` | Picked removed, TotalOnHand decremented |

**Why not decrement TotalOnHand at pick?** Because the item is still physically in the warehouse
(on a pick cart, at pack station, or staged). The warehouse clerk doing a physical count would
still see it. TotalOnHand should reflect "stock that is physically present."

**Authority model:** Inventory is the system of record for quantity. Fulfillment reports physical
facts. If `ItemPicked` reports a quantity different from committed (short pick), Inventory
appends `StockDiscrepancyFound`.

**New aggregate bucket:** `PickedAllocations` dictionary.
`TotalOnHand = Available + Reserved + Committed + Picked`.

### Q4: `InventoryTransferred` Lifecycle

**Answer: Separate `InventoryTransfer` aggregate with three-step lifecycle.**

`TransferRequested` → `TransferShipped` → `TransferReceived`

The `InventoryTransfer` aggregate coordinates the transfer, while inline `bus.InvokeAsync()` calls
trigger `StockTransferredOut` on the source `ProductInventory` and `TransferReceived` on the
destination (per bc-remaster.md Inline Policy Invocation Pattern).

**In-transit stock:** Between `TransferShipped` and `TransferReceived`, stock is not available at
either location. This is tracked as InTransitOut on the source and InTransitIn on the
destination via the `WarehouseSkuDetailView` read model.

**@principal-architect note:** The multi-aggregate coordination uses `bus.InvokeAsync()` (not
Wolverine cascading) because events written via `session.Events.StartStream()` cannot cascade
to downstream handlers — same limitation documented in Fulfillment remaster.

### Q5: Backorder Notification

**Answer: Option A — Inventory subscribes to `BackorderCreated`.**

When stock arrives for a backordered SKU, Inventory publishes `BackorderStockAvailable` to
Fulfillment for automatic re-routing.

**Contract enrichment required:** `BackorderCreated` must carry `Items: IReadOnlyList<BackorderedItem>`
with SKU and Quantity. Currently it only has OrderId, ShipmentId, Reason. This is a breaking
change coordinated with the Fulfillment implementation milestone.

**Downstream effects:**
- `BackorderRegistered` domain event on the `ProductInventory` stream
- `HasPendingBackorders` flag on the aggregate
- `BackorderImpactView` read model for operations dashboard
- On stock arrival: `BackorderCleared` + `BackorderStockAvailable` integration event

### Q6: Adjacent BC Gaps Surfaced

Four new gaps were surfaced during this session (added to the gap register below).

---

## Standard Questions — Answers

### Notification Ownership

**Answer:** Inventory publishes `LowStockThresholdBreached` and `LowStockDetected` (integration
event). Backoffice and Vendor Portal consume these. Inventory does NOT push directly to warehouse
operators — that's the Backoffice BFF's job (established CritterSupply pattern).

### Multi-Aggregate Coordination

**Answer:** `InventoryTransfer` coordinates source and destination `ProductInventory` aggregates.
The `TransferRequested` handler uses `bus.InvokeAsync()` to debit the source immediately. The
`TransferReceived` handler uses `bus.InvokeAsync()` to credit the destination.

In-transit stock is NOT tracked on either `ProductInventory` aggregate directly. The
`WarehouseSkuDetailView` read model computes InTransitIn/Out from the `InventoryTransfer`
stream events.

### P3+ Scope Deferral

**Confirmed deferred to P3+:**
- Bin-level tracking (Gap #7) — requires WMS hardware integration (RF scanners, bin management)
- `FulfillmentCenterCapacityView` (Gap #9) — routing engine can use stock availability as proxy
  for now; true capacity data requires space/volume modeling
- Lot/batch tracking — regulated pet food/medication; not in current product line
- Expiration date tracking — perishable goods; FEFO (First Expired, First Out) logic
- Demand forecasting — ML/statistical models for reorder point calculation
- Vendor returns — outbound return-to-supplier lifecycle; requires Purchasing BC

### Cross-BC Integration Test Gate

**Orders BC:** 55 integration tests + 144 unit tests — must stay green throughout implementation.
**Fulfillment BC:** 78 integration tests + 40 unit tests — must stay green throughout.
**Inventory BC:** Current test count to be verified at S1 start.

The dual-publish bridge (Slice 12) ensures Orders saga tests pass during Phase 1. Phase 2
(coordinated update) must run the full cross-BC test suite as a mandatory gate.

---

## Inventory Gap Register — Final State

### Pre-Session Gaps (Resolved)

| # | Gap | Severity | Resolution | Slice |
|---|---|---|---|---|
| 1 | Hardcoded WH-01 | 🔴 Critical | `OrderPlacedHandler` retired; `StockReservationRequested` carries WarehouseId | P0 Slices 4-5, 12 |
| 2 | No multi-warehouse allocation | 🔴 Critical | `StockAvailabilityView` projection + HTTP endpoint | P0 Slices 2-3 |
| 3 | StockReceived vs StockRestocked | 🟡 Medium | Keep separate; add `TransferReceived` as third variant | P0 Slices 8-9 |
| 4 | No InventoryTransferred event | 🟡 Medium | New `InventoryTransfer` aggregate | P2 Slices 25-29 |
| 5 | MD5 stream ID | 🟠 Low | UUID v5 via `InventoryStreamId.Compute()` — clean slate | P0 Slice 1 |
| 6 | Reservation commit timing | 🟡 Medium | `StockPicked` + `StockShipped` events from Fulfillment integration | P1 Slices 13-14 |
| 7 | No bin-level tracking | 🟡 Medium | Deferred to P3 (WMS hardware integration) | P3 Slice 36 |
| 8 | No backorder notification | 🟡 Medium | Subscribe to `BackorderCreated`; publish `BackorderStockAvailable` | P1 Slices 18-19 |
| 9 | No capacity data exposure | 🟠 Low | Deferred to P3 | P3 Slice 39 |

### New Gaps Surfaced During This Session

| # | Gap | Severity | Description | Target BC |
|---|---|---|---|---|
| 10 | `BackorderCreated` contract needs SKU data | 🟡 Medium | Current contract lacks Items/SKU; Inventory handler needs SKU to register backorder | Fulfillment |
| 11 | `ItemPicked` integration contract needs enrichment | 🟡 Medium | Needs WarehouseId and OrderId for Inventory correlation (currently only Sku, Quantity, BinLocation) | Fulfillment |
| 12 | Orders saga `FulfillmentRequested` timing | 🔴 Critical | Orders saga must send `FulfillmentRequested` before reservation (currently after). Requires coordinated update. | Orders |
| 13 | ConcurrencyException `.Discard()` policy | 🟡 Medium | Silently discarding failed reservations is worse than explicit failure. Change to `.MoveToErrorQueue()` or publish `ReservationFailed` | Inventory |

---

## Integration Contract Changes Summary

### New Integration Events

| Event | Direction | Purpose | Slice |
|---|---|---|---|
| `StockReservationRequested` | Fulfillment → Inventory | Routing-informed reservation request | P0 Slice 4 |
| `BackorderStockAvailable` | Inventory → Fulfillment | Re-routing trigger for backordered shipments | P1 Slice 19 |

### Enriched Contracts (Breaking Changes)

| Contract | Change | Coordinated With |
|---|---|---|
| `BackorderCreated` | Add `Items: IReadOnlyList<BackorderedItem>` | Fulfillment implementation |
| `ItemPicked` (integration) | Add `WarehouseId`, `OrderId` | Fulfillment implementation |

### New Subscriptions in Inventory

| Contract | Publisher | Purpose |
|---|---|---|
| `ShipmentHandedToCarrier` | Fulfillment | TotalOnHand decrement |
| `BackorderCreated` (enriched) | Fulfillment | Backorder tracking |
| `StockReservationRequested` | Fulfillment | Routing-informed reservation |
| `ItemPicked` (enriched) | Fulfillment | Physical pick reconciliation |

### Retired Contracts

| Contract | Phase | Reason |
|---|---|---|
| `OrderPlaced` subscription in Inventory | Phase 2 (coordinated) | Replaced by `StockReservationRequested` |

### Unchanged Contracts

All existing Inventory → Orders contracts (`ReservationConfirmed`, `ReservationFailed`,
`ReservationCommitted`, `ReservationReleased`) and Inventory → Vendor Portal contracts
(`InventoryAdjusted`, `LowStockDetected`, `StockReplenished`) remain unchanged.

---

## What Went Well

1. **Gap Register as charter.** The 9 gaps from the Fulfillment Remaster provided exceptional
   structure for the session. Each gap became a concrete design question with a clear resolution.
   This validates the remaster-series approach.

2. **Product Owner tiebreaker on stock receipt events.** The "three different people with three
   different forms" argument was decisive and grounded in operational reality. The architectural
   team was leaning toward merging; the domain expert corrected this.

3. **Routing integration resolution.** The five design questions forced a thorough analysis of the
   reservation flow sequencing problem. Option A (Fulfillment-initiated reservation) was not
   obvious until the flow diagram revealed the temporal coupling in Option B.

4. **QA gap-finding depth.** 23+ edge cases identified in Phase 2, including the
   `ShipmentHandedToCarrier` before `ItemPicked` out-of-order scenario and the concurrent
   reservation conflict behavior. These directly became P1 slice scenarios.

5. **Feature files from scratch.** Creating 4 feature files with 55 scenarios from zero is
   a significant domain specification deliverable. Future implementation sessions can
   reference these directly.

## What Could Improve

1. **Orders saga coordination is Phase 2 work.** The routing integration fix requires the
   Orders saga to send `FulfillmentRequested` earlier — this is a cross-BC coordination change
   that this session scoped but cannot implement alone. It needs its own dedicated session with
   Orders BC focus.

2. **Transfer lifecycle needs deeper modeling.** The `InventoryTransfer` aggregate (P2) was
   specified at a high level. Short receipt, transfer cancellation after ship, and transit
   timeout scenarios need more detail during the P2 implementation session.

3. **Per-SKU configurable thresholds deferred.** The hardcoded `LowStockThreshold = 10` is
   acknowledged but not addressed in P0/P1. Per-SKU and per-category thresholds require an
   admin workflow that doesn't exist yet.

4. **No Purchasing BC.** Backorder notification and replenishment triggering stop at "publish
   an event." There is no Purchasing BC to receive and act on it. The notification goes to
   Backoffice for manual action.

---

## Artifacts Produced

| Artifact | Location | Status |
|---|---|---|
| Slice table (42 slices) | `docs/planning/inventory-remaster-slices.md` | ✅ Complete |
| ADR 0060 — Inventory BC Remaster Rationale | `docs/decisions/0060-inventory-bc-remaster-rationale.md` | ✅ Complete |
| Session retrospective (this document) | `docs/planning/milestones/inventory-remaster-event-modeling-retrospective.md` | ✅ Complete |
| Feature files (4, from scratch) | `docs/features/inventory/*.feature` | ✅ Complete |
| CONTEXTS.md — Inventory entry | `CONTEXTS.md` | ✅ Updated |

---

## Implementation Session Progression (Recommended)

| Session | Scope | Slices | Key Deliverables |
|---|---|---|---|
| S1 | P0 Foundation | 1–12 | UUID v5 stream IDs, StockAvailabilityView, StockReservationRequested handler, receipt/restock/adjust, OrderPlacedHandler bridge |
| S2 | P1 Failure Modes | 13–24 | Physical pick/ship tracking, short pick detection, reservation expiry, backorder tracking, cycle counts |
| S3 | P2 Transfers + Advanced | 25–35 | InventoryTransfer aggregate, quarantine lifecycle, replenishment trigger, dashboards |
| S4 | Orders Saga Coordination | — | Phase 2 migration: Orders saga timing change, Fulfillment routing activation, OrderPlacedHandler removal |
| S5 | Cleanup + Dead Handler Sweep | — | Migration Closure Pattern: remove dead handlers, verify all test suites, update CONTEXTS.md |

---

## Next Steps

1. **Implementation milestone (M42.1+)** — Create milestones for S1 through S5
2. **Fulfillment contract enrichment** — Coordinate `BackorderCreated` and `ItemPicked`
   integration contract changes with Fulfillment implementation
3. **Orders saga coordination session** — Plan S4 with Orders BC focus (FulfillmentRequested
   timing change, dual-publish removal)
4. **Transfer deep-dive** — P2 implementation session should include additional transfer
   edge case scenarios
