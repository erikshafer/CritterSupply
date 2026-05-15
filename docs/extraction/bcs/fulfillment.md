# Fulfillment

> **Source folder:** `src/Fulfillment/`
> **Status:** Implemented
> **Most recent material milestone:** M41.0 — Fulfillment remaster (carrier lifecycle + warehouse operations)
> **Dossier depth:** S2 — full

## Purpose

Fulfillment manages the physical journey of an order — from the routing decision that picks a fulfillment center, through warehouse pick / pack operations, into the carrier lifecycle (label generation, manifest, staging, pickup, in-transit scans, delivery attempts, return-to-sender, reshipment), into return-receipt at the warehouse, and on to backorder handling when stock is unavailable. Two event-sourced aggregates split the work: `WorkOrder` for warehouse-floor operations and `Shipment` for the routing decision and the carrier lifecycle.

## Aggregates

### WorkOrder

- **Stream ID:** UUID v5, derived deterministically from `(ShipmentId, FulfillmentCenterId)` via `WorkOrder.StreamId(...)` (`src/Fulfillment/Fulfillment/WorkOrders/WorkOrder.cs:36-53`). A reroute closes the original WorkOrder stream and starts a new one at the new FC; the shipment-level identity is unaffected.
- **File:** `src/Fulfillment/Fulfillment/WorkOrders/WorkOrder.cs` (record at L11-29; `Create(WorkOrderCreated)` at L69; `Apply(...)` overloads continue to L177).
- **Key state:** `LineItems`, `Status` (`WorkOrderStatus` enum), `AssignedPicker`, `PickedQuantities`, `VerifiedQuantities`, `BillableWeightLbs`, `CartonSize`, SLA escalation thresholds met, plus per-phase timestamps (`WaveReleasedAt`, `PickListAssignedAt`, `PickStartedAt`, `PickCompletedAt`, `PackingStartedAt`, `PackingCompletedAt`).
- **Lifecycle stages:** Intake → Picking → Packing, with cross-cutting Hazmat, Exception, and SLA / Cancel branches. `AllItemsPicked` and `AllItemsVerified` derived predicates (`WorkOrder.cs:58-67`) gate phase transitions.

### Shipment

- **Stream ID:** UUID v5, derived deterministically from `OrderId` via `Shipment.StreamId(orderId)` (`src/Fulfillment/Fulfillment/Shipments/Shipment.cs:53-68`). The hash is one-way; events that need to surface the parent `OrderId` (e.g. `TrackingNumberAssigned`, `ShipmentDelivered`, `ReshipmentCreated`) carry it denormalized on the payload — see the `<para>` blocks in `ShipmentEvents.cs:18-27` and `:163-167` and `ShipmentDelivered.cs:5-10` for the rationale.
- **File:** `src/Fulfillment/Fulfillment/Shipments/Shipment.cs` (record at L21-39; `Create(FulfillmentRequested)` at L70; `Apply(...)` overloads continue to L214). `IsTerminal` predicate at L44-47 enumerates the terminal-state set.
- **Key state:** `OrderId`, `CustomerId`, `ShippingAddress`, `LineItems`, `ShippingMethod`, `Status` (`ShipmentStatus` enum), `AssignedFulfillmentCenter`, `TrackingNumber`, `Carrier`, `DeliveryAttemptCount`, `LastScanLocation`, `EstimatedDelivery`, plus per-phase timestamps (`FulfillmentCenterAssignedAt`, `LabelGeneratedAt`, `HandedToCarrierAt`, `DeliveredAt`).
- **Lifecycle stages:** Routing → Label → Carrier handoff → Tracking → Delivery, with cross-cutting Return / Reship and Exception / Claims branches.

> **Stream-ID note vs. S1 stub:** the S1 stub recorded both aggregates' stream IDs as UUID v7. Both are UUID v5 (the deterministic SHA-1 hash documented above). Recorded factually here.

## Commands

Total: **31 command records** in `src/Fulfillment/Fulfillment/`, dispatched via Wolverine (HTTP, in-process bus, or scheduled-message tick). See deviation note at the end of this section. Each command record is colocated with its handler in the same file; line ranges below point at the command's `public sealed record` declaration.

### WorkOrder commands (13)

#### Intake

- `AssignPickList` — `src/Fulfillment/Fulfillment/WorkOrders/AssignPickList.cs:8`
- `ReleaseWave` — `src/Fulfillment/Fulfillment/WorkOrders/ReleaseWave.cs:8`

#### Picking

- `StartPicking` — `src/Fulfillment/Fulfillment/WorkOrders/StartPicking.cs:8`
- `RecordItemPick` — `src/Fulfillment/Fulfillment/WorkOrders/RecordItemPick.cs:8`
- `ResumePick` — `src/Fulfillment/Fulfillment/WorkOrders/ResumePick.cs:11`
- `ReportShortPick` — `src/Fulfillment/Fulfillment/WorkOrders/ReportShortPick.cs:11`

#### Packing

