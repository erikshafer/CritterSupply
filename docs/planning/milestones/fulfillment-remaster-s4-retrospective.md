# Fulfillment BC Remaster — S4 Retrospective

**Milestone:** M41.0 — Fulfillment BC Remaster: S4 (Orders Saga Migration + Legacy Contract Retirement)
**Date:** 2026-04-07
**ADR:** [0059 — Fulfillment BC Remaster Rationale](../../decisions/0059-fulfillment-bc-remaster-rationale.md)

---

## Summary

S4 completed the coordinated migration of the Orders saga to the new Fulfillment contract
surface and retired the dual-publish migration strategy introduced in S1. This was the
highest-risk session of the entire Fulfillment remaster series, as the Orders saga
coordinates Payments, Inventory, Fulfillment, and Returns. The strict add-first-verify-remove-second
sequencing was followed without deviation.

---

## Final Test Counts

| Test Suite | Before S4 | After S4 | Delta |
|---|---|---|---|
| Orders Integration | 48 | 55 | +7 (8 added, 1 legacy removed) |
| Orders Unit | 131 | 144 | +13 (legacy tests replaced with expanded new tests) |
| Fulfillment Integration | 78 | 78 | 0 (dual-publish test updated) |
| Fulfillment Unit | 40 | 40 | 0 |
| Correspondence Integration | 5 | 5 | 0 |
| Correspondence Unit | 12 | 12 | 0 |

**Build:** 0 errors, 19 warnings (unchanged from S3 baseline).

---

## What Was Delivered

### Part 1: New Orders Saga Handlers

- **3 new `OrderStatus` values:** `DeliveryFailed`, `Reshipping`, `Backordered`
- **3 new saga properties:** `TrackingNumber`, `ShipmentCount`, `ActiveReshipmentShipmentId`
- **3 new `OrderDecision` fields:** `TrackingNumber`, `ActiveReshipmentShipmentId`, `ShipmentCount`
- **7 new `OrderDecider` pure functions:** `HandleShipmentHandedToCarrier`, `HandleTrackingNumberAssigned`,
  `HandleReturnToSenderInitiated`, `HandleReshipmentCreated`, `HandleBackorderCreated`,
  `HandleFulfillmentCancelled`, `HandleOrderSplitIntoShipments`
- **7 new `Order` saga handlers:** Matching `Handle()` methods for each Fulfillment event

### Part 1B: New Integration Tests

- **`FulfillmentMigrationTests.cs`** — 8 new integration tests:
  1. `ShipmentHandedToCarrier_Transitions_To_Shipped`
  2. `TrackingNumberAssigned_Stores_Tracking_Number`
  3. `ReturnToSenderInitiated_Transitions_To_DeliveryFailed`
  4. `ReshipmentCreated_Transitions_To_Reshipping`
  5. `BackorderCreated_Transitions_To_Backordered`
  6. `FulfillmentCancelled_Triggers_Refund_And_Cancels`
  7. `Idempotency_ShipmentHandedToCarrier_Already_Shipped`
  8. `Full_Lifecycle_With_Reshipment` (place → ship → RTS → reship → ship → deliver)

### Part 2: Legacy Handler Removal

- Removed `Handle(ShipmentDispatched)` and `Handle(ShipmentDeliveryFailed)` from `Order.cs`
- Removed `HandleShipmentDispatched` and `HandleShipmentDeliveryFailed` from `OrderDecider.cs`
- Updated `CanBeCancelled` guard to allow cancellation of Shipped orders
  (FulfillmentCancelled can arrive pre-handoff; Shipped was removed from exclusion list)
- Updated all existing tests that referenced `ShipmentDispatched` to use `ShipmentHandedToCarrier`
- Replaced `Order_Remains_Shipped_When_Delivery_Fails` test with coverage in FulfillmentMigrationTests
- Replaced `Order_Cannot_Be_Cancelled_After_Shipped` → `Order_Cannot_Be_Cancelled_After_Delivered`
- Unit tests: Rewrote `OrderDeciderFulfillmentTests` with expanded coverage for all 7 new decider methods

### Part 3: Dual-Publish Removal from Fulfillment

- Removed legacy `ShipmentDispatched` dual-publish from `ConfirmCarrierPickupHandler`
- Removed `LegacyMessages` using alias from `CarrierHandlers.cs`
- Removed `ShipmentDispatched` publish routes from `Fulfillment.Api/Program.cs`
  (orders-fulfillment-events, storefront-fulfillment-events, backoffice-shipment-dispatched)
- Added `ReturnToSenderInitiated` publish to `correspondence-fulfillment-events` queue
- Replaced `ShipmentDispatched` on backoffice queue with `ShipmentHandedToCarrier`
- Updated `ConfirmCarrierPickup_Publishes_DualMessages` test to verify legacy is no longer published

### Part 4: Correspondence BC Update

- Created `ReturnToSenderInitiatedHandler` — replaces `ShipmentDeliveryFailedHandler`
- Deleted `ShipmentDeliveryFailedHandler.cs`
- Handler follows same pattern as other Correspondence handlers (IStartStream + OutgoingMessages)
- Correspondence BC already listens to `correspondence-fulfillment-events` queue

### Part 5: Documentation

- **CONTEXTS.md:** Updated Fulfillment, Orders, and Correspondence entries
  - Fulfillment: Added BackorderCreated, FulfillmentCancelled, OrderSplitIntoShipments to communication table;
    noted legacy contract retirement
  - Orders: Detailed all 8 Fulfillment events received
  - Correspondence: Updated to reflect ReturnToSenderInitiated replacing ShipmentDeliveryFailed
