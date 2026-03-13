# Cycle 27: Returns BC Phase 3

**Dates:** 2026-03-13 (Planning)
**Status:** 📋 **PLANNED** — Ready for implementation
**BC:** Returns
**Port:** 5245 (existing)
**Depends On:** Cycle 26 (Returns BC Phase 2) ✅ COMPLETE

---

## Objectives

Deliver the headline Phase 3 feature (exchange workflow) while closing supporting gaps from Phase 2 retrospective priorities.

### What Phase 3 Delivers

- ✅ **UC-11: Exchange workflow** — Customer returns item A, receives item B in exchange (~20-30% of return volume)
- ✅ **CE SignalR handlers** — Real-time push for all 7 return lifecycle events
- ✅ **Sequential returns** — Multiple returns per order before window expires
- ✅ **DeliveredAt endpoint fix** — Populate `DeliveredAt` in `GetReturnableItems` response
- ✅ **$0 refund guard** — Defensive check in Orders saga `ReturnCompleted` handler
- ✅ **Anticorruption layer** — Customer-friendly text for internal enum values
- ✅ **Cross-BC smoke tests** — RabbitMQ publish→consume pipeline verification
- ✅ **Fraud detection patterns** — Documentation for future implementation

### What Phase 3 Does NOT Include

- Exchange workflow v2: Cross-product exchanges (return dog bed → get cat bed)
- Exchange workflow v2: Upcharge payment collection (replacement costs more)
- Multi-item exchanges (v1 supports single-item only)
- Fraud detection active implementation (documentation only)
- Admin Portal CS agent tooling (deferred to Cycle 30+)

---

## Key Deliverables

### 🔴 P0: Exchange Workflow (UC-11)

**Business Context:** Industry data shows 20-30% of returns are exchanges (size/color swaps). This closes the #1 customer friction point from Phase 2.

**v1 Scope Constraints:**
- **Same-SKU only** — Different size/color within same product family
- **Replacement must cost same or less** — No upcharge payment collection
- **Stock check at approval time** — Prevents customer disappointment
- **30-day window** — Same as refunds (no extension)
- **Inspection failure** — Downgrades to no-refund rejection

**Technical Implementation:**

1. **New Aggregate State Extensions:**
   - Add `ReturnType` enum: `Refund` (default), `Exchange`
   - Add `ExchangeRequest` record to `RequestReturn` command
   - Add states: `ExchangeApproved`, `ExchangeShipped`, `ExchangeReceived`, `ExchangeCompleted`, `ExchangeDenied`, `ExchangeExpired`

2. **New Domain Events:**
   - `ExchangeRequested` (extends `ReturnRequested` with replacement SKU/quantity)
   - `ExchangeApproved` (includes replacement order details + price difference)
   - `ExchangeDenied` (reason: ReplacementOutOfStock, ReplacementMoreExpensive, OutsideReturnWindow)
   - `ExchangeInspectionPassed` (original item approved for return)
   - `ExchangeShipmentDispatched` (Fulfillment sends replacement)
   - `ExchangeReceived` (customer confirms receipt)
   - `ExchangeCompleted` (terminal state)
   - `ExchangeRejected` (inspection failed, no replacement)
   - `ExchangeExpired` (customer never shipped original item)

3. **New Integration Messages (Messages.Contracts/Returns):**
   - `ExchangeRequested` → Orders BC (validate replacement items)
   - `ExchangeApproved` → Inventory BC (reserve replacement items)
   - `ExchangeDenied` → Customer Experience BC (display reason + message)
   - `ExchangeCompleted` → Orders BC (credit original, debit replacement + price difference)

4. **API Endpoints:**
   - `POST /api/returns` — Add `exchangeRequest` optional field (ReplacementSku, Quantity)
   - `GET /api/returns/{returnId}` — Include exchange details (ReplacementSku, PriceDifference, ReplacementShipmentStatus)

5. **Price Difference Handling:**