- `StartPacking` — `src/Fulfillment/Fulfillment/WorkOrders/StartPacking.cs:8`
- `VerifyItemAtPack` — `src/Fulfillment/Fulfillment/WorkOrders/VerifyItemAtPack.cs:10`
- `ReportPackDiscrepancy` — `src/Fulfillment/Fulfillment/WorkOrders/ReportPackDiscrepancy.cs:20`

#### Hazmat / special handling

- `ApplyColdPack` — `src/Fulfillment/Fulfillment/WorkOrders/ApplyColdPack.cs:11`
- `CreateBackorder` — `src/Fulfillment/Fulfillment/WorkOrders/CreateBackorder.cs:14` (also publishes the `BackorderCreated` integration message — `CreateBackorder.cs:79-83`)

#### SLA / Cancel

- `CheckWorkOrderSLA` — `src/Fulfillment/Fulfillment/WorkOrders/SLAMonitoring.cs:9` (scheduled tick; appends `SLAEscalationRaised` at 50 % and 75 % of the window, `SLABreached` at 100 %)

#### Cross-aggregate (lives in `WorkOrders/`, mutates both streams)

- `RerouteShipment` — `src/Fulfillment/Fulfillment/WorkOrders/RerouteShipment.cs:13` (handler at L33-95: appends `PickExceptionRaised` to the original WorkOrder, `ShipmentRerouted` to the Shipment, then opens a new WorkOrder stream at the new FC)

### Shipment commands (18)

#### Routing

- `SplitOrderIntoShipments` — `src/Fulfillment/Fulfillment/Routing/SplitOrderIntoShipments.cs:15` (publishes `OrderSplitIntoShipments` integration message — `SplitOrderIntoShipments.cs:48`)

#### Label

- `GenerateShippingLabel` — `src/Fulfillment/Fulfillment/Shipments/CarrierHandlers.cs:12` (handler at L90-157 catches carrier-API exceptions and appends `ShippingLabelGenerationFailed` instead of propagating)

#### Carrier handoff

- `ManifestShipment` — `src/Fulfillment/Fulfillment/Shipments/CarrierHandlers.cs:28`
- `StagePackage` — `src/Fulfillment/Fulfillment/Shipments/CarrierHandlers.cs:42`
- `ConfirmCarrierPickup` — `src/Fulfillment/Fulfillment/Shipments/CarrierHandlers.cs:58` (handler at L226-275 appends both `CarrierPickupConfirmed` and `ShipmentHandedToCarrier`, then publishes the `ShipmentHandedToCarrier` integration message)

#### Tracking / Delivery

> Carrier-driven; no command records. The `CarrierWebhookPayload` record (`CarrierHandlers.cs:75`) is dispatched by the `POST /api/fulfillment/carrier-webhook` endpoint into `CarrierWebhookHandler.Handle` (`CarrierHandlers.cs:281-384`), which translates the carrier scan event-type string into one of `ShipmentInTransit`, `OutForDelivery`, `ShipmentDelivered`, `DeliveryAttemptFailed`, or `ReturnToSenderInitiated` and publishes the matching integration message.

#### Return / Reship

- `ReceiveReturnAtWarehouse` — `src/Fulfillment/Fulfillment/Shipments/ReceiveReturnAtWarehouse.cs:11`
- `CreateReshipment` — `src/Fulfillment/Fulfillment/Shipments/CreateReshipment.cs:16` (gated by `Shipment.Status` ∈ {`LostInTransit`, `ReturnReceived`, `Delivered`, `DeliveryDisputed`} — `CreateReshipment.cs:37-43`; publishes `ReshipmentCreated` integration message)

#### Exception / Claims

- `CancelFulfillment` — `src/Fulfillment/Fulfillment/Shipments/CancelFulfillment.cs:15` (publishes `FulfillmentCancelled` integration message)
- `ReportCarrierPickupMissed` — `src/Fulfillment/Fulfillment/Shipments/ReportCarrierPickupMissed.cs:11`
- `ArrangeAlternateCarrier` — `src/Fulfillment/Fulfillment/Shipments/ArrangeAlternateCarrier.cs:13` (also publishes a fresh `TrackingNumberAssigned` integration message for the new carrier)
- `HandoffToThirdPartyLogistics` — `src/Fulfillment/Fulfillment/Shipments/HandoffToThirdPartyLogistics.cs:12`
- `DisputeDelivery` — `src/Fulfillment/Fulfillment/Shipments/DisputeDelivery.cs:13`
- `RaiseRateDispute` — `src/Fulfillment/Fulfillment/Shipments/RaiseRateDispute.cs:11`
- `ResolveRateDispute` — `src/Fulfillment/Fulfillment/Shipments/ResolveRateDispute.cs:11`
- `FileCarrierClaim` — `src/Fulfillment/Fulfillment/Shipments/FileCarrierClaim.cs:12`
- `ResolveCarrierClaim` — `src/Fulfillment/Fulfillment/Shipments/ResolveCarrierClaim.cs:11`
- `CheckForGhostShipment` — `src/Fulfillment/Fulfillment/Shipments/GhostShipmentDetection.cs:10` (scheduled tick fired 24 h after `ShipmentHandedToCarrier`; appends `GhostShipmentDetected` only if no `ShipmentInTransit` has arrived)
- `CheckForLostShipment` — `src/Fulfillment/Fulfillment/Shipments/ShipmentLostInTransit.cs:11` (scheduled tick fired ~5 business days after `ShipmentHandedToCarrier`; appends `ShipmentLostInTransit` + `CarrierTraceOpened` and publishes the `ShipmentLostInTransit` integration message)

