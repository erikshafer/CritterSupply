# Fulfillment BC Remaster — Implementation Session 2 (S2)

## Context

S1 (PR #530) implemented all 15 P0 slices. The remaster foundation is in place:
two aggregates (`WorkOrder` + `Shipment`), stub routing engine, full pick/pack and
carrier lifecycle, dual-publish migration, 50 tests passing. S2 has three parts:

1. **Debt clearance (first act, non-negotiable):** `WorkOrderHandlers.cs` violates
   vertical slice organization. Fix this before touching anything else.

2. **Deferred S1 items:** Two items were explicitly scoped out of S1 and must land in S2
   before P1 work begins.

3. **P1 slices (16–29):** All 14 failure mode slices.

Do not begin P1 work until Parts 1 and 2 are complete and the build is green.

---

## Required Reading — Do First

1. **`docs/skills/vertical-slice-organization.md`** — read completely before writing a
   single line of code. The debt clearance in Part 1 is a direct application of this skill.
   The P1 failure mode handlers must follow it from the start.

2. **`docs/planning/fulfillment-remaster-slices.md`** — P1 slices 16–29 (Phase 5 scenarios)
   are your acceptance criteria. Read every scenario. The idempotency scenarios in particular
   are mandatory, not optional.

3. **`docs/planning/milestones/fulfillment-remaster-s1-retrospective.md`** — know the four
   documented S1 deviations. They carry forward: `Before()/Handle()` over `[WriteAggregate]`,
   and `RecordItemPick` auto-starting picking. Do not relitigate them.

4. **`docs/decisions/0059-fulfillment-bc-remaster-rationale.md`** — architectural decisions
   are final. Two aggregates, dual-publish, routing in Fulfillment. No reopening.

5. **`docs/skills/wolverine-message-handlers.md`** — compound handler lifecycle,
   cascading handlers (policy pattern), `OutgoingMessages` for integration events.

6. **`docs/skills/marten-event-sourcing.md`** — aggregate `Apply()` patterns, snapshot
   configuration, stream ID conventions.

7. **`docs/skills/critterstack-testing-patterns.md`** — integration test patterns,
   `DisableAllExternalWolverineTransports`, idempotency test structure.

---

## Mandatory Bookend — Run Before Starting

```bash
dotnet build
# Expected: 0 errors, 19 warnings

dotnet test tests/Fulfillment/Fulfillment.Api.IntegrationTests
# Expected: 25 passing

dotnet test tests/Fulfillment/Fulfillment.UnitTests
# Expected: 25 passing

dotnet test tests/Orders/Orders.Api.IntegrationTests
# Expected: 48 passing — must stay green throughout entire session
```

Record all four counts. The Orders count must not decrease at any point in this session.

---

## Part 1 — Debt Clearance: Split `WorkOrderHandlers.cs`

**This is the first act. Do not proceed to Part 2 until it is complete and the build is green.**

### What Exists (Wrong)

`src/Fulfillment/Fulfillment/WorkOrders/WorkOrderHandlers.cs` contains six commands with
validators and six handlers in a single file. This violates vertical slice organization.

### What It Becomes (Correct)

Delete `WorkOrderHandlers.cs` and create six individual files in
`src/Fulfillment/Fulfillment/WorkOrders/`:

| New File | Contains |
|---|---|
| `ReleaseWave.cs` | `ReleaseWave` command + `Validator` + `ReleaseWaveHandler` |
| `AssignPickList.cs` | `AssignPickList` command + `Validator` + `AssignPickListHandler` |
| `StartPicking.cs` | `StartPicking` command + `Validator` + `StartPickingHandler` |
| `RecordItemPick.cs` | `RecordItemPick` command + `Validator` + `RecordItemPickHandler` |
| `StartPacking.cs` | `StartPacking` command + `Validator` + `StartPackingHandler` |
| `VerifyItemAtPack.cs` | `VerifyItemAtPack` command + `Validator` + `VerifyItemAtPackHandler` |

No logic changes — this is a pure structural reorganization. All `namespace`,
`using`, and handler logic copies verbatim from `WorkOrderHandlers.cs`.

### Verification

```bash
dotnet build                                          # 0 errors, 19 warnings
dotnet test tests/Fulfillment/Fulfillment.UnitTests   # 25 passing (unchanged)
dotnet test tests/Fulfillment/Fulfillment.Api.IntegrationTests  # 25 passing (unchanged)
```

Commit before moving to Part 2:
```
fulfillment-remaster-s2: split WorkOrderHandlers.cs into vertical slices (debt clearance)
```

---

## Part 2 — Deferred S1 Items

Complete both before starting P1 slices.

### 2A: HTTP Carrier Webhook Endpoint

The `CarrierWebhookPayload` command handler exists but is not wired to an HTTP endpoint.
Add:

```csharp
[WolverinePost("/api/fulfillment/carrier-webhook")]
public static async Task<IResult> Handle(
    CarrierWebhookPayload payload,
    IDocumentSession session,
    CancellationToken ct) { ... }
```

The handler already exists — this is purely adding the `[WolverinePost]` attribute and
wiring it to the endpoint. Ensure the endpoint is registered in `Program.cs` and covered
by `[Authorize]` per the convention established in M36.0 (all non-auth endpoints are
authorized from first commit).

Add one integration test that posts to the endpoint and verifies a domain event is appended.

### 2B: `PackingCompleted` → `GenerateShippingLabel` Cascading Policy

Currently, label generation requires a manual `GenerateShippingLabel` command. The
retrospective explicitly deferred this policy bridge. Implement it now as a Wolverine
cascading handler:

```csharp
// When PackingCompleted fires on a WorkOrder stream, automatically trigger
// label generation on the corresponding Shipment stream.
public static class PackingCompletedPolicy
{
    public static GenerateShippingLabel Handle(PackingCompleted @event, WorkOrder workOrder)
    {
        // Cascade: return the command that triggers the next step
        return new GenerateShippingLabel(workOrder.ShipmentId);
    }
}
```

This is a Wolverine side-effect cascade — the handler returns a command that Wolverine
automatically dispatches. See `wolverine-message-handlers.md` for the cascading handler
pattern.

Update any integration tests that previously manually triggered `GenerateShippingLabel`
to instead trigger `VerifyItemAtPack` (the last step before `PackingCompleted`) and verify
that label generation happens automatically.

Commit before moving to Part 3:
```
fulfillment-remaster-s2: wire HTTP carrier webhook endpoint
fulfillment-remaster-s2: PackingCompleted -> GenerateShippingLabel cascading policy
```

---

## Part 3 — P1 Failure Mode Slices (16–29)

### Vertical Slice Convention — Mandatory

Every P1 handler lives in its own file, colocated with its command and validator.
New files go in the appropriate folder (`WorkOrders/` for warehouse ops, `Shipments/`
for carrier/delivery). No new bulk handler files. If you find yourself adding a handler
to an existing `*Handlers.cs` file: stop and create a new file instead.

### Slice 16: Item Not Found at Bin

**Command:** `ReportShortPick`
**Events:** `ItemNotFoundAtBin`, `ShortPickDetected`
**File:** `src/Fulfillment/Fulfillment/WorkOrders/ReportShortPick.cs`

The `WorkOrder` must be in `PickStarted` status. The handler validates the SKU is in the
work order, appends `ItemNotFoundAtBin` (with the bin that was checked), then appends
`ShortPickDetected` (with shortage quantity). The WorkOrder transitions to `ShortPickPending`.

**`WorkOrder` changes needed:**
- Add `WorkOrderStatus.ShortPickPending`
- Add `Apply(ItemNotFoundAtBin)` and `Apply(ShortPickDetected)` methods

### Slice 17: Short Pick — Alternative Bin Resolves

**Command:** `ResumePick`
**Events:** `PickResumed`, `ItemPicked` (+ auto-completion chain if all items now picked)
**File:** `src/Fulfillment/Fulfillment/WorkOrders/ResumePick.cs`

WorkOrder must be in `ShortPickPending`. The handler appends `PickResumed` with the
alternative bin, then appends `ItemPicked` for the quantity found. Apply the same
auto-completion logic as `RecordItemPickHandler` — if `AllItemsPicked` after the resumed
pick, append `PickCompleted`.

### Slice 18: Short Pick — No Stock at FC → Reroute

**Command:** `RerouteShipment`
**Events:** `PickExceptionRaised` (on WorkOrder), `ShipmentRerouted` (on Shipment),
`WorkOrderCreated` (new WorkOrder stream for new FC)
**Files:**
- `src/Fulfillment/Fulfillment/WorkOrders/RerouteShipment.cs`

This slice crosses both aggregates. The handler:
1. Appends `PickExceptionRaised` to the original `WorkOrder` stream (closes the old stream)
2. Appends `ShipmentRerouted` to the `Shipment` stream with `{ OriginalFC, NewFC }`
3. Creates a new `WorkOrder` stream (UUID v5 from `(ShipmentId, NewFC)`) with `WorkOrderCreated`

The `Shipment` aggregate needs `Apply(ShipmentRerouted)` — update `FulfillmentCenterId`
and reset to `Assigned` status.

**Important:** The new WorkOrder creation should re-use the same `WorkOrderCreated` path
as Slice 3. Do not duplicate that logic — extract it if necessary.

### Slice 19: Short Pick — No Stock Anywhere → Backorder

**Command:** `CreateBackorder`
**Events:** `PickExceptionRaised` (on WorkOrder), `BackorderCreated` (on Shipment)
**File:** `src/Fulfillment/Fulfillment/WorkOrders/CreateBackorder.cs`

WorkOrder must be in `ShortPickPending`. The `Shipment` receives `BackorderCreated` and
transitions to `Backordered` status. Publish integration event `BackorderCreated` so Orders
saga can notify the customer.

### Slice 20: Wrong Item Scanned at Pack Station

**Command:** `ReportPackDiscrepancy`
**Events:** `WrongItemScannedAtPack`, `PackDiscrepancyDetected`
**File:** `src/Fulfillment/Fulfillment/WorkOrders/ReportPackDiscrepancy.cs`

WorkOrder must be in `PackingStarted`. Validates that the scanned SKU does not match any
expected SKU in the work order (or matches a SKU but the wrong quantity). The WorkOrder
transitions to `PackDiscrepancyPending`. The operator must correct the item before packing
can continue.

Add `WorkOrderStatus.PackDiscrepancyPending` and the corresponding `Apply()` methods.

### Slice 21: Weight Mismatch at Pack Station

Reuses `ReportPackDiscrepancy` command but with a different `DiscrepancyType`:
`WeightMismatch`. The same handler distinguishes the type from the payload. No separate
command needed — extend `ReportPackDiscrepancy` with a `DiscrepancyType` enum field.

### Slice 22: Shipping Label Generation Failed

**Trigger:** System — carrier API failure or invalid address
**Events:** `ShippingLabelGenerationFailed` (on Shipment)
**File:** `src/Fulfillment/Fulfillment/Shipments/ShippingLabelGenerationFailed.cs`
(domain event only — the failure is detected in `GenerateShippingLabelHandler` via try/catch)

Update `GenerateShippingLabelHandler` to catch carrier API failures and append
`ShippingLabelGenerationFailed` instead of propagating the exception. The `Shipment`
transitions to `LabelGenerationFailed` status.

Add `Apply(ShippingLabelGenerationFailed)` to the `Shipment` aggregate.

### Slice 23: Carrier Pickup Missed → Alternate Carrier Arranged

**Commands:** `ReportCarrierPickupMissed`, `ArrangeAlternateCarrier`
**Events:** `CarrierPickupMissed`, `CarrierRelationsEscalated`, `AlternateCarrierArranged`,
`ShippingLabelVoided`, `ShippingLabelGenerated` (new carrier), `TrackingNumberAssigned` (new)
**Files:**
- `src/Fulfillment/Fulfillment/Shipments/ReportCarrierPickupMissed.cs`
- `src/Fulfillment/Fulfillment/Shipments/ArrangeAlternateCarrier.cs`

`ReportCarrierPickupMissed` is triggered by a scheduled job checking for missed pickup
windows. `ArrangeAlternateCarrier` is a manual command by the dock supervisor.

When `ArrangeAlternateCarrier` is handled:
1. Append `AlternateCarrierArranged`
2. Append `ShippingLabelVoided` (voids the original carrier's label)
3. Generate a new label: append `ShippingLabelGenerated` (new carrier) + `TrackingNumberAssigned`

The new `TrackingNumberAssigned` triggers the dual-publish of the updated tracking number
to the Orders saga.

### Slice 24: Delivery Attempt Failed (Attempt 1/2/3)

**Trigger:** Carrier webhook — `EventType: "DELIVERY_ATTEMPTED"`
**Events:** `DeliveryAttemptFailed(AttemptNumber)` (on Shipment)
**File:** Handled in `CarrierWebhookHandler` (already exists from S1)

Update `CarrierWebhookHandler` to correctly route `DELIVERY_ATTEMPTED` events and include
`AttemptNumber` from the payload.

**`Shipment` aggregate changes:**
- Track `DeliveryAttemptCount`
- `Apply(DeliveryAttemptFailed)` increments the count
- After the third attempt, the aggregate is in `AllAttemptsExhausted` status — awaiting
  `ReturnToSenderInitiated` from the carrier

Idempotency: if `DeliveryAttemptFailed { AttemptNumber: N }` arrives when the aggregate
already has attempt N recorded, silently skip (no duplicate event).

### Slice 25: Ghost Shipment Detection

**Trigger:** Scheduled job — 24 hours after `ShipmentHandedToCarrier` with no carrier scan
**Events:** `GhostShipmentDetected` (on Shipment)
**File:** `src/Fulfillment/Fulfillment/Shipments/GhostShipmentDetection.cs`

Implement as a Wolverine scheduled message that fires 24 hours after `ShipmentHandedToCarrier`.
The handler checks whether `ShipmentInTransit` has been appended since handoff. If not,
appends `GhostShipmentDetected`.

The Shipment transitions to `UnderInvestigation` status. The ghost resolves when
`ShipmentInTransit` arrives (update `Apply` to exit investigation if a scan arrives while
in `UnderInvestigation`).

For tests: use `bus.ScheduleAsync()` with a short delay to verify the scheduled message
fires and produces the expected event.

### Slice 26: Shipment Lost in Transit

**Trigger:** Scheduled job — 5 business days after `ShipmentHandedToCarrier` with no scan
**Events:** `ShipmentLostInTransit`, `CarrierTraceOpened` (on Shipment)
**File:** `src/Fulfillment/Fulfillment/Shipments/ShipmentLostInTransit.cs`

Similar pattern to Slice 25. The handler for the scheduled job checks business days since
handoff (stub: calendar days / 1.4, rounded). If threshold exceeded, appends both events.

`CarrierTraceOpened` carries `{ Carrier, TraceWindowDays: 15, TraceReferenceId }`.

Publish integration event `ShipmentLostInTransit` to Orders saga — this triggers the
reshipment flow (P2, not in this session, but the event needs to be published).

### Slice 27: Return to Sender Initiated

**Trigger:** Carrier webhook — `EventType: "RETURN_TO_SENDER"`
**Events:** `ReturnToSenderInitiated` (on Shipment)
**File:** Handled in `CarrierWebhookHandler`

Update `CarrierWebhookHandler` to route `RETURN_TO_SENDER` events. The handler appends
`ReturnToSenderInitiated { Carrier, TotalAttempts, EstimatedReturnDays }`.

The `Shipment` transitions to `ReturningToSender` status. This is already in the dual-publish
integration event set from S1 — verify the integration event is published.

### Slice 28: Return Received at Warehouse

**Command:** `ReceiveReturnAtWarehouse`
**Events:** `ReturnReceivedAtWarehouse` (on Shipment)
**File:** `src/Fulfillment/Fulfillment/Shipments/ReceiveReturnAtWarehouse.cs`

Shipment must be in `ReturningToSender` status. The handler appends
`ReturnReceivedAtWarehouse { ReceivedAt, WarehouseId }`. The Shipment transitions to
`ReturnReceived` status, awaiting a customer decision (reship vs. refund — P2).

### Slice 29: SLA Escalation

**Trigger:** Scheduled job — checks SLA thresholds on active WorkOrders
**Events:** `SLAEscalationRaised` (at 50% and 75% threshold), `SLABreached` (at 100%)
**File:** `src/Fulfillment/Fulfillment/WorkOrders/SLAMonitoring.cs`

The SLA window is determined by order type (standard: 4h, expedited: 2h — stub for now,
use standard). The scheduled job fires at a configurable interval (stub: every 5 minutes).

For each `WorkOrder` in a non-terminal status:
- If elapsed > 50% of SLA and no escalation raised: append `SLAEscalationRaised { Threshold: 50 }`
- If elapsed > 75% and only 50% escalation raised: append `SLAEscalationRaised { Threshold: 75 }`
- If elapsed > 100%: append `SLABreached`

The WorkOrder aggregate tracks `EscalationThresholdsMet` (a set of thresholds already fired)
to prevent duplicate escalations.

---

## Integration Tests — P1

For every P1 slice, write tests covering the three mandatory scenarios from the event
modeling session Phase 5:

1. **The failure scenario** — the failure event is correctly appended
2. **The compensation path** — the recovery flow produces the right events
3. **The idempotency scenario** — duplicate messages do not produce duplicate events

**File:** Add to existing test classes or create new ones in
`tests/Fulfillment/Fulfillment.Api.IntegrationTests/`

Suggested test class additions:
- `PickFailureTests` — Slices 16–19
- `PackFailureTests` — Slices 20–22
- `CarrierExceptionTests` — Slices 23–24
- `ShipmentMonitoringTests` — Slices 25–27
- `ReturnReceivedTests` — Slices 28–29

---

## `WorkOrder` and `Shipment` Aggregate Changes Summary

Keep a running list as you implement. Expected additions:

**WorkOrder statuses to add:**
`ShortPickPending`, `PackDiscrepancyPending`

**WorkOrder events to add to `WorkOrderEvents.cs`:**
`ItemNotFoundAtBin`, `ShortPickDetected`, `PickResumed`, `PickExceptionRaised`,
`WrongItemScannedAtPack`, `PackDiscrepancyDetected`

**Shipment statuses to add:**
`Rerouted`, `Backordered`, `LabelGenerationFailed`, `GhostShipmentInvestigation`,
`AllAttemptsExhausted`, `ReturningToSender`, `ReturnReceived`

**Shipment events to add to `ShipmentEvents.cs`:**
`ShipmentRerouted`, `BackorderCreated`, `ShippingLabelGenerationFailed`,
`CarrierPickupMissed`, `CarrierRelationsEscalated`, `AlternateCarrierArranged`,
`ShippingLabelVoided`, `GhostShipmentDetected`, `ShipmentLostInTransit`,
`CarrierTraceOpened`, `ReturnReceivedAtWarehouse`

---

## Integration Event Publishing — P1 Additions

| Event | Integration Message | Direction | Notes |
|---|---|---|---|
| `BackorderCreated` | `BackorderCreated` | Fulfillment → Orders | Customer notification trigger |
| `ShipmentLostInTransit` | `ShipmentLostInTransit` | Fulfillment → Orders | Reshipment trigger (P2) |
| `ReturnToSenderInitiated` | Already in S1 dual-publish set | — | Verify it fires correctly from webhook |

All new integration messages go in `src/Shared/Messages.Contracts/Fulfillment/`.

---

## Session Bookend — Before Closing

```bash
dotnet build
# Required: 0 errors, 19 warnings

dotnet test tests/Fulfillment/Fulfillment.Api.IntegrationTests
# Required: > 25 (record new count)

dotnet test tests/Fulfillment/Fulfillment.UnitTests
# Required: > 25 (record new count — WorkOrder/Shipment Apply() methods for new events)

dotnet test tests/Orders/Orders.Api.IntegrationTests
# Required: 48 — must not decrease
```

Commit the session retrospective at
`docs/planning/milestones/fulfillment-remaster-s2-retrospective.md`.

The retrospective must cover:
- Which P1 slices were fully implemented (by number)
- Whether the cascading policy (PackingCompleted → GenerateShippingLabel) worked as expected
- Final test counts across all four suites
- Any deviations from the slice table scenarios (with rationale)
- Known gaps or deferred items (update for S3 if needed)

Update `docs/planning/CURRENT-CYCLE.md` with S2 completion status.

---

## Commit Convention

```
fulfillment-remaster-s2: split WorkOrderHandlers.cs into vertical slices (debt clearance)
fulfillment-remaster-s2: wire HTTP carrier webhook endpoint
fulfillment-remaster-s2: PackingCompleted -> GenerateShippingLabel cascading policy
fulfillment-remaster-s2: Slices 16-17 — short pick + alternative bin resolution
fulfillment-remaster-s2: Slices 18-19 — reroute + backorder
fulfillment-remaster-s2: Slices 20-22 — pack discrepancy + label failure
fulfillment-remaster-s2: Slice 23 — carrier pickup missed + alternate carrier
fulfillment-remaster-s2: Slice 24 — delivery attempt failed chain
fulfillment-remaster-s2: Slices 25-26 — ghost shipment + lost in transit
fulfillment-remaster-s2: Slices 27-29 — return to sender + received + SLA escalation
fulfillment-remaster-s2: integration tests — P1 failure modes
fulfillment-remaster-s2: WorkOrder/Shipment aggregate — new statuses and Apply methods
fulfillment-remaster-s2: integration event contracts — BackorderCreated, ShipmentLostInTransit
fulfillment-remaster-s2: retrospective + CURRENT-CYCLE.md
```

---

## Role

**@PSA — Principal Software Architect**
Owns the implementation. Part 1 is debt clearance — structural only, no logic changes.
Parts 2 and 3 follow the established S1 patterns for aggregate design and handler structure.
The cascading policy in 2B is a new Wolverine pattern for this BC — see `wolverine-message-handlers.md`.

**@QAE — QA Engineer**
Owns integration tests. Every P1 slice requires all three scenario types: failure, compensation,
and idempotency. The idempotency tests are particularly important for carrier webhook events —
carrier APIs send duplicate webhooks in production. Verify Orders integration tests remain at
48 throughout.
