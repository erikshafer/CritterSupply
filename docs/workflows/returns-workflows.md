# Returns BC Workflows

> ⚠️ **Architectural Corrections Applied (2026-03-05):** This document has been updated based on Principal Architect review. Key changes: removed refund coordination from Returns scope (Orders BC owns this), renamed events for clarity (`InspectionPassed`/`InspectionFailed`, `ReturnLabelGenerated`), added `ReturnEligibilityEstablished` event, corrected state machine. See [`docs/returns/RETURNS-BC-SPEC.md`](../returns/RETURNS-BC-SPEC.md) for the authoritative domain specification.

**Bounded Context:** Returns  
**Implementation Status:** 🚧 Planned (Not Yet Implemented)  
**Priority:** Medium (Cycle 21+)  
**Estimated Effort:** 3-5 sessions  

---

## Overview

The Returns BC manages the complete return lifecycle from customer request through refund processing and inventory restocking. It orchestrates cross-BC interactions with Orders (return eligibility), Payments (refunds), Inventory (restocking), and Fulfillment (return shipment tracking).

### Key Business Rules (From 10+ Years E-Commerce Experience)

1. **Return Window:** 30 days from delivery date (configurable)
2. **Return Reasons:** Defective, wrong item, unwanted, damaged in transit
3. **Restocking Fees:** 15% for non-defective returns (configurable, may waive for loyalty)
4. **Non-Returnable Items:** Personalized products, opened consumables, final sale items
5. **Return Shipping:** Customer pays unless defective/wrong item (then merchant pays)
6. **Refund Timeline:** 5-7 business days after return inspection approval
7. **Partial Returns:** Support returning some (not all) line items from an order

---

## Aggregate: Return Request

### Aggregate Lifecycle

```
[NotExist] --Create--> [Requested] --Approve--> [Approved] --ShipmentReceived--> [Inspecting] --Approve--> [Completed]
                                  \--Deny--> [Denied]                          \--Reject--> [Rejected]
                                  
[Approved] --Timeout(7 days)--> [Expired]
```

### Domain Events

> ⚠️ **Naming updates applied (2026-03-05):** `ReturnShipmentCreated` → `ReturnLabelGenerated`; `ReturnApprovedAfterInspection` → `InspectionPassed`; `ReturnRejectedAfterInspection` → `InspectionFailed`. Refund and inventory events (`RefundInitiated`, `RefundCompleted`, `RefundFailed`, `InventoryRestocked`) removed — those belong to Orders BC and Inventory BC respectively. See domain spec for full rationale.

**Initialization:**
- `ReturnRequested` — Customer submits return request
  - ReturnId (Guid)
  - OrderId (Guid) — Original order reference
  - CustomerId (Guid)
  - LineItems (List<ReturnLineItem>) — Which items to return
    - OrderLineItemId (Guid)
    - Sku (string)
    - Quantity (int)
    - ReturnReason (enum: Defective, WrongItem, Unwanted, DamagedInTransit, Other)
    - ReturnReasonDetails (string) — Customer explanation
  - InitiatedBy (string) — CustomerId or CS agent ID
  - RequestedAt (DateTimeOffset)

**Eligibility Window (separate stream, keyed by OrderId):**
- `ReturnEligibilityEstablished` — Delivery confirmed; return window opens; eligible line items snapshotted
  - OrderId (Guid)
  - CustomerId (Guid)
  - EligibleItems (List<EligibleLineItem>) — Snapshotted from Orders BC at delivery time
  - DeliveredAt (DateTimeOffset)
  - WindowExpiresAt (DateTimeOffset) — DeliveredAt + 30 days

**Authorization Phase:**
- `ReturnApproved` — Customer service (or system) approves return
  - ReturnId
  - ApprovedBy (string) — CS agent ID or "System" for auto-approval
  - ApprovedAt (DateTimeOffset)
  - ShipByDate (DateTimeOffset) — Customer must ship by this date (Wolverine schedules expiry at this time)
  - ReturnLabelUrl (string) — Prepaid label URL (if merchant pays)
  - TrackingNumber (string)
  - ExpectedRefundAmount (Money) — Calculated refund (may include restocking fee deduction)
  - RestockingFeeApplied (bool)
  - RestockingFeeAmount (Money, nullable)
  - MerchantPaysShipping (bool) — True if defective/wrong item

