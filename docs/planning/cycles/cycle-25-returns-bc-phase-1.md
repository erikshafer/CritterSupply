# Cycle 25: Returns BC Phase 1 — Plan & Retrospective

**Status:** ✅ **COMPLETE**  
**Date:** 2026-03-12  
**Duration:** 1 session  
**BC:** Returns  
**Port:** 5245  

---

## Phase 1 Scope

Returns BC Phase 1 delivers the core infrastructure, event-sourced domain model, and API endpoints for the reverse logistics flow — from return request submission through warehouse inspection and completion.

### What Was Delivered

#### Domain Project (`src/Returns/Returns/`)
- **Return aggregate** (event-sourced, immutable record): Full lifecycle — Requested → Approved/Denied → Received → Inspecting → Completed/Rejected/Expired
- **9 domain events**: ReturnRequested, ReturnApproved, ReturnDenied, ReturnReceived, InspectionStarted, InspectionPassed, InspectionFailed, ReturnExpired
- **6 command handlers**: RequestReturn (auto-approve), ApproveReturn, DenyReturn, ReceiveReturn, SubmitInspection, ExpireReturn
- **ReturnEligibilityWindow** Marten document (populated from Fulfillment.ShipmentDelivered)
- **Restocking fee calculation**: 15% for Unwanted/Other; 0% for Defective/WrongItem/DamagedInTransit
- **Auto-approval logic**: Defective/WrongItem/DamagedInTransit/Unwanted auto-approve; "Other" requires CS review
- **FluentValidation validators** for RequestReturn, SubmitInspection, DenyReturn
- **Query endpoints**: GET /api/returns/{returnId}, GET /api/returns

#### API Project (`src/Returns/Returns.Api/`)
- Wolverine + Marten + RabbitMQ configuration (port 5245, schema `returns`)
- Listens to `returns-fulfillment-events` queue (ShipmentDelivered from Fulfillment BC)
- Publishes to `orders-returns-events` queue (ReturnRequested, ReturnCompleted, ReturnDenied)
- Swagger/OpenAPI documentation
- Dockerfile for containerized deployment
- Aspire service defaults (OpenTelemetry, health checks)

#### Test Coverage
- **48 unit tests** (all passing): Aggregate state transitions, lifecycle paths, restocking fee calculations, response mapping
- **5 integration tests** (build verified): Request creation, eligibility validation, fee calculation, query endpoints

#### Infrastructure
- Solution file (CritterSupply.slnx) updated
- Docker Compose entry (port 5245, profile `returns`)
- PostgreSQL database `returns` in create-databases.sh

### Use Cases Covered (Phase 1)

| Use Case | Status | Notes |
|----------|--------|-------|
| UC-01: Defective Return (Happy Path) | ✅ Full | Auto-approved, 0% fee, full lifecycle |
| UC-02: Unwanted Item (Restocking Fee) | ✅ Full | Auto-approved, 15% fee disclosed at request time |
| UC-03: Denied — Outside Window | ✅ Full | Auto-denied via eligibility check |
| UC-04: Partial Return | ✅ Full | Multi-item support in aggregate from day one |
| UC-05: Item Fails Inspection | ✅ Partial | Rejected terminal state; no store credit (Phase 2) |
| UC-06: Wrong Item Returned | ✅ Full | Inspection disposition handling |
| UC-07: Customer Never Ships (Expiration) | ✅ Full | Wolverine scheduled message |
| UC-10: Cancelled Order Return | ✅ Implicit | No eligibility window → denied |

### Phase 1 State Machine (Simplified)

```
Requested → Approved → Received → Inspecting → Completed (terminal)
    │            │                                    │
    └→ Denied    └→ Expired                    Rejected (terminal)
      (terminal)   (terminal)
```

Phase 1 skips `LabelGenerated` and `InTransit` states (carrier integration deferred to Phase 2).

---

## Event Modeling Exercise

Conducted with Product Owner and UX Engineer before implementation.

### Product Owner Sign-Off ✅
- Phase 1 scope approved: 7 of 12 use cases
- `ReturnCompleted` contract expansion identified as P0 (tracked for Phase 2)
- CS agent runbook acknowledged as P1 (API + runbook approach)
- Mixed inspection results deferred to Phase 2

