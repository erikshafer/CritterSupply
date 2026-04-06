# Fulfillment BC Remaster — S2 Retrospective

**Milestone:** M41.0 S2
**Date:** 2026-04-06
**PR:** (current PR)
**ADR:** [0059 — Fulfillment BC Remaster Rationale](../../decisions/0059-fulfillment-bc-remaster-rationale.md)

---

## Summary

S2 completed all three parts: debt clearance, deferred S1 items, and all 14 P1 failure mode slices (16–29).

---

## Part 1: Debt Clearance — Complete ✅

`WorkOrderHandlers.cs` (6 commands/validators/handlers in one file) was split into 6 individual vertical slice files:

| File | Contents |
|------|----------|
| `ReleaseWave.cs` | ReleaseWave command + Validator + Handler |
| `AssignPickList.cs` | AssignPickList command + Validator + Handler |
| `StartPicking.cs` | StartPicking command + Validator + Handler |
| `RecordItemPick.cs` | RecordItemPick command + Validator + Handler |
| `StartPacking.cs` | StartPacking command + Validator + Handler |
| `VerifyItemAtPack.cs` | VerifyItemAtPack command + Validator + Handler |

No logic changes — pure structural reorganization. All 25/25 unit and 25/25 integration tests unchanged.

---

## Part 2: Deferred S1 Items — Complete ✅

### 2A: HTTP Carrier Webhook Endpoint

Added `POST /api/fulfillment/carrier-webhook` endpoint with `[WolverinePost]` attribute in `CarrierWebhookEndpoint.cs`. The endpoint receives `CarrierWebhookPayload` and dispatches it via `IMessageBus.InvokeAsync()`. Integration test verifies event appended via HTTP call.

### 2B: PackingCompleted → GenerateShippingLabel Cascading Policy

**Deviation from spec:** The problem statement specified a standalone `PackingCompletedPolicy` class using Wolverine's cascading handler pattern. This doesn't work with the S1 `Before()/Handle()` pattern because `PackingCompleted` is emitted via `session.Events.Append()` (not returned from the handler), so Wolverine can't cascade it.

**Resolution:** The cascade was implemented inline in `VerifyItemAtPackHandler.Handle()` — when packing completes, the handler dispatches `GenerateShippingLabel` via `IMessageBus.InvokeAsync()`. This achieves the same result (label generation automatically follows packing completion) and is consistent with the S1 pattern.

All existing tests that manually called `GenerateShippingLabel` after work order completion were updated — the helper method was renamed from `CreateReadyForLabelingAsync()` to `CreateLabeledShipmentAsync()` since the cascade now handles labeling automatically.

---

## Part 3: P1 Failure Mode Slices — Complete ✅

### Slices Implemented

| Slice | Description | File(s) | Tests |
|-------|-------------|---------|-------|
| 16 | Short pick — item not found at bin | `WorkOrders/ReportShortPick.cs` | 3 integration |
| 17 | Resume pick from alternative bin | `WorkOrders/ResumePick.cs` | 2 integration |
| 18 | Reroute shipment to new FC | `WorkOrders/RerouteShipment.cs` | 1 integration |
| 19 | Create backorder — no stock anywhere | `WorkOrders/CreateBackorder.cs` | 2 integration |
| 20 | Wrong item scanned at pack | `WorkOrders/ReportPackDiscrepancy.cs` | 1 integration |
| 21 | Weight mismatch at pack | `WorkOrders/ReportPackDiscrepancy.cs` (reused with DiscrepancyType) | 1 integration |
| 22 | Label generation failure | `Shipments/CarrierHandlers.cs` (try/catch in handler) | Unit test (Apply method) |
| 23 | Carrier pickup missed + alternate carrier | `Shipments/ReportCarrierPickupMissed.cs`, `Shipments/ArrangeAlternateCarrier.cs` | 2 integration |
| 24 | Delivery attempt failed chain | Already in S1 + idempotency test | 2 integration |
| 25 | Ghost shipment detection | `Shipments/GhostShipmentDetection.cs` | 3 integration |
| 26 | Lost in transit | `Shipments/ShipmentLostInTransit.cs` | Unit test (Apply method) |
| 27 | Return to sender via webhook | `Shipments/CarrierHandlers.cs` (RETURN_TO_SENDER route) | 2 integration |
| 28 | Return received at warehouse | `Shipments/ReceiveReturnAtWarehouse.cs` | 2 integration |
| 29 | SLA escalation monitoring | `WorkOrders/SLAMonitoring.cs` | 2 integration |

