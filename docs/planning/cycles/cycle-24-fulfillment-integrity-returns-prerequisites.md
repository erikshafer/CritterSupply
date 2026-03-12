# Cycle 24: Fulfillment Integrity + Returns Prerequisites

**Planned Start:** 2026-03-12
**Estimated Duration:** 1–2 days (implementation already complete; validation + documentation)
**Status:** ✅ COMPLETE

---

## Objectives

Fix critical Fulfillment BC bugs and add Orders saga prerequisites required before the Returns BC can ship in Cycle 25. This cycle was re-scoped from the originally planned Admin Portal Phase 1 based on the cross-functional priority review ([`priority-review-post-cycle-23.md`](../priority-review-post-cycle-23.md)).

### Why This Cycle Exists

Three independently discovered problems converged to make this work urgent:

1. **Product Owner:** Customers cannot return items — no refund path exists. Returns BC spec is ready; no justification for further deferral.
2. **UX Engineer:** Order Confirmation page showed "Delivery Failed" with no actionable information. The "order confirmation email" promise was a lie (no email system exists).
3. **Principal Architect:** Orders saga's `Handle(ReturnWindowExpired)` would unconditionally close the saga even if a return is in progress — corrupting saga state and making refunds impossible.

All three perspectives agreed: **fix Fulfillment + Orders integrity first, then build Returns.**

---

## Event Modeling Exercise: Fulfillment + Returns Integration

> **Conducted by:** Principal Software Architect, UX Engineer, Product Owner  
> **Date:** 2026-03-12  
> **Method:** Review of existing BDD feature files, CONTEXTS.md specifications, and fulfillment-evolution-plan.md

### Fulfillment → Orders → Returns Event Flow

```
┌─────────────────┐    ┌─────────────────┐    ┌──────────────────┐
│  Fulfillment BC │    │    Orders BC     │    │    Returns BC     │
│                 │    │    (Saga)        │    │    (Cycle 25)     │
└────────┬────────┘    └────────┬────────┘    └────────┬─────────┘
         │                      │                      │
    ShipmentDispatched ──────►  │ Status → Shipped     │
         │                      │                      │
    ShipmentDelivered ───────►  │ Status → Delivered   │
         │                      │ Schedule              │
         │                      │ ReturnWindowExpired   │
         │                      │ (30 days)             │
         │                      │                      │
    ShipmentDeliveryFailed ──►  │ Status stays Shipped  │
         │                      │ (carrier will retry)  │
         │                      │                      │
         │                      │ ◄── ReturnRequested   │
         │                      │ IsReturnInProgress=T  │
         │                      │ ActiveReturnId=X      │
         │                      │                      │
         │                 ReturnWindowExpired fires    │
         │                 Guard: IsReturnInProgress    │
         │                 → Stay open (don't close)    │
         │                      │                      │
         │                      │ ◄── ReturnCompleted   │
         │                      │ RefundRequested ─────►│ Payments BC
         │                      │ MarkCompleted()       │
         │                      │                      │
         │                      │ ◄── ReturnDenied      │
         │                      │ IsReturnInProgress=F  │
         │                      │ If ReturnWindowFired: │
         │                      │   MarkCompleted()     │
         │                      │                      │
```

### Key Domain Events (Blue Stickies)

| Event | Source BC | Consumer BCs | Purpose |
|-------|-----------|--------------|---------|
| `ShipmentDispatched` | Fulfillment | Orders, Storefront | Carrier took possession |
| `ShipmentDelivered` | Fulfillment | Orders, Storefront, Returns | Delivery confirmed; starts 30-day window |
| `ShipmentDeliveryFailed` | Fulfillment | Orders, Storefront | Delivery unsuccessful; carrier retries |
| `ReturnRequested` | Returns | Orders | Customer initiated return |
| `ReturnCompleted` | Returns | Orders, Inventory | Return processed; refund due |
| `ReturnDenied` | Returns | Orders | Return rejected; reason provided |
| `ReturnWindowExpired` | Orders (scheduled) | Orders (self) | 30-day window closed |
| `RefundRequested` | Orders | Payments | Refund customer for return |

### Commands (Orange Stickies)

| Command | Target BC | Trigger | Validation |
|---------|-----------|---------|------------|
| `RecordDeliveryFailure` | Fulfillment | Carrier notification | Shipment must be in Shipped status |
| `GetReturnableItems` | Orders (query) | Returns BC FR-01 | Order must be Delivered or Closed |