- `ReturnDenied` — Return request rejected
  - ReturnId
  - DenialReason (enum: OutsideReturnWindow, NonReturnableItem, PolicyViolation, Other)
  - DenialReasonDetails (string)
  - DeniedBy (string)
  - DeniedAt (DateTimeOffset)

**Return Shipment Tracking:**
- `ReturnLabelGenerated` — Return shipping label created and provided to customer (previously `ReturnShipmentCreated`)
  - ReturnId
  - TrackingNumber (string)
  - Carrier (string) — USPS, UPS, FedEx
  - LabelUrl (string)
  - GeneratedAt (DateTimeOffset)

- `ReturnShipmentInTransit` — Carrier scans package
  - ReturnId
  - TrackingNumber
  - LastScanLocation (string)
  - EstimatedArrival (DateTimeOffset)

- `ReturnShipmentReceived` — Package arrives at warehouse
  - ReturnId
  - ReceivedAt (DateTimeOffset)
  - ReceivedByWarehouse (string) — Warehouse ID

**Inspection Phase:**
- `ReturnInspectionStarted` — Warehouse begins inspection
  - ReturnId
  - InspectorId (string)
  - StartedAt (DateTimeOffset)

- `ReturnInspectionCompleted` — Inspection process finished; per-item results recorded
  - ReturnId
  - InspectorId
  - CompletedAt (DateTimeOffset)
  - LineItemInspectionResults (List<LineItemInspectionResult>)
    - OrderLineItemId
    - Condition (enum: AsExpected, BetterThanExpected, WorseThanExpected, NotReceived)
    - ConditionNotes (string)
    - Restockable (bool)
    - WarehouseLocation (string, nullable)

- `InspectionPassed` — Items in acceptable condition; disposition and refund amount recorded (previously `ReturnApprovedAfterInspection`)
  - ReturnId
  - InspectorId
  - PassedAt (DateTimeOffset)
  - FinalRefundAmount (Money) — May differ from initial estimate if condition worse than expected
  - Items (List<InspectedItem>)
    - Sku, Quantity, IsRestockable, WarehouseLocation, RestockCondition

- `InspectionFailed` — Items not in acceptable condition (previously `ReturnRejectedAfterInspection`)
  - ReturnId
  - InspectorId
  - FailedAt (DateTimeOffset)
  - FailureReason (string)
  - FailedItems (List<FailedItem>)
  - Disposition (enum: ReturnToCustomer, Dispose, Quarantine)

**Terminal States:**
- `ReturnCompleted` — Full return lifecycle finished; downstream BCs (Orders, Inventory) react to this event
  - ReturnId
  - OrderId
  - CustomerId
  - FinalRefundAmount (Money) — Used by Orders BC to initiate the correct refund amount
  - Items (List<ReturnedItem>) — Used by Inventory BC to restock eligible items
    - Sku, Quantity, IsRestockable, WarehouseId, RestockCondition
  - CompletedAt (DateTimeOffset)

- `ReturnRejected` — Items failed inspection; no refund
  - ReturnId
  - CompletedAt (DateTimeOffset)

- `ReturnExpired` — Customer never shipped return within 30-day approval window
  - ReturnId
  - ExpiredAt (DateTimeOffset)

---

## Workflows

### Workflow 1: Happy Path - Return Request to Refund

**Scenario:** Customer receives defective dog bowl, requests return, warehouse approves, refund issued

