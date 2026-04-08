# Fulfillment BC Remaster — Milestone Closure Retrospective

**Milestone:** M41.0 — Fulfillment BC Remaster
**Date Range:** 2026-04-06 (S1) to 2026-04-07 (S5)
**Sessions:** 5 implementation sessions + 1 event modeling session (pre-milestone)
**ADR:** [0059 — Fulfillment BC Remaster Rationale](../../decisions/0059-fulfillment-bc-remaster-rationale.md)

---

## Full Session Summary

### Pre-Milestone: Event Modeling Session (2026-04-06)

The Fulfillment remaster began with a full event modeling session using the `bc-remaster.md`
skill template. The session identified 77 events, produced 46 implementation slices across
P0/P1/P2/P3 priority tiers, and generated 28 BDD scenarios. Key design decisions: split
monolithic Shipment aggregate into WorkOrder (pick/pack) + Shipment (routing + carrier lifecycle);
StubFulfillmentRoutingEngine for deferred routing logic; dual-publish migration strategy for
backward compatibility with Orders saga.

### S1: P0 Foundation (2026-04-06)

**15 slices.** Track A (intake → wave → pick → pack) and Track B (label → carrier → delivery).
Established WorkOrder + Shipment aggregates, StubFulfillmentRoutingEngine, dual-publish
ShipmentHandedToCarrier + legacy ShipmentDispatched, ShipmentStatusView projection.
**50 tests** (25 integration + 25 unit).

### S2: P1 Failure Modes (2026-04-06)

**14 slices.** Cleared WorkOrderHandlers.cs vertical slice debt (split into 6 files). HTTP
carrier webhook endpoint. PackingCompleted → label cascade inline. 14 failure mode slices:
short pick, pack discrepancy, missed pickup, delivery attempts (1-3), ghost shipment, lost
in transit, return-to-sender, SLA escalation. ICarrierLabelService + ISystemClock test
infrastructure. **28 new tests** (78 integration total).

### S3: P2 Compensation + Advanced (2026-04-07)

**10 slices.** Reshipment, delivery dispute, multi-FC split, carrier claims, cancellation,
cold pack, hazmat inline policy, rate dispute, 3PL handoff. CarrierPerformanceView +
MultiShipmentView projections. 3 new integration contracts (ReshipmentCreated,
OrderSplitIntoShipments, FulfillmentCancelled). **78 integration + 40 unit tests** (refactored).

### S4: Orders Saga Migration (2026-04-07)

**7 new saga handlers + Decider methods.** 3 new OrderStatus values (DeliveryFailed, Reshipping,
Backordered). Legacy ShipmentDispatched + ShipmentDeliveryFailed handlers removed from Orders.
Dual-publish removed from Fulfillment. Correspondence ShipmentDeliveryFailedHandler replaced
with ReturnToSenderInitiatedHandler. **Orders: 55 integration + 144 unit. Fulfillment: 78/78.**

### S5: Milestone Closure (2026-04-07)

Dead handler cleanup across Customer Experience, Backoffice, and Correspondence. 3 new
Correspondence notification handlers (DeliveryAttemptFailed, BackorderCreated, ShipmentLostInTransit).
2 new integration contracts (DeliveryAttemptFailed, GhostShipmentDetected). **Correspondence: 12 tests** (up from 5).

---

## Cumulative Test Counts at Closure

| Test Suite | Pre-Remaster | At Closure | Delta |
|---|---|---|---|
| Fulfillment Integration | 0 (new BC model) | 78 | +78 |
| Fulfillment Unit | 0 | 40 | +40 |
| Orders Integration | 48 | 55 | +7 |
| Orders Unit | 131 | 144 | +13 |
| Correspondence Integration | 5 | 12 | +7 |
| Customer Experience Integration | 54 | 54 | 0 (migrated) |
| Backoffice Integration | 95 | 95 | 0 (migrated) |
| **Total** | **333** | **478** | **+145** |

**Build:** 0 errors, 17 warnings (improved from 19 pre-remaster).

---

## What the Remaster Achieved vs. Original State

### Before (Pre-M41.0)

- Single monolithic `Shipment` aggregate handling all fulfillment logic
- ~6 domain events (FulfillmentRequested, ShipmentDispatched, ShipmentDelivered,
  ShipmentDeliveryFailed, and a few status updates)
- No failure modes modeled (delivery failures were terminal states)
- No compensation patterns (no reshipment, no backorder handling, no carrier claims)
- Hardcoded routing (no routing engine concept)
- Orders saga used legacy ShipmentDispatched/ShipmentDeliveryFailed contracts

### After (Post-M41.0)

- Two aggregates: **WorkOrder** (pick/pack lifecycle) + **Shipment** (routing + carrier lifecycle)
- **77 domain events** covering happy path, failure modes, and compensation flows
- **39 implemented slices** across P0/P1/P2 (7 P3 international slices deferred)
- Full failure mode coverage: short pick, pack discrepancy, missed pickup, delivery attempts,
  ghost shipment, lost in transit, return-to-sender, SLA escalation
