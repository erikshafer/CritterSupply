# Inventory BC Remaster — Event Modeling Session

> **Skill:** `docs/skills/bc-remaster.md` (v0.3)
> **Session type:** Event Modeling Workshop — Full Five-Phase Process

---

## What "Remaster" Means

Same domain, same BC purpose — designed properly from first principles. The output is **not
code**: it is a complete event model, new feature files (none exist yet), integration
contract implications, and an ADR. Implementation follows in a subsequent milestone.

---

## Required Reading — Do First, In This Order

Every persona reads all of these before the session begins.

### 1. Current implementation

Read the entire `src/Inventory/Inventory/Management/` folder (17 files). Key observations:

- **`ProductInventory.cs`** — aggregate already keyed by `(SKU, WarehouseId)`. The
  per-warehouse structure exists. The problem is downstream.

- **`OrderPlacedHandler.cs`** — `const string defaultWarehouse = "WH-01"` with a TODO
  comment. This single line makes multi-warehouse routing impossible. The whole remaster
  starts here.

- **`AdjustInventory.cs`** — `LowStockThreshold = 10` hardcoded. `CrossedLowStockThreshold`
  helper exists but `LowStockDetected` is never published.

- **`ReceiveStock.cs` + `StockReceived.cs`** — PO receipt. **`StockRestocked.cs`** —
  return restocking. Different payloads, but both Apply methods add identically to
  `AvailableQuantity`. The semantic distinction is in the code but not enforced.

- **`GetStockLevel.cs`** — defaults to `"WH-01"`. Same structural problem as
  `OrderPlacedHandler`.

### 2. Feature files

**There are no Inventory feature files.** `docs/features/inventory/` does not exist.

Unlike the Fulfillment Remaster, there are no files to use as domain expert input.
The **Gap Register below** and domain knowledge of warehouse inventory management
are the primary Phase 1 inputs. Creating `docs/features/inventory/` from scratch is
a key deliverable of Phase 5.

### 3. Domain context

- **`CONTEXTS.md`** — Inventory entry (note: the "no HTTP endpoints" line is outdated).

- **`docs/planning/milestones/fulfillment-remaster-event-modeling-retrospective.md`** —
  read the **Inventory Gap Register section completely**. The 9 gaps with severity ratings
  are the structured charter for this session.

- **`docs/decisions/0059-fulfillment-bc-remaster-rationale.md`** — skim Q5 (routing).
  Fulfillment's routing engine will query Inventory for per-warehouse stock availability.

### 4. Workshop skills

- **`docs/skills/bc-remaster.md`** v0.3 — Standard Question #2 (multi-aggregate
  coordination + Wolverine cascade limitation), Standard Question #5 (contract retirement),
  Dual-Publish Migration Strategy, Inline Policy Invocation Pattern.

- **`docs/skills/event-modeling-workshop.md`** — all five phases, multi-persona guidance.

---

## Personas and Agents

All agent definitions at `.github/agents/`.

| Persona | Agent | Key focus in this session |
|---|---|---|
| **@event-modeling-facilitator** | `event-modeling-facilitator.md` | Drives phases; ensures routing integration question is answered before Phase 4 |
| **@product-owner** | `product-owner.md` | Tiebreaker on `StockReceived` vs `StockRestocked`; confirms warehouse operator mental model |
| **@principal-architect** | `principal-architect.md` | Owns routing integration design; flags integration contract implications |
| **@qa-engineer** | `qa-engineer.md` | Short picks, concurrent reservations, cycle count discrepancies, backorder edge cases |
| **@ux-engineer** | `ux-engineer.md` | Warehouse operator dashboard, Backoffice stock visibility, routing query surface |

---

## Phase 1 — Brain Dump

### Charter

Scope: the **entire Inventory domain as it should exist**, from stock receipt through
physical shipment. Because there are no feature files, aim for breadth — this session
establishes the Inventory domain model from first principles.

### Seed Events

**Stock Receipt and Replenishment:**
`StockReceived`, `StockRestocked`, `StockTransferred`, `InventoryInitialized`,
`StockThresholdBreached`, `ReplenishmentTriggered`, `ReplenishmentReceived`, `BackorderNotified`

**Reservation Lifecycle:**
`StockReserved`, `ReservationConfirmed`, `ReservationCommitted`, `ReservationReleased`,
`ReservationExpired`, `ReservationFailed`, `StockPickedForOrder`, `OverReservationDetected`