| Scenario | Action | Payment Flow |
|----------|--------|--------------|
| Same price | Proceed | No payment/refund |
| Replacement cheaper | Proceed + refund | Orders BC issues difference refund after inspection passes |
| Replacement more expensive | **Deny exchange** | Customer-facing message: "Replacement costs more. Please request a refund and place a new order." |

6. **Stock Availability Check:**
   - **At approval time:** Returns BC calls Inventory BC HTTP API (`GET /api/inventory/availability?sku={replacementSku}`)
   - **If out of stock:** Exchange auto-denied with message: "Replacement item currently unavailable."

**Estimated Effort:** 2.5 sessions

---

### 🟡 P1: Customer Experience BC SignalR Handlers

**Requirement:** All 7 return lifecycle events must push real-time updates to Storefront UI.

**Implementation:**

1. **New Handler Files (src/Customer Experience/Storefront/Notifications/):**
   - `ReturnRequestedHandler.cs`
   - `ReturnApprovedHandler.cs`
   - `ReturnDeniedHandler.cs`
   - `ReturnReceivedHandler.cs`
   - `ReturnCompletedHandler.cs`
   - `ReturnRejectedHandler.cs`
   - `ReturnExpiredHandler.cs`

2. **StorefrontEvent Extensions:**
   - Add 7 new discriminated union variants for return events
   - Follow existing pattern from `ItemAddedHandler`, `ShipmentDispatchedHandler`

3. **Event Payload Schema:**

| Handler | Integration Message | StorefrontEvent Payload |
|---------|---------------------|-------------------------|
| `ReturnRequestedHandler` | `Returns.ReturnRequested` | `{ returnId, orderId, status: "Requested", requestedAt }` |
| `ReturnApprovedHandler` | `Returns.ReturnApproved` | `{ returnId, orderId, status: "Approved", estimatedRefund, restockingFee, shipByDeadline, trackingNumber, labelUrl }` |
| `ReturnDeniedHandler` | `Returns.ReturnDenied` | `{ returnId, orderId, status: "Denied", reason, message, deniedAt }` |
| `ReturnReceivedHandler` | `Returns.ReturnReceived` | `{ returnId, orderId, status: "Received", receivedAt }` |
| `ReturnCompletedHandler` | `Returns.ReturnCompleted` | `{ returnId, orderId, status: "Completed", finalRefund, items[], completedAt }` |
| `ReturnRejectedHandler` | `Returns.ReturnRejected` | `{ returnId, orderId, status: "Rejected", reason, items[], rejectedAt }` |
| `ReturnExpiredHandler` | `Returns.ReturnExpired` | `{ returnId, orderId, status: "Expired", expiredAt }` |

**Estimated Effort:** 1 session

---

### 🟡 P1: Sequential Returns Support

**Current Limitation:** Orders saga closes on first `ReturnCompleted`. Prevents second return.

**Implementation:**

1. **Orders Saga Changes:**
   - Replace `IsReturnInProgress` (bool) with `ActiveReturnIds` (List<Guid>)
   - `ReturnRequested` → Add `ReturnId` to list
   - `ReturnCompleted`/`ReturnDenied`/`ReturnExpired`/`ReturnRejected` → Remove `ReturnId` from list
   - Saga closes when `ActiveReturnIds.Count == 0 AND WindowExpiresAt < Now`

2. **Returns BC Changes:**
   - `ReturnEligibilityWindow` query must check: "Are all eligible items already returned?"
   - Block return request if no unreturned items remain (409 Conflict)

**Estimated Effort:** 1 session

---

### 🟢 P2: DeliveredAt Endpoint Fix

**Issue:** `GET /api/orders/{orderId}/returnable-items` returns `null` for `DeliveredAt` despite Orders saga persisting it.

**Fix:**
- Update `GetReturnableItems` query handler to include `DeliveredAt` from Order aggregate
- Add integration test verifying `DeliveredAt` populated

**Estimated Effort:** 0.25 sessions

---

### 🟢 P2: $0 Refund Guard

**Issue:** Defensive check needed in Orders saga `ReturnCompleted` handler.