### Read Models (Green Stickies)

| View | BC | Data Source | Purpose |
|------|------|------------|---------|
| `ReturnableItemsResponse` | Orders | Order saga state | Snapshot of line items eligible for return |
| SSE `shipment-delivery-failed` | Storefront | Fulfillment event | Real-time customer notification |

### Invariants (Red Stickies)

1. **Saga cannot close while return in progress** — `Handle(ReturnWindowExpired)` guarded by `IsReturnInProgress`
2. **Idempotent shipment creation** — UUID v5 from OrderId prevents duplicate shipments
3. **Return window is exactly 30 days** — `OrderDecider.ReturnWindowDuration = TimeSpan.FromDays(30)`
4. **Delivery failure doesn't change status** — Order stays in Shipped; carrier will retry
5. **RefundRequested only from ReturnCompleted** — Not from ReturnDenied or ReturnExpired

### Risks Identified

| Risk | Severity | Mitigation |
|------|----------|------------|
| Marten event stream migration for `ShippingAddress` rename | Medium | Phase A uses `[JsonPropertyName]` dual annotations; no stream migration needed |
| Scheduled `ReturnWindowExpired` cannot be cancelled | Low | Guard in handler; `IsReturnInProgress` flag prevents premature closure |
| `RecordDeliveryFailure` endpoint has no carrier webhook integration | Low | Manual trigger for now; carrier integration planned for Fulfillment Phase 2 |

---

## Success Criteria

- [x] Fulfillment.Api has RabbitMQ transport configured (not just local queues)
- [x] `ShipmentDeliveryFailed` integration message published to Orders + Storefront queues
- [x] `RecordDeliveryFailure` endpoint exists with proper validation
- [x] `OrderConfirmation.razor` handles `shipment-delivery-failed` SSE case
- [x] `Shipment.Create()` uses `Guid.Empty` (Marten-supplied ID, not `Guid.CreateVersion7()`)
- [x] No dead `Picking`/`Packing` states in `ShipmentStatus` enum
- [x] `FulfillmentRequestedHandler` uses UUID v5 from OrderId for idempotent creation
- [x] `SharedShippingAddress` exists with dual `[JsonPropertyName]` annotations
- [x] Order saga has `IsReturnInProgress`, `ActiveReturnId`, `ReturnWindowFired` properties
- [x] `Handle(ReturnWindowExpired)` guarded by `!IsReturnInProgress`
- [x] `Handle(ReturnRequested)` sets `IsReturnInProgress = true`
- [x] `Handle(ReturnCompleted)` publishes `RefundRequested` then `MarkCompleted()`
- [x] `Handle(ReturnDenied)` resets flags and conditionally closes saga
- [x] `GET /api/orders/{orderId}/returnable-items` endpoint exists
- [x] All existing tests pass (build clean, 0 warnings)

---

## Deliverables

### 🔴 P0 — Fulfillment Bug Fix Sweep

- [x] **RabbitMQ transport wired** in `Fulfillment.Api/Program.cs`
  - Publishes `ShipmentDispatched`, `ShipmentDelivered`, `ShipmentDeliveryFailed` to `orders-fulfillment-events` and `storefront-fulfillment-events` queues
  - Listens on `fulfillment-requests` queue for `FulfillmentRequested` from Orders BC
  - File: `src/Fulfillment/Fulfillment.Api/Program.cs` (lines 79–108)

- [x] **`RecordDeliveryFailure` endpoint** — `POST /api/fulfillment/shipments/{shipmentId}/record-delivery-failure`
  - Validates: shipment exists, status is Shipped, reason ≤ 500 chars
  - Publishes `ShipmentDeliveryFailed` integration message to RabbitMQ
  - File: `src/Fulfillment/Fulfillment/Shipments/RecordDeliveryFailure.cs`

- [x] **`shipment-delivery-failed` SSE case** in `OrderConfirmation.razor`
  - Status: "Delivery Failed" with error color
  - Copy: "Your delivery was unsuccessful — our team has been notified."
  - File: `src/Customer Experience/Storefront.Web/Components/Pages/OrderConfirmation.razor`

- [x] **`Shipment.Create()` uses Marten-supplied ID** — `Guid.Empty` instead of `Guid.CreateVersion7()`
  - File: `src/Fulfillment/Fulfillment/Shipments/Shipment.cs` (line 29)

- [x] **No dead states** — `ShipmentStatus` enum has only: Pending, Assigned, Shipped, Delivered, DeliveryFailed
  - File: `src/Fulfillment/Fulfillment/Shipments/ShipmentStatus.cs`

