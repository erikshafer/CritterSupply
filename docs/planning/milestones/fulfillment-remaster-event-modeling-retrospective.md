# Fulfillment BC Remaster — Event Modeling Session Retrospective

**Session Date:** 2026-04-06  
**Session Type:** Event Modeling Workshop — Full Five-Phase Process  
**Facilitator:** @event-modeling-facilitator  
**Participants:** @product-owner, @principal-architect, @qa-engineer, @ux-engineer  
**Duration:** Full session  
**ADR Produced:** [ADR 0059 — Fulfillment BC Remaster Rationale](../../decisions/0059-fulfillment-bc-remaster-rationale.md)

---

## Session Summary

This session conducted a complete Event Modeling remaster of the Fulfillment BC. The term "remaster" means: same domain, same BC purpose, but redesigned from first principles using event modeling. The output is a complete event model — not code.

### Quantitative Outcomes

| Metric | Value |
|---|---|
| Candidate events (Phase 1) | 77 |
| Events after deduplication | 77 (no duplicates — clean brain dump) |
| Swim lanes in timeline | 4 (Warehouse, Label/Carrier, Transit/Delivery, International) |
| Read models identified | 10 |
| Total slices (Phase 4) | 46 |
| P0 slices (foundation) | 15 |
| P1 slices (failure modes) | 14 |
| P2 slices (compensation/advanced) | 10 |
| P3 slices (international) | 7 |
| Scenarios written (Phase 5) | 28 (all P0 + P1 slices covered) |
| Aggregates in remastered model | 2 (`WorkOrder` + `Shipment`) |
| Integration contract changes identified | 8 new events, 2 replacements, 1 removal |
| Inventory gaps surfaced | 9 |

---

## Mandatory Open Questions — Answers

### Q1: One Stream or Two?

**Answer: Two aggregates — `WorkOrder` and `Shipment`.**

The `WorkOrder` aggregate owns the warehouse lifecycle (`WorkOrderCreated` through `PackingCompleted`). The `Shipment` aggregate owns routing decisions (`FulfillmentRequested`, `FulfillmentCenterAssigned`) and the carrier lifecycle (`ShippingLabelGenerated` through terminal delivery states).

**Reasoning:** The Product Owner confirmed that warehouse operations teams think in work orders, not shipments. A work order can be cancelled and recreated at a different FC during rerouting — the shipment identity persists. The Principal Architect argued that the warehouse and carrier domains have different consistency boundaries, different actors, and scale independently. The two-aggregate split at the warehouse/carrier boundary captures the real domain separation.

**Dissent:** The Principal Architect noted that one aggregate would be simpler for CritterSupply's current scale. The UX Engineer pointed out that the customer sees one entity ("my shipment"), not two. However, the internal operational reality favors two aggregates, and the customer-facing view is handled by a projection that spans both streams.

### Q2: When Does Inventory Commit From the Warehouse Perspective?

**Answer: `ItemPicked` is the physical commit; `ReservationCommitted` remains the logical commit.**

These are two different business facts in two different bounded contexts:
- `ReservationCommitted` (Inventory BC) = "stock is logically reserved for this order" — triggered by Orders saga after payment
- `ItemPicked` (Fulfillment BC) = "a human has physically removed the item from the bin" — recorded by picker RF scanner

The Orders saga does not change — it continues to react to `ReservationCommitted`. Fulfillment publishes `ItemPicked` to Inventory for physical bin count reconciliation. This is a new integration event.

### Q3: `DeliveryAttemptFailed` Is Not Terminal — Model It Explicitly

**Answer:** `DeliveryAttemptFailed` carries an `AttemptNumber` (1, 2, or 3). Each attempt is a separate event on the `Shipment` stream. The `Shipment` aggregate tracks `DeliveryAttemptCount`.

The transition chain is:
```
DeliveryAttemptFailed(1) → DeliveryAttemptFailed(2) → DeliveryAttemptFailed(3) → ReturnToSenderInitiated
```

`ReturnToSenderInitiated` is a carrier-side automatic process (not a CritterSupply decision). CritterSupply detects it via carrier webhook.

