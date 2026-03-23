# M33.0 Session 8: Priority 2 — Three Missing Marten Projections

**Date:** 2026-03-23
**Status:** 📋 Planning Complete — Ready for Implementation
**Context:** Post-Priority 3 completion; moving to next milestone deliverable

---

## Purpose

Session 8 addresses M33.0 Priority 2: Build the three missing Marten projections that will replace stub zeros on the Backoffice dashboard. This session depends on Session 1's INV-3 + F-8 fixes being complete (they are), which ensures the event streams these projections consume are reliable.

**From M33+M34 Engineering Proposal:**
> "The projections are not a new feature — they are two stub zeros on a screen that executives use today. M33 fixes every broken feedback loop in the currently-shipped codebase."

---

## What Must Ship

### MUST (Exit Criteria for M33.0 Priority 2)

1. ✅ **ReturnMetricsView Projection**
   - **Purpose:** Replace `PendingReturns` stub (currently shows `0`)
   - **Data Model:**
     - `ActiveReturnCount` — count of returns not in terminal state
     - Pipeline stage breakdown (Requested, InTransit, Received, etc.)
     - Avg time to resolution (optional for MVP)
   - **Events Consumed:**
     - `ReturnInitiated` (formerly `ReturnRequested`)
     - `ReturnApproved`
     - `ReturnDenied`
     - `ReturnReceived`
     - `InspectionStarted`
     - `InspectionPassed`
     - `InspectionFailed`
     - `InspectionPartiallyPassed` (formerly `InspectionMixed`)
     - `ReturnCompleted`
     - `ReturnExpired`
   - **Implementation:**
     - MultiStreamProjection (aggregates across all return streams)
     - Inline lifecycle (zero lag)
     - Document ID: singleton `"singleton"`
     - Wire to BackofficeHub for real-time push

2. ✅ **CorrespondenceMetricsView Projection**
   - **Purpose:** Power future "Delivery Success Rate" KPI
   - **Data Model:**
     - `TotalMessagesQueued` — count of `MessageQueued`
     - `TotalMessagesDelivered` — count of `MessageDelivered`
     - `TotalMessagesFailed` — count of `MessageDeliveryFailed`
     - `DeliverySuccessRate` — computed field
   - **Events Consumed:**
     - `MessageQueued`
     - `MessageDelivered`
     - `MessageDeliveryFailed` (renamed from `DeliveryFailed` in M33 Phase 4)
     - `MessageSkipped`
   - **Implementation:**
     - MultiStreamProjection (aggregates across all message streams)
     - Inline lifecycle
     - Document ID: singleton `"singleton"`

3. ✅ **FulfillmentPipelineView Projection**
   - **Purpose:** Operational pipeline visibility (largest of the three)
   - **Data Model (from ADR 0036):**
     - Stage distribution: `StockReserved`, `ReservationCommitted`, `WarehouseAssigned`, `ShipmentDispatched`, `Delivered`, `Failed`
     - Longest-unresolved-assignment signal (shipment ID + days since dispatch)
     - Total active shipments
   - **Events Consumed:**
     - `StockReserved` (Inventory BC)
     - `ReservationCommitted` (Inventory BC)
     - `WarehouseAssigned` (Fulfillment BC)
     - `ShipmentDispatched` (Fulfillment BC)
     - `ShipmentDelivered` (Fulfillment BC)
     - `ShipmentDeliveryFailed` (Fulfillment BC)
   - **Implementation:**
     - MultiStreamProjection (cross-BC aggregation)
     - Inline lifecycle
     - Document ID: singleton `"singleton"`
     - **Note:** This is the largest/most complex projection

4. ✅ **Update GetDashboardSummary Query**
   - Remove `PendingReturns` stub (currently `0`)
   - Replace with `ReturnMetricsView.ActiveReturnCount`
   - Add `CorrespondenceMetricsView.DeliverySuccessRate` (if KPI added to dashboard)

5. ✅ **Integration Tests**
   - Test each projection's event handling
   - Use `BackofficeTestFixture.ExecuteAndWaitAsync()` pattern from Session 1 (F-8)
   - Verify projection updates via HTTP query (not direct Marten append)

---

## Sequencing

**Dependencies:**
1. Session 1 (INV-3 + F-8) MUST be complete ✅ — events now publish correctly, test harness instrumented
2. Returns BC event renames (Phase 3) deferred to later session — use current event names

