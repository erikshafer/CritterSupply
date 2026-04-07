# Fulfillment BC Remaster — S3 Retrospective

**Milestone:** M41.0 S3
**Date:** 2026-04-07
**PR:** (current PR)
**ADR:** [0059 — Fulfillment BC Remaster Rationale](../../decisions/0059-fulfillment-bc-remaster-rationale.md)

---

## Summary

S3 completed all three parts: test quality improvements (Part 1), all 10 P2 slices (Part 2), and documentation updates.

---

## Part 1: Test Quality Improvements — Complete ✅

### 1A: ICarrierLabelService Extraction (Slice 22)

Extracted `ICarrierLabelService` interface from the inline carrier label generation logic in `GenerateShippingLabelHandler`. Registered `StubCarrierLabelService` in `Program.cs`. Created `AlwaysFailingCarrierLabelService` test stub and a dedicated `LabelFailureTestFixture` (separate xUnit collection with its own Postgres container) to verify that label generation failure correctly appends `ShippingLabelGenerationFailed` and transitions to `LabelGenerationFailed` status.

**Files:**
- `src/Fulfillment/Fulfillment/Shipments/ICarrierLabelService.cs` — interface + `StubCarrierLabelService`
- `tests/Fulfillment/Fulfillment.Api.IntegrationTests/AlwaysFailingCarrierLabelService.cs`
- `tests/Fulfillment/Fulfillment.Api.IntegrationTests/LabelFailureTestFixture.cs`
- `tests/Fulfillment/Fulfillment.Api.IntegrationTests/Shipments/LabelGenerationFailureTests.cs`

### 1B: ISystemClock Abstraction (Slices 26, 29)

Introduced `ISystemClock` interface with `SystemClock` (production) and `FrozenSystemClock` (test) implementations. Updated `CheckForLostShipmentHandler` and `CheckWorkOrderSLAHandler` to use `ISystemClock` instead of `DateTimeOffset.UtcNow`. The `FrozenSystemClock` is registered as a singleton in the main `TestFixture` and reset to `DateTimeOffset.UtcNow` in each test class's `InitializeAsync()`.

**Time-based tests added:**
- Slice 26: `CheckForLostShipment_After_8_Days_Detects_Lost` — advances clock 8 days, verifies `ShipmentLostInTransit` appended + integration event published
- Slice 26: `CheckForLostShipment_At_5_Days_Does_Not_Detect_Lost` — advances clock 5 days (below threshold), verifies no change
- Slice 29: `CheckWorkOrderSLA_Past_50_Percent_Raises_Escalation` — advances clock 2.5h, verifies 50% threshold
- Slice 29: `CheckWorkOrderSLA_Past_100_Percent_Breaches_SLA` — advances clock 5h, verifies 50% + 75% thresholds + `SLABreached` event

**Files:**
- `src/Fulfillment/Fulfillment/ISystemClock.cs` — interface + `SystemClock`
- `tests/Fulfillment/Fulfillment.Api.IntegrationTests/FrozenSystemClock.cs`
- `tests/Fulfillment/Fulfillment.Api.IntegrationTests/Shipments/TimeBasedMonitoringTests.cs`

---

## Part 2: P2 Slices — Complete ✅

### Slices Implemented

| Slice | Description | File(s) | Tests |
|-------|-------------|---------|-------|
| 30 | Reshipment creation (dual-stream) | `Shipments/CreateReshipment.cs` | 2 integration |
| 31 | Delivery dispute — first offense reship | `Shipments/DisputeDelivery.cs` | 2 integration |
| 32 | Multi-FC split order routing | `Routing/SplitOrderIntoShipments.cs` | 2 integration |
| 33 | Carrier claim filing | `Shipments/FileCarrierClaim.cs` | 2 integration |
| 34 | Carrier claim resolution | `Shipments/ResolveCarrierClaim.cs` | 2 integration |
| 35 | Fulfillment cancellation | `Shipments/CancelFulfillment.cs` | 3 integration |
| 36 | Cold pack special handling | `WorkOrders/ApplyColdPack.cs` | 2 integration |
| 37 | Hazmat policy | `WorkOrders/HazmatPolicy.cs` | 2 integration |
| 38 | Rate dispute (raise + resolve) | `Shipments/RaiseRateDispute.cs`, `Shipments/ResolveRateDispute.cs` | 3 integration |
| 39 | 3PL handoff | `Shipments/HandoffToThirdPartyLogistics.cs` | 2 integration |