**Adjustments and Corrections:**
`InventoryAdjusted`, `CycleCountInitiated`, `CycleCountCompleted`,
`StockDiscrepancyFound`, `StockWrittenOff`, `DamageRecorded`

**Inter-Warehouse Operations:**
`InventoryTransferred`, `TransferRequested`, `TransferShipped`, `TransferReceived`,
`StockAvailabilityQueried`

**Physical Operations (from Fulfillment):**
`ItemPicked`, `ShortPickDetected`, `ReturnedStockReceived`, `StockQuarantined`,
`QuarantineReleased`, `QuarantineDisposed`

**Prompts to surface more:**
- What events happen when the routing engine queries Inventory for per-warehouse availability?
- What happens when a pick is attempted but physical quantity doesn't match the system?
- What happens when a supplier delivery arrives short of the PO quantity?
- What happens when stock is below threshold at ALL warehouses simultaneously?

**Target:** 40–60 events. Do not filter during Phase 1.

---

## Phase 2 — Storytelling

### Charter

Arrange events into a chronological timeline across swim lanes: **Stock Lifecycle**
(receipt, adjustment, transfers) and **Reservation Lifecycle** (soft hold → commit →
picked/released). A **Routing/Query** lane may emerge.

**@qa-engineer leads gap-finding.** For every adjacent pair of events: *"What's missing?
What can go wrong here?"*

### Five Key Design Questions

The Facilitator must push through all five. Each has genuine design tension that must be
resolved before Phase 4 begins.

**Q1 — Routing Integration (🔴 Critical — resolve before any other question)**

The Fulfillment Remaster established that Fulfillment owns the routing decision and its
routing engine queries Inventory for per-warehouse stock availability. The current
`OrderPlacedHandler` hardcodes WH-01 and must change.

Design the exact new flow:
- What query surface does Inventory expose? (`StockAvailabilityView`? HTTP endpoint?)
- Does `OrderPlacedHandler` survive in some form, or is it removed entirely?
- When does Inventory receive the WarehouseId — at `OrderPlaced` time, at
  `FulfillmentRequested` time, or at `FulfillmentCenterAssigned` time?
- What is the migration path for the current Orders saga reservation flow?

The @principal-architect designs the flow. The @product-owner confirms whether the
proposed sequence matches real-world e-commerce operations.

**Q2 — `StockReceived` vs `StockRestocked`**

Both Apply methods currently add identically to `AvailableQuantity`. The payloads differ:
`StockReceived` carries `Source` (supplier/transfer), `StockRestocked` carries `ReturnId`.

Are these the same domain fact ("stock increased") or meaningfully different operational
events with different audit requirements? The @product-owner has the tiebreaker.
Also check: is a transfer receipt a third variant, or does it use `StockReceived`?

**Q3 — Physical Commit and `ItemPicked`**

`ReservationCommitted` = logical hard allocation (Orders saga, post-payment).
`ItemPicked` (new Fulfillment event) = physical removal from bin.

Does Inventory receive `ItemPicked`? If yes, what does it do — move committed → shipped,
decrement `TotalOnHand`? If no, how does physical stock reduction get captured?
What is the authority model: does Inventory own the physical count?

**Q4 — `InventoryTransferred` Lifecycle**

A warehouse-to-warehouse transfer has three stages (requested → shipped → received). During
transit, stock is not available at source or destination. Does the `ProductInventory`
aggregate need in-transit quantities, or is this a separate `InventoryTransfer` aggregate?

**Q5 — Backorder Notification**

When `BackorderCreated` fires from Fulfillment (all FCs have zero stock), should Inventory
subscribe? Use case: flagging a SKU for proactive replenishment when orders are waiting.
Option A: Inventory subscribes, appends `BackorderRegistered` event, triggers
`ReplenishmentTriggered` when stock arrives. Option B: Purchasing concerns, not Inventory's.

---

## Phase 3 — Storyboarding

Add UI wireframes above the timeline, read models below.
**@ux-engineer** owns this phase.

**Warehouse operator view:**
- SKU detail screen at a specific warehouse — what quantities are shown? Available, reserved, committed, in-transit?
- Inbound receiving screen — fields, PO reference, variance handling
- Short pick screen — what does the picker see and do next?
- Cycle count discrepancy — who is notified, what is the correction flow?

**Backoffice operations manager view:**
- Low-stock alert feed — per-warehouse breakdown or aggregate?
- Backorder impact for a SKU — how does the operations dashboard show it?