`ShipmentDeliveryFailed` as a terminal event is **removed** from the model. It was always incorrect — delivery failure is a multi-step process, not a single terminal event.

### Q4: `ReshipmentCreated` Identity

**Answer: `ReshipmentCreated` is an event on the original `Shipment` stream. The reshipment itself is a new `Shipment` stream.**

- Original stream receives `ReshipmentCreated { NewShipmentId }` and transitions to terminal state (`Lost — Replacement Shipped`)
- New stream starts with `FulfillmentRequested { OriginalShipmentId }` (flagged as reshipment)
- Orders saga learns about the reshipment through the `ReshipmentCreated` integration event published from the original stream

**Reasoning:** A reshipment is physically a different package (different tracking number, potentially different FC). It needs its own lifecycle stream. The original stream needs the `ReshipmentCreated` event for audit trail and customer-facing status display ("Original: Lost — Replacement Shipped / Replacement: In progress").

### Q5: Where Does Warehouse Routing Live?

**Answer: Routing lives in Fulfillment.**

The event is `FulfillmentCenterAssigned` on the `Shipment` stream. The routing decision ("which FC fulfills this shipment?") is a Fulfillment concern. Inventory provides stock availability data (queried, not commanded), but Inventory does not make the routing decision.

The `WH-01` hardcode in Inventory's `OrderPlacedHandler` becomes irrelevant post-remaster. The routing flow becomes:
1. Orders saga sends `FulfillmentRequested` to Fulfillment
2. Fulfillment's routing engine queries Inventory for stock availability per FC
3. Fulfillment appends `FulfillmentCenterAssigned` and creates a `WorkOrder` at the chosen FC
4. The `WarehouseId` is passed through to Inventory via the reservation request flow

### Q6: Inventory Gaps Surfaced

Nine gaps were identified during the session. See the complete **Inventory Gap Register** below.

---

## Inventory Gap Register

This register feeds the Inventory BC Remaster (the next milestone in the remaster series).

| # | Gap | Severity | Current State | Required State | Surfaced During |
|---|---|---|---|---|---|
| 1 | **Hardcoded `WH-01` warehouse** | 🔴 Critical | `const string defaultWarehouse = "WH-01"` in `OrderPlacedHandler.cs` | Routing decision from Fulfillment provides `WarehouseId` | Phase 2 — routing discussion |
| 2 | **No multi-warehouse allocation** | 🔴 Critical | Single `ProductInventory` aggregate per SKU | Per-warehouse stock levels: `ProductInventory` keyed by `(SKU, WarehouseId)` | Phase 2 — multi-FC routing |
| 3 | **`StockReceived` vs `StockRestocked` redundancy** | 🟡 Medium | Both events do identical things | Clarify: `StockReceived` = PO receipt, `StockRestocked` = internal transfer/return | Phase 1 |
| 4 | **No `InventoryTransferred` event** | 🟡 Medium | No inter-warehouse transfer concept | Need `InventoryTransferred { FromWarehouse, ToWarehouse, SKU, Qty }` | Phase 2 — reroute |
| 5 | **`ProductInventory` stream ID uses MD5** | 🟠 Low | MD5-based deterministic ID | UUID v5 per ADR 0016 | Phase 2 |
| 6 | **Reservation commit timing ambiguity** | 🟡 Medium | `ReservationCommitted` fires from Orders saga | `ItemPicked` from Fulfillment = physical commit; Inventory should receive for bin reconciliation | Phase 2 — Q2 |
| 7 | **No bin-level inventory tracking** | 🟡 Medium | Stock levels at warehouse level only | WMS needs bin-level quantities for pick optimization and short pick detection | Phase 3 |
| 8 | **No backorder notification to Inventory** | 🟡 Medium | `BackorderCreated` not published to Inventory | Inventory should flag SKU for replenishment when all FCs report zero stock | Phase 2 |
| 9 | **No capacity data exposure** | 🟠 Low | Inventory has no API for warehouse capacity queries | Routing engine needs `FulfillmentCenterCapacityView` fed by Inventory data | Phase 3 |