**Can be parallelized:**
- ReturnMetricsView + CorrespondenceMetricsView can be built independently
- FulfillmentPipelineView is the largest — start it first or last depending on strategy

**Recommended sequence:**
1. ReturnMetricsView (smallest, high-value, validates F-8 pattern)
2. CorrespondenceMetricsView (medium, straightforward aggregation)
3. FulfillmentPipelineView (largest, cross-BC complexity)
4. Update GetDashboardSummary
5. Integration tests for all three

---

## Implementation Checklist

### Phase 1: ReturnMetricsView

- [ ] Create `src/Backoffice/Backoffice/Projections/ReturnMetricsView.cs`
  - Document model: singleton, `ActiveReturnCount`, stage breakdown
  - MultiStreamProjection base class
  - `Identity()` returns `"singleton"` for all events
  - `Create()` method for `ReturnInitiated`
  - `Apply()` methods for each return lifecycle event
  - Increment `ActiveReturnCount` on `ReturnInitiated`
  - Decrement `ActiveReturnCount` on terminal events (`ReturnCompleted`, `ReturnDenied`, `ReturnExpired`)
  - Update stage counters on status transitions

- [ ] Register projection in `src/Backoffice/Backoffice.Api/Program.cs`
  ```csharp
  opts.Projections.Add<ReturnMetricsView>(ProjectionLifecycle.Inline);
  ```

- [ ] Update `src/Backoffice/Backoffice.Api/Queries/GetDashboardSummary.cs`
  - Remove `PendingReturns = 0` stub
  - Add query for `ReturnMetricsView` singleton
  - Return `view.ActiveReturnCount`

### Phase 2: CorrespondenceMetricsView

- [ ] Create `src/Backoffice/Backoffice/Projections/CorrespondenceMetricsView.cs`
  - Document model: singleton, message counts, success rate
  - MultiStreamProjection base class
  - `Identity()` returns `"singleton"`
  - `Create()` + `Apply()` for `MessageQueued`, `MessageDelivered`, `MessageDeliveryFailed`, `MessageSkipped`
  - Computed property: `DeliverySuccessRate` = `TotalMessagesDelivered / (TotalMessagesQueued - TotalMessagesSkipped)`

- [ ] Register projection in `Program.cs`
  ```csharp
  opts.Projections.Add<CorrespondenceMetricsView>(ProjectionLifecycle.Inline);
  ```

- [ ] (Optional) Add KPI to dashboard query if desired

### Phase 3: FulfillmentPipelineView

- [ ] Create `src/Backoffice/Backoffice/Projections/FulfillmentPipelineView.cs`
  - Document model: stage distribution, longest-unresolved, total active
  - MultiStreamProjection base class
  - `Identity()` returns `"singleton"`
  - Internal tracking: dictionary of shipment IDs → stage + timestamp
  - `Create()` + `Apply()` for all 6 events
  - Update stage counters on transitions
  - Track longest-unresolved (shipment ID + days since dispatch)
  - Terminal events (`ShipmentDelivered`, `ShipmentDeliveryFailed`) remove from active tracking

- [ ] Register projection in `Program.cs`
  ```csharp
  opts.Projections.Add<FulfillmentPipelineView>(ProjectionLifecycle.Inline);
  ```

- [ ] (Optional) Add endpoint to query fulfillment pipeline view

### Phase 4: Integration Tests

- [ ] Create `tests/Backoffice/Backoffice.Api.IntegrationTests/Projections/ReturnMetricsViewTests.cs`
  - Test: Publish `ReturnInitiated` → verify `ActiveReturnCount` increments
  - Test: Publish `ReturnCompleted` → verify `ActiveReturnCount` decrements
  - Test: Multiple returns → verify correct aggregation
  - Use `ExecuteAndWaitAsync()` pattern (F-8)

- [ ] Create `tests/Backoffice/Backoffice.Api.IntegrationTests/Projections/CorrespondenceMetricsViewTests.cs`
  - Test: Publish `MessageQueued` + `MessageDelivered` → verify success rate
  - Test: Publish `MessageDeliveryFailed` → verify failed count

- [ ] Create `tests/Backoffice/Backoffice.Api.IntegrationTests/Projections/FulfillmentPipelineViewTests.cs`
  - Test: Publish `StockReserved` → `ShipmentDispatched` → `ShipmentDelivered` → verify stage transitions
  - Test: Multiple shipments → verify stage distribution

