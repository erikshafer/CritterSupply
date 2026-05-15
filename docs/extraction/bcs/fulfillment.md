# Fulfillment

> **Source folder:** `src/Fulfillment/`
> **Status:** Implemented
> **Most recent material milestone:** M41.0 — Fulfillment remaster (carrier lifecycle + warehouse operations)
> **Stub depth:** S1 — to be deepened in S2

## Purpose

Fulfillment manages the physical journey of an order — from the routing decision that picks a warehouse, through warehouse pick / pack / ship operations, into the carrier lifecycle (label generation, manifest, pickup, in-transit, delivery, lost, return-to-sender, reshipment), and on to backorder handling when stock is unavailable. Two aggregates split the work: `WorkOrder` for warehouse-floor operations and `Shipment` for routing and the carrier lifecycle.

## Top-level structure

### Aggregates

- `WorkOrder` — stream ID: UUID v7.
- `Shipment` — stream ID: UUID v7.

### Commands

- Routing: `SplitOrderIntoShipments`
- WorkOrder: `AssignPickList`, `StartPicking`, `RecordItemPick`, `ResumePick`, `ReportShortPick`, `ReleaseWave`, `CreateBackorder`, `StartPacking`, `VerifyItemAtPack`, `ReportPackDiscrepancy`, `ApplyColdPack`
- Shipment: `CancelFulfillment`, `CreateReshipment`, `RerouteShipment`, `GenerateShippingLabel`, `StagePackage`, `ManifestShipment`, `ConfirmCarrierPickup`, `ReportCarrierPickupMissed`, `HandoffToThirdPartyLogistics`, `ReceiveReturnAtWarehouse`, `DisputeDelivery`, `RaiseRateDispute`, `ResolveRateDispute`, `FileCarrierClaim`, `ResolveCarrierClaim`, `ArrangeAlternateCarrier`, `CheckForGhostShipment`, `CheckForLostShipment`, `CheckWorkOrderSLA`

### Domain events

- WorkOrder lifecycle: `WorkOrderCreated`, `PickListCreated`, `PickListAssigned`, `WaveReleased`, `PickStarted`, `PickResumed`, `PickCompleted`, `ItemPicked`, `ItemNotFoundAtBin`, `ShortPickDetected`, `PickExceptionRaised`, `PackingStarted`, `PackingCompleted`, `ItemVerifiedAtPack`, `WrongItemScannedAtPack`, `PackDiscrepancyDetected`, `CartonSelected`, `ColdPackApplied`, `HazmatItemFlagged`, `HazmatShippingRestrictionApplied`, `DIMWeightCalculated`, `WorkOrderCancelled`, `SLABreached`, `SLAEscalationRaised`
- Shipment lifecycle: `FulfillmentRequested`, `FulfillmentCenterAssigned`, `OutForDelivery`, `ShipmentInTransit`, `ShipmentRerouted`, `ShipmentManifested`, `PackageStagedForPickup`, `ShippingLabelGenerated`, `ShippingLabelGenerationFailed`, `ShippingLabelVoided`, `CarrierPickupConfirmed`, `CarrierPickupMissed`, `ShipmentHandedToCarrier`, `TrackingNumberAssigned`, `ShipmentDelivered`, `DeliveryAttemptFailed`, `DeliveryDisputed`, `ReturnReceivedAtWarehouse`, `ReturnToSenderInitiated`, `ReshipmentCreated`, `ShipmentLostInTransit`, `GhostShipmentDetected`, `BackorderCreated`, `FulfillmentCancelled`, `ThirdPartyLogisticsHandoff`, `AlternateCarrierArranged`, `CarrierTraceOpened`, `CarrierClaimFiled`, `CarrierClaimResolved`, `RateDisputeRaised`, `RateDisputeResolved`, `CarrierRelationsEscalated`

### Projections

- `Shipment` snapshot — inline, keyed by stream id.
- `WorkOrder` snapshot — inline, keyed by stream id.
- `ShipmentStatusView` — inline.
- `CarrierPerformanceView` — inline.
- `MultiShipmentView` — inline.

### Integration events

- `Fulfillment.ShipmentHandedToCarrier` — publishes
- `Fulfillment.TrackingNumberAssigned` — publishes
- `Fulfillment.ShipmentDelivered` — publishes
- `Fulfillment.DeliveryAttemptFailed` — publishes
- `Fulfillment.ReturnToSenderInitiated` — publishes
- `Fulfillment.ReshipmentCreated` — publishes
- `Fulfillment.BackorderCreated` — publishes
- `Fulfillment.FulfillmentCancelled` — publishes
- `Fulfillment.OrderSplitIntoShipments` — publishes
- `Fulfillment.GhostShipmentDetected` — publishes
- `Fulfillment.ShipmentLostInTransit` — publishes
- `Fulfillment.ItemPicked` — publishes
- `Fulfillment.StockReservationRequested` — publishes
- `Fulfillment.FulfillmentRequested` — bidirectional helper contract
- `Fulfillment.ShipmentDispatched` / `ShipmentDeliveryFailed` — legacy contracts retained for migration; replaced by `ShipmentHandedToCarrier` / `ReturnToSenderInitiated`
- Subscribes to Orders fulfillment requests and Inventory `StockAvailabilityView` queries

### HTTP / API surface (one line)

`Fulfillment.Api` exposes warehouse-operation and carrier-operation commands plus shipment / work-order read endpoints consumed by Orders, Backoffice, and the storefront BFF.

### Frontend surface (if applicable)

Not applicable — no frontend in this BC.

### Identity / auth posture (if applicable)

JWT Bearer (Backoffice + Vendor schemes).

## Prior event modeling

- `docs/planning/fulfillment-remaster-slices.md`
- `docs/planning/fulfillment-evolution-plan.md`

## ADRs

- ADR 0059 — Fulfillment BC Remaster Rationale

## Source citations (S1 stub)

- `src/Fulfillment/`
- `src/Shared/Messages.Contracts/Fulfillment/`
- `src/Fulfillment/Fulfillment.Api/Program.cs` (projection registrations)
- `CONTEXTS.md` (section: `Fulfillment`)
- `docs/decisions/0059-fulfillment-bc-remaster-rationale.md`
- `docs/planning/fulfillment-remaster-slices.md`
- `docs/planning/fulfillment-evolution-plan.md`