```
1. Customer Action: Submit Return Request
   Command: RequestReturn
     - OrderId: "order-abc-123"
     - LineItems: [{ OrderLineItemId: "line-456", Sku: "DOG-BOWL-01", Quantity: 1, ReturnReason: Defective }]
   Event: ReturnRequested

2. System: Validate Return Eligibility
   - Query Orders BC: Is order delivered? Within 30-day window?
   - Query Order Line Items: Are items eligible for return (not final sale)?
   - If valid → Auto-approve or queue for manual review

3. Customer Service (or System): Approve Return
   Command: ApproveReturn
     - ReturnId
     - MerchantPaysShipping: true (defective item)
   Event: ReturnApproved
   Integration: Generate prepaid return label via Fulfillment BC carrier integration

4. Customer: Prepare Return Package
   Command: RecordReturnLabel (label generated by Fulfillment BC carrier integration)
     - ReturnId
     - TrackingNumber: "1Z999AA10123456784"
   Event: ReturnLabelGenerated

5. Carrier: Package Scanned
   Integration: Carrier webhook → Fulfillment BC → Returns BC
   Event: ReturnShipmentInTransit

6. Warehouse: Receive Return Package
   Command: ReceiveReturnShipment
     - ReturnId
   Event: ReturnShipmentReceived

7. Warehouse: Inspect Items
   Command: StartReturnInspection
     - ReturnId
   Event: ReturnInspectionStarted

   Command: CompleteReturnInspection
     - ReturnId
     - LineItemInspectionResults: [{ Condition: AsExpected, Restockable: true }]
   Event: ReturnInspectionCompleted
   Event: InspectionPassed

8. System: Publish ReturnCompleted (Orders BC orchestrates refund; Inventory BC restocks)
   Event: ReturnCompleted
     - FinalRefundAmount: $19.99
     - Items: [{ Sku: "DOG-BOWL-01", Qty: 1, IsRestockable: false }]
   Integration: Returns.ReturnCompleted → Orders BC (triggers RefundRequested to Payments)
   Integration: Returns.ReturnCompleted → Inventory BC (triggers dispose of defective item)

9. Orders BC: Orchestrate Refund (separate BC; Returns has no visibility into this)
   Orders holds PaymentId from original order placement
   Orders publishes RefundRequested → Payments BC
   Payments processes refund → publishes RefundCompleted → Orders saga

10. Inventory BC: React to ReturnCompleted (separate BC; Returns has no visibility into this)
    Inventory reads IsRestockable flag from ReturnCompleted
    Defective item: dispose (not restocked)

11. System: Return Lifecycle Complete
    Return stream is in terminal "Completed" state

TOTAL DURATION: 7-10 days (3 days shipping + 1-2 days inspection + 5-7 days refund processing)
```

---

### Workflow 2: Edge Case - Restocking Fee Applied (Unwanted Item)

**Scenario:** Customer changes mind about cat toy, return approved but 15% restocking fee applied

```
1. Customer: Submit Return Request
   Command: RequestReturn
     - OrderId: "order-xyz-789"
     - LineItems: [{ Sku: "CAT-TOY-05", Quantity: 1, ReturnReason: Unwanted }]
   Event: ReturnRequested

2. System: Auto-Approve with Restocking Fee
   Command: ApproveReturn
     - RestockingFeeApplied: true
     - RestockingFeeAmount: $4.50 (15% of $29.99)
     - ExpectedRefundAmount: $25.49 ($29.99 - $4.50)
     - MerchantPaysShipping: false (customer pays return shipping)
   Event: ReturnApproved

3-7. [Same as Happy Path: Customer ships, warehouse receives/inspects]

8. System: Publish ReturnCompleted with Restocking Fee Applied
   Event: ReturnCompleted
     - FinalRefundAmount: $25.49 (restocking fee already deducted)
     - Items: [{ Sku: "CAT-TOY-05", Qty: 1, IsRestockable: true }]
   Integration: Returns.ReturnCompleted → Orders BC (issues partial refund via Payments)
   Integration: Returns.ReturnCompleted → Inventory BC (restocks the item)

9. Orders BC: Orchestrate Partial Refund (separate BC)

10. Inventory BC: Restock Item (separate BC)

11. System: Return Lifecycle Complete

CUSTOMER IMPACT: Pays $4.50 restocking fee + return shipping cost (~$7-$10)
```

---

### Workflow 3: Edge Case - Return Denied (Outside Return Window)

**Scenario:** Customer attempts return 45 days after delivery (policy: 30 days)

```
1. Customer: Submit Return Request
   Command: RequestReturn
     - OrderId: "order-old-456"
     - LineItems: [{ Sku: "DOG-FOOD-01", Quantity: 1, ReturnReason: Unwanted }]
   Event: ReturnRequested

2. System: Validate Return Eligibility
   - Query Orders BC: Order delivered 45 days ago
   - ReturnWindowExpiresAt: 30 days from delivery
   - VALIDATION FAILURE: Outside return window

3. System: Deny Return Automatically
   Command: DenyReturn
     - DenialReason: OutsideReturnWindow
     - DenialReasonDetails: "Order was delivered more than 30 days ago (45 days). Our return policy allows returns within 30 days of delivery."
   Event: ReturnDenied

4. System: Notify Customer
   Integration: Send email via Notifications BC
   - Subject: "Return Request Denied - Order #order-old-456"
   - Body: [Polite explanation + offer to contact customer service for exceptions]

CUSTOMER IMPACT: No refund, but may contact customer service for exception (store credit at manager's discretion)
```