**Routing engine (machine-to-machine):**
- What read model does Fulfillment's routing engine query?
- `StockAvailabilityView` shape: key=SKU, carries per-warehouse available quantities?
- Real-time (inline projection) or eventually-consistent (async)?

**Read models to define:**
- `ProductInventory` snapshot — already exists; confirm fields after remaster
- `StockAvailabilityView` — cross-warehouse per-SKU availability for routing (new)
- `LowStockView` — should the current query become a projection?
- `BackorderRegistryView` — if Q5 Option A is chosen

---

## Phase 4 — Identify Slices

Slice format:

| # | Slice Name | Command | Events | View | Aggregate | BC | Priority |
|---|---|---|---|---|---|---|---|

**P0 — foundation:**
- Routing integration fix — `OrderPlacedHandler` uses WarehouseId from routing decision
- `StockAvailabilityView` — multi-stream projection for Fulfillment routing queries
- `InitializeInventory` with UUID v5 stream IDs (replace MD5, Gap #5)
- `ReceiveStock` / `StockReceived` — confirmed semantic distinction, enriched payload
- `StockRestocked` — confirmed as distinct from `StockReceived`

**P1 — failure modes:**
- Short pick detection — `ItemPicked` with wrong quantity from Fulfillment
- `ReservationExpired` — soft-hold window elapses without commit
- `StockDiscrepancyFound` — cycle count reveals physical ≠ system
- Concurrent reservation conflict — two orders race for last units
- `BackorderNotified` — if Q5 Option A

**P2 — compensation and advanced:**
- `InventoryTransferred` — full multi-step transfer lifecycle
- `ItemPicked` physical commit integration — if Q3 Option A
- `ReplenishmentTriggered` — automatic when threshold breached
- `CycleCountCompleted` — reconciliation workflow
- `StockQuarantined` / `QuarantineReleased` — damaged goods

**P3+ (dedicated sub-session required):**
- **Bin-level tracking (Gap #7)** — WMS hardware integration out of scope for CritterSupply
- **Demand forecasting / configurable thresholds** — requires separate admin workflow
- **Lot/batch tracking** — regulated goods; not in current scope

---

## Phase 5 — Scenarios

**@qa-engineer** owns this phase. Scenarios become the first `docs/features/inventory/`
feature files — create from scratch.

For every P0 slice: happy path + at least one failure case.
For every P1 slice: failure scenario + compensation + idempotency case.

**Required stress-test questions for every reservation slice:**
- *"What if two orders reserve the last 3 units simultaneously?"*
- *"What if `ReservationCommitted` is delivered twice?"*
- *"What if `ItemPicked` arrives for a reservation already released?"*

**For every short-pick or discrepancy slice:**
- *"What happens to in-flight orders when a discrepancy is discovered?"*
- *"Who is notified — warehouse operator, Orders saga, Fulfillment?"*

**Suggested feature file structure:**
```
docs/features/inventory/
  stock-lifecycle.feature         — receipt, restocking, adjustments
  reservation-lifecycle.feature   — reserve, commit, release, expiry
  routing-integration.feature     — Fulfillment routing query surface
  failure-modes.feature           — short pick, discrepancy, backorder
```

---

## Mandatory Open Questions

All six must be answered in the session retrospective.

1. **Routing integration:** What is the exact new flow? Does `OrderPlacedHandler` survive?
   What read model does the Fulfillment routing engine query?

2. **`StockReceived` vs `StockRestocked`:** Keep separate or merge? If separate, what
   payload does each carry?

3. **`ItemPicked`:** Does Inventory receive it? If yes, what happens? If no, how does
   physical stock reduction get captured?

4. **`InventoryTransferred` lifecycle:** Single event on source aggregate, or separate
   `InventoryTransfer` aggregate?

5. **Backorder notification:** Does Inventory subscribe to `BackorderCreated`? If yes,
   what is the downstream effect?

6. **Adjacent BC gaps surfaced:** Any new coordination implications from answering Q1–Q5
   must be added to the Gap Register before the session closes.

---

## Standard Questions — Ask Before Closing

**Notification ownership:** When stock drops below threshold, who notifies the warehouse
operator? Inventory publishes `StockThresholdBreached` for Backoffice to consume, or
Inventory pushes directly?

**Multi-aggregate coordination:** If `InventoryTransferred` introduces a two-step lifecycle,
what coordinates source and destination? Is in-transit stock on the source aggregate, or
a separate aggregate?

**P3+ scope deferral:** Confirm bin-level tracking is P3 (WMS hardware integration). Is
`FulfillmentCenterCapacityView` (Gap #9) P2 or P3?

**Cross-BC integration test gate:** Orders saga has 55 integration tests; Fulfillment has
78. Both must stay green throughout the implementation milestone. Document as mandatory
bookend for S1.

---

## Integration Contract Implications

Current contracts (from CONTEXTS.md):
- Orders → Inventory: `ReservationReleaseRequested`, `ReservationCommitRequested`
- Inventory → Orders: `ReservationConfirmed`, `ReservationFailed`, `ReservationCommitted`, `ReservationReleased`
- Fulfillment → Inventory: **nothing today** — routing is hardcoded because no integration exists

Implied changes from the gap register to document (not implement) during this session:
- `OrderPlacedHandler` flow changes — WarehouseId source must be resolved
- New: `StockAvailabilityView` HTTP query for Fulfillment routing engine
- New: Inventory subscribes to `BackorderCreated` (if Q5 Option A)
- New: Inventory subscribes to `ItemPicked` (if Q3 Option A)
- `ProductInventory` stream IDs: MD5 → UUID v5 — **breaking change**; plan migration strategy (clean slate is acceptable for CritterSupply)

---

## Session Outputs

**Required:**
1. New feature files at `docs/features/inventory/` — create from scratch
2. Slice table at `docs/planning/inventory-remaster-slices.md` (include Aggregate column)
3. Session retrospective at `docs/planning/milestones/inventory-remaster-event-modeling-retrospective.md`
   — answers all 6 mandatory open questions, all 4 standard questions, severity-rated gap register
4. **ADR 0060** at `docs/decisions/0060-inventory-bc-remaster-rationale.md`
   — must document the routing integration decision and UUID v5 migration strategy
5. CONTEXTS.md update for the Inventory entry

**Recommended (in the retrospective):**
- Stub infrastructure strategy for S1
- Integration contract migration strategy for `OrderPlacedHandler` flow change
- UUID v5 migration plan (clean slate confirmed for CritterSupply)
- P3 scope confirmation

---

## Adjacent BC Gap Register — Pre-Session State

| # | Gap | Severity | Current State | Required State | Surfaced During |
|---|---|---|---|---|---|
| 1 | Hardcoded `WH-01` | 🔴 Critical | `const string defaultWarehouse = "WH-01"` | WarehouseId from Fulfillment routing decision | Fulfillment remaster Ph.2 |
| 2 | No multi-warehouse allocation | 🔴 Critical | Reservations only at WH-01 | Per-warehouse routing connected to `FulfillmentCenterAssigned` | Fulfillment remaster Ph.2 |
| 3 | `StockReceived` vs `StockRestocked` redundancy | 🟡 Medium | Identical Apply methods | Confirm semantic distinction; verify payloads | Fulfillment remaster Ph.1 |
| 4 | No `InventoryTransferred` event | 🟡 Medium | No inter-warehouse transfer concept | Full transfer lifecycle with in-transit state | Fulfillment remaster Ph.2 |
| 5 | MD5 stream ID | 🟠 Low | `CombinedGuid()` uses MD5 | UUID v5 per ADR 0016 | Fulfillment remaster Ph.2 |
| 6 | Reservation commit timing | 🟡 Medium | `ReservationCommitted` = logical only | `ItemPicked` = physical bin removal for reconciliation | Fulfillment remaster Ph.2 |
| 7 | No bin-level tracking | 🟡 Medium | Warehouse-level only | Bin-level for pick optimization (P3) | Fulfillment remaster Ph.3 |
| 8 | No backorder notification | 🟡 Medium | `BackorderCreated` not published to Inventory | SKU flagged for replenishment when all FCs zero | Fulfillment remaster Ph.2 |
| 9 | No capacity data exposure | 🟠 Low | No capacity query API | `FulfillmentCenterCapacityView` for routing engine | Fulfillment remaster Ph.3 |

Add rows as the session proceeds. Final register = charter for next adjacent BC remaster.

---

## Commit Convention

```
inventory-remaster: Phase 1–5 event model — brain dump, timeline, storyboard
inventory-remaster: slice table — P0 through P2 defined, P3 scoped
inventory-remaster: feature files — new docs/features/inventory/ from Phase 5
inventory-remaster: ADR 0060 — inventory remaster rationale + routing integration decision
inventory-remaster: session retrospective + adjacent BC gap register
inventory-remaster: CONTEXTS.md — Inventory entry updated
```
