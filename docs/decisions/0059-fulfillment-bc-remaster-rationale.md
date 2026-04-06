# ADR 0059 — Fulfillment BC Remaster Rationale

**Status:** Accepted  
**Date:** 2026-04-06  
**Milestone:** Fulfillment BC Remaster — Event Modeling Session  

---

## Context

The Fulfillment BC was built as one of CritterSupply's early bounded contexts. It shipped with a minimal, happy-path-only event model: 5 domain events (`FulfillmentRequested`, `WarehouseAssigned`, `ShipmentDispatched`, `ShipmentDelivered`, `ShipmentDeliveryFailed`), 4 `Apply()` methods, 5 statuses, and 4 HTTP endpoints. This was appropriate for establishing the vertical slice and proving the Critter Stack integration patterns, but it does not model the actual complexity of physical fulfillment.

The M39.0 code quality pass (Sessions S0–S5) resolved handler-level anti-patterns and modernized the Fulfillment codebase. However, M39.0 explicitly did not address the fundamental model gaps — the events were too few, the statuses too coarse, failure paths were absent, and compensation events did not exist.

Real-world e-commerce fulfillment involves:
- Multi-warehouse routing decisions (not a hardcoded `WH-01`)
- Warehouse operations (wave management, pick lists, RF scanner workflows, pack station verification)
- Carrier lifecycle management (label generation, manifest, staging, pickup, in-transit tracking)
- Multiple delivery attempts before return-to-sender
- Ghost shipment detection, lost-in-transit flows, carrier claims
- Reshipment creation as a compensation pattern
- International customs documentation, holds, seizures, and duty refusal
- SLA monitoring and escalation

The existing feature files (`docs/features/fulfillment/`) described this full domain in detail but remained almost entirely unimplemented. They represented domain expert knowledge that the codebase had not yet absorbed.

## Decision

We conducted a full five-phase Event Modeling workshop (brain dump → storytelling → storyboarding → identify slices → scenarios) to produce a complete, honest domain model for the Fulfillment BC. This is a **remaster** — same domain purpose, same BC boundaries, but designed from first principles with proper depth.

### Key Architectural Decisions Made During the Session

#### 1. Two Aggregates: `WorkOrder` and `Shipment`

The single `Shipment` aggregate is split into two:

- **`WorkOrder`** — owns the warehouse lifecycle from `WorkOrderCreated` through `PackingCompleted`. Represents internal warehouse operations (pick, pack, verify). Can be cancelled and recreated at a different FC without affecting shipment identity. Stream ID is derived from `(ShipmentId, FC)`.

- **`Shipment`** — owns the routing decision (`FulfillmentRequested`, `FulfillmentCenterAssigned`) and the carrier lifecycle (`ShippingLabelGenerated` through terminal delivery states). Represents the customer-facing shipment identity. Contains the tracking number, carrier information, and delivery status.

The boundary point is `PackingCompleted` on the `WorkOrder` stream, which triggers a policy handler that initiates the labeling/dispatch flow on the `Shipment` stream.

**Rationale:** The warehouse and carrier domains serve different actors (warehouse operators vs. customers), have different consistency requirements (internal operational vs. external integration), and scale independently. A warehouse system replacement should not require changes to carrier tracking logic. Work orders can be cancelled and recreated at different FCs during rerouting — this is a natural lifecycle that doesn't map to a single aggregate.

#### 2. `ShipmentDispatched` Replaced by `ShipmentHandedToCarrier`

The current `ShipmentDispatched` event conflates label generation, manifesting, staging, and carrier pickup into a single event. The remaster breaks this into discrete events: `ShippingLabelGenerated`, `TrackingNumberAssigned`, `ShipmentManifested`, `PackageStagedForPickup`, `CarrierPickupConfirmed`, `ShipmentHandedToCarrier`.

The integration event to Orders BC changes from `ShipmentDispatched` to `ShipmentHandedToCarrier` (physical custody transfer is the meaningful business fact).

#### 3. `ShipmentDeliveryFailed` Replaced by Explicit Attempt Chain

The current model has a single terminal `ShipmentDeliveryFailed` event. In reality, carriers attempt delivery up to three times. The remaster models each attempt as a separate `DeliveryAttemptFailed` event with an `AttemptNumber`. After the third attempt, the carrier initiates `ReturnToSenderInitiated`. This is not a CritterSupply decision — it's a carrier-side automatic process.

The integration event to Orders BC changes from `ShipmentDeliveryFailed` to `ReturnToSenderInitiated`.

