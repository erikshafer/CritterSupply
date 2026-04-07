# Fulfillment BC Remaster — S5 Retrospective

**Milestone:** M41.0 — Fulfillment BC Remaster: S5 (Dead Handler Cleanup + Correspondence Enrichment)
**Date:** 2026-04-07
**ADR:** [0059 — Fulfillment BC Remaster Rationale](../../decisions/0059-fulfillment-bc-remaster-rationale.md)

---

## Summary

S5 is the milestone closure session. It cleaned up all dead handlers and projections across
Customer Experience, Backoffice, and Correspondence BCs that still referenced the retired
`ShipmentDispatched` and `ShipmentDeliveryFailed` integration contracts. It also added three
new Correspondence customer notification handlers for Fulfillment events that now surface but
had no customer communication path.

After S5, M41.0 closes.

---

## Final Test Counts

| Test Suite | Before S5 | After S5 | Delta |
|---|---|---|---|
| Customer Experience Integration | 54 | 54 | 0 (test migrated from ShipmentDispatched → ShipmentHandedToCarrier) |
| Backoffice Integration | 95 | 95 | 0 (6 tests migrated to new event surface) |
| Correspondence Integration | 5 | 12 | +7 (3 handler tests + 4 CorrespondenceQueued tests) |
| Fulfillment Integration | 78 | 78 | 0 (unchanged) |
| Fulfillment Unit | 40 | 40 | 0 (unchanged) |
| Orders Integration | 55 | 55 | 0 (unchanged) |
| Orders Unit | 144 | 144 | 0 (unchanged) |

**Build:** 0 errors, 17 warnings (improved from 18 baseline — removed `customerEmail` unused variable warnings from deleted ShipmentDispatchedHandler).

---

## What Was Delivered

### Track A: Dead Handler Cleanup

**Customer Experience BC:**
- Deleted `ShipmentDispatchedHandler.cs` — replaced with `ShipmentHandedToCarrierHandler.cs`
- Deleted `ShipmentDeliveryFailedHandler.cs` — replaced with `ReturnToSenderInitiatedHandler.cs`
- Added 4 new stub handlers: `TrackingNumberAssignedHandler`, `DeliveryAttemptFailedHandler`,
  `BackorderCreatedHandler`, `ShipmentLostInTransitHandler`
- All handlers follow the established SignalR `ShipmentStatusChanged` pattern
- Removed `ShipmentDeliveryFailed` record from `StorefrontEvent.cs` (dead discriminator)
- Updated `SignalRNotificationTests` to test `ShipmentHandedToCarrierHandler`

**Backoffice BC:**
- Deleted `ShipmentDispatchedHandler.cs` — replaced with `ShipmentHandedToCarrierHandler.cs`
- Deleted `ShipmentDeliveryFailedHandler.cs` — replaced with `ReturnToSenderInitiatedHandler.cs`
- Added 3 new handlers: `BackorderCreatedHandler`, `ShipmentLostInTransitHandler`, `GhostShipmentDetectedHandler`
- Updated `FulfillmentPipelineViewProjection`: `ShipmentDispatched` → `ShipmentHandedToCarrier`,
  `ShipmentDeliveryFailed` → `ReturnToSenderInitiated`, added `BackorderCreated` + `ShipmentLostInTransit`
- Updated `FulfillmentPipelineView` model: added `Backorders` and `ShipmentsLostInTransit` counters
- Updated `AlertFeedViewProjection`: `ShipmentDeliveryFailed` → `ReturnToSenderInitiated`,
  added `GhostShipmentDetected` + `ShipmentLostInTransit`
- Updated `AlertFeedView` model: added `GhostShipment` and `ShipmentLost` alert types
- Updated `Program.cs`: added `backoffice-fulfillment-alerts` queue subscription
- Migrated 6 integration tests from legacy to new event surface

**Correspondence BC:**
- Deleted `ShipmentDispatchedHandler.cs` — replaced with `ShipmentHandedToCarrierHandler.cs`
- Handler sends "Your order has shipped" email triggered by `ShipmentHandedToCarrier` instead of `ShipmentDispatched`

**Integration Contracts:**
- Created `Messages.Contracts.Fulfillment.DeliveryAttemptFailed` — new integration contract for
  delivery attempt notifications (includes OrderId, ShipmentId, AttemptNumber, Carrier, ExceptionCode)
- Created `Messages.Contracts.Fulfillment.GhostShipmentDetected` — new integration contract for
  Backoffice alert visibility (includes TrackingNumber, TimeSinceHandoff)