---

### Workflow 4: Edge Case - Return Rejected After Inspection (Damaged by Customer)

**Scenario:** Customer returns item but inspection reveals customer-caused damage

```
1-7. [Same as Happy Path: Request → Approve → Ship → Receive → Inspect]

8. Warehouse: Inspection Reveals Customer Damage
   Command: CompleteReturnInspection
     - LineItemInspectionResults: [{ Condition: WorseThanExpected, ConditionNotes: "Screen cracked, water damage visible", Restockable: false }]
   Event: ReturnInspectionCompleted
   Event: InspectionFailed
     - Disposition: Dispose
     - FailureReason: "DamagedByCustomer"

9. System: Notify Customer
   Integration: Email with photos of damage
   - Offer: $15 store credit as goodwill (customer can accept or dispute)

10a. Customer Accepts Store Credit:
    Command: IssueStoreCredit
      - CustomerId
      - Amount: $15.00
    Event: StoreCreditIssued
    Event: ReturnCompleted (no inventory restocking)

10b. Customer Disputes:
    → Manual customer service escalation
    → Manager reviews photos + customer explanation
    → Final decision: Approve full refund (rare) or uphold rejection

CUSTOMER IMPACT: No full refund, but goodwill store credit offered
```

---

### Workflow 5: Edge Case - Partial Return (Multiple Items, Some Returned)

**Scenario:** Customer orders 3 items, returns 2 (keeps 1)

```
Original Order:
  - Line Item 1: DOG-BOWL-01 (Qty: 2, Total: $39.98)
  - Line Item 2: CAT-TOY-05 (Qty: 1, Total: $29.99)
  - Line Item 3: DOG-FOOD-01 (Qty: 1, Total: $49.99)
  Order Total: $119.96

1. Customer: Submit Partial Return Request
   Command: RequestReturn
     - OrderId: "order-partial-123"
     - LineItems: [
         { OrderLineItemId: "line-1", Sku: "DOG-BOWL-01", Quantity: 2, ReturnReason: Unwanted },
         { OrderLineItemId: "line-2", Sku: "CAT-TOY-05", Quantity: 1, ReturnReason: Defective }
       ]
   Event: ReturnRequested

2. System: Calculate Partial Refund
   - DOG-BOWL-01: $39.98 (15% restocking fee = $6.00) → Refund $33.98
   - CAT-TOY-05: $29.99 (no restocking fee, defective) → Refund $29.99
   - Expected Refund Total: $63.97

3. System: Approve Partial Return
   Command: ApproveReturn
     - ExpectedRefundAmount: $63.97
     - MerchantPaysShipping: true (one item defective)
   Event: ReturnApproved

4-10. [Same as Happy Path: Ship → Receive → Inspect → Refund → Restock]

11. System: Complete Partial Return
    Event: ReturnCompleted
    - Order Status: Remains "Delivered" (not fully returned)
    - Line Items NOT Returned: DOG-FOOD-01 (customer keeps this)

CUSTOMER IMPACT: Refunds for 2 of 3 items, keeps 1 item, order history shows partial return
```

---

### Workflow 6: Edge Case - Return Expired (Customer Never Shipped)

**Scenario:** Customer gets return approval but never ships package within 7-day window

```
1. Customer: Submit Return Request
   Event: ReturnRequested

2. System: Approve Return
   Command: ApproveReturn
     - ReturnShippingLabel.ExpiresAt: 7 days from approval
   Event: ReturnApproved

3. Customer: [NO ACTION] - Never ships package

4. System: Detect Expiration (Background Job)
   - Runs daily, checks for returns in "Approved" state past expiration
   - Found: ReturnId "return-expired-789" approved 8 days ago, no shipment created

5. System: Expire Return
   Command: ExpireReturn
     - ReturnId
   Event: ReturnExpired

6. System: Notify Customer
   Integration: Email notification
   - "Your return request has expired. If you still need to return this item, please submit a new request."

BUSINESS IMPACT: No inventory reservation (didn't hold stock for return), no refund processed
CUSTOMER IMPACT: Must submit new return request if still within 30-day window
```