### Aggregate Changes

**ShipmentStatus added:** `ReturnedReplacementShipped`, `FulfillmentCancelled`, `DeliveryDisputed`, `HandedToThirdParty`

**Shipment events added:** `ReshipmentCreated`, `DeliveryDisputed`, `CarrierClaimFiled`, `CarrierClaimResolved`, `FulfillmentCancelled`, `RateDisputeRaised`, `RateDisputeResolved`, `ThirdPartyLogisticsHandoff`

**WorkOrderStatus added:** `Cancelled`

**WorkOrder events added:** `WorkOrderCancelled`, `ColdPackApplied`, `HazmatItemFlagged`, `HazmatShippingRestrictionApplied`

### New Projections

| Projection | Type | Key | Events Consumed |
|-----------|------|-----|-----------------|
| `CarrierPerformanceView` | MultiStream (inline) | Carrier name | `ShipmentHandedToCarrier`, `GhostShipmentDetected`, `ShipmentLostInTransit`, `CarrierClaimFiled`, `CarrierClaimResolved`, `RateDisputeRaised`, `CarrierPickupMissed` |
| `MultiShipmentView` | MultiStream (inline) | OrderId | `FulfillmentRequested`, `TrackingNumberAssigned`, `ShipmentDelivered`, `ReshipmentCreated` |

### Integration Events Published

| Slice | Event | Direction | Orders handler exists? |
|-------|-------|-----------|----------------------|
| 30 | `ReshipmentCreated` | Fulfillment → Orders | No (S4) |
| 32 | `OrderSplitIntoShipments` | Fulfillment → Orders | No (S4) |
| 35 | `FulfillmentCancelled` | Fulfillment → Orders | No (S4) |

---

## Deviations from Spec

1. **HazmatPolicy (Slice 37):** The spec described a Wolverine cascading handler pattern where `HazmatPolicy` handles `WorkOrderCreated` events. This doesn't work because `WorkOrderCreated` is appended via `session.Events.StartStream()` in `FulfillmentRequestedHandler`, and Wolverine doesn't auto-trigger handlers from inline-appended events. Instead, `HazmatPolicy.CheckAndApply()` is called inline in `FulfillmentRequestedHandler`, `CreateReshipmentHandler`, and `SplitOrderIntoShipmentsHandler` — all places that create WorkOrder streams. This is consistent with the S2 `PackingCompleted → GenerateShippingLabel` inline invocation pattern.

2. **MultiShipmentView projection:** The multi-stream projection uses simplified Identity resolution. The `TrackingNumberAssigned` and `ShipmentDelivered` events use `Guid.Empty` as their identity key since they don't carry `OrderId` directly. This means the projection is functional for `FulfillmentRequested` and `ReshipmentCreated` but the tracking and delivery updates are best-effort. A production implementation would need Marten's `ViewProjection` base class with custom stream matching.

3. **CarrierPerformanceView projection:** The `GhostShipmentDetected` and `CarrierClaimResolved` events don't carry the carrier name directly. They use `"Unknown"` as the identity key. A production implementation would need to resolve the carrier from the shipment's event stream.

4. **Slice 32 Decision A:** `OrderSplitIntoShipments` is published as an integration event only — no persistent routing aggregate. The Orders saga will need this event to track per-shipment status, but no handler exists yet.

---

## Deferred Items

### Orders Saga Coordinated Update (S4)

