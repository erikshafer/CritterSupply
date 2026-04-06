# Fulfillment BC Remaster — Slice Table

**Session:** Event Modeling Workshop — Fulfillment Remaster  
**Date:** 2026-04-06  
**Facilitator:** @event-modeling-facilitator  
**Participants:** @product-owner, @principal-architect, @qa-engineer, @ux-engineer

---

## Phase 1 — Brain Dump (Complete Event Inventory)

All five personas contributed events. No filtering was applied during collection. Events are grouped by domain area for readability — ordering comes in Phase 2.

### Warehouse / Pre-Dispatch Track (27 events)

| # | Event Name | Surfaced By | Notes |
|---|---|---|---|
| 1 | `FulfillmentRequested` | PO | Exists today — integration message from Orders saga |
| 2 | `FulfillmentCenterAssigned` | PO/PSA | Routing decision — replaces hardcoded WH-01 |
| 3 | `WorkOrderCreated` | PO | Work order initiated at assigned FC |
| 4 | `WaveReleased` | PO | WMS batches shipments into pick waves |
| 5 | `PickListCreated` | PO | Pick list generated for a wave |
| 6 | `PickListAssigned` | PO | Pick list assigned to a specific picker |
| 7 | `PickStarted` | PO | Picker begins picking at first bin |
| 8 | `ItemPicked` | PO | Individual SKU scanned and picked at bin |
| 9 | `ItemNotFoundAtBin` | QA | Picker scans bin but item is absent |
| 10 | `ShortPickDetected` | QA | Primary bin has insufficient stock |
| 11 | `PickResumed` | QA | Alternative bin identified, picking continues |
| 12 | `PickExceptionRaised` | QA | No stock at any bin in assigned FC |
| 13 | `ShipmentRerouted` | QA/PSA | Emergency re-route to alternate FC |
| 14 | `BackorderCreated` | PO/QA | No stock at any FC — item backordered |
| 15 | `PickCompleted` | PO | All items for shipment picked successfully |
| 16 | `PackingStarted` | PO | Items arrive at pack station |
| 17 | `ItemVerifiedAtPack` | PO | SVP scan confirms correct item at pack station |
| 18 | `WrongItemScannedAtPack` | QA | Pack station scan detects wrong SKU |
| 19 | `ItemSubstitutedAtPack` | PO | Authorized substitution at pack station |
| 20 | `PackDiscrepancyDetected` | QA | Weight mismatch, wrong item, or no valid carton |
| 21 | `DIMWeightCalculated` | PO | Dimensional weight calculated for carton |
| 22 | `CartonSelected` | PO | System selects carton size |
| 23 | `ColdPackApplied` | PO | Cold pack applied for temperature-sensitive items |
| 24 | `PackingCompleted` | PO | All items verified, carton sealed |
| 25 | `HazmatItemFlagged` | PO | Item identified as hazmat during work order creation |
| 26 | `HazmatShippingRestrictionApplied` | PO | Air shipping blocked, downgraded to ground |
| 27 | `SLAEscalationRaised` | QA | Pick/pack SLA threshold breached |

### Label / Carrier Track (13 events)

| # | Event Name | Surfaced By | Notes |
|---|---|---|---|
| 28 | `ShippingLabelGenerated` | PO | Label created with carrier, service, billable weight |
| 29 | `ShippingLabelVoided` | QA | Label voided (carrier change, error correction) |
| 30 | `TrackingNumberAssigned` | PO | Tracking number assigned — first customer-visible event |
| 31 | `ShipmentManifested` | PO | Manifest created for carrier pickup |
| 32 | `PackageStagedForPickup` | PO | Package moved to carrier staging lane |
| 33 | `CarrierPickupConfirmed` | PO | Carrier driver scans manifest at FC |
| 34 | `CarrierPickupMissed` | QA | Scheduled carrier did not arrive |
| 35 | `CarrierRelationsEscalated` | QA | Dock supervisor escalates to carrier |
| 36 | `AlternateCarrierArranged` | QA | Backup carrier arranged after missed pickup |
| 37 | `ShipmentHandedToCarrier` | PO | Physical custody transferred to carrier |
| 38 | `RateDisputeRaised` | QA | Carrier raises billable weight dispute post-shipment |
| 39 | `RateDisputeResolved` | QA | Rate dispute settled |
| 40 | `SLABreached` | QA | SLA fully breached at 100% threshold |

### In-Transit / Delivery Track (14 events)

| # | Event Name | Surfaced By | Notes |
|---|---|---|---|
| 41 | `ShipmentInTransit` | PO | First carrier facility scan after pickup |
| 42 | `OutForDelivery` | PO | Carrier's last-mile out-for-delivery scan |
| 43 | `DeliveryAttemptFailed` | QA | Single failed delivery attempt (attempt 1, 2, or 3) |
| 44 | `ShipmentDelivered` | PO | Carrier confirms successful delivery |
| 45 | `ShipmentDeliveryFailed` | QA | Terminal delivery failure (all attempts exhausted) — RENAMED: see note |
| 46 | `GhostShipmentDetected` | QA | No carrier scan within 24h of handoff |
| 47 | `ShipmentLostInTransit` | QA | No carrier scan for 5 business days |
| 48 | `CarrierTraceOpened` | QA | Formal carrier trace initiated |
| 49 | `CarrierTraceClosed` | QA | Carrier trace resolved or expired |
| 50 | `CarrierClaimFiled` | PO | Insurance/loss claim filed with carrier |
| 51 | `CarrierClaimResolved` | PO | Claim paid, denied, or settled |
| 52 | `ReturnToSenderInitiated` | QA | Carrier returns package after failed delivery |
| 53 | `ReturnReceivedAtWarehouse` | PO | Returned package received back at FC |
| 54 | `DeliveryDisputed` | QA | Customer disputes "delivered" status |

### Compensation / Reshipment Track (6 events)

| # | Event Name | Surfaced By | Notes |
|---|---|---|---|
| 55 | `ReshipmentCreated` | PO | New shipment stream created to replace lost/failed original |
| 56 | `ReshipmentFulfillmentRequested` | PSA | Integration event — new fulfillment request for reshipment |
| 57 | `FulfillmentCancelled` | QA | Fulfillment cancelled before dispatch |
| 58 | `ShipmentMarkedLostReplacementShipped` | QA | Terminal state on original stream after reshipment |
| 59 | `ShipmentMarkedReturnedReshippable` | QA | Terminal state after RTS received, awaiting customer decision |
| 60 | `FulfillmentHeldForBackorder` | QA | Fulfillment paused pending stock replenishment |