> **Deviation from S1 stub command count:** the S1 stub bullet list enumerates **31 commands** (1 routing + 11 WorkOrder + 19 Shipment when each name is counted); the M48.0 S2 retro and the S2h prompt both record the count as **27**. The actual record-file enumeration in `src/Fulfillment/Fulfillment/{WorkOrders,Shipments,Routing}/` yields **31** distinct `public sealed record` command types, matching the S1 stub bullet count. The 27-figure in the retro / prompt is a tally artifact, not a command-surface change. Recorded here as 31.

## Domain events

Total: **55** in-domain Marten events appended to the two streams (WorkOrder = 24, Shipment = 31), grouped below by aggregate × lifecycle phase. Sub-counts are stated per group.

### WorkOrder events (24)

#### Intake (4)

- `WorkOrderCreated` — `src/Fulfillment/Fulfillment/WorkOrders/WorkOrderEvents.cs:4`
- `WaveReleased` — `WorkOrderEvents.cs:12`
- `PickListCreated` — `WorkOrderEvents.cs:17`
- `PickListAssigned` — `WorkOrderEvents.cs:22`

#### Picking (4) — happy path

- `PickStarted` — `WorkOrderEvents.cs:27`
- `ItemPicked` — `WorkOrderEvents.cs:31`
- `PickCompleted` — `WorkOrderEvents.cs:39`
- `PickResumed` — `WorkOrderEvents.cs:88`

#### Packing (5)

- `PackingStarted` — `WorkOrderEvents.cs:43`
- `ItemVerifiedAtPack` — `WorkOrderEvents.cs:47`
- `DIMWeightCalculated` — `WorkOrderEvents.cs:53`
- `CartonSelected` — `WorkOrderEvents.cs:62`
- `PackingCompleted` — `WorkOrderEvents.cs:67`

#### Hazmat / special handling (3)

- `ColdPackApplied` — `WorkOrderEvents.cs:131`
- `HazmatItemFlagged` — `WorkOrderEvents.cs:137`
- `HazmatShippingRestrictionApplied` — `WorkOrderEvents.cs:143`

#### Exception (5)

- `ItemNotFoundAtBin` — `WorkOrderEvents.cs:75`
- `ShortPickDetected` — `WorkOrderEvents.cs:81`
- `PickExceptionRaised` — `WorkOrderEvents.cs:94`
- `WrongItemScannedAtPack` — `WorkOrderEvents.cs:99`
- `PackDiscrepancyDetected` — `WorkOrderEvents.cs:105`

#### SLA / Cancel (3)

- `SLAEscalationRaised` — `WorkOrderEvents.cs:111`
- `SLABreached` — `WorkOrderEvents.cs:118`
- `WorkOrderCancelled` — `WorkOrderEvents.cs:126`

### Shipment events (31)

#### Routing (3 in-domain events, plus 1 stream-initiation event)

- `FulfillmentCenterAssigned` — `src/Fulfillment/Fulfillment/Shipments/ShipmentEvents.cs:6`
- `ShipmentRerouted` — `ShipmentEvents.cs:91`
- *(stream-initiation)* `FulfillmentRequested` — `src/Fulfillment/Fulfillment/Shipments/FulfillmentRequested.cs:6`. Appended via `session.Events.StartStream<Shipment>(...)` in `FulfillmentRequestedHandler.cs:65` after being constructed from the inbound `Messages.Contracts.Fulfillment.FulfillmentRequested` integration message at L50-56. Excluded from the 31-event Shipment tally per the S2 reconciliation framing — see the count reconciliation note at the end of this section and the Integration events section.

#### Label (3)

- `ShippingLabelGenerated` — `ShipmentEvents.cs:11`
- `ShippingLabelGenerationFailed` — `ShipmentEvents.cs:102`
- `ShippingLabelVoided` — `ShipmentEvents.cs:126`

#### Carrier handoff (5)

- `TrackingNumberAssigned` — `ShipmentEvents.cs:28` (denormalises `OrderId` for the `MultiShipmentView` projection — rationale at L18-27)
- `ShipmentManifested` — `ShipmentEvents.cs:35`
- `PackageStagedForPickup` — `ShipmentEvents.cs:40`
- `CarrierPickupConfirmed` — `ShipmentEvents.cs:46`
- `ShipmentHandedToCarrier` — `ShipmentEvents.cs:52` (M41.0 successor — see successor-pair note below)

#### Tracking (2)

- `ShipmentInTransit` — `ShipmentEvents.cs:58`
- `OutForDelivery` — `ShipmentEvents.cs:64`

#### Delivery (2)