The following items are explicitly deferred to a coordinated M41.0 S4 or dedicated milestone session:

- Adding handlers for `BackorderCreated`, `ShipmentLostInTransit`, `ShipmentHandedToCarrier`, `TrackingNumberAssigned`, `DeliveryAttemptFailed`, `ReturnToSenderInitiated`, `ReshipmentCreated`, `FulfillmentCancelled`, and `OrderSplitIntoShipments` to the Orders saga
- Removing the legacy `ShipmentDispatched` / `ShipmentDeliveryFailed` dual-publish
- The Orders saga's 48 integration tests must stay green — this is a coordinated breaking change

### Other Deferred Items

- **P3 international slices** — Deferred per the event modeling session decision
- **Inventory BC Remaster** — `StubFulfillmentRoutingEngine` remains a stub
- **MultiShipmentView production quality** — Needs proper multi-stream identity resolution
- **CarrierPerformanceView carrier resolution** — Needs carrier name propagation from shipment stream

---

## Final Test Counts

| Suite | Count | Change from S2 |
|-------|-------|----------------|
| Build | 0 errors, 19 warnings | Unchanged |
| Fulfillment Integration | **78** | +27 |
| Fulfillment Unit | **40** | Unchanged |
| Orders Integration | **48** | Unchanged |

---

## Files Changed Summary

### New Domain Files (10 handlers)
- `src/Fulfillment/Fulfillment/Shipments/CreateReshipment.cs`
- `src/Fulfillment/Fulfillment/Shipments/DisputeDelivery.cs`
- `src/Fulfillment/Fulfillment/Shipments/FileCarrierClaim.cs`
- `src/Fulfillment/Fulfillment/Shipments/ResolveCarrierClaim.cs`
- `src/Fulfillment/Fulfillment/Shipments/CancelFulfillment.cs`
- `src/Fulfillment/Fulfillment/Shipments/RaiseRateDispute.cs`
- `src/Fulfillment/Fulfillment/Shipments/ResolveRateDispute.cs`
- `src/Fulfillment/Fulfillment/Shipments/HandoffToThirdPartyLogistics.cs`
- `src/Fulfillment/Fulfillment/WorkOrders/ApplyColdPack.cs`
- `src/Fulfillment/Fulfillment/WorkOrders/HazmatPolicy.cs`

### New Routing
- `src/Fulfillment/Fulfillment/Routing/SplitOrderIntoShipments.cs`

### New Projections
- `src/Fulfillment/Fulfillment/Shipments/CarrierPerformanceView.cs`
- `src/Fulfillment/Fulfillment/Shipments/MultiShipmentView.cs`

### New Integration Contracts
- `src/Shared/Messages.Contracts/Fulfillment/ReshipmentCreated.cs`
- `src/Shared/Messages.Contracts/Fulfillment/OrderSplitIntoShipments.cs`
- `src/Shared/Messages.Contracts/Fulfillment/FulfillmentCancelled.cs`

### New Test Infrastructure
- `src/Fulfillment/Fulfillment/ISystemClock.cs`
- `src/Fulfillment/Fulfillment/Shipments/ICarrierLabelService.cs`
- `tests/Fulfillment/Fulfillment.Api.IntegrationTests/FrozenSystemClock.cs`
- `tests/Fulfillment/Fulfillment.Api.IntegrationTests/AlwaysFailingCarrierLabelService.cs`
- `tests/Fulfillment/Fulfillment.Api.IntegrationTests/LabelFailureTestFixture.cs`

### New Integration Test Files (8)
- `Shipments/LabelGenerationFailureTests.cs`
- `Shipments/TimeBasedMonitoringTests.cs`
- `Shipments/ReshipmentTests.cs`
- `Shipments/SplitOrderTests.cs`
- `Shipments/CarrierClaimTests.cs`
- `Shipments/CancellationTests.cs`
- `Shipments/RateDisputeAnd3PLTests.cs`
- `WorkOrders/SpecialHandlingTests.cs`