- [x] **UUID v5 idempotent handler** — `FulfillmentRequestedHandler` creates deterministic ID from OrderId
  - Idempotency guard: checks `FetchStreamStateAsync()` before `StartStream()`
  - File: `src/Fulfillment/Fulfillment/Shipments/FulfillmentRequestedHandler.cs`

### 🟡 P0.5 — ShippingAddress Consolidation Phase A

- [x] **`SharedShippingAddress`** — `Messages.Contracts.Common.SharedShippingAddress`
  - Primary fields: `AddressLine1`, `AddressLine2`, `City`, `StateProvince`, `PostalCode`, `Country`
  - Backward-compatible aliases: `Street` → `AddressLine1`, `State` → `StateProvince` (marked `[Obsolete]`)
  - Dual `[JsonPropertyName]` annotations for both naming conventions
  - File: `src/Shared/Messages.Contracts/Common/SharedShippingAddress.cs`

### 🔴 P0.5 — Orders Saga Prerequisites for Returns BC

- [x] **`IsReturnInProgress`** property on `Order` saga — prevents premature closure
  - File: `src/Orders/Orders/Placement/Order.cs` (line 118)

- [x] **`ActiveReturnId`** property on `Order` saga — tracks current return
  - File: `src/Orders/Orders/Placement/Order.cs` (line 123)

- [x] **`ReturnWindowFired`** property — idempotency guard for scheduled expiry
  - File: `src/Orders/Orders/Placement/Order.cs` (line 129)

- [x] **`Handle(ReturnWindowExpired)`** guarded — sets `ReturnWindowFired = true`, returns early if `IsReturnInProgress`
  - File: `src/Orders/Orders/Placement/Order.cs` (lines 392–398)

- [x] **`Handle(ReturnRequested)`** — sets `IsReturnInProgress = true`, tracks `ActiveReturnId`
  - File: `src/Orders/Orders/Placement/Order.cs` (lines 404–408)

- [x] **`Handle(ReturnCompleted)`** — publishes `RefundRequested`, then `MarkCompleted()`
  - File: `src/Orders/Orders/Placement/Order.cs` (lines 414–426)

- [x] **`Handle(ReturnDenied)`** — resets flags; closes saga if window already fired
  - File: `src/Orders/Orders/Placement/Order.cs` (lines 432–441)

- [x] **`GET /api/orders/{orderId}/returnable-items`** endpoint
  - Returns: `ReturnableItemsResponse` with `OrderId`, `Items` (Sku, Quantity, UnitPrice, LineTotal), `DeliveredAt`
  - File: `src/Orders/Orders.Api/Orders/GetReturnableItems.cs`

- [x] **Integration message contracts** for Returns BC
  - `ReturnRequested(ReturnId, OrderId, CustomerId, RequestedAt)`
  - `ReturnCompleted(ReturnId, OrderId, FinalRefundAmount, CompletedAt)`
  - `ReturnDenied(ReturnId, OrderId, Reason, DeniedAt)`
  - Files: `src/Shared/Messages.Contracts/Returns/`

- [x] **RabbitMQ routing** — Orders.Api listens on `orders-returns-events` queue
  - File: `src/Orders/Orders.Api/Program.cs`

---

## Architectural Decisions

### AD1 — Guard ReturnWindowExpired Instead of Cancelling Scheduled Message
**Context:** Wolverine does not support cancelling scheduled messages by logical ID. The `ReturnWindowExpired` message is in flight 30 days before it fires.
**Decision:** Use a boolean guard (`IsReturnInProgress`) in the handler rather than attempting to cancel the message.
**Rationale:** Simpler, more reliable, no coupling to Wolverine internals. The guard is a single-line check.
**Trade-off:** The scheduled message still fires even if a return completed days ago — but the handler is a no-op.

### AD2 — SharedShippingAddress with Dual JSON Annotations
**Context:** Orders BC uses `Street`/`State` naming; Fulfillment BC uses `AddressLine1`/`StateProvince`.
**Decision:** Introduce `SharedShippingAddress` with Fulfillment naming as primary, Orders naming as `[Obsolete]` aliases.
**Rationale:** Non-breaking Phase A migration. Both BCs can read/write the same JSON. Phase B will remove obsolete aliases.
**Trade-off:** Temporary duplication of property accessors until Phase B cleanup.

