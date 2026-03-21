# M33.0 Session 2 Plan — Build Three Missing Marten Projections

**Date:** 2026-03-21
**Status:** 🚀 IN PROGRESS
**Session Goal:** Build ReturnMetricsView, CorrespondenceMetricsView, and FulfillmentPipelineView projections

---

## Context

**Priority 2 from M33-M34 Proposal:** Build 3 missing Marten projections to replace dashboard stubs.

**Current State:**
- `PendingReturns` KPI: Hard-coded `0` with `// STUB` comment
- `LowStockAlerts` KPI: Hard-coded `0` with `// STUB` comment (will be fixed by `LowStockDetected` integration once INV-3 events flow)
- No `FulfillmentPipelineView` projection (referenced in ADR 0036 TODO comment)
- No `CorrespondenceMetricsView` projection
- No `ReturnMetricsView` projection

**Reference Projections to Follow:**
- `AdminDailyMetricsProjection` — Date-keyed, multi-stream, inline lifecycle
- `AlertFeedViewProjection` — Guid-keyed, multi-stream, inline lifecycle
- `CouponLookupViewProjection` — String-keyed, multi-stream, inline lifecycle

---

## Projection Requirements (from M33 Proposal)

### 1. ReturnMetricsView
**Data Model:**
- Active count (returns not in terminal state)
- Pipeline stage breakdown (pending, approved, denied, received, inspection, completed, expired)
- Average time to resolution
- Mixed-inspection count

**Event Sources:**
- `ReturnRequested` (current name; will be renamed to `ReturnInitiated` in Phase 3)
- `ReturnApproved`
- `ReturnDenied`
- `ReturnReceived`
- `InspectionStarted`
- `InspectionPassed`
- `InspectionFailed`
- `InspectionMixed` (current name; will be renamed to `InspectionPartiallyPassed` in Phase 3)
- `ReturnCompleted`
- `ReturnExpired`

**Key:** Likely aggregate-level key (single document tracking all returns, or daily rollup)

**Dashboard Integration:** Remove `// STUB` from `GetDashboardSummary.cs` and `OrderPlacedHandler.cs`

---

### 2. CorrespondenceMetricsView
**Data Model:**
- Total messages queued (today)
- Total messages delivered (today)
- Total delivery failures (today)
- Delivery success rate (calculated)

**Event Sources:**
- `MessageQueued`
- `MessageDelivered`
- `DeliveryFailed` (current name; will be renamed to `MessageDeliveryFailed` in Phase 4)
- `MessageSkipped` (if exists)

**Key:** Date-keyed (matches `AdminDailyMetrics` pattern)

**Dashboard Integration:** Not currently displayed, but enables future "Correspondence Health" KPI

---

### 3. FulfillmentPipelineView
**Data Model:**
- Stage distribution breakdown (reserved, committed, warehouse assigned, dispatched, delivered, failed)
- Longest unresolved assignment signal
- Pipeline count for "awaiting shipment" state

**Event Sources:**
- `StockReserved`
- `ReservationCommitted`
- `WarehouseAssigned`
- `ShipmentDispatched`
- `ShipmentDelivered`
- `ShipmentDeliveryFailed`

**Key:** TBD (likely per-fulfillment-request or daily rollup)

**Dashboard Integration:** Not currently used (ADR 0036 has TODO comment)

**Note from ADR 0036:** "Largest of the three; ADR 0036 has the data model sketch"

---

## Sequencing

Per M33 proposal sequencing diagram:
```
[Phase 1] INV-3 fix ──→ F-8 fixture ──→ [Phase 6] Three projections + pages
              └───────────────────────────────────────────────────────────────┘
                           MUST be sequential. No Phase 6 before Phase 1.
```

Phase 1 (INV-3 + F-8) is complete ✅, so we can proceed with building projections.

---

## Session Plan

### Step 1: Research Integration Message Contracts (Measure Twice)
- [x] Read `src/Shared/Messages.Contracts/` to find exact message shapes for all three projection event sources
- [x] Identify which BCs publish each message (Returns, Correspondence, Fulfillment, Inventory)
- [x] Verify RabbitMQ queue subscriptions exist in `Backoffice.Api/Program.cs`
- [x] Check if integration message handlers already exist (or need to be created)

**Research Findings:**

**Returns BC Integration Messages** (Published to `storefront-returns-events` and `orders-returns-events`):
- `ReturnRequested`, `ReturnApproved`, `ReturnDenied`, `ReturnRejected`, `ReturnExpired`, `ReturnCompleted`, `ReturnReceived`
- Note: Inspection events (`InspectionStarted`, `InspectionPassed`, `InspectionFailed`, `InspectionMixed`) do NOT exist as integration messages - they are domain events only

**Correspondence BC Integration Messages** (Published to `monitoring-correspondence-events`, `analytics-correspondence-events`, `admin-correspondence-failures`):
- `CorrespondenceQueued` → `monitoring-correspondence-events`
- `CorrespondenceDelivered` → `analytics-correspondence-events`
- `CorrespondenceFailed` → `admin-correspondence-failures`
- Note: No `MessageSkipped` event exists

**Fulfillment BC Integration Messages** (Published to `orders-fulfillment-events`, `storefront-fulfillment-events`, `returns-fulfillment-events`):
- `ShipmentDispatched`, `ShipmentDelivered`, `ShipmentDeliveryFailed`
- Note: No warehouse assignment events published as integration messages