### Aggregate Changes

**WorkOrder statuses added:** `ShortPickPending`, `PackDiscrepancyPending`, `PickExceptionClosed`

**WorkOrder events added:** `ItemNotFoundAtBin`, `ShortPickDetected`, `PickResumed`, `PickExceptionRaised`, `WrongItemScannedAtPack`, `PackDiscrepancyDetected`, `SLAEscalationRaised`, `SLABreached`

**Shipment statuses added:** `Rerouted`, `Backordered`, `LabelGenerationFailed`, `GhostShipmentInvestigation`, `AllAttemptsExhausted`, `LostInTransit`

**Shipment events added:** `ShipmentRerouted`, `BackorderCreated`, `ShippingLabelGenerationFailed`, `CarrierPickupMissed`, `CarrierRelationsEscalated`, `AlternateCarrierArranged`, `ShippingLabelVoided`, `GhostShipmentDetected`, `ShipmentLostInTransit`, `CarrierTraceOpened`, `ReturnReceivedAtWarehouse`

### Integration Event Publishing

| Event | Integration Message | Direction |
|-------|-------------------|-----------|
| `BackorderCreated` | `Messages.Contracts.Fulfillment.BackorderCreated` | Fulfillment → Orders |
| `ShipmentLostInTransit` | `Messages.Contracts.Fulfillment.ShipmentLostInTransit` | Fulfillment → Orders |
| `ReturnToSenderInitiated` (via webhook) | Already in S1 dual-publish set | Verified |

---

## Deviations from Spec

1. **PackingCompleted cascading policy:** Implemented inline in `VerifyItemAtPackHandler.Handle()` instead of a standalone `PackingCompletedPolicy` class. Rationale: the S1 `Before()/Handle()` pattern emits events via `session.Events.Append()`, not as handler return values, so Wolverine can't cascade them. The inline approach is functionally equivalent.

2. **Slice 22 (label generation failure):** The stub carrier API never fails, so the try/catch in `GenerateShippingLabelHandler` is structural only. The `ShippingLabelGenerationFailed` event and `Apply()` method are tested via unit tests (direct aggregate state transitions). No integration test for the failure path since the stub always succeeds.

3. **Slice 26 (lost in transit):** The handler uses a stub threshold of 7 calendar days instead of 5 business days / 1.4. The scheduled message pattern is tested via direct invocation of `CheckForLostShipment` command.

4. **WorkOrder `EscalationThresholdsMet`:** Used `ImmutableHashSet<int>` instead of `IReadOnlySet<int>` because System.Text.Json cannot deserialize interfaces. This is consistent with the project's use of `ImmutableDictionary` for other tracked quantities.

---

## Final Test Counts

| Suite | Count | Change from S1 |
|-------|-------|----------------|
| Build | 0 errors, 19 warnings | Unchanged |
| Fulfillment Integration | **51** | +26 |
| Fulfillment Unit | **40** | +15 |
| Orders Integration | **48** | Unchanged |

---

## Known Gaps / S3 Candidates

- **Slice 22 integration test:** Needs a way to simulate carrier API failure in tests (e.g., injectable ICarrierLabelService stub)
- **Slice 26 integration test:** `CheckForLostShipment` threshold check is time-based — would need test clock injection to properly test the 7-day threshold
- **Slice 29 SLA escalation integration test:** Same time-based issue — fresh work orders never hit 50% threshold in tests
- **P2 work:** Reshipment flow (triggered by `ShipmentLostInTransit`), customer decision after return received (reship vs. refund)
- **Orders saga update:** Orders needs to handle `BackorderCreated` and `ShipmentLostInTransit` integration events