**Fulfillment Publishing:**
- Added 4 new events to `storefront-fulfillment-events` queue: `ReturnToSenderInitiated`,
  `DeliveryAttemptFailed`, `BackorderCreated`, `ShipmentLostInTransit`
- Added 4 new events to `correspondence-fulfillment-events` queue: `ShipmentHandedToCarrier`,
  `DeliveryAttemptFailed`, `BackorderCreated`, `ShipmentLostInTransit`
- Added `ReturnToSenderInitiated` to `backoffice-shipment-delivery-failed` queue (repurposed)
- Added `BackorderCreated`, `ShipmentLostInTransit`, `GhostShipmentDetected` to new
  `backoffice-fulfillment-alerts` queue

### Track B: Correspondence Enrichment

Three new customer notification handlers, all following the established `IStartStream` + `OutgoingMessages` pattern:

1. **`DeliveryAttemptFailedHandler`** — differentiates between attempt 1/2 (gentle retry notification)
   and final attempt 3 (urgent "action may be needed" notification). Template IDs: `delivery-attempt-failed`
   and `delivery-final-attempt`.

2. **`BackorderCreatedHandler`** — notifies customer their order is active but waiting on stock.
   Includes link to cancel. Template ID: `backorder-notification`.

3. **`ShipmentLostInTransitHandler`** — proactive notification that CritterSupply has already reshipped
   at no charge. Sets expectation that original package can be kept if it arrives. Template ID:
   `shipment-lost-in-transit`.

**Integration tests:** 7 new tests covering:
- DeliveryAttemptFailed attempt 1 (retry subject)
- DeliveryAttemptFailed attempt 3 (final attempt urgent subject)
- DeliveryAttemptFailed publishes CorrespondenceQueued
- BackorderCreated creates message with backorder subject
- BackorderCreated publishes CorrespondenceQueued
- ShipmentLostInTransit creates message with replacement subject
- ShipmentLostInTransit publishes CorrespondenceQueued

---

## Observations

### 1. Warning Count Improvement

The build warning count dropped from 18 to 17 after Track A. The deleted `ShipmentDispatchedHandler.cs`
in Correspondence had an unused `customerEmail` variable warning that was eliminated. The new
`ShipmentHandedToCarrierHandler.cs` doesn't have this warning because it uses the customerId
placeholder directly.

### 2. Dead Handler Discovery Pattern

The `grep -r "RetiredEventName" src/` approach from S4 proved correct. All 7 dead references
identified in the S4 retrospective were confirmed and resolved. This validates the migration
closure checklist pattern being added to `bc-remaster.md`.

### 3. Storefront Event Model Cleanup

The `ShipmentDeliveryFailed` record in `StorefrontEvent.cs` was a type discriminator for
SignalR WebSocket messages. Since all fulfillment status updates now flow through the generic
`ShipmentStatusChanged` record with a string status field, the specific failure record was
unnecessary. Removing it simplifies the discriminator hierarchy.

### 4. Backoffice Projection Growth

The `FulfillmentPipelineViewProjection` grew from 3 event types to 5, and `AlertFeedViewProjection`
from 4 to 6. This is expected — the Fulfillment remaster surfaced many more operational events
that the Backoffice dashboard should track. The singleton "current" document pattern for
`FulfillmentPipelineView` scales well for these additions.

---

## Known Deferred Items (Inherited by Next Milestone)

1. **P3 international slices** — explicitly deferred throughout the remaster; warrants dedicated sub-session
2. **MultiShipmentView production identity resolution** — S3 debt
3. **CarrierPerformanceView carrier resolution** — S3 debt
4. **CustomerId resolution** — All Storefront and Correspondence handlers use `Guid.Empty` as
   CustomerId placeholder. Requires Orders BC HTTP query or read model lookup. Tracked for future cycle.
5. **Inventory BC Remaster** — M42.0 (9 gaps from gap register)

---

## Assessment

**M41.0 is complete.** All five sessions delivered their objectives:

| Session | Focus | Slices | Tests Added |
|---|---|---|---|
| S1 | P0 Foundation | 15 | 50 |
| S2 | P1 Failure Modes | 14 | 28 |
| S3 | P2 Compensation | 10 | 0 (refactored) |
| S4 | Orders Saga Migration | 7 handlers | 20 |
| S5 | Milestone Closure | cleanup | 7 |

**Cumulative at closure:**
- Fulfillment: 78 integration + 40 unit = 118 tests
- Orders: 55 integration + 144 unit = 199 tests
- Correspondence: 12 integration tests
- Customer Experience: 54 integration tests
- Backoffice: 95 integration tests
- Build: 0 errors, 17 warnings