---

## Integration Contract Changes Summary

### Events Replaced

| Current Event | Replacement | Reason |
|---|---|---|
| `ShipmentDispatched` | `ShipmentHandedToCarrier` | More precise — physical custody transfer vs. vague "dispatched" |
| `ShipmentDeliveryFailed` | `ReturnToSenderInitiated` | Delivery failure is multi-attempt, not terminal at first failure |

### New Integration Events (Fulfillment → Orders)

| Event | Purpose |
|---|---|
| `TrackingNumberAssigned` | Customer-facing tracking number |
| `DeliveryAttemptFailed` | Customer notification of each delivery attempt |
| `GhostShipmentDetected` | Investigation notification |
| `ShipmentLostInTransit` | Triggers reshipment flow |
| `ReturnToSenderInitiated` | Carrier returning package |
| `ReshipmentCreated` | Orders saga learns about replacement shipment |

### New Integration Events (Fulfillment → Inventory)

| Event | Purpose |
|---|---|
| `ItemPicked` | Physical bin count reconciliation |
| `ShipmentRerouted` | Stock availability re-check at new FC |

---

## What Went Well

1. **Feature files as domain input.** The three existing feature files provided exceptional domain detail. Using them as "domain expert input" during Phase 1 accelerated the brain dump and grounded the session in realistic workflows.

2. **Aggregate boundary decision.** The two-aggregate split (`WorkOrder` + `Shipment`) was the most productive argument of the session. The Product Owner's tiebreaker ("warehouse teams think in work orders, not shipments") was decisive and well-grounded in operational reality.

3. **QA Engineer's gap-finding.** The QA Engineer's systematic gap analysis between adjacent events in Phase 2 surfaced critical missing events: `ShippingLabelGenerationFailed`, `CarrierRelationsEscalated`, the explicit delivery attempt chain, and the ghost shipment detection pattern.

4. **Inventory gaps emerged naturally.** The session wasn't about Inventory, but 9 concrete gaps surfaced from modeling Fulfillment's interactions. This validates the remaster-series approach — each BC remaster surfaces gaps in adjacent BCs.

## What Could Improve

1. **International scope (P3) needs its own session.** The 7 international slices are underspecified compared to P0–P2. Customs documentation, USMCA certificates, and duty refusal flows warrant deeper event modeling with trade compliance expertise.

2. **Customer notifications are modeled but ownership is unclear.** Several scenarios describe customer emails, but it's unclear whether Fulfillment publishes events that Correspondence consumes, or whether Fulfillment triggers notifications directly. This needs resolution before implementation.

3. **3PL handoff (TX FC) is undermodeled.** The feature files mention a 3PL partner in Dallas, TX with different SLAs (8 hours vs. 4 hours standard). The 3PL integration pattern (API vs. EDI vs. file drop) was not explored in this session.

---

## Artifacts Produced

| Artifact | Location | Status |
|---|---|---|
| Slice table (Phase 1–5 complete) | `docs/planning/fulfillment-remaster-slices.md` | ✅ Complete |
| ADR 0059 — Fulfillment BC Remaster Rationale | `docs/decisions/0059-fulfillment-bc-remaster-rationale.md` | ✅ Complete |
| Session retrospective (this document) | `docs/planning/milestones/fulfillment-remaster-event-modeling-retrospective.md` | ✅ Complete |
| Updated feature files | `docs/features/fulfillment/*.feature` | ✅ Updated |
| CONTEXTS.md — Fulfillment entry | `CONTEXTS.md` | ✅ Updated |

---

## Next Steps

1. **Implementation milestone** — Create a milestone for implementing the remastered model (P0 slices first, then P1)
2. **Inventory BC Remaster** — Use the Inventory Gap Register as the charter for the next event modeling session
3. **Orders saga updates** — Plan the integration contract changes (new message handlers for `TrackingNumberAssigned`, `DeliveryAttemptFailed`, `ReturnToSenderInitiated`, `ReshipmentCreated`)
4. **International deep-dive** — Schedule a focused session for P3 international slices with trade compliance input
