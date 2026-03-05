# Returns BC — Domain Specification

**Bounded Context:** Returns  
**Status:** 🚧 Planned (Not Yet Implemented)  
**Priority:** Low-to-Medium (Cycle 21+)  
**Estimated Effort:** 3–5 sessions  
**Document Owner:** Product Owner (CritterSupply)  
**Last Reviewed:** 2026-03-05  
**Principal Architect Sign-Off:** ✅ Incorporated (see [Architect Review Notes](#architect-review-notes))

---

## Table of Contents

1. [Overview](#overview)
2. [What Initiates a Return](#what-initiates-a-return)
3. [Functional Requirements](#functional-requirements)
4. [Non-Functional Requirements](#non-functional-requirements)
5. [Business Rules and Core Invariants](#business-rules-and-core-invariants)
6. [Use Cases](#use-cases)
7. [Adjacent Bounded Contexts](#adjacent-bounded-contexts)
8. [Domain Events](#domain-events)
9. [Integration Events](#integration-events)
10. [State Transition Model](#state-transition-model)
11. [Risks](#risks)
12. [Architect Review Notes](#architect-review-notes)
13. [Implementation Guidance](#implementation-guidance)
14. [Testing Strategy](#testing-strategy)
15. [Open Questions and Future Considerations](#open-questions-and-future-considerations)

---

## Overview

The Returns BC manages the complete reverse logistics flow for CritterSupply. It handles customer return requests from initial submission through warehouse inspection, and publishes the facts that allow downstream bounded contexts (Orders, Inventory) to orchestrate refunds and restocking.

Returns BC **does not** own refund processing or inventory restocking decisions. It publishes domain facts (`ReturnCompleted` with disposition details) and downstream BCs react:

- **Orders** orchestrates the refund via Payments BC (it holds the `PaymentId`).
- **Inventory** reacts to `ReturnCompleted` and updates available stock.

This separation is intentional. Returns knows reverse logistics — the physical movement and condition assessment of goods. Nothing more.

### Scope

| In Scope | Out of Scope |
|----------|--------------|
| Return request submission and eligibility validation | Refund calculation (deferred to Orders) |
| Return authorization (approval/denial) | Payment processing and refund mechanics (Payments BC) |
| Return label generation | Inventory restocking execution (Inventory BC) |
| Carrier tracking for inbound return shipments | Customer-facing notifications (Notifications BC) |
| Warehouse receipt and inspection workflow | Store credit ledger management (future BC) |
| Return disposition (restockable, dispose, quarantine) | Original order state management (Orders saga) |

---

## What Initiates a Return

A return can be initiated through the following triggers:

### Primary Trigger — Customer Self-Service

**Customer submits a return request** via the storefront UI (Customer Experience BC) after an order has been delivered.

- **Precondition:** `Fulfillment.ShipmentDelivered` has been received by Returns BC, establishing the 30-day eligibility window.
- **Entry point:** `POST /api/returns` → `RequestReturn` command → Returns aggregate stream created.
- **Command data:** `OrderId`, `CustomerId`, `LineItems` (which items to return + quantities), `ReturnReason` per item.

### Secondary Trigger — Customer Service Override

**A customer service agent initiates a return** on behalf of a customer (e.g., customer called in, unable to use self-service portal).

- Same `RequestReturn` command, with `InitiatedBy` indicating the agent ID instead of the customer.
- Return window enforcement can be overridden with manager-level authorization (future state: `Escalated` flow).

### What Must Exist Before a Return Can Be Initiated

1. The order must have been delivered (`Fulfillment.ShipmentDelivered` received).
2. The delivery date must be within the 30-day return window.
3. At least one line item must be eligible for return (not a non-returnable category).
4. The order must exist in Returns BC's `ReturnEligibilityWindow` read model.

---

## Functional Requirements

### FR-01: Return Eligibility Window Management

- Returns BC **must** subscribe to `Fulfillment.ShipmentDelivered` to establish the return eligibility window.
- Upon receiving `ShipmentDelivered`, Returns BC **must** project a `ReturnEligibilityWindow` read model keyed by `OrderId`.
- The eligibility window **must** be 30 calendar days from the delivery date (configurable via system settings).
- Returns BC **must** schedule a `ReturnWindowExpiry` timeout at window close time using Wolverine's `ScheduleMessage`.
- Returns BC **must** query Orders BC HTTP API once at delivery time to snapshot eligible line items into the `ReturnEligibilityWindow`.

### FR-02: Return Request Submission

- Customers **must** be able to request returns for one or more line items from an order in a single submission.
- Each line item in a return request **must** include a `ReturnReason` (Defective, WrongItem, Unwanted, DamagedInTransit, Other).
- Customers **may** include a free-text explanation per item.
- Partial returns (returning some but not all items from an order) **must** be supported.
- Multiple return requests against the same order **must** be supported (for items not included in a prior return).

### FR-03: Return Authorization

- The system **must** automatically deny return requests that violate eligibility rules (outside window, non-returnable item).
- Returns **should** support auto-approval for straightforward cases (defective items, wrong items shipped).
- Returns **must** support manual approval by customer service agents.
- Upon approval, a return shipping label **must** be generated. Merchant-paid labels apply when the defect is CritterSupply's fault (wrong item shipped, defective item).

### FR-04: Return Shipment Tracking

- Returns BC **must** record when a return label is generated (`ReturnLabelGenerated`).
- Returns BC **must** receive inbound carrier tracking events from Fulfillment BC (`Fulfillment.ReturnShipmentInTransit`).
- Returns BC **must** record when the return package is received at the warehouse (`ReturnShipmentReceived`).
- Approved returns that are never shipped **must** be expired after 30 days using a Wolverine-scheduled command.

### FR-05: Warehouse Inspection

- Returns BC **must** track inspection start and completion.
- Inspectors **must** record per-line-item condition assessment: `AsExpected`, `BetterThanExpected`, `WorseThanExpected`, `NotReceived`.
- Each line item **must** have a disposition decision: `Restockable`, `Dispose`, `Quarantine`.
- `InspectionPassed` **must** record which items are restockable and their warehouse location.
- `InspectionFailed` **must** record the failure reason and a `DispositionDecision` (ReturnToCustomer, Dispose, Quarantine).

### FR-06: Return Completion and Downstream Notification

- Upon `InspectionPassed`, Returns BC **must** publish `Returns.ReturnCompleted` with full line item details (SKU, quantity, restockable flag, warehouse location).
- Orders BC handles `ReturnCompleted` and initiates the refund via Payments BC.
- Inventory BC handles `ReturnCompleted` and restocks eligible items.
- Returns BC **must not** directly communicate with Payments BC or Inventory BC.

### FR-07: Return Expiration

- Approved returns not shipped within 30 days **must** automatically transition to `Expired`.
- Expiration **must** be implemented via Wolverine's `ScheduleMessage` (not polling).
- Customers **must** be notified of expiration (via Notifications BC reacting to `Returns.ReturnExpired` integration event).

### FR-08: Restocking Fee Calculation

- Restocking fees (15% for non-defective returns) **must** be calculated and communicated at approval time as part of `ReturnApproved`.
- Final refund amount (after restocking fee deduction) **must** be included in `Returns.ReturnCompleted` so Orders can issue the correct refund.

### FR-09: API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/returns` | Submit a return request |
| `GET` | `/api/returns/{returnId}` | Get return status |
| `GET` | `/api/returns?orderId={orderId}` | Get returns for an order |
| `POST` | `/api/returns/{returnId}/approve` | Approve a return (CS agent) |
| `POST` | `/api/returns/{returnId}/deny` | Deny a return (CS agent) |
| `POST` | `/api/returns/{returnId}/receive` | Record warehouse receipt |
| `POST` | `/api/returns/{returnId}/inspection` | Submit inspection results |

---

## Non-Functional Requirements

### NFR-01: Return Processing Latency

- Return request submission **must** respond in < 500ms (p99).
- Return eligibility check **must** be served from local read model (no synchronous cross-BC HTTP calls at request time).

### NFR-02: Availability

- Returns BC **must** remain operational even if Orders BC is temporarily unavailable.
- The `ReturnEligibilityWindow` read model enables autonomous operation after initial data capture.

### NFR-03: Idempotency

- All command handlers **must** be idempotent. Re-delivering `Fulfillment.ShipmentDelivered` **must not** create duplicate `ReturnEligibilityWindow` records.
- Return request submission **must** reject duplicate requests for the same order + line item combination.

### NFR-04: Auditability

- All state transitions **must** be persisted as immutable domain events in a Marten event stream.
- The full return lifecycle (from request to disposition) **must** be queryable from the event stream.

### NFR-05: Persistence

- **Event store:** Marten (PostgreSQL), schema `returns`.
- **Read models:** Marten document store for `ReturnEligibilityWindow` and `ReturnSummary` projections.
- **Messaging:** RabbitMQ via Wolverine for integration messages.

---

## Business Rules and Core Invariants

These invariants are enforced in the domain layer and cannot be bypassed via the API:

| # | Invariant | Consequence of Violation |
|---|-----------|--------------------------|
| INV-01 | A return cannot be approved outside the 30-day eligibility window | Auto-denied with `OutsideReturnWindow` reason |
| INV-02 | A return cannot be approved for non-returnable items (personalized, opened consumables, final sale) | Auto-denied with `NonReturnableItem` reason |
| INV-03 | A return cannot transition to `Received` without a prior `ReturnApproved` event | Command rejected |
| INV-04 | A return cannot be marked `Completed` without physical receipt and passed inspection | Command rejected |
| INV-05 | A return cannot be processed for an order that was never delivered | `ReturnEligibilityWindow` record does not exist; request denied |
| INV-06 | The refund amount in `ReturnCompleted` cannot exceed the original purchase price of returned items | Validation error at inspection completion |
| INV-07 | A restocking fee (≤ 15%) may only be applied to non-defective returns | Enforced in approval calculation |
| INV-08 | An expired return cannot be reactivated — a new request must be submitted | `ExpireReturn` command is terminal |
| INV-09 | A denied return cannot be re-approved — a new request must be submitted | `ReturnDenied` is a terminal state |
| INV-10 | Only items included in the original `ReturnRequested` event can appear in inspection results | Validation at inspection submission |

---

## Use Cases

### Common Use Cases

#### UC-01: Customer Returns a Defective Item (Happy Path)

**Actor:** Customer  
**Frequency:** High  
**Trigger:** Customer finds purchased item is defective upon opening

1. Customer navigates to order history, selects item, submits return request with reason `Defective`.
2. System validates eligibility (within 30-day window, item is returnable).
3. Return auto-approved; merchant-paid return label generated.
4. Customer ships item; carrier scan received.
5. Warehouse receives package; inspector evaluates condition.
6. Inspection passes (`InspectionPassed`): item condition matches reported defect.
7. Returns BC publishes `ReturnCompleted` with item marked restockable: false (defective).
8. Orders BC receives `ReturnCompleted`, issues `RefundRequested` to Payments BC for full purchase price.
9. Inventory BC receives `ReturnCompleted`, disposes of defective item (no restock).

**Expected outcome:** Customer receives full refund (no restocking fee, no return shipping charge).

---

#### UC-02: Customer Returns an Unwanted Item (Restocking Fee Applied)

**Actor:** Customer  
**Frequency:** High  
**Trigger:** Customer decides they no longer want the item (changed mind)

1. Customer submits return request with reason `Unwanted`.
2. Return auto-approved with 15% restocking fee noted in `ReturnApproved`.
3. Customer pays return shipping; customer-paid label generated.
4. Warehouse receives and inspects item; condition is `AsExpected`.
5. `InspectionPassed`; item is restockable.
6. `ReturnCompleted` published with `FinalRefundAmount` = purchase price − 15% restocking fee.
7. Orders BC initiates refund for partial amount.
8. Inventory BC restocks the item.

**Expected outcome:** Customer receives partial refund; item returns to available inventory.

---

#### UC-03: Return Request Denied — Outside Return Window

**Actor:** Customer  
**Frequency:** Medium  
**Trigger:** Customer attempts return after 30-day window has closed

1. Customer submits return request.
2. System checks `ReturnEligibilityWindow` read model: `WindowExpiresAt` has passed.
3. Return auto-denied with `OutsideReturnWindow` reason.
4. `ReturnDenied` integration event published; Notifications BC sends denial email.

**Expected outcome:** No return processed; customer directed to contact customer service for exceptions.

---

#### UC-04: Partial Return (Multiple Items, Some Returned)

**Actor:** Customer  
**Frequency:** Medium  
**Trigger:** Customer wants to return some (not all) items from a multi-item order

1. Customer selects specific line items to return; excludes others.
2. System validates eligibility for selected items only.
3. Restocking fee applied per-item based on reason (defective items fee-free, unwanted items 15% fee).
4. Return approved for selected items; excluded items remain with customer.
5. Inspection covers only returned items; disposition set per item.
6. `ReturnCompleted` includes only the returned items with their disposition.
7. Refund covers only returned items; kept items remain purchased.

**Expected outcome:** Partial refund; order history shows partial return.

---

### Uncommon Use Cases

#### UC-05: Item Fails Inspection (Customer-Caused Damage)

**Actor:** Warehouse Inspector  
**Frequency:** Low  
**Trigger:** Returned item is more damaged than expected (beyond reported defect)

1. Inspector records item condition as `WorseThanExpected` with condition notes (e.g., "screen cracked, water damage").
2. `InspectionFailed` event recorded with `Disposition: Dispose`.
3. Returns BC publishes `Returns.ReturnCompleted` with `IsRestockable: false`.
4. Orders BC receives `ReturnCompleted` — decides refund outcome per policy (no refund, or goodwill partial store credit as future enhancement).
5. Item is disposed; customer is notified via Notifications BC.

**Expected outcome:** Refund denied or reduced at Orders BC discretion; customer may dispute through customer service.

---

#### UC-06: Wrong Item Returned (Not What Was Ordered)

**Actor:** Warehouse Inspector  
**Frequency:** Low  
**Trigger:** Customer sends back a different item than what was purchased

1. Inspector records item as `NotReceived` for the expected SKU.
2. `InspectionFailed` with `Disposition: ReturnToCustomer` (ship wrong item back to customer).
3. Returns BC publishes `ReturnCompleted` indicating no restockable items.
4. Orders BC: no refund issued (or issues partial credit at customer service discretion).
5. Fulfillment BC coordinated to return wrong item to customer.

**Expected outcome:** No refund; item returned to customer; potential fraud flag raised.

---

#### UC-07: Customer Never Ships Return Package (Return Expires)

**Actor:** System (scheduled)  
**Frequency:** Low  
**Trigger:** 30 days pass after return approval with no carrier scan

1. When `ReturnApproved` is processed, Wolverine schedules `ExpireReturn` command for 30 days later.
2. If customer ships the package, a `ReturnLabelGenerated`/`ReturnShipmentInTransit` event fires, and the scheduled command is cancelled or becomes a no-op (return is no longer in `Approved` state).
3. If no shipment event received, scheduled `ExpireReturn` fires.
4. `ReturnExpired` event appended; integration event published.
5. Notifications BC sends expiration email to customer.
6. Customer must submit a new return request if still within original 30-day delivery window.

**Expected outcome:** Return closed with no refund; customer notified.

---

#### UC-08: Customer Service Overrides Return Window

**Actor:** Customer Service Agent  
**Frequency:** Very Low  
**Trigger:** Customer contacts CS after window closure; manager approves exception

1. CS agent uses internal tooling to submit `ApproveReturn` with `OverrideReason` indicating manager approval.
2. Eligibility validation is bypassed for agent-approved overrides (requires elevated role).
3. Return proceeds through standard approval → inspect → complete flow.

**Status:** This requires an `Escalated` state and role-based authorization. Documented as future scope.

---

#### UC-09: Fraudulent Return (High-Risk Indicators)

**Actor:** Fraud Detection System (future)  
**Frequency:** Very Low  
**Trigger:** Return request triggers fraud heuristics (e.g., high return rate for customer, item-swap pattern)

1. `ReturnRequested` triggers a fraud scoring check (future: Fraud Detection BC or inline scoring).
2. If score exceeds threshold, return is flagged and routed to manual review instead of auto-approval.
3. Return remains in `Requested` state pending manual review.
4. CS agent reviews and either approves (standard flow) or denies (with `PolicyViolation` reason).

**Status:** Documented as future scope. For v1, implement manual approval workflow; fraud automation in a later cycle.

---

#### UC-10: Return for Item from Cancelled Order

**Actor:** Customer  
**Frequency:** Very Low  
**Trigger:** Customer attempts return on order that was cancelled after partial delivery (rare race condition)

1. Customer submits `RequestReturn` with `OrderId` for a partially-fulfilled, then cancelled order.
2. Returns BC checks `ReturnEligibilityWindow` — if `ShipmentDelivered` was received before cancellation, window exists.
3. If items were delivered, return is eligible for those items only.
4. If no delivery occurred, `ReturnEligibilityWindow` does not exist → request denied.

---

#### UC-11: Exchange Instead of Refund (Future)

**Actor:** Customer  
**Frequency:** Low  
**Trigger:** Customer wants to exchange for a different size/color rather than a refund

**Status:** Not in scope for v1. Would require integration with Shopping BC to place a replacement order and Orders BC to credit/debit the difference. Documented as future enhancement.

---

#### UC-12: Warranty Claim (Beyond Return Window)

**Actor:** Customer  
**Frequency:** Low  
**Trigger:** Item fails due to manufacturing defect, but beyond the 30-day return window

**Status:** Warranty claims are distinct from returns. This would require a dedicated `Warranty` workflow or BC. Documented as future scope. For v1, customer service agents use the override workflow (UC-08) for legitimate warranty cases.

---

## Adjacent Bounded Contexts

### Integration Map

```
                    ┌─────────────────────────────────────────────────────┐
                    │                   RETURNS BC                         │
                    │                                                       │
  Fulfillment BC ──►│ ShipmentDelivered (establishes return window)        │
  Fulfillment BC ──►│ ReturnShipmentInTransit (carrier tracking)           │
                    │                                                       │
                    │ Returns.ReturnRequested ─────────────────────────────►│ Customer Experience BC (real-time UI)
                    │ Returns.ReturnApproved ──────────────────────────────►│ Customer Experience BC, Notifications BC
                    │ Returns.ReturnDenied ────────────────────────────────►│ Customer Experience BC, Notifications BC
                    │ Returns.ReturnExpired ───────────────────────────────►│ Notifications BC
                    │ Returns.ReturnCompleted ─────────────────────────────►│ Orders BC (orchestrates refund)
                    │                         ─────────────────────────────►│ Inventory BC (restocking)
                    │                                                       │
                    │ [One-time HTTP query at ShipmentDelivered time]       │
                    │ ─── GET /api/orders/{orderId}/line-items ────────────►│ Orders BC
                    └─────────────────────────────────────────────────────┘
```

### Per-Context Interaction Summary

| Adjacent BC | Relationship | How Returns Interacts |
|-------------|-------------|----------------------|
| **Fulfillment** | Upstream | Consumes `ShipmentDelivered` (establishes window) and `ReturnShipmentInTransit` (carrier updates). Fulfillment BC also generates return labels (carrier integration lives in Fulfillment). |
| **Orders** | Upstream + Downstream | Queries Orders HTTP once at delivery time to snapshot line item eligibility. Publishes `ReturnCompleted` → Orders orchestrates refund. |
| **Payments** | **None (no direct interaction)** | Returns BC does NOT talk to Payments. Orders holds the `PaymentId` and coordinates refund. |
| **Inventory** | Downstream | Publishes `ReturnCompleted` with disposition → Inventory BC reacts to restock eligible items. Returns does NOT call Inventory directly. |
| **Customer Experience** | Downstream | Publishes integration events for real-time UI updates (My Returns page, order history). |
| **Notifications** | Downstream | Publishes events that trigger customer emails (approval, denial, expiration, rejection). |
| **Customer Identity** | Query-only (future) | May query for customer contact information for notification routing. In v1, customer ID is sufficient. |

### What Returns BC Does NOT Own

- Refund payment processing → **Payments BC**
- Determining final refund amount when disputes arise → **Orders BC** (has policy context)
- Inventory restocking execution → **Inventory BC**
- Return shipping label generation (carrier API) → **Fulfillment BC** (already has carrier integration)
- Customer-facing notifications → **Notifications BC**
- Store credit ledger (future) → **dedicated Ledger/Wallet BC**
- Order state management → **Orders saga**

---

## Domain Events

These events form the immutable audit trail in the Returns BC Marten event stream. Events are appended only — never modified.

### Return Request Aggregate Stream

| # | Event | Description | Terminal? |
|---|-------|-------------|-----------|
| 1 | `ReturnRequested` | Customer submits return request with items and reasons | No |
| 2 | `ReturnApproved` | Eligibility validated; return authorized; refund estimate calculated | No |
| 3 | `ReturnDenied` | Return ineligible (outside window, non-returnable item, policy violation) | **Yes** |
| 4 | `ReturnLabelGenerated` | Return shipping label created and provided to customer | No |
| 5 | `ReturnShipmentInTransit` | Carrier scan received; package is on its way back | No |
| 6 | `ReturnShipmentReceived` | Package physically received at warehouse | No |
| 7 | `ReturnInspectionStarted` | Warehouse inspector begins item evaluation | No |
| 8 | `ReturnInspectionCompleted` | Inspection process finished; per-item results recorded | No |
| 9 | `InspectionPassed` | All returned items in acceptable condition; disposition recorded | No |
| 10 | `InspectionFailed` | Items not in acceptable condition; disposition recorded | No |
| 11 | `ReturnCompleted` | Terminal success — all items assessed; downstream BCs notified | **Yes** |
| 12 | `ReturnRejected` | Terminal rejection — items failed inspection; no refund | **Yes** |
| 13 | `ReturnExpired` | Terminal — customer never shipped within 30-day approval window | **Yes** |

### Eligibility Window Stream (keyed by OrderId)

| # | Event | Description |
|---|-------|-------------|
| 1 | `ReturnEligibilityEstablished` | Delivery confirmed; 30-day return window opens; eligible line items snapshotted |

> **Note:** `ReturnEligibilityEstablished` is an internal domain event appended to a separate stream keyed by `OrderId`. It is **not** published as an integration message. It drives the `ReturnEligibilityWindow` read model.

### Key Event Payload Highlights

**`ReturnRequested`:**
```csharp
public sealed record ReturnRequested(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<ReturnLineItem> LineItems,  // Item, Qty, ReturnReason, Notes
    string InitiatedBy,                        // CustomerId or CS agent ID
    DateTimeOffset RequestedAt);
```

**`ReturnApproved`:**
```csharp
public sealed record ReturnApproved(
    Guid ReturnId,
    string ApprovedBy,                         // "System" for auto-approval, or agent ID
    DateTimeOffset ApprovedAt,
    DateTimeOffset ShipByDate,                 // Customer must ship by this date
    string ReturnLabelUrl,                     // From Fulfillment BC carrier integration
    string TrackingNumber,
    Money ExpectedRefundAmount,                // After restocking fee (if any)
    bool RestockingFeeApplied,
    Money? RestockingFeeAmount,
    bool MerchantPaysShipping);
```

**`InspectionPassed`:**
```csharp
public sealed record InspectionPassed(
    Guid ReturnId,
    string InspectorId,
    DateTimeOffset PassedAt,
    IReadOnlyList<InspectedItem> Items);

public sealed record InspectedItem(
    Guid OrderLineItemId,
    string Sku,
    int Quantity,
    ItemCondition Condition,                   // AsExpected | BetterThanExpected | WorseThanExpected
    string? ConditionNotes,
    bool IsRestockable,
    string? WarehouseLocation);                // Bin location for restocking
```

**`InspectionFailed`:**
```csharp
public sealed record InspectionFailed(
    Guid ReturnId,
    string InspectorId,
    DateTimeOffset FailedAt,
    string FailureReason,
    IReadOnlyList<FailedItem> FailedItems,
    DispositionDecision Disposition);          // ReturnToCustomer | Dispose | Quarantine
```

**`ReturnCompleted`:**
```csharp
public sealed record ReturnCompleted(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    Money FinalRefundAmount,                   // After restocking fee; used by Orders for refund
    IReadOnlyList<ReturnedItem> Items,
    DateTimeOffset CompletedAt);

public sealed record ReturnedItem(
    string Sku,
    int Quantity,
    bool IsRestockable,
    string? WarehouseId,
    string? RestockCondition);                 // New | LikeNew | Opened | Damaged
```

**`ReturnEligibilityEstablished`:**
```csharp
public sealed record ReturnEligibilityEstablished(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<EligibleLineItem> EligibleItems,  // Snapshotted from Orders BC at delivery time
    DateTimeOffset DeliveredAt,
    DateTimeOffset WindowExpiresAt);                // DeliveredAt + 30 days
```

---

## Integration Events

### Published by Returns BC

| Integration Event | Namespace | Key Consumers | Trigger |
|-------------------|-----------|---------------|---------|
| `ReturnRequested` | `Messages.Contracts.Returns` | Customer Experience BC | Return request submitted |
| `ReturnApproved` | `Messages.Contracts.Returns` | Customer Experience BC, Notifications BC | Return approved |
| `ReturnDenied` | `Messages.Contracts.Returns` | Customer Experience BC, Notifications BC | Return denied |
| `ReturnExpired` | `Messages.Contracts.Returns` | Notifications BC | Approved return never shipped |
| `ReturnCompleted` | `Messages.Contracts.Returns` | **Orders BC** (refund), **Inventory BC** (restock), Customer Experience BC | Inspection passed; all items assessed |
| `ReturnRejected` | `Messages.Contracts.Returns` | Customer Experience BC, Notifications BC | Inspection failed; no refund |

> **`ReturnCompleted` is the most important integration event.** It must carry enough data for both Orders (refund amount) and Inventory (SKU, quantity, warehouse, condition) to act autonomously without querying Returns.

### Consumed by Returns BC

| Integration Event | Published By | Handler Purpose |
|-------------------|-------------|-----------------|
| `Fulfillment.ShipmentDelivered` | Fulfillment BC | Establish return eligibility window; snapshot eligible line items (one-time Orders HTTP query); schedule expiry timeout |
| `Fulfillment.ReturnShipmentInTransit` | Fulfillment BC | Update return stream with carrier tracking milestone |

> **Returns BC does NOT consume `Orders.OrderPlaced`, `Payments.RefundCompleted`, `Payments.RefundFailed`, or `Inventory.InventoryRestocked`.** These responsibilities belong to Orders and Inventory BCs respectively.

---

## State Transition Model

```
[NotExist]
    │
    │ ReturnRequested
    ▼
┌───────────┐
│ Requested │──────────── ReturnDenied ──────────────► [Denied] (terminal)
└───────────┘
    │
    │ ReturnApproved
    ▼
┌──────────┐
│ Approved │──── ReturnExpired (30-day Wolverine scheduled) ─► [Expired] (terminal)
└──────────┘
    │
    │ ReturnLabelGenerated
    ▼
┌────────────────┐
│ LabelGenerated │
└────────────────┘
    │
    │ ReturnShipmentInTransit (carrier scan)
    ▼
┌───────────┐
│ InTransit │
└───────────┘
    │
    │ ReturnShipmentReceived
    ▼
┌──────────┐
│ Received │
└──────────┘
    │
    │ ReturnInspectionStarted
    ▼
┌────────────┐
│ Inspecting │
└────────────┘
    │                    │
    │ InspectionPassed   │ InspectionFailed
    ▼                    ▼
┌───────────┐       ┌──────────┐
│ Completed │       │ Rejected │
│ (terminal)│       │(terminal)│
└───────────┘       └──────────┘
```

### Terminal States

| State | Meaning |
|-------|---------|
| `Denied` | Return ineligible; no refund |
| `Expired` | Approval granted but never shipped; no refund |
| `Completed` | Inspection passed; `ReturnCompleted` published; refund and restock handled downstream |
| `Rejected` | Inspection failed; disposition applied; no refund (or partial goodwill at Orders discretion) |

### Future States (Reserved for Later Cycles)

| Future State | Trigger | Notes |
|--------------|---------|-------|
| `Escalated` | CS agent issues `EscalateReturn` command | For disputed inspections or policy exceptions |
| `PendingReopenApproval` | Agent requests to reopen a denied/expired return | Requires manager authorization |

---

## Risks

### Business Risks

| ID | Risk | Likelihood | Impact | Mitigation |
|----|------|-----------|--------|------------|
| BR-01 | **Fraud and return abuse** — customers exploit the 30-day window by returning used or damaged items, claiming they were defective | Medium | High | Warehouse inspection with photo documentation; disposition decisions; customer return rate monitoring; flag for `Escalated` review when abuse patterns detected |
| BR-02 | **Restocking fee backlash** — customers react negatively to 15% fee, creating support tickets and negative reviews | Medium | Medium | Clear policy communication at checkout and on product pages; fee waiver for loyal/high-value customers (future: loyalty tier integration) |
| BR-03 | **Non-returnable item policy edge cases** — ambiguity about what qualifies as "opened consumable" or "personalized" | Medium | Medium | Explicit non-returnable category flags in Product Catalog BC; CS override workflow for genuine edge cases |
| BR-04 | **Return window timing disputes** — delivery date discrepancies between carrier and CritterSupply systems | Low | Medium | Use `Fulfillment.ShipmentDelivered` timestamp as authoritative delivery date; document this in policy |
| BR-05 | **High-value item returns** — returns of expensive items create significant refund exposure, especially if inspection processes are not rigorous | Low | High | Mandatory photo documentation for items above a value threshold (configurable); manager review for high-value returns |
| BR-06 | **Multiple return requests on same order** — customer attempts to return the same line item twice | Low | Medium | Check `ReturnEligibilityWindow` for already-returned items at request submission; block duplicate returns |
| BR-07 | **Return without original packaging** — return policies often require original packaging, but this is hard to enforce remotely | Medium | Low | Inspection process captures packaging condition; no blocking rule (just affects restockability grade) |

### Technical Risks

| ID | Risk | Likelihood | Impact | Mitigation |
|----|------|-----------|--------|------------|
| TR-01 | **`ReturnEligibilityWindow` out of sync** — if `Fulfillment.ShipmentDelivered` is missed or processed out of order, Returns BC may deny valid returns | Low | High | Use Wolverine's at-least-once delivery with idempotency; validate event ordering; add compensation for late delivery events |
| TR-02 | **Wolverine scheduled message reliability** — if the scheduled `ExpireReturn` command is lost (service restart, infrastructure issue), approved returns never expire | Low | Medium | Use Marten's durable outbox for scheduled messages; periodic audit query for stale `Approved` returns (backstop scan) |
| TR-03 | **Orders BC HTTP query at delivery time fails** — if Orders BC is unavailable when `ShipmentDelivered` is received, line items cannot be snapshotted | Low | High | Retry with backoff using Wolverine's durable inbox; store partial `ReturnEligibilityWindow` with `Unresolved` status and retry until Orders BC responds |
| TR-04 | **`ReturnCompleted` event not received by Orders or Inventory** — at-least-once semantics in RabbitMQ, but if retry budget exhausted, refund and restock don't happen | Very Low | High | Wolverine durable outbox; DLQ monitoring; manual replay tooling; operations runbook for DLQ investigation |
| TR-05 | **State machine violations** — handler bugs that allow invalid state transitions (e.g., `InspectionPassed` on a `Denied` return) | Low | Medium | Guard all state transitions in handlers against current state; throw meaningful domain exceptions; integration tests for all transition paths |
| TR-06 | **Carrier integration ownership ambiguity** — unclear whether Fulfillment BC or Returns BC handles return label generation and carrier webhooks | Medium | Medium | ADR required (documented as open question below); until ADR resolved, Fulfillment BC owns all carrier interactions |

### Operational Risks

| ID | Risk | Likelihood | Impact | Mitigation |
|----|------|-----------|--------|------------|
| OR-01 | **Inspection backlog** — warehouse receives more returns than can be inspected in a timely manner, leading to SLA breaches on refund timelines | Medium | High | SLA monitoring (time in `Received` state); auto-escalation for returns exceeding 2-business-day inspection target |
| OR-02 | **Return label expiration** — Fulfillment BC-generated labels have carrier-imposed expiry; if customer waits too long, label is invalid | Low | Medium | Label expiry date included in `ReturnApproved`; reminder notifications (Notifications BC) at 3-day and 1-day before expiry |
| OR-03 | **Refund timeline customer expectations** — "5–7 business days" for refunds after inspection may not meet customer expectations set by Amazon/Walmart (2–3 days) | Medium | Medium | Clear communication of timeline at approval; consider expedited refund for high-value/loyal customers |
| OR-04 | **Disposition decisions require clear warehouse SOPs** — if inspectors are inconsistent about what "Restockable" means, inventory quality degrades | Medium | High | Clear inspection rubric and training; `ConditionNotes` field for documentation; periodic quality audits |

---

## Architect Review Notes

> **Incorporated from Principal Architect review (2026-03-05)**

### Critical Corrections Applied

1. **Returns BC does NOT own refund coordination.** Removed `RefundInitiated`, `RefundCompleted`, and `RefundFailed` from the domain event stream and consumed messages list. `CONTEXTS.md` is clear: *"Refund processing (Payments, triggered by Orders)."* Orders holds the `PaymentId` and orchestrates the refund after receiving `ReturnCompleted`.

2. **`Returns.InventoryRestocked` is not a Returns event.** Removed from integration messages. Inventory BC reacts to `ReturnCompleted` and emits its own stock events. Returns has no business emitting events about stock changes it didn't make.

### Important Naming Corrections Applied

3. `ReturnApprovedAfterInspection` → **`InspectionPassed`** — clearer ubiquitous language; avoids confusion with the pre-shipment `ReturnApproved` event.
4. `ReturnRejectedAfterInspection` → **`InspectionFailed`** — consistent with #3.
5. `ReturnShipmentCreated` → **`ReturnLabelGenerated`** — precision matters: at this point the customer has a label, not a physical shipment.
6. Added **`ReturnEligibilityEstablished`** as an explicit domain event on a separate stream (keyed by `OrderId`) to create an auditable, projectable record of when the return window opened.

### Architecture Decisions Applied

7. **`Orders.OrderPlaced` removed from consumed messages.** Returns BC should not subscribe to all order placements. The data needed arrives via `Fulfillment.ShipmentDelivered` + a one-time Orders HTTP query at that moment. Subscribing to every `OrderPlaced` wastes resources and creates ordering dependencies.

8. **Return eligibility uses a local read model** (`ReturnEligibilityWindow` projected from `ShipmentDelivered`), not a synchronous HTTP call to Orders at return request time. BC autonomy requires that Returns can validate eligibility even if Orders BC is temporarily unavailable.

9. **Use Wolverine `ScheduleMessage`** for `ReturnExpired` timeout — not a polling background job.

10. **`ReturnCompleted` integration event enriched** with full `IReadOnlyList<ReturnedItem>` (SKU, quantity, restockable flag, warehouse, condition) so Inventory BC can act autonomously without querying Returns.

### Open Architectural Decision

11. **Carrier integration ownership for return labels and inbound tracking:** Should Fulfillment BC own all carrier interactions (including return labels and return shipment webhooks), or should Returns BC integrate with carriers directly? For v1, Fulfillment BC owns all carrier interactions. This decision requires a formal ADR.

---

## Implementation Guidance

### Project Structure

```
src/Returns/
├── Returns/                          # Domain project (regular SDK)
│   ├── Returns.csproj
│   ├── RequestReturn/
│   │   ├── RequestReturn.cs          # Command
│   │   ├── RequestReturnHandler.cs   # Handler
│   │   ├── RequestReturnValidator.cs # FluentValidation
│   │   ├── ReturnRequested.cs        # Domain event
│   ├── ApproveReturn/
│   │   ├── ApproveReturn.cs
│   │   ├── ApproveReturnHandler.cs
│   │   ├── ReturnApproved.cs
│   │   ├── ReturnDenied.cs
│   ├── InspectReturn/
│   │   ├── StartInspection.cs
│   │   ├── CompleteInspection.cs
│   │   ├── InspectionPassed.cs
│   │   ├── InspectionFailed.cs
│   ├── ReturnAggregate/
│   │   ├── Return.cs                 # Aggregate root (pure; Apply methods)
│   │   ├── ReturnStatus.cs           # Enum
│   │   ├── ReturnLineItem.cs
│   │   ├── InspectedItem.cs
│   ├── ReturnEligibility/
│   │   ├── ReturnEligibilityEstablished.cs  # Domain event
│   │   ├── ReturnEligibilityWindow.cs       # Marten document (read model)
│   │   ├── ShipmentDeliveredHandler.cs      # Integration message handler
│   └── Projections/
│       └── ReturnSummaryProjection.cs
│
└── Returns.Api/                      # API project (Web SDK)
    ├── Returns.Api.csproj
    ├── Program.cs
    ├── appsettings.json
    ├── Properties/launchSettings.json  # Port: 5241 (next available)
    ├── RequestReturn/
    │   └── RequestReturnEndpoint.cs
    └── GetReturn/
        └── GetReturnEndpoint.cs
```

### Aggregate Design Skeleton

```csharp
public sealed record Return
{
    public Guid Id { get; init; }
    public ReturnStatus Status { get; init; }
    public Guid OrderId { get; init; }
    public Guid CustomerId { get; init; }
    public IReadOnlyList<ReturnLineItem> LineItems { get; init; } = [];
    public DateTimeOffset RequestedAt { get; init; }
    public Money? ExpectedRefundAmount { get; init; }
    public Money? FinalRefundAmount { get; init; }

    public Return Apply(ReturnApproved e) =>
        this with { Status = ReturnStatus.Approved, ExpectedRefundAmount = e.ExpectedRefundAmount };

    public Return Apply(ReturnDenied _) =>
        this with { Status = ReturnStatus.Denied };

    public Return Apply(ReturnLabelGenerated _) =>
        this with { Status = ReturnStatus.LabelGenerated };

    public Return Apply(ReturnShipmentInTransit _) =>
        this with { Status = ReturnStatus.InTransit };

    public Return Apply(ReturnShipmentReceived _) =>
        this with { Status = ReturnStatus.Received };

    public Return Apply(ReturnInspectionStarted _) =>
        this with { Status = ReturnStatus.Inspecting };

    public Return Apply(InspectionPassed e) =>
        this with { Status = ReturnStatus.Completed, FinalRefundAmount = e.FinalRefundAmount };

    public Return Apply(InspectionFailed _) =>
        this with { Status = ReturnStatus.Rejected };

    public Return Apply(ReturnCompleted _) => this; // Already Completed from InspectionPassed

    public Return Apply(ReturnExpired _) =>
        this with { Status = ReturnStatus.Expired };
}
```

### Key Wolverine Patterns

- **`[WriteAggregate]`** on command handlers that append events to the Return stream.
- **`ScheduleMessage`** for `ExpireReturn` command when processing `ReturnApproved`.
- **`IMartenStore`** for projecting `ReturnEligibilityWindow` from `ReturnEligibilityEstablished`.
- **`OutgoingMessages`** for publishing integration events alongside domain events.

---

## Testing Strategy

### Integration Tests (Alba + TestContainers)

**Happy Path Tests:**
1. Submit return → auto-approve → generate label → receive → inspect → complete
2. Verify all events appended to Marten stream in correct order
3. Verify `ReturnCompleted` integration message published to RabbitMQ with correct payload

**Eligibility Tests:**
4. Return denied: outside 30-day window
5. Return denied: non-returnable item category
6. Return approved: within window, eligible item

**Inspection Tests:**
7. Inspection passed: items restockable
8. Inspection failed: customer damage, `Dispose` disposition
9. Inspection failed: wrong item received, `ReturnToCustomer` disposition

**Edge Case Tests:**
10. Partial return (subset of order line items)
11. Return expires (Wolverine-scheduled command fires)
12. Multiple return requests against same order

**Integration Message Tests:**
13. `Returns.ReturnCompleted` triggers Orders BC refund flow (verify via RabbitMQ)
14. `Returns.ReturnCompleted` triggers Inventory BC restock flow (verify via RabbitMQ)

### BDD Feature Files

Location: `docs/features/returns/`

| File | Scenarios Covered |
|------|-------------------|
| `return-request.feature` | ✅ Exists — happy path, restocking fee, denial, inspection rejection |
| `return-inspection.feature` | ❌ Missing — inspection pass/fail, disposition decisions |
| `return-expiration.feature` | ❌ Missing — approval timeout, expiry notification |
| `return-eligibility.feature` | ❌ Missing — window boundaries, non-returnable items |

---

## Open Questions and Future Considerations

### Open Questions (Require ADR)

1. **Carrier Integration Ownership:** Does Fulfillment BC own all carrier interactions (including return label generation and inbound return tracking webhooks), or does Returns BC integrate with carriers directly? Recommendation: Fulfillment BC for v1 (keeps carrier logic centralized). Requires ADR.

2. **Non-Returnable Item Registry:** Where does the list of non-returnable categories/SKUs live? Options: (a) Product Catalog BC attribute on product, (b) Returns BC policy configuration, (c) Rules engine. Recommendation: Product Catalog attribute (`IsReturnable` flag), queried at eligibility check time or included in `ReturnEligibilityWindow` snapshot.

3. **Restocking Fee Policy:** Is the 15% restocking fee universal, or does it vary by product category or customer tier? If variable, should it be computed in Returns BC or Orders BC? Recommendation: Returns BC calculates based on `ReturnReason` and a configurable policy; include calculated amount in `ReturnCompleted` for Orders to use.

4. **Goodwill Store Credit for Failed Inspections:** When inspection fails and a goodwill gesture is offered (e.g., 50% store credit), where is this handled? Returns? Orders? A future Ledger BC? Recommendation: Document `StoreCredit` as a future `DispositionDecision` option; do not implement store credit mechanics in v1.

### Future Considerations

- **`Escalated` state** for CS dispute resolution (returns that customers contest).
- **`ReturnReopenedByAgent`** event for manager overrides of denied returns.
- **Fraud detection integration** — automatic flagging based on customer return history.
- **Exchange workflow** — return item and place replacement order in one transaction.
- **Warranty claims** — beyond return window but within product warranty.
- **International returns** — different policies, customs declarations, carrier requirements.
- **Store Credit Ledger BC** — first-class store credit as a refund method.
- **Batch return processing** — for B2B customers returning multiple orders simultaneously.

---

*This document is the authoritative domain specification for the Returns bounded context. It supersedes workflow-level notes in `docs/workflows/returns-workflows.md` where there are conflicts. For technical implementation patterns, refer to the [skill documents](../skills/) and [existing BC implementations](../../src/).*