---

## Integration Flows

### Returns BC Receives (Inbound Integration Messages)

| Integration Message | Published By | Handler | Outcome |
|---|---|---|---|
| `Fulfillment.ShipmentDelivered` | Fulfillment BC | Establish return eligibility window; one-time HTTP query to Orders BC for line item snapshot; schedule 30-day expiry | Project `ReturnEligibilityWindow` read model |
| `Fulfillment.ReturnShipmentInTransit` | Fulfillment BC | Track return package in transit | Append `ReturnShipmentInTransit` event to return stream |

---

### Returns BC Publishes (Outbound Integration Messages)

| Integration Message | Consumed By | Purpose |
|---|---|---|
| `Returns.ReturnRequested` | Customer Experience BC | Real-time UI update: "Return request submitted" |
| `Returns.ReturnApproved` | Customer Experience BC, Notifications BC | UI update + email customer with return label |
| `Returns.ReturnDenied` | Customer Experience BC, Notifications BC | UI update + email customer with denial reason |
| `Returns.ReturnExpired` | Notifications BC | Email customer: return approval expired |
| `Returns.ReturnCompleted` | **Orders BC** (orchestrates refund via Payments), **Inventory BC** (restocks eligible items), Customer Experience BC | Terminal success event; carries full item disposition |
| `Returns.ReturnRejected` | Customer Experience BC, Notifications BC | UI update + email customer with rejection reason |

---

## State Transition Diagram

```mermaid
stateDiagram-v2
    [*] --> Requested: ReturnRequested

    Requested --> Approved: ReturnApproved
    Requested --> Denied: ReturnDenied

    Denied --> [*]

    Approved --> Expired: ReturnExpired (30-day Wolverine scheduled command)
    Approved --> LabelGenerated: ReturnLabelGenerated

    Expired --> [*]

    LabelGenerated --> InTransit: ReturnShipmentInTransit

    InTransit --> Received: ReturnShipmentReceived

    Received --> Inspecting: ReturnInspectionStarted

    Inspecting --> Completed: InspectionPassed
    Inspecting --> Rejected: InspectionFailed

    Completed --> [*]
    Rejected --> [*]
```

---

## Business Events Summary

### Aggregate Events (Return Aggregate Stream)

1. `ReturnRequested` — Customer submits return
2. `ReturnApproved` — Authorization granted
3. `ReturnDenied` — Authorization denied (terminal)
4. `ReturnLabelGenerated` — Label created and provided to customer
5. `ReturnShipmentInTransit` — Carrier scans package
6. `ReturnShipmentReceived` — Warehouse receives package
7. `ReturnInspectionStarted` — Inspection begins
8. `ReturnInspectionCompleted` — Inspection process finished
9. `InspectionPassed` — Items acceptable; disposition recorded
10. `InspectionFailed` — Items not acceptable; disposition recorded
11. `ReturnCompleted` — Terminal state (success; triggers Orders refund + Inventory restock)
12. `ReturnRejected` — Terminal state (inspection failed; no refund)
13. `ReturnExpired` — Terminal state (customer never shipped)

### Eligibility Window Events (Separate Stream, keyed by OrderId)

14. `ReturnEligibilityEstablished` — Delivery confirmed; 30-day window opens

---

## Integration Messages

### Published by Returns BC

- `Returns.ReturnRequested`
- `Returns.ReturnApproved`
- `Returns.ReturnDenied`
- `Returns.ReturnExpired`
- `Returns.ReturnCompleted` ← primary integration event; carries full item disposition for Orders (refund) and Inventory (restock)
- `Returns.ReturnRejected`

### Consumed by Returns BC

- `Fulfillment.ShipmentDelivered` ← establishes return eligibility window; triggers one-time Orders HTTP query to snapshot line items
- `Fulfillment.ReturnShipmentInTransit` ← carrier tracking updates for inbound return shipments