- **CURRENT-CYCLE.md:** Updated Quick Status and Active Milestone sections

---

## CanBeCancelled Guard Update

The `CanBeCancelled` method was updated to remove `Shipped` from the exclusion list:

```csharp
// Before (S3):
public static bool CanBeCancelled(OrderStatus status) =>
    status is not (OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Closed
        or OrderStatus.Cancelled or OrderStatus.OutOfStock or OrderStatus.PaymentFailed);

// After (S4):
public static bool CanBeCancelled(OrderStatus status) =>
    status is not (OrderStatus.Delivered or OrderStatus.Closed
        or OrderStatus.Cancelled or OrderStatus.OutOfStock or OrderStatus.PaymentFailed);
```

**Rationale:** `FulfillmentCancelled` can arrive before carrier handoff — technically the order
is in `Shipped` status but fulfillment hasn't completed. In practice, `FulfillmentCancelled`
won't fire post-handoff because Fulfillment's own state machine prevents it. The new statuses
(`DeliveryFailed`, `Reshipping`, `Backordered`) are also cancellable since they represent
non-terminal fulfillment states.

---

## ShipmentDeliveryFailed Contract Fate

**Deleted:** `ShipmentDeliveryFailedHandler.cs` in Correspondence was deleted (not obsoleted).

**Contract file retained:** `Messages.Contracts/Fulfillment/ShipmentDeliveryFailed.cs` is NOT deleted
because Backoffice BC and Customer Experience (Storefront) BC still reference it in their notification
handlers and projections. These BCs' migration to the new contracts is out of scope for S4.

**ShipmentDispatched contract also retained:** Same reason — Backoffice and Storefront still reference it.

---

## Legacy Message References Outside Dual-Publish

A `grep -r "ShipmentDispatched\|ShipmentDeliveryFailed" src/` confirmed these remaining consumers:

| BC | Handler/File | Legacy Contract | Status |
|---|---|---|---|
| Correspondence | `ShipmentDispatchedHandler.cs` | `ShipmentDispatched` | **Active** — still subscribes |
| Customer Experience | `ShipmentDeliveryFailedHandler.cs` | `ShipmentDeliveryFailed` | **Dead** — no publisher |
| Customer Experience | `ShipmentDispatchedHandler.cs` | `ShipmentDispatched` | **Dead** — no publisher |
| Backoffice | `ShipmentDeliveryFailedHandler.cs` | `ShipmentDeliveryFailed` | **Dead** — no publisher |
| Backoffice | `ShipmentDispatchedHandler.cs` | `ShipmentDispatched` | **Dead** — no publisher |
| Backoffice | `FulfillmentPipelineViewProjection` | Both | **Dead** — no publisher |
| Backoffice | `AlertFeedViewProjection` | `ShipmentDeliveryFailed` | **Dead** — no publisher |

**Assessment:** Handlers in Customer Experience and Backoffice are dead code — no publisher emits these
messages anymore. Correspondence `ShipmentDispatchedHandler` is also dead since the Fulfillment Program.cs
no longer publishes `ShipmentDispatched` to any Correspondence queue. These should be cleaned up in a
follow-on session.

---

## Known Deferred Items

1. **Correspondence handlers for new Fulfillment events:**
   - `DeliveryAttemptFailed` → customer notification ("delivery attempt failed, carrier will retry")
   - `BackorderCreated` → customer notification ("your order is backordered, we'll update you")
   - `ShipmentLostInTransit` → customer notification ("your package appears lost, investigating")
   - These are follow-on session work for Correspondence BC enrichment.

2. **Dead handler cleanup in Customer Experience + Backoffice:**
   - ShipmentDispatchedHandler, ShipmentDeliveryFailedHandler, projections
   - Should be migrated to new contracts (ShipmentHandedToCarrier, ReturnToSenderInitiated)

3. **MultiShipmentView production identity resolution:** flagged debt from S3.

4. **CarrierPerformanceView carrier resolution:** flagged debt from S3.

5. **P3 international slices:** deferred throughout the remaster.

---

## Idempotency Guard Deviation

The problem statement specified `Reshipping` in the idempotency guard for `HandleShipmentHandedToCarrier`.
This was corrected during implementation: `Reshipping` must NOT be in the guard because the reshipment
lifecycle requires `ShipmentHandedToCarrier` to transition the order from `Reshipping` back to `Shipped`
when the replacement shipment is dispatched. The full lifecycle test (`Full_Lifecycle_With_Reshipment`)
verified this flow end-to-end.

---

## Assessment: Is the Fulfillment Remaster Milestone Complete?

**No — an S5 may follow for cleanup**, but the core remaster is functionally complete:

- ✅ S1: P0 slices (15 slices, 2 aggregates, dual-publish strategy)
- ✅ S2: P1 slices (14 slices, test infrastructure)
- ✅ S3: P2 slices (10 slices, advanced compensation flows)
- ✅ S4: Orders saga migration + legacy contract retirement

**Remaining for milestone closure:**
- Dead handler cleanup in Customer Experience and Backoffice BCs
- Correspondence BC enrichment (3 new customer notification handlers)
- P3 international slices (explicitly deferred — may be a separate milestone)

The Fulfillment Remaster core objective — replacing the monolithic Shipment aggregate with WorkOrder +
Shipment, implementing 39 slices across P0/P1/P2, and retiring legacy integration contracts — is achieved.
The remaining work is incremental consumer migration, not core domain changes.

**Recommendation:** Close M41.0 after dead handler cleanup (quick S5). Correspondence enrichment
and P3 slices belong in separate milestones.