### AD3 — UUID v5 for Idempotent Shipment Creation
**Context:** At-least-once delivery means `FulfillmentRequested` may arrive multiple times for the same OrderId.
**Decision:** Generate deterministic UUID v5 from OrderId for the shipment stream key. Check stream existence before starting.
**Rationale:** Same OrderId always maps to same ShipmentId. Second delivery is a no-op. No external state needed.
**Reference:** ADR 0016 (UUID v5 for Natural Key Stream IDs)

---

## Metrics

| Category | Value |
|----------|-------|
| Build Status | ✅ Clean (0 warnings, 0 errors) |
| Total Tests | 895 ([Fact] + [Theory]) |
| Fulfillment Tests | 38 (14 unit + 24 integration) |
| Orders Tests | ~45 (unit + integration) |
| Duration | Implementation pre-completed; documentation + validation in Cycle 24 |

---

## Sign-offs

- [x] **Product Owner** — ✅ APPROVED (2026-03-12). Business requirements met. Observations: add `DeliveredAt` persistence to saga, expand `ReturnCompleted` contract for Inventory item dispositions, add exchange workflow placeholder and mixed-inspection scenario to feature files, write CS agent runbook (Option A) before Returns ships.
- [x] **UX Engineer** — ✅ APPROVED (2026-03-12). Conditionally approved with 5 forward-looking observations for Cycle 25: alert severity modulation for delivery failures, `DeliveredAt` needed for return window countdown, restocking fee disclosure must be at initiation (not approval), auto-approval trust signal for defective/wrong-item returns, and CS agent gap decision needed.
- [x] **Principal Software Architect** — ✅ APPROVED (2026-03-12). All architectural integrity checks pass. Event modeling exercise conducted. Implementation verified against CONTEXTS.md specifications.

---

## Stakeholder Observations for Cycle 25 Planning

### From Product Owner
1. **`DeliveredAt` persistence** — Order saga should persist `DeliveredAt` timestamp when handling `ShipmentDelivered` (one line of code; needed for 30-day window calculation)
2. **`ReturnCompleted` contract expansion** — Inventory BC needs per-item disposition data (`IsRestockable`, `RestockCondition`). Either expand `ReturnCompleted` or publish separate `ReturnItemsDispositioned` event
3. **Exchange workflow placeholder** — Add deferred feature file for exchanges (20-30% of return volume in real e-commerce)
4. **Mixed inspection results scenario** — Add scenario where some items pass and some fail within same return
5. **CS Agent runbook (Option A)** — Write documented runbook for API-based manual return approvals before Returns ships
6. **RBAC ADR timing** — Consider authoring during Cycle 26 (Notifications) instead of Cycle 27 (Promotions)

### From UX Engineer
1. **Alert severity modulation** — `OrderConfirmation.razor` uses `Severity.Info` for delivery failure; should be `Severity.Warning` or `Severity.Error` in Cycle 25
2. **`DeliveredAt` for return window countdown** — Customers need "Return window closes in X days" display (saga option preferred over BFF composition)
3. **Restocking fee disclosure timing** — Fee must appear on return request form *before* submission, not after approval
4. **Auto-approval trust signal** — Defective/WrongItem returns should show immediate approval + prepaid label, not "under review"
5. **Post-rejection journey** — Missing scenario for customer experience after inspection rejection (store credit UX)
6. **CS agent gap** — Decision made: Option A (API + runbook), must be written before Returns ships

---

## Next Steps

- **Cycle 25:** Returns BC Phase 1 — Self-Service Returns + Order History page
  - Feature specs ready: `docs/features/returns/` (4 feature files)
  - Integration contracts ready: `Messages.Contracts.Returns/` (3 messages)
  - Orders saga handlers ready: `ReturnRequested`, `ReturnCompleted`, `ReturnDenied`
  - Prerequisite endpoint ready: `GET /api/orders/{orderId}/returnable-items`
  - **Pre-implementation tasks from stakeholder observations:**
    - Add `DeliveredAt` to Order saga's `Handle(ShipmentDelivered)`
    - Add exchange workflow placeholder feature file
    - Add mixed-inspection-results scenario to `return-inspection.feature`
    - Write CS agent runbook for manual return approvals
    - Decide on `ReturnCompleted` contract expansion for Inventory BC

---

*Created: 2026-03-12*
*Priority Review: [priority-review-post-cycle-23.md](../priority-review-post-cycle-23.md)*
*Fulfillment Evolution: [fulfillment-evolution-plan.md](../fulfillment-evolution-plan.md)*