> **Note:** Returns BC does NOT consume `Orders.OrderPlaced`, `Payments.RefundCompleted`, `Payments.RefundFailed`, or `Inventory.InventoryRestocked`. Refund coordination is owned by Orders BC (it holds the PaymentId). Inventory restocking is owned by Inventory BC (it reacts to `ReturnCompleted`).

---

## Invariants (Business Rules)

1. **Return Window Enforcement:**
   - Cannot approve return > 30 days after delivery
   - Exception: Customer service can override with manager approval

2. **Non-Returnable Items:**
   - Personalized products cannot be returned
   - Opened consumables (food, supplements) cannot be returned
   - Final sale items cannot be returned

3. **Refund Amount Validation:**
   - Refund cannot exceed original purchase amount
   - Restocking fee (if applied) must be ≤ 15% of item value
   - Shipping costs not refunded unless merchant error

4. **Restocking Rules:**
   - Only items in "Restockable" condition return to inventory
   - Damaged items disposed (not restocked)
   - Restocking location must be valid warehouse bin

5. **Return Expiration:**
   - Approved returns expire if not shipped within 7 days
   - Expired returns require new request submission

---

## Implementation Guidance

### Aggregate Design

```csharp
public sealed record ReturnRequest
{
    public Guid Id { get; init; }
    public ReturnStatus Status { get; private set; }
    public Guid OrderId { get; init; }
    public Guid CustomerId { get; init; }
    public List<ReturnLineItem> LineItems { get; init; } = [];
    public DateTimeOffset RequestedAt { get; init; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Money? ExpectedRefundAmount { get; private set; }
    public Money? FinalRefundAmount { get; private set; }
    
    // Factory method
    public static (ReturnRequest, ReturnRequested) Create(
        Guid orderId, 
        Guid customerId, 
        List<ReturnLineItem> lineItems)
    {
        var returnRequest = new ReturnRequest
        {
            Id = Guid.NewGuid(),
            Status = ReturnStatus.Requested,
            OrderId = orderId,
            CustomerId = customerId,
            LineItems = lineItems,
            RequestedAt = DateTimeOffset.UtcNow
        };
        
        var @event = new ReturnRequested(
            returnRequest.Id,
            orderId,
            customerId,
            lineItems,
            returnRequest.RequestedAt
        );
        
        return (returnRequest, @event);
    }
    
    // Apply methods for event sourcing
    public ReturnRequest Apply(ReturnApproved e)
    {
        return this with 
        { 
            Status = ReturnStatus.Approved,
            ApprovedAt = e.ApprovedAt,
            ExpectedRefundAmount = e.ExpectedRefundAmount
        };
    }
    
    public ReturnRequest Apply(ReturnDenied e)
    {
        return this with { Status = ReturnStatus.Denied };
    }
    
    // ... more Apply methods for each event
}

public enum ReturnStatus
{
    Requested,
    Approved,
    Denied,
    LabelGenerated,
    InTransit,
    Received,
    Inspecting,
    Completed,
    Rejected,
    Expired
}
```

### Handler Pattern Example

```csharp
public sealed record ApproveReturn(
    Guid ReturnId,
    bool MerchantPaysShipping,
    bool RestockingFeeApplied,
    Money? RestockingFeeAmount
);

public static class ApproveReturnHandler
{
    public static (Events, OutgoingMessages) Handle(
        ApproveReturn command,
        Return returnAggregate)
    {
        // Guard: only Requested returns can be approved
        if (returnAggregate.Status != ReturnStatus.Requested)
            throw new InvalidOperationException($"Cannot approve return in {returnAggregate.Status} state");

        // Calculate refund amount
        var refundAmount = CalculateRefundAmount(returnAggregate, command.RestockingFeeApplied, command.RestockingFeeAmount);

        var shipByDate = DateTimeOffset.UtcNow.AddDays(30);

        var @event = new ReturnApproved(
            command.ReturnId,
            "System",
            DateTimeOffset.UtcNow,
            shipByDate,
            labelUrl: "https://carrier.example/label/xyz",
            trackingNumber: "1Z999AA10123456784",
            refundAmount,
            command.RestockingFeeApplied,
            command.RestockingFeeAmount,
            command.MerchantPaysShipping
        );

        var integrationEvent = new Messages.Contracts.Returns.ReturnApproved(
            command.ReturnId, returnAggregate.OrderId, returnAggregate.CustomerId,
            @event.ReturnLabelUrl, @event.TrackingNumber, @event.ApprovedAt, shipByDate);

        // Schedule expiry using Wolverine's durable scheduling
        var expiry = new ScheduledMessage<ExpireReturn>(
            new ExpireReturn(command.ReturnId), shipByDate);

        return (
            Events.Append(@event),
            new OutgoingMessages(integrationEvent, expiry)
        );
    }
}
```