**Fix:**
- Add guard: `if (message.FinalRefundAmount <= Money.Zero) { /* log warning, return */ }`
- Add unit test for $0 refund scenario

**Estimated Effort:** 0.25 sessions

---

### 🟢 P2: SSE→SignalR Doc Amendment

**Issue:** 4 stale "SSE" references in `cycle-26-returns-bc-phase-2.md` (should say "SignalR" per ADR 0013).

**Fix:**
- Find-replace in retrospective document
- No code changes

**Estimated Effort:** 0.1 sessions

---

### 🟢 P2: Anticorruption Layer Design

**Issue:** Internal enum values must be translated to customer-friendly text.

**Implementation:**
- Create `EnumTranslations` static class in `Returns.Api/Translations/`
- Methods:
  - `ToCustomerFacingText(ReturnStatus status, DateTimeOffset? shipByDeadline = null)`
  - `ToCustomerFacingText(ReturnReason reason)`
  - `ToCustomerFacingText(DispositionDecision disposition)`
  - `ToCustomerFacingText(ItemCondition condition)`

**Enum Translation Examples:**

| Internal Value | Customer-Facing Text |
|----------------|----------------------|
| `ReturnStatus.Inspecting` | "Your return is being inspected" |
| `ReturnStatus.Approved` | "Return approved — ship by {date}" |
| `ReturnReason.Defective` | "Item was defective or damaged" |
| `DispositionDecision.Dispose` | "Item cannot be restocked" |
| `ItemCondition.WorseThanExpected` | "Item condition was worse than reported" |

**Estimated Effort:** 0.75 sessions

---

### 🟢 P3: Cross-BC Integration Smoke Tests

**Lesson from Phase 2:** Fulfillment wasn't publishing `ShipmentDelivered` to Returns queue. Integration tests masked this.

**Implementation:**
- Create `tests/Integration/CrossBcIntegrationTests.cs`
- 3-host Alba fixture: Returns.Api + Orders.Api + Fulfillment.Api
- Shared TestContainers (Postgres + RabbitMQ)

**Test Scenarios:**
1. Fulfillment publishes `ShipmentDelivered` → Returns creates `ReturnEligibilityWindow`
2. Returns publishes `ReturnCompleted` → Orders saga receives it
3. Returns publishes `ReturnCompleted` → Inventory BC receives it (queue delivery verified)

**Estimated Effort:** 1 session

---

### 🟢 P3: Fraud Detection Patterns (Documentation Only)

**Scope:** Document patterns, NOT implement detection logic.

**Deliverable:**
- Create `docs/returns/FRAUD-DETECTION-PATTERNS.md`
- Cover:
  - Return frequency monitoring (customer returning >3 orders/month)
  - Risk scoring heuristics (high-value items, item-swap patterns)
  - Integration point: `ReturnRequested` handler could call fraud service
  - Future: Fraud Detection BC publishes `ReturnFlaggedForReview` → Returns BC holds in `Requested` state

**Estimated Effort:** 0.5 sessions

---

## Implementation Sequence

| Order | Deliverable | Dependency | Sessions |
|-------|------------|------------|----------|
| 1 | SSE→SignalR doc fix | None | 0.1 |
| 2 | DeliveredAt endpoint fix | None | 0.25 |
| 3 | $0 refund guard | None | 0.25 |
| 4 | Anticorruption layer | None | 0.75 |
| 5 | CE SignalR handlers | Phase 2 queue wiring | 1 |
| 6 | Sequential returns | Orders saga | 1 |
| 7 | Cross-BC smoke tests | Returns + Orders + Fulfillment | 1 |
| 8 | Fraud patterns doc | Research | 0.5 |
| 9 | **Exchange workflow** | All above (clean slate) | 2.5 |
| | **Total** | | **7.35 sessions** |

---

## Test Coverage

**New Tests:** 46 (bringing total return-related tests to ~145)