- `ShipmentDelivered` — `src/Fulfillment/Fulfillment/Shipments/ShipmentDelivered.cs:12` (separate file; denormalises `OrderId` for the `MultiShipmentView` projection — rationale at L5-10)
- `DeliveryAttemptFailed` — `ShipmentEvents.cs:70`

#### Return / Reship (4)

- `ReturnToSenderInitiated` — `ShipmentEvents.cs:77` (M41.0 successor — see successor-pair note below)
- `ReturnReceivedAtWarehouse` — `ShipmentEvents.cs:84`
- `ReshipmentCreated` — `ShipmentEvents.cs:169` (denormalises `OrderId`)
- `BackorderCreated` — `ShipmentEvents.cs:97`

#### Exception / Claims (12)

- `CarrierPickupMissed` — `ShipmentEvents.cs:108`
- `CarrierRelationsEscalated` — `ShipmentEvents.cs:114`
- `AlternateCarrierArranged` — `ShipmentEvents.cs:120`
- `GhostShipmentDetected` — `ShipmentEvents.cs:140` (denormalises `Carrier` so `CarrierPerformanceView` can attribute correctly — rationale at L131-139)
- `ShipmentLostInTransit` — `ShipmentEvents.cs:147`
- `CarrierTraceOpened` — `ShipmentEvents.cs:153`
- `DeliveryDisputed` — `ShipmentEvents.cs:177`
- `CarrierClaimFiled` — `ShipmentEvents.cs:184`
- `CarrierClaimResolved` — `ShipmentEvents.cs:199` (denormalises `Carrier`)
- `FulfillmentCancelled` — `ShipmentEvents.cs:206`
- `RateDisputeRaised` — `ShipmentEvents.cs:211`
- `RateDisputeResolved` — `ShipmentEvents.cs:219`
- `ThirdPartyLogisticsHandoff` — `ShipmentEvents.cs:226`

> **Count reconciliation vs. S1 stub:** S1 stub recorded **56** events. Actual in-domain count is **55** (WorkOrder 24 + Shipment 31). The 56th in S1 is `FulfillmentRequested`, which exists as a record at `src/Fulfillment/Fulfillment/Shipments/FulfillmentRequested.cs:6` and is appended to the Shipment stream as a stream-initiation event by `FulfillmentRequestedHandler` — but its payload shape mirrors the inbound integration contract `Messages.Contracts.Fulfillment.FulfillmentRequested`, and the S2 retro reconciliation excludes it from the in-domain tally on that basis. Listed above in the Routing phase as the stream-initiation event for traceability; not added to the 31-event Shipment count.

> **M41.0 legacy / successor pair:** `ShipmentDispatched` and `ShipmentDeliveryFailed` exist as records only on the integration-contract surface (`src/Shared/Messages.Contracts/Fulfillment/`) — they have no domain-event counterpart in `Shipments/ShipmentEvents.cs` and no handler in `src/Fulfillment/` instantiates or publishes them. The M41.0 successors `ShipmentHandedToCarrier` (replaces `ShipmentDispatched`, with custody-transfer semantics — see the XML comment in `Messages.Contracts/Fulfillment/ShipmentHandedToCarrier.cs:5-8`) and `ReturnToSenderInitiated` (replaces the terminal `ShipmentDeliveryFailed` with explicit attempt-chain semantics — see `Messages.Contracts/Fulfillment/ReturnToSenderInitiated.cs:5-7`) are emitted instead. Retained alongside the M41.0 successors; present-as-record-only on the integration contract surface. Cross-listed in the Integration events section.

## Projections

All five projections are registered as **inline** in `src/Fulfillment/Fulfillment.Api/Program.cs:45-55`. Source events listed reflect the `Apply(...)` overloads in each projection class.

| Projection | Key | Source events | Served endpoints / consumers |
|---|---|---|---|
| `Shipment` snapshot | Shipment stream id | All 31 Shipment events + `FulfillmentRequested` (stream-init) — every event the aggregate `Apply(...)` overloads at `Shipment.cs:85-214` handle | `GET /api/fulfillment/shipments?orderId=…` (`OrderFulfillment/GetShipmentsForOrder.cs:31-56`); `Shipment` document loaded by every command-handler `Before(...)` for guard checks |
| `WorkOrder` snapshot | WorkOrder stream id | All 24 WorkOrder events — every event the aggregate `Apply(...)` overloads in `WorkOrder.cs:85-177` handle | Loaded by every WorkOrder command-handler `Before(...)` for status-gate validation |
| `ShipmentStatusView` (`ShipmentStatusViewProjection`, `src/Fulfillment/Fulfillment/Shipments/ShipmentStatusView.cs`) | Shipment stream id | Per `Apply(...)` overloads: `FulfillmentRequested`, `FulfillmentCenterAssigned`, `TrackingNumberAssigned`, `ShipmentHandedToCarrier`, `ShipmentInTransit`, `OutForDelivery`, `ShipmentDelivered`, `DeliveryAttemptFailed`, `ReturnToSenderInitiated`, `ReturnReceivedAtWarehouse` (status-history timeline) | Customer-facing tracking read model; consumed via the Shipment snapshot path (no dedicated endpoint at present) |
| `CarrierPerformanceView` (`CarrierPerformanceViewProjection`, multi-stream, `Shipments/CarrierPerformanceView.cs`) | Carrier name (`string`) | `ShipmentHandedToCarrier`, `GhostShipmentDetected`, `ShipmentLostInTransit`, `CarrierClaimFiled`, `CarrierClaimResolved`, `RateDisputeRaised`, `CarrierPickupMissed` | Carrier reliability metrics; surfaced via Backoffice queries |
| `MultiShipmentView` (`MultiShipmentViewProjection`, multi-stream, `Shipments/MultiShipmentView.cs`) | `OrderId` (`Guid`) | `TrackingNumberAssigned`, `ShipmentHandedToCarrier`, `ShipmentDelivered`, `ReshipmentCreated` (cross-stream attachment) | Split-order and reshipment tracking; supports M41.0 Slice 30 and Slice 32 |