**Backoffice.Api RabbitMQ Subscriptions Status:**
- ✅ Already subscribed: `backoffice-order-placed`, `backoffice-order-cancelled`, `backoffice-payment-captured`, `backoffice-payment-failed`, `backoffice-low-stock-detected`, `backoffice-shipment-delivery-failed`, `backoffice-return-expired`
- ⚠️ **MISSING (lines 230-236 are commented out)**: `backoffice-shipment-dispatched`, `backoffice-stock-replenished`, `backoffice-return-requested`, `backoffice-correspondence-failed`

**Existing Backoffice Handlers (already implemented):**
- `LowStockDetectedHandler`, `OrderCancelledHandler`, `OrderPlacedHandler`, `PaymentCapturedHandler`, `PaymentFailedHandler`, `ReturnExpiredHandler`, `ShipmentDeliveryFailedHandler`

**Missing Handlers (need to create):**
- `ReturnRequestedHandler`, `ReturnApprovedHandler`, `ReturnDeniedHandler`, `ReturnCompletedHandler`, `ReturnReceivedHandler`
- `CorrespondenceQueuedHandler`, `CorrespondenceDeliveredHandler`, `CorrespondenceFailedHandler`
- `ShipmentDispatchedHandler`

**Key Decision Made:**
- Returns projection will use **published integration messages only** (no inspection events): `ReturnRequested`, `ReturnApproved`, `ReturnDenied`, `ReturnCompleted`, `ReturnReceived`, `ReturnExpired` (already handled)
- Fulfillment projection will use **published integration messages only**: `ShipmentDispatched`, `ShipmentDelivered`, `ShipmentDeliveryFailed`
- Correspondence projection will use **published integration messages only**: `CorrespondenceQueued`, `CorrespondenceDelivered`, `CorrespondenceFailed`

### Step 2: Build ReturnMetricsView Projection
- [ ] Create `ReturnMetricsView.cs` document model (in `Backoffice/Projections/`)
- [ ] Create `ReturnMetricsViewProjection.cs` (MultiStreamProjection)
- [ ] Add projection registration in `Backoffice.Api/Program.cs`
- [ ] Create integration message handlers (if missing) to append events to Backoffice stream
- [ ] Write integration test verifying projection updates from `ReturnRequested` → count increment
- [ ] Update `GetDashboardSummary.cs` to query `ReturnMetricsView` instead of stub `0`
- [ ] Update `OrderPlacedHandler.cs` to publish live metric with real `PendingReturns` count

### Step 3: Build CorrespondenceMetricsView Projection
- [ ] Create `CorrespondenceMetricsView.cs` document model (date-keyed like AdminDailyMetrics)
- [ ] Create `CorrespondenceMetricsViewProjection.cs` (MultiStreamProjection)
- [ ] Add projection registration in `Backoffice.Api/Program.cs`
- [ ] Create integration message handlers (if missing)
- [ ] Write integration test verifying projection updates
- [ ] (Optional) Add "Correspondence Health" KPI to dashboard

### Step 4: Build FulfillmentPipelineView Projection
- [ ] Read ADR 0036 for data model sketch
- [ ] Create `FulfillmentPipelineView.cs` document model
- [ ] Create `FulfillmentPipelineViewProjection.cs` (MultiStreamProjection)
- [ ] Add projection registration in `Backoffice.Api/Program.cs`
- [ ] Create integration message handlers (if missing)
- [ ] Write integration test verifying projection updates
- [ ] (Optional) Add fulfillment pipeline KPI to dashboard

### Step 5: Verify All Tests Pass
- [ ] Run `dotnet test tests/Backoffice/Backoffice.Api.IntegrationTests` (should be 75+ tests passing)
- [ ] Verify no regressions in other BCs
- [ ] Build entire solution (`dotnet build`) with 0 errors

### Step 6: Documentation
- [ ] Update session plan with actual decisions made
- [ ] Create `m33-0-session-2-retrospective.md`
- [ ] Update `CURRENT-CYCLE.md` with session 2 progress

---

## Exit Criteria

- ✅ `ReturnMetricsView` projection built and tested
- ✅ `CorrespondenceMetricsView` projection built and tested
- ✅ `FulfillmentPipelineView` projection built and tested
- ✅ `GetDashboardSummary.cs` uses real `ReturnMetricsView` count (no `// STUB`)
- ✅ `OrderPlacedHandler.cs` uses real `ReturnMetricsView` count (no `// STUB`)
- ✅ All Backoffice integration tests passing
- ✅ Build: 0 errors, no new warnings
- ✅ Retrospective documenting projection patterns and decisions

---

## Key Decisions to Make

1. **ReturnMetricsView key strategy:** Single aggregate document vs date-keyed documents?
2. **FulfillmentPipelineView key strategy:** Per-fulfillment vs daily rollup?
3. **Dashboard KPI expansion:** Add Correspondence Health and Fulfillment Pipeline KPIs now or defer to M34?
4. **Event name compatibility:** Use current event names (`ReturnRequested`, `DeliveryFailed`, `InspectionMixed`) and update in Phase 3/4, or anticipate renames now?

**Recommended Approach:** Use current event names from Messages.Contracts (what's in the codebase today), document that Phase 3/4 will update them when refactors ship.

---

## Notes

- **Measure Twice, Cut Once:** Read all integration message contracts before writing projection code
- **Test Thoroughly:** Each projection gets at least one integration test proving event → projection → query pipeline works
- **Follow Existing Patterns:** Match `AdminDailyMetricsProjection` and `AlertFeedViewProjection` structure
- **Commit Often:** One commit per projection (3 total), plus one for dashboard stub removal