---

## Testing Strategy

### Integration Tests (Alba + TestContainers)

1. **Happy Path Tests:**
   - Request return → Approve → Ship → Receive → Inspect → Refund → Restock → Complete
   - Verify all events persisted to Marten stream
   - Verify integration messages published to RabbitMQ

2. **Edge Case Tests:**
   - Return denied (outside window, non-returnable item)
   - Return rejected after inspection (damaged by customer)
   - Restocking fee applied (unwanted item)
   - Partial return (multiple items, some returned)
   - Return expired (customer never shipped)

3. **Integration Message Tests:**
   - Returns.ReturnCompleted triggers Orders BC refund flow
   - Returns.ReturnCompleted triggers Inventory BC restock flow

### BDD Feature Files

Location: `docs/features/returns/`

Recommended feature files:
- `return-request.feature` — ✅ Exists: Happy path + denial scenarios
- `return-inspection.feature` — ✅ Exists: Inspection approval/rejection, disposition decisions
- `return-eligibility.feature` — ✅ Exists: Window boundaries, non-returnable items
- `return-expiration.feature` — ✅ Exists: Approval timeout, expiry notification

---

## Dependencies

**Must Be Implemented First:**
- ✅ Orders BC (for return eligibility validation)
- ✅ Payments BC (for refund processing)
- ✅ Inventory BC (for restocking)
- ✅ Fulfillment BC (for return shipment tracking)

**Nice to Have:**
- Notifications BC (for email notifications — can stub initially)
- Customer Experience BC (for real-time UI updates — can implement separately)

---

## Estimated Implementation Effort

**Cycle Breakdown:**

**Session 1-2:** Returns BC Foundation
- Aggregate design (ReturnRequest, events, Apply methods)
- Command handlers (RequestReturn, ApproveReturn, DenyReturn)
- Integration tests for happy path
- Marten event sourcing configuration

**Session 3:** Return Shipment & Inspection
- ShipmentTracking handlers (ReceiveReturnShipment, InspectReturn)
- Integration with Fulfillment BC for carrier updates
- Integration tests for inspection approval/rejection

**Session 4:** Cross-BC Integration
- `Returns.ReturnCompleted` verifies Orders BC receives it and triggers refund
- `Returns.ReturnCompleted` verifies Inventory BC receives it and restocks
- Integration tests for cross-BC flows via RabbitMQ
- Return expiry (Wolverine-scheduled `ExpireReturn` command)

**Session 5:** Edge Cases & Polish
- Return expiration (background job)
- Partial return support
- Restocking fee calculation
- BDD feature files
- API endpoints (POST /api/returns, GET /api/returns/{id})

**Total Effort:** 3-5 sessions (6-10 hours)

---

## Success Criteria

- [ ] All 13 aggregate events implemented with Apply methods (+ 1 eligibility window event)
- [ ] `ReturnEligibilityWindow` read model projected from `Fulfillment.ShipmentDelivered`
- [ ] 6 integration messages published (ReturnRequested, ReturnApproved, ReturnDenied, ReturnExpired, ReturnCompleted, ReturnRejected)
- [ ] 2 integration messages consumed (Fulfillment.ShipmentDelivered, Fulfillment.ReturnShipmentInTransit)
- [ ] 15+ integration tests passing (happy path + edge cases)
- [ ] State transition diagram validated with tests
- [ ] Cross-BC integration verified: `ReturnCompleted` → Orders and Inventory BCs
- [ ] Wolverine-scheduled `ExpireReturn` command works correctly
- [ ] BDD feature files written and linked to integration tests
- [ ] ADR created: carrier integration ownership (Fulfillment vs Returns)
- [ ] CONTEXTS.md updated with finalized Returns BC integration contracts

---

**Document Owner:** Product Owner (Erik Shafer)  
**Last Updated:** 2026-02-18  
**Status:** 🟢 Ready for Implementation