## Integration events

### Inbound (subscribed)

- `Messages.Contracts.Fulfillment.FulfillmentRequested` — listened on the `fulfillment-requests` Rabbit queue (`Program.cs:110`); handled by `FulfillmentRequestedHandler.Handle` (`Shipments/FulfillmentRequestedHandler.cs:20-101`), which starts the `Shipment` stream, invokes the routing engine to assign an FC, starts a `WorkOrder` stream at the assigned FC, applies the hazmat policy (`HazmatPolicy.CheckAndApply` — `WorkOrders/HazmatPolicy.cs`), and emits one `StockReservationRequested` per line item. Idempotency guard at L45-47 short-circuits if the stream already exists.
- *(no Returns or Inventory inbound message handlers in `src/Fulfillment/`)* — the `ReceiveReturnAtWarehouse` command is invoked operationally rather than driven by an inbound integration message.

### Outbound (published)

Configured in `Program.cs:115-182`. Payload top-level fields are listed for each contract.

#### To Inventory BC

- `StockReservationRequested(OrderId, Sku, WarehouseId, ReservationId, Quantity)` — emitted via `OutgoingMessages` from `FulfillmentRequestedHandler.Handle` (`FulfillmentRequestedHandler.cs:89-98`); published to `inventory-fulfillment-events` (`Program.cs:115-116`). M43.0 Slice 12 — replaces the legacy hardcoded WH-01 path with routing-informed warehouse selection.

#### To Orders BC (queue: `orders-fulfillment-events`)

- `ShipmentHandedToCarrier(OrderId, ShipmentId, Carrier, TrackingNumber, HandedAt)` — `Program.cs:119`; emitted by `ConfirmCarrierPickupHandler` (`CarrierHandlers.cs:268-273`). **M41.0 successor; replaces `ShipmentDispatched`** (custody-transfer semantics — see comment in `ShipmentHandedToCarrier.cs:5-8`).
- `ShipmentDelivered(OrderId, ShipmentId, DeliveredAt, RecipientName?)` — `Program.cs:121`; emitted by `CarrierWebhookHandler` on the `DELIVERED` scan (`CarrierHandlers.cs:320-324`).
- `ReturnToSenderInitiated(OrderId, ShipmentId, Carrier, TotalAttempts, EstimatedReturnDays, InitiatedAt)` — `Program.cs:123`; emitted by `CarrierWebhookHandler` after the third failed delivery attempt or on the `RETURN_TO_SENDER` scan (`CarrierHandlers.cs:344-358`, L362-381). **M41.0 successor; replaces `ShipmentDeliveryFailed`** with explicit attempt-chain semantics.
- `TrackingNumberAssigned(OrderId, ShipmentId, TrackingNumber, Carrier, AssignedAt)` — `Program.cs:125`; emitted by `GenerateShippingLabelHandler` on label success (`CarrierHandlers.cs:140-145`) and by `ArrangeAlternateCarrierHandler` on carrier change (`ArrangeAlternateCarrier.cs:82`).
- `BackorderCreated(OrderId, ShipmentId, Reason, Items[BackorderedItem(Sku, WarehouseId, Quantity)], CreatedAt)` — `Program.cs:127`; emitted by `CreateBackorderHandler` (`CreateBackorder.cs:79-87`).
- `ShipmentLostInTransit(OrderId, ShipmentId, Carrier, TimeSinceHandoff, DetectedAt)` — `Program.cs:129`; emitted by `CheckForLostShipmentHandler` (`ShipmentLostInTransit.cs:54`).
- `ReshipmentCreated(OrderId, OriginalShipmentId, NewShipmentId, Reason, CreatedAt)` — `Program.cs:131`; emitted by `CreateReshipmentHandler` (`CreateReshipment.cs:98`).
- `OrderSplitIntoShipments(OrderId, ShipmentCount, SplitAt)` — `Program.cs:133`; emitted by `SplitOrderIntoShipmentsHandler` (`SplitOrderIntoShipments.cs:48`).
- `FulfillmentCancelled(OrderId, ShipmentId, Reason, CancelledAt)` — `Program.cs:135`; emitted by `CancelFulfillmentHandler` (`CancelFulfillment.cs:83`).

#### Legacy / successor pair (publish-side)