### UX Engineer Sign-Off ✅
- RequestReturn response contract approved (includes per-item fee breakdown)
- Auto-approval matrix confirmed: Defective/WrongItem/DamagedInTransit/Unwanted auto-approve
- Restocking fee disclosure at request time confirmed as non-negotiable
- Phase 1 API-first approach accepted with known gap (no Order History UI)
- Customer-facing state labels documented for future UI

### QA Engineer Sign-Off ✅
- 48 unit tests covering state machine, lifecycle, calculations, response mapping
- Integration test infrastructure in place (TestContainers + Alba)
- Gap analysis completed; remaining gaps documented for Phase 2

---

## Principal Architect Assessment

### What Went Well
- Event-sourced aggregate follows established Critter Stack patterns (Fulfillment, Pricing)
- Immutable records with `Apply()` methods and `Create()` factory
- FluentValidation for command validation
- Proper Marten schema isolation (`returns` schema)
- RabbitMQ integration follows existing queue naming conventions
- Auto-approval logic keeps the common path fast and friction-free
- Restocking fee calculation is pure and testable

### Architecture Alignment
- ✅ Bounded context boundaries respected (Returns doesn't own refund processing)
- ✅ Integration messages defined in Messages.Contracts (existing contracts reused)
- ✅ Choreography pattern used correctly (publishes events, doesn't call downstream)
- ✅ ReturnEligibilityWindow enables autonomous operation (no runtime dependency on Orders)

---

## Risks, Dependencies, and Considerations for Phase 2+

### 🔴 P0 — Must Address Before Phase 2

1. **`ReturnCompleted` contract expansion**: Current contract lacks per-item disposition data (SKU, quantity, IsRestockable, RestockCondition, WarehouseId). Inventory BC cannot process restocking without this. Expand before Phase 2 work begins.

2. **`DeliveredAt` persistence in Order saga**: Verify that the Order saga persists `DeliveredAt` from `ShipmentDelivered`. Returns BC needs this for the 30-day window calculation. The BFF needs it for "return by {date}" display.

### 🟡 P1 — Address During Phase 2

3. **ReturnRejected integration event**: Phase 1 handles rejection internally (terminal state). Phase 2 needs `ReturnRejected` published for Customer Experience BC to show rejection details.

4. **ReturnApproved / ReturnExpired integration events**: Customer Experience BC needs these for real-time status updates via SSE.

5. **Mixed inspection results**: Phase 1 handles all-pass or all-fail. Phase 2 should support mixed results (some items pass, some fail within the same return).

6. **CS agent runbook**: Write minimal operational documentation for approve/deny endpoints.

7. **Carrier integration ADR**: Resolve Open Question #1 (who owns label generation) before implementing `LabelGenerated` and `InTransit` states.

### 🟢 P2+ — Future Phases

8. **Exchange workflow**: 20–30% of real return volume. Architecturally complex (new order creation).
9. **RBAC for CS agent endpoints**: Requires RBAC ADR (Cycle 26–27).
10. **Fraud detection**: Pattern recognition, risk scoring. Phase 3+.
11. **Post-rejection journey**: Store credit / goodwill offers for rejected returns.
12. **Order History UI**: Storefront.Web page with "Request Return" button.

---

## Metrics

| Metric | Value |
|--------|-------|
| Files created | 24 (src) + 8 (tests) |
| Unit tests | 48 (all passing) |
| Integration tests | 5 (build verified) |
| Domain events | 8 |
| API endpoints | 7 |
| Lifecycle states | 10 (8 active in Phase 1, 2 reserved for Phase 2) |
| Port | 5245 |
| Database | `returns` (PostgreSQL) |
| RabbitMQ queues | 1 inbound, 1 outbound |

---

## Sign-Offs

- ✅ **Product Owner**: Phase 1 scope approved, 7/12 use cases covered, risks documented
- ✅ **UX Engineer**: API response contracts approved, auto-approval matrix confirmed, customer state labels documented
- ✅ **QA Engineer**: 48 unit tests, integration test infrastructure, gap analysis complete
- ✅ **Principal Software Architect**: Architecture aligned, patterns followed, risks documented for Phase 2

---

*Last Updated: 2026-03-12*