### Phase 5: Build & Verification

- [ ] Run `dotnet build` (expect 0 errors)
- [ ] Run `dotnet test tests/Backoffice/Backoffice.Api.IntegrationTests` (all green)
- [ ] Manual verification:
  - Start infrastructure
  - Create a return via Returns.Api → verify dashboard `PendingReturns` increments
  - Complete a return → verify `PendingReturns` decrements
  - Verify no console errors

---

## Expected Outcomes

### Must Have (Session Success Criteria)

1. ✅ All three projections registered and functional
2. ✅ Dashboard `PendingReturns` shows **real data** (not stub `0`)
3. ✅ Integration tests prove projections update via event streams
4. ✅ All existing tests still pass (no regressions)
5. ✅ Build succeeds (0 errors)

### Nice to Have (Defer if Time Runs Out)

- Real-time SignalR push for `PendingReturns` changes (M34 untapped value item)
- Fulfillment pipeline KPI tile on dashboard
- Correspondence success rate KPI tile

---

## Exit Criteria

Session 8 is complete when **all** of these are true:

1. ✅ Build succeeds: `dotnet build` (0 errors)
2. ✅ All tests pass: `dotnet test` (91+ Backoffice.Api.IntegrationTests + new projection tests)
3. ✅ Manual verification complete (dashboard shows real return count)
4. ✅ Three projections complete (ReturnMetricsView, CorrespondenceMetricsView, FulfillmentPipelineView)
5. ✅ Session retrospective written: `m33-0-session-8-retrospective.md`

---

## References

- [M33+M34 Engineering Proposal](./m33-m34-engineering-proposal-2026-03-21.md) — Priority 2 scope
- [ADR 0036: BFF-Owned Projections Strategy](../../decisions/0036-bff-projections-strategy.md) — FulfillmentPipelineView data model
- [Session 1 Retrospective](./m33-0-session-1-retrospective.md) — INV-3 + F-8 completion
- [Session 7 Retrospective](./m33-0-session-7-retrospective.md) — Priority 3 recovery
- [Event Sourcing Projections Skill](../../skills/event-sourcing-projections.md) — Implementation patterns

---

## Notes for Implementation

### Projection Pattern Reminder

From `docs/skills/event-sourcing-projections.md`:

**MultiStreamProjection for singleton aggregations:**
```csharp
public sealed class ReturnMetricsView : MultiStreamProjection<ReturnMetricsView, string>
{
    public int ActiveReturnCount { get; init; }
    public Dictionary<ReturnStage, int> StageBreakdown { get; init; } = new();

    // Identity method — all events map to same singleton document
    public override string Identity(IEvent<ReturnInitiated> @event) => "singleton";
    public override string Identity(IEvent<ReturnCompleted> @event) => "singleton";
    // ...

    public static ReturnMetricsView Create(ReturnInitiated evt) =>
        new() { ActiveReturnCount = 1 };

    public ReturnMetricsView Apply(ReturnCompleted evt) =>
        this with { ActiveReturnCount = ActiveReturnCount - 1 };
}
```

**Inline Projection Registration:**
```csharp
opts.Projections.Add<ReturnMetricsView>(ProjectionLifecycle.Inline);
```

### Event Naming Note

Use **current** Returns BC event names (from code, not post-rename):
- `ReturnRequested` (not yet renamed to `ReturnInitiated`)
- `InspectionMixed` (not yet renamed to `InspectionPartiallyPassed`)

**Rationale:** Phase 3 (Returns refactor + UXE renames) hasn't happened yet. Use what exists today.

### Testing Pattern

From Session 1 (F-8), use `ExecuteAndWaitAsync()`:
```csharp
// Publish event via Wolverine bus (not direct Marten append)
await fixture.ExecuteAndWaitAsync(async bus =>
{
    await bus.PublishAsync(new ReturnRequested(...));
});

// Query projection via HTTP endpoint (not direct Marten query)
var result = await fixture.Host
    .GetJson<DashboardSummary>("/api/backoffice/dashboard/summary");

result.PendingReturns.ShouldBe(1);
```

---

**Session Start:** 2026-03-23 (after plan approval)
**Estimated Duration:** 4-6 hours (largest scope in M33.0 so far)
**Priority:** HIGH (completes Priority 2, unblocks dashboard truthfulness)