- `ShipmentDispatched(OrderId, ShipmentId, Carrier, TrackingNumber, DispatchedAt)` — `src/Shared/Messages.Contracts/Fulfillment/ShipmentDispatched.cs:7`. Present-as-record-only on the integration contract surface; **no handler in `src/Fulfillment/` currently emits this contract** — `grep` for `IntegrationMessages.ShipmentDispatched` and `Messages.Contracts.Fulfillment.ShipmentDispatched` in `src/Fulfillment/` returns no instantiation sites and the contract is not registered in any `PublishMessage<…>` route in `Program.cs`. Retained alongside the M41.0 successor `ShipmentHandedToCarrier` (above), which is the contract emitted by current handlers. Note: `Fulfillment.Api/README.md` and `CONTEXTS.md:250` still reference `ShipmentDispatched` in narrative diagrams.
- `ShipmentDeliveryFailed(OrderId, ShipmentId, Reason, FailedAt)` — `src/Shared/Messages.Contracts/Fulfillment/ShipmentDeliveryFailed.cs:7`. Present-as-record-only on the integration contract surface; **no handler in `src/Fulfillment/` currently emits this contract** — same `grep` evidence as above; not registered in any `PublishMessage<…>` route. Retained alongside the M41.0 successor `ReturnToSenderInitiated` (above).

#### To Storefront / Customer Experience BFF (queue: `storefront-fulfillment-events`)

`ShipmentHandedToCarrier`, `ShipmentDelivered`, `TrackingNumberAssigned`, `ReturnToSenderInitiated`, `DeliveryAttemptFailed`, `BackorderCreated`, `ShipmentLostInTransit` — `Program.cs:139-152`. (Payload shapes match the Orders-bound publishes above; `DeliveryAttemptFailed(OrderId, ShipmentId, AttemptNumber, Carrier, ExceptionCode, ExceptionDescription, AttemptDate)` is defined at `Messages.Contracts/Fulfillment/DeliveryAttemptFailed.cs:7-14`.)

#### To Returns BC (queue: `returns-fulfillment-events`)

- `ShipmentDelivered` — `Program.cs:155`. Used by Returns to establish the return-eligibility window.

#### To Correspondence BC (queue: `correspondence-fulfillment-events`)

`ShipmentHandedToCarrier`, `ReturnToSenderInitiated`, `DeliveryAttemptFailed`, `BackorderCreated`, `ShipmentLostInTransit` — `Program.cs:159-168`.

#### To Backoffice BC

- `ShipmentHandedToCarrier` → `backoffice-shipment-dispatched` (`Program.cs:171`)
- `ShipmentDelivered` → `backoffice-shipment-delivered` (`Program.cs:173`)
- `ReturnToSenderInitiated` → `backoffice-shipment-delivery-failed` (`Program.cs:175`)
- `BackorderCreated`, `ShipmentLostInTransit`, `GhostShipmentDetected(OrderId, ShipmentId, TrackingNumber, TimeSinceHandoff, DetectedAt)` → `backoffice-fulfillment-alerts` (`Program.cs:177-182`).

#### Routes configured but no instantiator located

- `Messages.Contracts.Fulfillment.DeliveryAttemptFailed` — Rabbit publish routes registered at `Program.cs:147` and `:163`, but no `new IntegrationMessages.DeliveryAttemptFailed(...)` or `bus.PublishAsync(new Messages.Contracts.Fulfillment.DeliveryAttemptFailed(...))` site exists in `src/Fulfillment/` (grep). The domain event of the same name is appended on `DELIVERY_ATTEMPTED` scans (`CarrierHandlers.cs:334-339`); the integration contract is currently unpublished. Recorded factually.
- `Messages.Contracts.Fulfillment.GhostShipmentDetected` — Rabbit publish route registered at `Program.cs:181`, but no handler in `src/Fulfillment/` instantiates the integration-contract version (grep). The domain event is appended by `CheckForGhostShipmentHandler` (`GhostShipmentDetection.cs:37`); the integration contract is currently unpublished. Recorded factually.
- `Messages.Contracts.Fulfillment.ItemPicked(OrderId, Sku, WarehouseId, Quantity, PickedAt)` — defined at `Messages.Contracts/Fulfillment/ItemPicked.cs:7`; not registered in any `PublishMessage<…>` route in `Program.cs` and not instantiated anywhere in `src/Fulfillment/` (grep). Listed by `CONTEXTS.md:88` as a Fulfillment → Inventory publish; presently unwired.

## Sagas / orchestration

Fulfillment is **not a saga orchestrator**. It is a **participant** in two cross-BC flows:

- **Order saga (Orders BC).** Fulfillment receives `Messages.Contracts.Fulfillment.FulfillmentRequested` from Orders after payment is confirmed and inventory is committed (see comment at `Messages.Contracts/Fulfillment/FulfillmentRequested.cs:5-8`). It then drives Inventory's routing-aware reservation flow by emitting `StockReservationRequested` per line item (M43.0 Slice 12). The carrier-lifecycle outbound events (`ShipmentHandedToCarrier`, `ShipmentDelivered`, `ReturnToSenderInitiated`, `BackorderCreated`, `ReshipmentCreated`, `OrderSplitIntoShipments`, `FulfillmentCancelled`, `ShipmentLostInTransit`) drive the Order saga forward.
- **Cross-product-exchange flow (Returns BC).** When a return arrives back at the warehouse, `ReceiveReturnAtWarehouse` appends `ReturnReceivedAtWarehouse` to the Shipment stream. When a reshipment is required, `CreateReshipment` appends `ReshipmentCreated` and publishes the integration event consumed downstream. ADR 0061 (replacement reservation) and ADR 0062 (Payments choreography) place the orchestration on Returns and Payments respectively; Fulfillment participates by sourcing the return-receipt and reship signals.

## HTTP / API surface

Only **two HTTP endpoints** are mapped on `Fulfillment.Api`. Every other command (and the carrier webhook payload after dispatch) is message-handled.

| Verb | Path | File | Auth |
|---|---|---|---|
| `GET` | `/api/fulfillment/shipments?orderId=…` | `src/Fulfillment/Fulfillment.Api/OrderFulfillment/GetShipmentsForOrder.cs:31` | `[Authorize(Policy = "CustomerService")]` |
| `POST` | `/api/fulfillment/carrier-webhook` | `src/Fulfillment/Fulfillment.Api/OrderFulfillment/CarrierWebhookEndpoint.cs:16` | `[Authorize(Policy = "AnyAuthenticated")]` |

Wolverine endpoint discovery is enabled in `Program.cs:293-296`; no other `[Wolverine{Get,Post,Put,Delete}]`-attributed endpoints exist in the API project. All command intake outside these two endpoints is via the in-process bus (Wolverine local queues) or the Rabbit `fulfillment-requests` listener.

## Frontend surface

Not applicable — no frontend in this BC.

## Identity / auth posture

JWT Bearer multi-issuer (`Program.cs:189-261`). Two issuers are registered:

- **Backoffice** scheme (`Program.cs:190-206`) — issuer `https://localhost:5249`, role-claim type `role`.
- **Vendor** scheme (`Program.cs:207-223`) — issuer `https://localhost:5240`, role-claim type `role`.

Authorization policies (`Program.cs:226-261`):

- `CustomerService` — Backoffice scheme; roles `CustomerService`, `OperationsManager`, `SystemAdmin`. Gates `GetShipmentsForOrder`.
- `WarehouseClerk` — Backoffice scheme; roles `WarehouseClerk`, `OperationsManager`, `SystemAdmin`.
- `OperationsManager` — Backoffice scheme; roles `OperationsManager`, `SystemAdmin`.
- `VendorAdmin` — Vendor scheme; role `VendorAdmin`.
- `AnyAuthenticated` — cross-issuer (Backoffice or Vendor); requires authenticated user. Gates the carrier webhook endpoint.

## Tests as behavioral evidence

### BDD features (`docs/features/fulfillment/`)

- `warehouse-picking-and-packing.feature` — 10 scenarios. No `@pending` / `@wip` tags.
- `shipment-dispatch-and-tracking.feature` — 9 scenarios. No `@pending` / `@wip` tags.
- `international-fulfillment.feature` — 8 scenarios. No `@pending` / `@wip` tags.

### Integration tests (`tests/Fulfillment/Fulfillment.Api.IntegrationTests/`)

Alba + Testcontainers; collection fixtures `IntegrationTestCollection`, `TestFixture`, `LabelFailureTestFixture`. `[Fact]` / `[Theory]` counts per file:

#### `Shipments/`
- `CancellationTests.cs` — 3
- `CarrierClaimTests.cs` — 4
- `CarrierDispatchAndDeliveryTests.cs` — 9
- `CarrierExceptionTests.cs` — 4
- `CarrierWebhookEndpointTests.cs` — 2
- `FulfillmentRequestedHandlerTests.cs` — 9
- `LabelGenerationFailureTests.cs` — 1
- `RateDisputeAnd3PLTests.cs` — 5
- `ReshipmentTests.cs` — 4
- `ReturnAndSLATests.cs` — 4
- `ShipmentMonitoringTests.cs` — 5
- `ShipmentQueryTests.cs` — 2
- `SplitOrderTests.cs` — 2
- `TimeBasedMonitoringTests.cs` — 4

#### `WorkOrders/`
- `PackFailureTests.cs` — 3
- `PickFailureTests.cs` — 8
- `SpecialHandlingTests.cs` — 4
- `WorkOrderLifecycleTests.cs` — 7

### Unit tests (`tests/Fulfillment/Fulfillment.UnitTests/`)

#### `Shipments/`
- `CarrierPerformanceViewProjectionTests.cs` — 12
- `MultiShipmentViewProjectionTests.cs` — 10
- `ShipmentFailureModeTests.cs` — 8
- `ShipmentTests.cs` — 14
- `WorkOrderTests.cs` — 11 *(file lives under `Shipments/` despite testing the WorkOrder aggregate)*

#### `WorkOrders/`
- `WorkOrderFailureModeTests.cs` — 7

## ADRs