| Deliverable | Integration Tests | Unit Tests | Total |
|------------|-------------------|------------|-------|
| Exchange workflow | 15 | 8 | 23 |
| CE SignalR handlers | 7 | 0 | 7 |
| Sequential returns | 5 | 0 | 5 |
| P2 items | 5 | 3 | 8 |
| Cross-BC smoke tests | 3 | 0 | 3 |
| **Total** | **35** | **11** | **46** |

---

## Risks and Mitigations

**R1: Exchange workflow scope creep**
- **Risk:** Exchange adds 6+ new states, 3 integration messages, cross-BC coordination. Could balloon to 5+ sessions.
- **Mitigation:** Strict v1 scope — same-SKU only, no upcharge, stock check at approval.

**R2: Sequential returns saga refactoring breaks existing tests**
- **Risk:** Changing `IsReturnInProgress` → `ActiveReturnIds` touches 12+ Orders saga tests.
- **Mitigation:** Dual-write migration pattern (add new property, keep old, then remove old).

**R3: Cross-BC smoke tests require 3 BCs running simultaneously**
- **Risk:** Test fixture complexity (3 Alba hosts + RabbitMQ TestContainer).
- **Mitigation:** Use existing TestFixture patterns. Document clearly.

---

## Sign-Off Criteria

- ✅ All 9 deliverables implemented
- ✅ Exchange workflow supports single-item same-SKU exchanges with price difference refunds
- ✅ CE SignalR handlers push all 7 return events to Blazor UI
- ✅ Sequential returns work for multi-item orders (2+ returns per order)
- ✅ All tests passing (~145 return-related tests)
- ✅ Cross-BC smoke tests verify RabbitMQ pipelines
- ✅ Fraud detection patterns documented

---

## Multi-Agent Workflow

Phase 3 follows the collaborative workflow:

1. **PSA:** Generated implementation plan with 9 deliverables
2. **UXE:** Reviewed plan, returned 4 revision requests (2 blocking, 2 non-blocking)
3. **PSA:** Addressed all 4 UXE revision requests, created `exchange-workflow.feature`
4. **UXE:** Approved revised plan ✅
5. **QA:** Wrote test plan (41 tests → 46 after PSA review)
6. **PSA:** Reviewed and signed off on test plan ✅
7. **Show and Tell:** PSA + UXE + PO presented findings
8. **PO:** Signed off on business value and success metrics ✅
9. **Retrospective:** All 4 roles contributed lessons learned

---

## Business Metrics for Phase 3

| Metric | Target | Measurement Method |
|--------|--------|-------------------|
| Exchange adoption rate | ≥15% of returns | Track `RequestReturn` with `exchangeRequest != null` |
| Exchange approval rate | ≥50% | `(ExchangeApproved / ExchangeRequested) * 100` |
| Exchange completion rate | ≥70% | `(ExchangeCompleted / ExchangeApproved) * 100` |
| Real-time event latency | <2 sec | SignalR publish→receive timestamp delta |
| Sequential return usage | ≥5% of multi-item orders | Orders with 2+ `ReturnRequested` events |

---

## Phase 4 Priorities (Consensus)

| Priority | Item | Stakeholder |
|----------|------|-------------|
| 🔴 P0 | **Notifications BC** — Transactional emails for all 7 return lifecycle events | PO |
| 🟡 P1 | **Exchange v2** — Cross-product exchanges, upcharge payment collection | UXE |
| 🟡 P1 | **Fraud detection active implementation** — Customer return rate monitoring, risk scoring | PO |
| 🟢 P2 | **Admin Portal returns dashboard** — CS agent tooling for disputed inspections | PO |
| 🟢 P2 | **Exchange UI wireframes** — Detailed mockups for "choose replacement" flow | UXE |

---

*Created: 2026-03-13*
*Phase 2 Retrospective: [cycle-26-returns-bc-phase-2-retrospective.md](cycle-26-returns-bc-phase-2-retrospective.md)*
*Feature File: [../../features/returns/exchange-workflow.feature](../../features/returns/exchange-workflow.feature)*
*Status: Ready for implementation*