### Multi-FC / Routing Track (7 events)

| # | Event Name | Surfaced By | Notes |
|---|---|---|---|
| 61 | `OrderSplitIntoShipments` | PSA | Order requires fulfillment from multiple FCs |
| 62 | `ShipmentRouted` | PSA | Routing engine assigns optimal FC |
| 63 | `WarehouseCapacityChecked` | PSA | FC capacity validated before assignment |
| 64 | `ShipmentTransferRequested` | QA | Inter-FC stock transfer requested |
| 65 | `ShipmentTransferCompleted` | QA | Inter-FC transfer completed |
| 66 | `RoutingOverrideApplied` | PO | Manual routing override by operations |
| 67 | `ThirdPartyLogisticsHandoff` | PO | Shipment handed to 3PL partner (e.g., TX FC) |

### International Track (10 events)

| # | Event Name | Surfaced By | Notes |
|---|---|---|---|
| 68 | `CustomsDocumentationPrepared` | PO | Commercial invoice and customs docs generated |
| 69 | `USMCACertificateOfOriginIssued` | PO | USMCA certificate for Canada duty exemption |
| 70 | `CustomsHoldInitiated` | QA | Customs authority holds shipment |
| 71 | `CustomsHoldReleased` | QA | Customs hold cleared |
| 72 | `ProhibitedItemSeized` | QA | Customs authority seizes prohibited item |
| 73 | `DutyRefused` | QA | DDU recipient refuses duty payment |
| 74 | `HazmatItemBlockedFromInternational` | QA | Hazmat item blocked at checkout for international |
| 75 | `LandedCostCalculated` | PO | Duties/taxes calculated for international order |
| 76 | `CustomsCorrectionSubmitted` | QA | Corrected documentation submitted after hold |
| 77 | `InternationalRoutingDetermined` | PSA | Routing engine selects international hub |

**Total candidate events: 77** (target: 40–80 ✅)

---

## Phase 2 — Storytelling (Timeline + Aggregate Boundary Decision)

### Timeline — Swim Lanes

The fulfillment lifecycle is arranged in three parallel swim lanes that converge and diverge at specific points.

#### Swim Lane 1: Warehouse Track (Pick/Pack)

```
FulfillmentRequested
  → FulfillmentCenterAssigned (routing decision)
    → WorkOrderCreated
      → WaveReleased
        → PickListCreated
          → PickListAssigned
            → PickStarted
              → ItemPicked (per SKU, repeats)
                [Exception: ItemNotFoundAtBin → ShortPickDetected → PickResumed | PickExceptionRaised → ShipmentRerouted | BackorderCreated]
              → PickCompleted
                → PackingStarted
                  → ItemVerifiedAtPack (per SKU, repeats)
                    [Exception: WrongItemScannedAtPack → PackDiscrepancyDetected]
                    [Exception: Weight mismatch → PackDiscrepancyDetected]
                  → DIMWeightCalculated
                    → CartonSelected
                      [Special: ColdPackApplied | HazmatItemFlagged]
                      → PackingCompleted
```

#### Swim Lane 2: Label/Carrier Track (Dispatch)

```
PackingCompleted (joins from Swim Lane 1)
  → ShippingLabelGenerated
    → TrackingNumberAssigned ← (first customer-visible event)
      → ShipmentManifested
        → PackageStagedForPickup
          → CarrierPickupConfirmed
            [Exception: CarrierPickupMissed → CarrierRelationsEscalated → AlternateCarrierArranged → ShippingLabelVoided → ShippingLabelGenerated (new carrier)]
            → ShipmentHandedToCarrier
```

#### Swim Lane 3: Transit/Delivery Track

```
ShipmentHandedToCarrier (joins from Swim Lane 2)
  → ShipmentInTransit (first carrier facility scan)
    [Exception at 24h: GhostShipmentDetected]
    → OutForDelivery
      → ShipmentDelivered (terminal — happy path)
      OR
      → DeliveryAttemptFailed (attempt 1)
        → DeliveryAttemptFailed (attempt 2)
          → DeliveryAttemptFailed (attempt 3)
            → ReturnToSenderInitiated (terminal — all attempts exhausted)
              → ReturnReceivedAtWarehouse
                → ReshipmentCreated | Refund decision
    [Exception at 5 business days: ShipmentLostInTransit → CarrierTraceOpened → ReshipmentCreated]
    [Post-delivery: DeliveryDisputed → ReshipmentCreated (first offense)]
```

#### Swim Lane 4: International Track (branches from Swim Lane 2)

```
(After PackingCompleted, before/during ShippingLabelGenerated)
  → CustomsDocumentationPrepared
    → USMCACertificateOfOriginIssued (if USMCA-eligible)
    (Rejoins Swim Lane 2 at ShippingLabelGenerated)

(During transit — Swim Lane 3)
  → CustomsHoldInitiated
    → CustomsCorrectionSubmitted
      → CustomsHoldReleased
    OR
    → ProhibitedItemSeized
  → DutyRefused (at delivery attempt)
    → ReturnToSenderInitiated
```

### Gap Analysis — Phase 2 Discoveries

The QA Engineer identified the following gaps between adjacent events:

| Gap Location | Missing Event / Question | Resolution |
|---|---|---|
| Between `FulfillmentRequested` and `WorkOrderCreated` | What happens if the routing engine cannot find a suitable FC? | Added `RoutingFailed` to backlog — not in P0 but noted |
| Between `PickCompleted` and `PackingStarted` | What if items are damaged in conveyance to pack station? | Edge case — `ConveyanceDamageDetected` noted for P2 |
| Between `PackingCompleted` and `ShippingLabelGenerated` | Can label generation fail? (API timeout, invalid address) | Added `ShippingLabelGenerationFailed` to P1 |
| Between `ShipmentHandedToCarrier` and `ShipmentInTransit` | This is the ghost shipment window — already covered | Confirmed — `GhostShipmentDetected` at 24h |
| Between `DeliveryAttemptFailed` attempt 3 and `ReturnToSenderInitiated` | Who triggers the RTS? Carrier does automatically | Confirmed — carrier-initiated, not CritterSupply-initiated |
| Between `ReturnReceivedAtWarehouse` and reshipment | Customer choice (reship vs. refund) — who owns this? | CS task created → Orders BC or Customer Experience |