- **ADR 0059 — Fulfillment BC Remaster Rationale** (`docs/decisions/0059-fulfillment-bc-remaster-rationale.md`) — central. Records the two-aggregate split (`WorkOrder` for warehouse operations, `Shipment` for routing + carrier lifecycle), the multi-warehouse routing model, and the source of the 5-phase event-modeling workshop that produced the current event surface.
- ADR 0040 — Requested-integration-event naming convention (`docs/decisions/0040-requested-integration-event-convention.md`) — referenced for the `…Requested` naming applied to `FulfillmentRequested` and `StockReservationRequested`.
- ADR 0029 — Order saga design decisions (`docs/decisions/0029-order-saga-design-decisions.md`) — referenced for the saga-participant boundary (Orders orchestrates; Fulfillment is a participant that emits the carrier-lifecycle outbound events).
- ADR 0061 — Cross-product-exchange replacement reservation (`docs/decisions/0061-cross-product-exchange-replacement-reservation.md`) — referenced for the Returns ↔ Inventory ↔ Fulfillment edge (Fulfillment sources `ReturnReceivedAtWarehouse` and `ReshipmentCreated` into that flow).

## Prior event modeling

- `docs/planning/fulfillment-evolution-plan.md` — pre-remaster gap analysis; documents the shift from a 5-event single-aggregate skeleton (`FulfillmentRequested`, `WarehouseAssigned`, `ShipmentDispatched`, `ShipmentDelivered`, `ShipmentDeliveryFailed`) to the 49-event target catalog. The current implementation lands at 55 in-domain events, exceeding that target as additional warehouse-floor and carrier-claim phases were added in M41.0.
- `docs/planning/fulfillment-remaster-slices.md` — five-phase event-modeling workshop output (brain dump → storytelling → storyboarding → slice identification → scenarios) that produced the slice table executed across M41.0. The slice numbering referenced by handler XML comments (`Slice 12`, `Slice 18`, `Slice 25-32`, `Slice 37`) traces back to this document.

> **Reconciliation against current code:** the prior-EM 49-event target predates the M41.0-S4 successor-event rename (`ShipmentDispatched` → `ShipmentHandedToCarrier`; `ShipmentDeliveryFailed` → `ReturnToSenderInitiated`) and the M41.0-S5 SLA / hazmat / claims expansion (`SLAEscalationRaised`, `SLABreached`, `HazmatItemFlagged`, `HazmatShippingRestrictionApplied`, `CarrierClaimFiled`, `CarrierClaimResolved`, `RateDisputeRaised`, `RateDisputeResolved`, `ThirdPartyLogisticsHandoff`, etc.). The slice table remains accurate as the structural spine; event-name and per-aggregate placement diverge in places from current `WorkOrderEvents.cs` / `ShipmentEvents.cs` and are reconciled against the source files in the Domain events section above.

## Source citations (S2 full)

- `src/Fulfillment/Fulfillment/WorkOrders/WorkOrder.cs`
- `src/Fulfillment/Fulfillment/WorkOrders/WorkOrderEvents.cs`
- `src/Fulfillment/Fulfillment/WorkOrders/` (commands, SLA monitor, hazmat policy, line item, status enum)
- `src/Fulfillment/Fulfillment/Shipments/Shipment.cs`
- `src/Fulfillment/Fulfillment/Shipments/ShipmentEvents.cs`
- `src/Fulfillment/Fulfillment/Shipments/ShipmentDelivered.cs`
- `src/Fulfillment/Fulfillment/Shipments/FulfillmentRequested.cs`
- `src/Fulfillment/Fulfillment/Shipments/FulfillmentRequestedHandler.cs`
- `src/Fulfillment/Fulfillment/Shipments/CarrierHandlers.cs`
- `src/Fulfillment/Fulfillment/Shipments/` (remaining commands, view projections, status enum, label service interface)
- `src/Fulfillment/Fulfillment/Routing/`
- `src/Fulfillment/Fulfillment.Api/Program.cs` (Marten + Wolverine + Rabbit + auth wiring; projection registrations at L45-55; publish routing at L115-182; auth at L189-261; endpoint mapping at L293-296)
- `src/Fulfillment/Fulfillment.Api/OrderFulfillment/GetShipmentsForOrder.cs`
- `src/Fulfillment/Fulfillment.Api/OrderFulfillment/CarrierWebhookEndpoint.cs`
- `src/Shared/Messages.Contracts/Fulfillment/`
- `CONTEXTS.md` (section: `Fulfillment`, lines 79-92)
- `docs/decisions/0059-fulfillment-bc-remaster-rationale.md`
- `docs/decisions/0029-order-saga-design-decisions.md`
- `docs/decisions/0040-requested-integration-event-convention.md`
- `docs/decisions/0061-cross-product-exchange-replacement-reservation.md`
- `docs/planning/fulfillment-evolution-plan.md`
- `docs/planning/fulfillment-remaster-slices.md`
- `docs/planning/milestones/m48-0-session-2-retrospective.md` (S2h DEFERRED section, lines 108-122)
- `docs/features/fulfillment/`
- `tests/Fulfillment/`