#### 4. Reshipment Identity: New Stream, Event on Original

`ReshipmentCreated` is appended to the original `Shipment` stream (recording the fact that a replacement was created) and the reshipment starts a new `Shipment` stream with its own `FulfillmentRequested` event (flagged with `OriginalShipmentId`). The original stream transitions to a terminal state (`Lost — Replacement Shipped`).

**Rationale:** A reshipment is physically a different package with a different tracking number, fulfilling the same order. It needs its own stream for its own lifecycle. The original stream needs to know a replacement exists (for audit and customer-facing status).

#### 5. Routing Ownership: Fulfillment

The routing decision ("which FC fulfills this shipment?") belongs to Fulfillment, not Inventory. The event is `FulfillmentCenterAssigned`. Inventory provides stock availability data that the routing engine queries, but Inventory does not make the routing decision. The hardcoded `WH-01` in Inventory's `OrderPlacedHandler` is an Inventory BC gap (see Inventory Gap Register).

#### 6. `ItemPicked` ≠ `ReservationCommitted`

These are two different business facts. `ReservationCommitted` (Inventory BC, triggered by Orders saga after payment) means stock is logically reserved. `ItemPicked` (Fulfillment BC, recorded by picker RF scanner) means stock is physically removed from a bin. Both are authoritative for their respective domains. The Orders saga does not change — it continues to react to `ReservationCommitted`. Fulfillment publishes `ItemPicked` to Inventory for physical bin count reconciliation.

## Rationale

A "remaster" approach was chosen over incremental feature addition because:

1. **The existing event model was too thin to extend.** Adding failure paths to a 5-event model would require restructuring the aggregate anyway. It's cleaner to design the target model first and implement toward it.

2. **The feature files already contained domain expert knowledge.** The three feature files (warehouse picking/packing, dispatch/tracking, international fulfillment) represented months of business analysis. An event modeling session could absorb this knowledge into a structured, implementable model.

3. **Integration contracts need to evolve.** `ShipmentDispatched` is too coarse. `ShipmentDeliveryFailed` is incorrect (delivery failure isn't terminal). These contract changes are better planned holistically than discovered during implementation.

4. **The Inventory BC gaps surfaced directly from Fulfillment modeling.** The hardcoded warehouse, missing multi-warehouse allocation, and bin-level tracking gaps all became visible when modeling how Fulfillment actually routes and picks. This dependency chain is clearer from a complete model than from incremental work.

## Consequences

### Positive

- Complete event model with 77 candidate events, 46 slices, covering happy paths, failure modes, compensation, and international flows
- Aggregate boundary decision documented and justified (two aggregates vs. one)
- Integration contract changes identified and documented before implementation
- Inventory Gap Register provides a concrete charter for the next remaster session
- Feature files can be updated to align with the remastered model

### Negative

- The implementation milestone will be larger than a typical feature addition
- Integration contract changes (replacing `ShipmentDispatched` with `ShipmentHandedToCarrier`) require coordinated changes in Orders BC
- Two aggregates add operational complexity (policy handler bridging `WorkOrder` → `Shipment`)

### Neutral

- International scope (P3) may be deferred to a follow-on milestone
- Existing integration tests will need updating when the implementation milestone begins
- The `ShipmentStatus` enum grows from 5 to ~15 states across the two aggregates

## Alternatives Considered

### 1. Incremental Feature Addition

Add failure paths one at a time to the existing `Shipment` aggregate without restructuring.

**Rejected because:** The aggregate boundary question (one vs. two) affects every subsequent implementation. Answering it after partial implementation would require rework.

### 2. Keep Single Aggregate

Keep the `Shipment` aggregate but expand it with all 77 events.

**Rejected because:** A single aggregate with warehouse operations, carrier lifecycle, and delivery tracking events would grow to 25+ `Apply()` methods. The warehouse and carrier domains have different actors, consistency requirements, and lifecycle patterns. The Product Owner confirmed that warehouse teams think in work orders, not shipments.

### 3. Three Aggregates (WorkOrder + Shipment + DeliveryTracking)

Split the carrier lifecycle further into `Shipment` (label through handoff) and `DeliveryTracking` (in-transit through delivered).

**Rejected because:** The delivery tracking events are tightly coupled to the `Shipment` identity (same tracking number, same carrier). Splitting them would create coordination overhead without meaningful independence. The two-aggregate split at the warehouse/carrier boundary captures the real domain separation.