### Aggregate Boundary Decision

**[PRINCIPAL ARCHITECT] The argument for two aggregates:**

The warehouse lifecycle (`WorkOrder`: `WorkOrderCreated` through `PackingCompleted`) and the carrier lifecycle (`Shipment`: `ShippingLabelGenerated` through `ShipmentDelivered`) serve fundamentally different actors, have different consistency boundaries, and scale independently. The warehouse is an internal operational system; the carrier track is external-integration-driven. Separating them means:
- A warehouse system replacement doesn't touch carrier tracking
- Work orders can be cancelled and re-created without affecting shipment identity
- Batch/wave operations (`WaveReleased`) naturally scope to `WorkOrder`, not `Shipment`
- The `Shipment` aggregate stays lean — it tracks what the customer cares about

**[PRINCIPAL ARCHITECT] The argument for one aggregate:**

A single `Shipment` stream preserves the full audit trail of a physical shipment from request to delivery. Splitting creates a coordination problem: when `PackingCompleted` fires on the `WorkOrder`, who starts the `Shipment` stream? You need a policy or saga to bridge them, adding complexity. The customer doesn't care about the boundary — they want one tracking page. For CritterSupply's current scale, one aggregate is simpler and sufficient.

**[PRODUCT OWNER] Business tiebreaker:**

> "From the business perspective, a shipment is one thing from the customer's point of view. But internally, the warehouse operations team thinks in work orders, not shipments. A work order can be cancelled and re-created at a different FC without the customer ever knowing. That's a strong signal that these are different concepts."

**Decision: Two aggregates — `WorkOrder` and `Shipment`.**

- `WorkOrder` owns: `WorkOrderCreated` through `PackingCompleted` (inclusive)
- `Shipment` owns: `ShippingLabelGenerated` through terminal delivery states
- `FulfillmentRequested` and `FulfillmentCenterAssigned` live on the `Shipment` stream (they represent the shipment identity, not warehouse operations)
- A `PackingCompleted` event on the `WorkOrder` stream triggers creation of the labeling/dispatch flow on the `Shipment` stream via a policy handler
- The `WorkOrder` stream ID is derived from (ShipmentId + FC assignment) — a reroute creates a new `WorkOrder` stream but the `Shipment` stream persists
- `ShipmentRerouted` lives on the `Shipment` stream; a new `WorkOrderCreated` is on the new `WorkOrder` stream

### Mandatory Open Question Answers (Phase 2)

**Q1: One stream or two?**
**Answer: Two aggregates — `WorkOrder` and `Shipment`.** See decision above. The `WorkOrder` aggregate covers warehouse operations (pick/pack); the `Shipment` aggregate covers the carrier lifecycle and customer-facing tracking. The `Shipment` stream also holds the initial routing events (`FulfillmentRequested`, `FulfillmentCenterAssigned`).

**Q2: When does inventory commit from the warehouse perspective?**
**Answer: `ItemPicked` is the physical commit; `ReservationCommitted` remains the logical commit.** These are two different facts. `ReservationCommitted` (from Inventory BC) means "the stock is reserved for this order." `ItemPicked` (from Fulfillment BC) means "a human has physically removed the item from the bin." The Inventory BC's `ReservationCommitted` remains the authoritative event for the Orders saga. However, the Fulfillment BC should publish `ItemPicked` events so that Inventory can reconcile physical bin counts. The Orders saga does NOT need to change — it reacts to `ReservationCommitted`, not `ItemPicked`.

**Q3: `DeliveryAttemptFailed` is not terminal — model it explicitly.**
**Answer:** `DeliveryAttemptFailed` carries an `AttemptNumber` (1, 2, or 3). Each attempt is a separate event on the `Shipment` stream. The `Shipment` aggregate tracks `DeliveryAttemptCount`. After the third `DeliveryAttemptFailed`, the carrier automatically initiates return-to-sender. CritterSupply detects this via `ReturnToSenderInitiated` (a carrier webhook event). The aggregate transitions to `ReturningToSender` status. `ShipmentDeliveryFailed` as a terminal event is **removed** — replaced by the explicit attempt chain: `DeliveryAttemptFailed(3)` → `ReturnToSenderInitiated` → `ReturnReceivedAtWarehouse`.

**Q4: `ReshipmentCreated` identity — new stream or event on original stream?**
**Answer: `ReshipmentCreated` is an event on the original `Shipment` stream. The reshipment itself is a new `Shipment` stream.** The original stream receives `ReshipmentCreated` (with `NewShipmentId` reference) and transitions to a terminal state (`Lost — Replacement Shipped` or `Returned — Replacement Shipped`). The new `Shipment` stream starts with `FulfillmentRequested` (flagged as reshipment with `OriginalShipmentId` reference). The Orders saga learns about the reshipment through the `ReshipmentCreated` integration event published from the original stream.