- Compensation patterns: reshipment, delivery dispute resolution, carrier claims, cancellation
- `StubFulfillmentRoutingEngine` with clean interface for future real implementation
- HazmatPolicy and ColdPackPolicy inline invocation pattern
- Orders saga fully migrated to new 7-event contract surface
- 3 customer notification handlers in Correspondence (delivery attempts, backorder, lost shipment)
- Backoffice pipeline and alert projections consuming 11+ Fulfillment events

---

## Deferred Items (Explicitly Out of Scope for M41.0)

1. **P3 International Slices (7 slices)** — Customs documentation, international carrier routing,
   duties/taxes calculation, multi-currency labeling, cross-border SLA policies, international
   return logistics, export compliance. Deferred throughout all sessions; warrants dedicated sub-milestone.

2. **MultiShipmentView Production Identity Resolution** — S3 debt. The MultiShipmentView projection
   joins events across WorkOrder and Shipment streams but uses placeholder identity for customer-facing
   views.

3. **CarrierPerformanceView Carrier Resolution** — S3 debt. Carrier name normalization and
   aggregation logic uses string matching; needs a formal carrier registry.

4. **CustomerId Resolution in Storefront/Correspondence Handlers** — All notification handlers use
   `Guid.Empty` as CustomerId placeholder. Needs Orders BC HTTP query or materialized view.

5. **Inventory BC Remaster** — 9-gap register from event modeling session. Two critical gaps:
   hardcoded WH-01 (blocks real routing) and no multi-warehouse allocation.

---

## What Went Well

1. **Dual-publish migration strategy** — Allowed S1-S3 to proceed without breaking the Orders saga.
   The strategy was clean: add new → verify → remove old. S4 removed the bridge in one session.

2. **Vertical slice organization** — Each handler in its own file with Before()/Handle() compound
   pattern made S2's 14 failure mode handlers straightforward to implement and test.

3. **Test infrastructure investment** — ISystemClock + FrozenSystemClock (S2) and ICarrierLabelService
   + AlwaysFailingCarrierLabelService (S2) paid off immediately in P1/P2 handlers. Time-dependent
   tests (SLA escalation, ghost shipment detection) were trivial once the clock was injectable.

4. **HazmatPolicy inline pattern** — Discovered in S3 that Wolverine cascading doesn't work with
   `session.Events.Append()` workflows. The inline static method pattern became the established
   solution, documented for future use.

5. **S4 add-first-remove-second** — The strict sequencing discipline for Orders saga migration
   prevented any intermediate state where both old and new handlers could conflict.

6. **S5 dead handler cleanup checklist** — The grep table from S4's retrospective made S5's Track A
   mechanical. Every dead handler was identified before the session started.

7. **Correspondence enrichment** — Adding customer notifications for new domain events (delivery
   failures, backorders, lost shipments) was low-risk and high-value. The established handler
   pattern made all three handlers nearly copy-paste.

---

## What Would Be Done Differently

1. **Run dead handler cleanup in the same session as contract retirement.** S4 retired the contracts
   and S5 cleaned up. In hindsight, the cleanup could have been part of S4's scope since the
   grep table was already produced. The two-session split was unnecessary overhead. **→ Template update:
   add "migration closure" as a mandatory step in the retirement session itself.**

2. **Invest in CustomerId resolution earlier.** Every Storefront and Correspondence handler uses
   `Guid.Empty` as a CustomerId placeholder. This means SignalR group targeting doesn't work in
   production, and email notifications go to a placeholder address. A simple Orders BC HTTP query
   or materialized view would have been a one-time investment with compounding returns.

3. **Consider Correspondence handler tests alongside the main handler implementation.** Track B's
   7 tests were straightforward because the handlers follow an established pattern. If we'd added
   the Correspondence tests in S2 (when the domain events were first created), we'd have caught
   any contract issues earlier.

4. **Document the inline policy pattern earlier.** The HazmatPolicy inline invocation was discovered
   in S3 and used in S3, but wasn't documented until S5's `bc-remaster.md` update. Future
   remasters should document patterns as they're discovered, not retroactively.

---

## Template Material for bc-remaster.md

**New checklist item for "Step 4: Commit Outputs":**
> After any session that retires integration contracts, run `grep -r "RetiredEventName" src/`
> to find all consumers. Classify each as active (needs migration) or dead (publisher removed).
> Migrate or remove all dead handlers before the milestone closes. Add Correspondence
> notifications for any new domain events that represent customer-meaningful moments.

**New pattern: "Migration Closure"**
> Any BC remaster that changes integration contracts should include a closure session (or
> closure track within the final session) that:
> 1. Greps for all references to retired contracts
> 2. Classifies consumers as active vs dead
> 3. Migrates or removes all dead handlers
> 4. Adds Correspondence notifications for new customer-facing events
> 5. Updates projections and BFF views to consume new event surface

**Inline policy pattern note:**
> When Wolverine cascading doesn't work with `session.Events.Append()` workflows, use inline
> static method calls (e.g., `HazmatPolicy.Apply()`) instead of standalone cascading handlers.
> This was the established pattern for S2/S3 failure mode handlers.