**Q5: Where does warehouse routing live?**
**Answer: Routing lives in Fulfillment.** The routing decision is about "which FC will fulfill this shipment?" — that's a Fulfillment concern. The event is `FulfillmentCenterAssigned` on the `Shipment` stream. Inventory provides stock availability data (queried, not commanded), but the routing decision itself belongs to Fulfillment. The `WH-01` hardcode in Inventory's `OrderPlacedHandler` becomes irrelevant — Inventory receives a `WarehouseId` from the reservation request (passed through from the Orders saga, which got it from Fulfillment's routing).

**Q6: Inventory gaps surfaced — see Inventory Gap Register below.**

---

## Phase 3 — Storyboarding (UI → Command → Event → View)

### Warehouse Operator Views

| Stage | Screen | What Operator Sees | Command | Events | Read Model |
|---|---|---|---|---|---|
| Wave Management | WMS Dashboard | List of pending fulfillment requests grouped by FC, priority, SLA countdown | `ReleaseWave` | `WaveReleased`, `PickListCreated` | `ActiveWavesView` |
| Pick Assignment | WMS Dashboard | Available pickers, their zones, current workload | `AssignPickList` | `PickListAssigned` | `PickerWorkloadView` |
| Picking | RF Scanner | Bin location, SKU, quantity to pick, scan prompt | `RecordItemPick` | `ItemPicked` | `PickProgressView` |
| Pick Exception | RF Scanner | "SHORT PICK — Checking alternative bins..." or "NO STOCK — Escalating" | `ReportShortPick` | `ShortPickDetected`, `PickExceptionRaised` | `PickExceptionView` |
| Pack Station | Pack Station Display | Items to verify (SVP), expected weight, carton recommendation | `VerifyItemAtPack` | `ItemVerifiedAtPack` | `PackStationView` |
| Pack Discrepancy | Pack Station Display | "WEIGHT DISCREPANCY — Supervisor required" or "WRONG ITEM" | `ReportPackDiscrepancy` | `PackDiscrepancyDetected` | `PackExceptionView` |
| Staging | Dock Display | Manifested packages by carrier lane, pickup windows | `StagePackage` | `PackageStagedForPickup` | `StagingLaneView` |
| Missed Pickup | Dock Display | "CARRIER MISSED — FedEx — Arrange alternate" | `ArrangeAlternateCarrier` | `AlternateCarrierArranged` | `CarrierPickupView` |

### Customer Views

| Stage | Screen | What Customer Sees | Trigger | Read Model |
|---|---|---|---|---|
| Order Placed | Order Tracking | "We've received your order and are preparing it" — progress bar at "Preparing" | `FulfillmentRequested` | `ShipmentStatusView` |
| Label Created | Order Tracking | "Your order has shipped! Track with [Carrier]: [Tracking#]" + Track button | `TrackingNumberAssigned` | `ShipmentStatusView` |
| In Transit | Order Tracking | "In Transit — Last scan: [location]" | `ShipmentInTransit` | `ShipmentStatusView` |
| Out for Delivery | Order Tracking | "Out for Delivery" | `OutForDelivery` | `ShipmentStatusView` |
| Delivered | Order Tracking | "Delivered — [date] at [time]" | `ShipmentDelivered` | `ShipmentStatusView` |
| Delivery Failed | Order Tracking | "Delivery attempted — will retry tomorrow" + redelivery options | `DeliveryAttemptFailed` | `ShipmentStatusView` |
| Split Order | Order Tracking | "Your order ships in 2 groups" — per-shipment tracking | `OrderSplitIntoShipments` | `MultiShipmentView` |
| Ghost/Lost | Order Tracking | "We're looking into a delay with your shipment" | `GhostShipmentDetected` or `ShipmentLostInTransit` | `ShipmentStatusView` |
| Customs Hold | Order Tracking (intl) | "Customs Review — Pending (est. 2–5 business days)" | `CustomsHoldInitiated` | `ShipmentStatusView` |
| Reshipment | Order Tracking | Original: "Lost — Replacement Shipped" / New: "In progress" | `ReshipmentCreated` | `MultiShipmentView` |

### Required Read Models

| Read Model | Purpose | Key Fields | Consumers |
|---|---|---|---|
| `ShipmentStatusView` | Customer-facing order tracking | ShipmentId, OrderId, Status, TrackingNumber, Carrier, EstimatedDelivery, StatusHistory[] | Customer Experience BFF |
| `WarehouseWorkOrderView` | WMS active work orders | WorkOrderId, ShipmentId, FC, Status, SLADeadline, PickProgress, AssignedPicker | WMS Dashboard |
| `ActiveWavesView` | Wave management dashboard | WaveId, FC, ShipmentCount, Priority, SLAStats | WMS Dashboard |
| `PickProgressView` | Picker RF scanner state | WorkOrderId, Items[], PickedCount, RemainingCount, CurrentBin | RF Scanner |
| `PackStationView` | Pack station display | WorkOrderId, Items[], VerifiedCount, ExpectedWeight, CartonRecommendation | Pack Station |
| `FulfillmentCenterCapacityView` | Routing engine input | FCId, PendingWorkOrders, AvailableCapacity, AverageThroughput | Routing Engine |
| `DeliveryAttemptHistoryView` | Carrier delivery audit trail | ShipmentId, Attempts[], LastAttemptDate, ExceptionCodes | CS Dashboard, Audit |
| `StagingLaneView` | Dock supervisor pickup tracking | FCId, Carrier, Packages[], PickupWindow, Status | Dock Display |
| `CarrierPerformanceView` | Carrier SLA tracking | Carrier, MissedPickups, GhostShipments, LostShipments, AvgDeliveryDays | Operations Dashboard |
| `MultiShipmentView` | Split order tracking | OrderId, Shipments[], PerShipmentStatus, OverallStatus | Customer Experience BFF |

---

## Phase 4 — Slice Table

### P0 — Foundation (nothing works without these)

| # | Slice Name | Command | Events | View | Aggregate | BC | Priority |
|---|---|---|---|---|---|---|---|
| 1 | Fulfillment request intake | *(integration: FulfillmentRequested from Orders)* | `FulfillmentRequested` | `ShipmentStatusView` (status: Pending) | Shipment | Fulfillment | P0 |
| 2 | FC assignment / routing decision | `AssignFulfillmentCenter` | `FulfillmentCenterAssigned` | `ShipmentStatusView` (status: Assigned), `WarehouseWorkOrderView` | Shipment | Fulfillment | P0 |
| 3 | Work order creation | *(policy: triggered by FulfillmentCenterAssigned)* | `WorkOrderCreated` | `WarehouseWorkOrderView` (status: Created) | WorkOrder | Fulfillment | P0 |
| 4 | Wave release | `ReleaseWave` | `WaveReleased`, `PickListCreated` | `ActiveWavesView`, `WarehouseWorkOrderView` (status: WaveReleased) | WorkOrder | Fulfillment | P0 |
| 5 | Pick list assignment | `AssignPickList` | `PickListAssigned` | `PickerWorkloadView`, `WarehouseWorkOrderView` | WorkOrder | Fulfillment | P0 |
| 6 | Item picking (happy path) | `RecordItemPick` | `ItemPicked` | `PickProgressView` | WorkOrder | Fulfillment | P0 |
| 7 | Pick completion | *(automatic when all items picked)* | `PickCompleted` | `WarehouseWorkOrderView` (status: PickCompleted) | WorkOrder | Fulfillment | P0 |
| 8 | Packing start and item verification | `StartPacking`, `VerifyItemAtPack` | `PackingStarted`, `ItemVerifiedAtPack` | `PackStationView` | WorkOrder | Fulfillment | P0 |
| 9 | Packing completion | *(automatic when all items verified)* | `DIMWeightCalculated`, `CartonSelected`, `PackingCompleted` | `WarehouseWorkOrderView` (status: PackCompleted) | WorkOrder | Fulfillment | P0 |
| 10 | Shipping label generation | `GenerateShippingLabel` | `ShippingLabelGenerated`, `TrackingNumberAssigned` | `ShipmentStatusView` (tracking# visible) | Shipment | Fulfillment | P0 |
| 11 | Shipment manifesting and staging | `ManifestShipment`, `StagePackage` | `ShipmentManifested`, `PackageStagedForPickup` | `StagingLaneView` | Shipment | Fulfillment | P0 |
| 12 | Carrier pickup confirmation | *(integration: carrier webhook)* | `CarrierPickupConfirmed`, `ShipmentHandedToCarrier` | `ShipmentStatusView` (status: InTransit) | Shipment | Fulfillment | P0 |
| 13 | In-transit tracking | *(integration: carrier webhook)* | `ShipmentInTransit` | `ShipmentStatusView` (last scan location) | Shipment | Fulfillment | P0 |
| 14 | Out for delivery | *(integration: carrier webhook)* | `OutForDelivery` | `ShipmentStatusView` (status: OutForDelivery) | Shipment | Fulfillment | P0 |
| 15 | Delivery confirmed | *(integration: carrier webhook)* | `ShipmentDelivered` | `ShipmentStatusView` (status: Delivered, terminal) | Shipment | Fulfillment | P0 |

### P1 — Failure Modes (real-world correctness)

| # | Slice Name | Command | Events | View | Aggregate | BC | Priority |
|---|---|---|---|---|---|---|---|
| 16 | Item not found at bin | `ReportShortPick` | `ItemNotFoundAtBin`, `ShortPickDetected` | `PickExceptionView` | WorkOrder | Fulfillment | P1 |
| 17 | Short pick — alternative bin resolves | `ResumePick` | `PickResumed`, `ItemPicked` | `PickProgressView` | WorkOrder | Fulfillment | P1 |
| 18 | Short pick — no stock at FC → reroute | *(policy: escalation)* | `PickExceptionRaised`, `ShipmentRerouted`, `WorkOrderCreated` (new FC) | `WarehouseWorkOrderView`, `ShipmentStatusView` | WorkOrder + Shipment | Fulfillment | P1 |
| 19 | Short pick — no stock anywhere → backorder | *(policy: escalation)* | `BackorderCreated` | `ShipmentStatusView` (status: Backordered) | Shipment | Fulfillment | P1 |
| 20 | Wrong item scanned at pack station | `ReportPackDiscrepancy` | `WrongItemScannedAtPack`, `PackDiscrepancyDetected` | `PackExceptionView` | WorkOrder | Fulfillment | P1 |
| 21 | Weight mismatch at pack station | `ReportPackDiscrepancy` | `PackDiscrepancyDetected` | `PackExceptionView` | WorkOrder | Fulfillment | P1 |
| 22 | Shipping label generation failed | *(system: carrier API failure)* | `ShippingLabelGenerationFailed` | `ShipmentStatusView` (error state) | Shipment | Fulfillment | P1 |
| 23 | Carrier pickup missed → alternate carrier | `ArrangeAlternateCarrier` | `CarrierPickupMissed`, `AlternateCarrierArranged`, `ShippingLabelVoided`, `ShippingLabelGenerated`, `TrackingNumberAssigned` | `StagingLaneView`, `ShipmentStatusView` | Shipment | Fulfillment | P1 |
| 24 | Delivery attempt failed (attempt 1/2/3) | *(integration: carrier webhook)* | `DeliveryAttemptFailed` | `ShipmentStatusView`, `DeliveryAttemptHistoryView` | Shipment | Fulfillment | P1 |
| 25 | Ghost shipment detection | *(system: scheduled job at 24h)* | `GhostShipmentDetected` | `ShipmentStatusView` (investigation) | Shipment | Fulfillment | P1 |
| 26 | Shipment lost in transit | *(system: scheduled job at 5 business days)* | `ShipmentLostInTransit`, `CarrierTraceOpened` | `ShipmentStatusView` (lost) | Shipment | Fulfillment | P1 |
| 27 | Return to sender initiated | *(integration: carrier webhook)* | `ReturnToSenderInitiated` | `ShipmentStatusView` (status: ReturningToSender) | Shipment | Fulfillment | P1 |
| 28 | Return received at warehouse | `ReceiveReturn` | `ReturnReceivedAtWarehouse` | `ShipmentStatusView` (status: ReturnReceived) | Shipment | Fulfillment | P1 |
| 29 | SLA escalation | *(system: scheduled job)* | `SLAEscalationRaised`, `SLABreached` | `WarehouseWorkOrderView` (urgent flag) | WorkOrder | Fulfillment | P1 |

### P2 — Compensation and Advanced

| # | Slice Name | Command | Events | View | Aggregate | BC | Priority |
|---|---|---|---|---|---|---|---|
| 30 | Reshipment creation | `CreateReshipment` | `ReshipmentCreated` (on original), `FulfillmentRequested` (on new stream) | `MultiShipmentView`, `ShipmentStatusView` | Shipment (both) | Fulfillment | P2 |
| 31 | Delivery dispute — first offense reship | `DisputeDelivery` | `DeliveryDisputed`, `ReshipmentCreated` | `ShipmentStatusView`, `DeliveryAttemptHistoryView` | Shipment | Fulfillment | P2 |
| 32 | Multi-FC split order routing | `SplitOrderIntoShipments` | `OrderSplitIntoShipments`, multiple `FulfillmentCenterAssigned` | `MultiShipmentView` | Shipment (multiple) | Fulfillment | P2 |
| 33 | Carrier claim filing | `FileCarrierClaim` | `CarrierClaimFiled` | `CarrierPerformanceView` | Shipment | Fulfillment | P2 |
| 34 | Carrier claim resolution | *(integration: carrier response)* | `CarrierClaimResolved` | `CarrierPerformanceView` | Shipment | Fulfillment | P2 |
| 35 | Fulfillment cancellation | `CancelFulfillment` | `FulfillmentCancelled` | `ShipmentStatusView` (status: Cancelled) | Shipment | Fulfillment | P2 |
| 36 | Special handling — cold pack | `ApplyColdPack` | `ColdPackApplied` | `PackStationView` | WorkOrder | Fulfillment | P2 |
| 37 | Special handling — hazmat flagging | *(policy: at work order creation)* | `HazmatItemFlagged`, `HazmatShippingRestrictionApplied` | `WarehouseWorkOrderView` | WorkOrder | Fulfillment | P2 |
| 38 | Rate dispute | *(integration: carrier billing)* | `RateDisputeRaised`, `RateDisputeResolved` | `CarrierPerformanceView` | Shipment | Fulfillment | P2 |
| 39 | 3PL handoff | `HandoffToThirdPartyLogistics` | `ThirdPartyLogisticsHandoff` | `ShipmentStatusView` | Shipment | Fulfillment | P2 |

### P3 — International (scoped separately)

| # | Slice Name | Command | Events | View | Aggregate | BC | Priority |
|---|---|---|---|---|---|---|---|
| 40 | Canada routing (Toronto Hub) | `RouteInternationalShipment` | `InternationalRoutingDetermined`, `FulfillmentCenterAssigned` | `ShipmentStatusView` | Shipment | Fulfillment | P3 |
| 41 | Customs documentation — Canada | `PrepareCustomsDocumentation` | `CustomsDocumentationPrepared`, `USMCACertificateOfOriginIssued` | `ShipmentStatusView` (customs step) | Shipment | Fulfillment | P3 |
| 42 | UK routing (Birmingham Hub) | `RouteInternationalShipment` | `InternationalRoutingDetermined`, `FulfillmentCenterAssigned` | `ShipmentStatusView` | Shipment | Fulfillment | P3 |
| 43 | Customs hold and release | *(integration: customs authority)* | `CustomsHoldInitiated`, `CustomsCorrectionSubmitted`, `CustomsHoldReleased` | `ShipmentStatusView` (customs review) | Shipment | Fulfillment | P3 |
| 44 | Prohibited item seizure | *(integration: customs authority)* | `ProhibitedItemSeized` | `ShipmentStatusView` | Shipment | Fulfillment | P3 |
| 45 | DDU duty refusal | *(integration: carrier webhook)* | `DutyRefused`, `ReturnToSenderInitiated` | `ShipmentStatusView` | Shipment | Fulfillment | P3 |
| 46 | Hazmat international blocking | *(policy: at checkout)* | `HazmatItemBlockedFromInternational` | Checkout UI error | Shipment | Fulfillment / Shopping | P3 |

**Total slices: 46** (15 P0 + 14 P1 + 10 P2 + 7 P3)

---

## Phase 5 — Scenarios (Given/When/Then)

### P0 Scenarios

#### Slice 1: Fulfillment Request Intake

**Happy path:**
```
Given:  (no prior events — new stream)
When:   FulfillmentRequested { OrderId: "ord-1", CustomerId: "cust-1", ShippingAddress: {..}, LineItems: [{Sku: "DOG-FOOD-40LB", Qty: 1}], ShippingMethod: "Ground" }
Then:   Shipment stream created with deterministic UUID v5 from OrderId
        ShipmentStatusView: { Status: "Pending", TrackingNumber: null }
```

**Idempotency — duplicate message:**
```
Given:  FulfillmentRequested { OrderId: "ord-1" } already on stream
When:   FulfillmentRequested { OrderId: "ord-1" } arrives again (at-least-once delivery)
Then:   No new stream created (UUID v5 collision detected)
        No error raised — silently idempotent
```

#### Slice 2: FC Assignment / Routing Decision

**Happy path:**
```
Given:  FulfillmentRequested { OrderId: "ord-1", ShippingAddress: {State: "NJ"} }
When:   AssignFulfillmentCenter { ShipmentId: "ship-1", FulfillmentCenterId: "NJ-FC" }
Then:   FulfillmentCenterAssigned { FulfillmentCenterId: "NJ-FC", AssignedAt: now }
        ShipmentStatusView: { Status: "Assigned", FulfillmentCenter: "NJ-FC" }
```

**Already assigned:**
```
Given:  FulfillmentRequested, FulfillmentCenterAssigned { FC: "NJ-FC" }
When:   AssignFulfillmentCenter { FC: "OH-FC" }
Then:   Rejected — shipment already assigned (must reroute via ShipmentRerouted)
```

#### Slice 3: Work Order Creation

**Happy path:**
```
Given:  FulfillmentRequested, FulfillmentCenterAssigned { FC: "NJ-FC" }
When:   (policy triggers work order creation)
Then:   WorkOrderCreated { WorkOrderId: "wo-1", ShipmentId: "ship-1", FC: "NJ-FC", LineItems: [...] }
        WarehouseWorkOrderView: { Status: "Created", FC: "NJ-FC", SLADeadline: now + 4h }
```

#### Slice 6: Item Picking (Happy Path)

**Happy path:**
```
Given:  WorkOrderCreated, WaveReleased, PickListAssigned { Picker: "P-Martinez" }, PickStarted
When:   RecordItemPick { SKU: "DOG-FOOD-40LB", Quantity: 1, BinLocation: "A-12-03" }
Then:   ItemPicked { SKU: "DOG-FOOD-40LB", Quantity: 1, BinLocation: "A-12-03", PickedBy: "P-Martinez" }
        PickProgressView: { PickedCount: 1, RemainingCount: 1 }
```

**Wrong SKU scanned:**
```
Given:  WorkOrderCreated, PickStarted, expected SKU: "DOG-FOOD-40LB"
When:   RecordItemPick { SKU: "CAT-FOOD-WET-24", BinLocation: "A-12-03" }
Then:   ItemNotFoundAtBin { ExpectedSKU: "DOG-FOOD-40LB", ScannedSKU: "CAT-FOOD-WET-24", BinLocation: "A-12-03" }
        RF Scanner displays: "WRONG ITEM — Expected DOG-FOOD-40LB"
```

#### Slice 10: Shipping Label Generation

**Happy path:**
```
Given:  FulfillmentRequested, FulfillmentCenterAssigned, PackingCompleted (via WorkOrder policy)
When:   GenerateShippingLabel { ShipmentId: "ship-1", Carrier: "UPS", Service: "Ground" }
Then:   ShippingLabelGenerated { Carrier: "UPS", Service: "Ground", BillableWeight: "40lb", LabelZPL: [data] }
        TrackingNumberAssigned { TrackingNumber: "1Z999AA10123456784" }
        ShipmentStatusView: { Status: "Labeled", TrackingNumber: "1Z999AA10123456784" }
        Integration event TrackingNumberAssigned published to Orders BC
```

#### Slice 12: Carrier Pickup Confirmation

**Happy path:**
```
Given:  ShipmentManifested, PackageStagedForPickup { Carrier: "UPS", PickupWindow: "2:00-3:00 PM" }
When:   CarrierPickupConfirmed { Carrier: "UPS", DriverScan: true, PickupTime: "14:14 ET" }
Then:   CarrierPickupConfirmed event appended
        ShipmentHandedToCarrier event appended
        ShipmentStatusView: { Status: "HandedToCarrier" }
        Integration event ShipmentHandedToCarrier published to Orders BC
```

#### Slice 15: Delivery Confirmed

**Happy path:**
```
Given:  ShipmentHandedToCarrier, ShipmentInTransit, OutForDelivery
When:   ShipmentDelivered { DeliveredAt: "2026-06-03T11:43:00Z" }
Then:   ShipmentDelivered event appended (terminal state)
        ShipmentStatusView: { Status: "Delivered", DeliveredAt: "June 3, 2026 at 11:43 AM" }
        Integration event ShipmentDelivered published to Orders BC
```

### P1 Scenarios

#### Slice 16: Item Not Found at Bin

**Failure scenario:**
```
Given:  WorkOrderCreated, PickStarted, PickListAssigned
When:   ReportShortPick { SKU: "FISH-FOOD-API", BinLocation: "F-03-08", QuantityExpected: 2, QuantityFound: 1 }
Then:   ShortPickDetected { SKU: "FISH-FOOD-API", BinLocation: "F-03-08", Shortage: 1 }
        PickExceptionView: { Type: "ShortPick", WaitingForAlternativeBin: true }
```

**Compensation — alternative bin resolves:**
```
Given:  ShortPickDetected { SKU: "FISH-FOOD-API", Shortage: 1 }
When:   ResumePick { AlternativeBin: "F-03-09" }
Then:   PickResumed { AlternativeBin: "F-03-09" }
        ItemPicked { SKU: "FISH-FOOD-API", Quantity: 1, BinLocation: "F-03-09" }
        PickProgressView: { PickedCount: 2, RemainingCount: 0 }
```

**Idempotency — duplicate short pick report:**
```
Given:  ShortPickDetected { SKU: "FISH-FOOD-API", BinLocation: "F-03-08" } already recorded
When:   ReportShortPick { SKU: "FISH-FOOD-API", BinLocation: "F-03-08" } (duplicate)
Then:   No additional ShortPickDetected event (idempotent by SKU+Bin)
```

#### Slice 18: Short Pick — No Stock at FC → Reroute

**Failure scenario:**
```
Given:  WorkOrderCreated { FC: "NJ-FC" }, ShortPickDetected, PickExceptionRaised { Reason: "NoStockAtAssignedFC" }
When:   (routing engine identifies OH-FC has stock)
Then:   ShipmentRerouted { OriginalFC: "NJ-FC", NewFC: "OH-FC" } (on Shipment stream)
        WorkOrderCreated { FC: "OH-FC" } (new WorkOrder stream)
        Original WorkOrder closed
```

**Compensation — reroute also fails:**
```
Given:  ShipmentRerouted { NewFC: "OH-FC" }, PickExceptionRaised at OH-FC
When:   No FC has stock
Then:   BackorderCreated { SKU: "DOG-BED-ORTHO", AllFCsChecked: true }
        ShipmentStatusView: { Status: "Backordered" }
        Customer notification sent
```

#### Slice 23: Carrier Pickup Missed → Alternate Carrier

**Failure scenario:**
```
Given:  ShipmentManifested, PackageStagedForPickup { Carrier: "FedEx", Window: "12:00-1:00 PM" }
When:   (clock reaches 1:00 PM, no FedEx arrival)
Then:   CarrierPickupMissed { Carrier: "FedEx", ScheduledWindow: "12:00-1:00 PM" }
        CarrierRelationsEscalated
```

**Compensation — alternate carrier arranged:**
```
Given:  CarrierPickupMissed { Carrier: "FedEx" }
When:   ArrangeAlternateCarrier { OriginalCarrier: "FedEx", AlternateCarrier: "UPS" }
Then:   AlternateCarrierArranged { Original: "FedEx", Alternate: "UPS" }
        ShippingLabelVoided { Carrier: "FedEx" }
        ShippingLabelGenerated { Carrier: "UPS" }
        TrackingNumberAssigned { TrackingNumber: "1Z-new-tracking" }
        Customer notified: "Your tracking number has been updated"
```

**Idempotency — duplicate missed pickup:**
```
Given:  CarrierPickupMissed already recorded for this pickup window
When:   CarrierPickupMissed reported again (duplicate webhook)
Then:   No additional event (idempotent by ShipmentId + PickupWindow)
```

#### Slice 24: Delivery Attempt Failed

**Failure scenario — attempt 1:**
```
Given:  ShipmentHandedToCarrier, ShipmentInTransit, OutForDelivery
When:   DeliveryAttemptFailed { AttemptNumber: 1, ExceptionCode: "NI", Desc: "No one home" }
Then:   DeliveryAttemptFailed event appended
        Shipment.DeliveryAttemptCount = 1
        ShipmentStatusView: { Status: "DeliveryAttemptFailed", AttemptCount: 1 }
        Customer email: "UPS attempted delivery — will retry tomorrow"
```

**Failure scenario — attempt 3 (terminal):**
```
Given:  DeliveryAttemptFailed(1), DeliveryAttemptFailed(2)
When:   DeliveryAttemptFailed { AttemptNumber: 3 }
Then:   DeliveryAttemptFailed event appended
        Shipment.DeliveryAttemptCount = 3
        (Carrier automatically initiates RTS — next event is ReturnToSenderInitiated)
```

**Idempotency — duplicate attempt report:**
```
Given:  DeliveryAttemptFailed { AttemptNumber: 1 } already recorded
When:   DeliveryAttemptFailed { AttemptNumber: 1 } arrives again (duplicate webhook)
Then:   No additional event (idempotent by ShipmentId + AttemptNumber)
```

#### Slice 25: Ghost Shipment Detection

**Failure scenario:**
```
Given:  ShipmentHandedToCarrier { HandedAt: "Monday 2:10 PM" }
When:   (24 hours pass without any carrier scan)
Then:   GhostShipmentDetected { HoursWithoutScan: 24 }
        Operations team alerted
        ShipmentStatusView: { Status: "UnderInvestigation" }
```

**Compensation — carrier scan resolves ghost:**
```
Given:  GhostShipmentDetected
When:   ShipmentInTransit { FacilityScan: "Edison, NJ hub" }
Then:   ShipmentInTransit event appended — ghost resolved
        ShipmentStatusView: { Status: "InTransit" }
        No customer notification (resolved within tolerance)
```

#### Slice 26: Shipment Lost in Transit

**Failure scenario:**
```
Given:  ShipmentHandedToCarrier { Carrier: "USPS" }, last scan 5+ business days ago
When:   (scheduled job detects 5 business days without scan)
Then:   ShipmentLostInTransit { BusinessDaysNoScan: 5 }
        CarrierTraceOpened { TraceWindowDays: 15 }
        ReshipmentCreated { OriginalShipmentId: "ship-A", NewShipmentId: "ship-B", Reason: "LostInTransit" }
        Customer email: "We're reshipping your order"
        Original stream terminal: "Lost — Replacement Shipped"
```

**Compensation — trace resolves (original found after reshipment):**
```
Given:  ShipmentLostInTransit, ReshipmentCreated, CarrierTraceOpened
When:   Carrier locates original package and delivers it
Then:   Customer keeps both (per CritterSupply policy — documented in feature file)
        CarrierTraceClosed { Resolution: "DeliveredAfterReshipment" }
```

#### Slice 27: Return to Sender Initiated

**Failure scenario:**
```
Given:  DeliveryAttemptFailed(3)
When:   ReturnToSenderInitiated { Carrier: "UPS", TotalAttempts: 3 }
Then:   ReturnToSenderInitiated event appended
        ShipmentStatusView: { Status: "ReturningToSender" }
        Customer email: "Your package is being returned — we'll contact you about reship or refund"
```

**Idempotency — duplicate RTS:**
```
Given:  ReturnToSenderInitiated already recorded
When:   ReturnToSenderInitiated arrives again (duplicate carrier webhook)
Then:   No additional event (idempotent)
```

---

## Integration Contract Implications

### Current Contracts (from CONTEXTS.md)

| Direction | Current | Status |
|---|---|---|
| Orders → Fulfillment | `FulfillmentRequested` | **Unchanged** |
| Fulfillment → Orders | `ShipmentDispatched` | **Replace** with `ShipmentHandedToCarrier` (more precise) |
| Fulfillment → Orders | `ShipmentDelivered` | **Unchanged** |
| Fulfillment → Orders | `ShipmentDeliveryFailed` | **Remove** — replaced by `ReturnToSenderInitiated` |
| Fulfillment → Inventory | Stock adjustment on dispatch | **Unchanged** but timing changes (at `ShipmentHandedToCarrier`) |
| Fulfillment ↔ Returns | Return receipt | **Unchanged** |

### New Integration Events (proposed)

| Direction | Event | Purpose |
|---|---|---|
| Fulfillment → Orders | `TrackingNumberAssigned` | Customer-facing tracking info |
| Fulfillment → Orders | `DeliveryAttemptFailed` | Customer notification trigger |
| Fulfillment → Orders | `GhostShipmentDetected` | Investigation notification |
| Fulfillment → Orders | `ShipmentLostInTransit` | Triggers reshipment flow |
| Fulfillment → Orders | `ReturnToSenderInitiated` | Replaces terminal `ShipmentDeliveryFailed` |
| Fulfillment → Orders | `ReshipmentCreated` | Orders saga learns about replacement shipment |
| Fulfillment → Inventory | `ItemPicked` | Physical bin count reconciliation |
| Fulfillment → Inventory | `ShipmentRerouted` | Stock availability re-check at new FC |

### Orders Saga Impact

The Orders saga currently handles: `ShipmentDispatched`, `ShipmentDelivered`, `ShipmentDeliveryFailed`.

Post-remaster, it would need handlers for:
- `ShipmentHandedToCarrier` (replaces `ShipmentDispatched`)
- `TrackingNumberAssigned` (new — for customer notification)
- `DeliveryAttemptFailed` (new — for customer notification)
- `ReturnToSenderInitiated` (replaces `ShipmentDeliveryFailed`)
- `ReshipmentCreated` (new — links replacement shipment to order)

**Note:** These contract changes are documented only. No implementation in this session.

---

## Inventory Gap Register

Maintained by @principal-architect throughout the session. This list seeds the Inventory BC Remaster.

| # | Gap | Severity | Current State | Required State | Surfaced During |
|---|---|---|---|---|---|
| 1 | **Hardcoded `WH-01` warehouse** in `OrderPlacedHandler` | 🔴 Critical | `const string defaultWarehouse = "WH-01"` | Routing decision should provide `WarehouseId` from Fulfillment's `FulfillmentCenterAssigned` event, passed through Orders saga | Phase 2 — routing discussion |
| 2 | **No multi-warehouse allocation** | 🔴 Critical | Single `ProductInventory` aggregate per SKU — no warehouse dimension | Need per-warehouse stock levels: `ProductInventory` keyed by (SKU, WarehouseId) | Phase 2 — multi-FC routing |
| 3 | **`StockReceived` vs `StockRestocked` redundancy** | 🟡 Medium | Both events do identical things to aggregate state | Clarify semantics: `StockReceived` = PO receipt, `StockRestocked` = internal transfer/return | Phase 1 — brain dump |
| 4 | **No `InventoryTransferred` event** | 🟡 Medium | No concept of inter-warehouse transfer | Need `InventoryTransferred { FromWarehouse, ToWarehouse, SKU, Qty }` for FC-to-FC stock movement | Phase 2 — reroute discussion |
| 5 | **`ProductInventory` stream ID uses MD5** | 🟠 Low | MD5-based deterministic ID | Should use UUID v5 per ADR 0016 for consistency | Phase 2 |
| 6 | **Reservation commit timing ambiguity** | 🟡 Medium | `ReservationCommitted` fires from Orders saga (payment confirmed) | `ItemPicked` is the physical commit — Inventory should receive `ItemPicked` to reconcile bin counts, but `ReservationCommitted` remains the logical commit | Phase 2 — Q2 |
| 7 | **No bin-level inventory tracking** | 🟡 Medium | Stock levels at warehouse level only | WMS needs bin-level quantities for pick optimization and short pick detection | Phase 3 — RF scanner storyboard |
| 8 | **No backorder notification to Inventory** | 🟡 Medium | `BackorderCreated` not published to Inventory | Inventory should flag SKU for replenishment when all FCs report zero stock | Phase 2 — backorder flow |
| 9 | **No capacity data exposure** | 🟠 Low | Inventory has no API for warehouse capacity queries | Routing engine needs `FulfillmentCenterCapacityView` fed by Inventory data | Phase 3 — routing storyboard |
