# CS Agent Runbook — Returns BC

**Last Updated:** 2026-03-13  
**API Base URL:** `http://localhost:5245` (local development)

---

## Quick Reference: Return Statuses

| Status | Meaning | Customer-Facing Label | Next Action |
|--------|---------|----------------------|-------------|
| `Requested` | Return submitted, pending CS review ("Other" reason) | "Under Review" | Approve or Deny |
| `Approved` | Return approved, awaiting customer shipment | "Approved — Ship by {date}" | Wait for warehouse receipt |
| `Received` | Package received at warehouse | "We received your package" | Submit inspection |
| `Inspecting` | Inspector is reviewing items | "Being inspected" | Submit inspection results |
| `Completed` | Inspection passed (full or partial refund issued) | "Refund processed" | Terminal — no action |
| `Rejected` | All items failed inspection | "Return rejected" | Terminal — no action |
| `Denied` | CS agent denied the return request | "Return denied" | Terminal — no action |
| `Expired` | Customer never shipped within 30-day window | "Return expired" | Terminal — no action |

---

## 1. Look Up a Return

```
GET /api/returns/{returnId}
```

Returns full details including status, items, timestamps, refund amounts.

### List Returns for an Order

```
GET /api/returns?orderId={orderId}
```

Returns all returns associated with the given order.

---

## 2. Approve a Return (Manual Review)

**When:** Return is in `Requested` state (customer selected "Other" reason).

```
POST /api/returns/{returnId}/approve
Content-Type: application/json

{ "returnId": "{returnId}" }
```

**Result:** Status → `Approved`, ship-by deadline set (30 days), expiration scheduled.

---

## 3. Deny a Return

**When:** Return is in `Requested` state and does not meet return policy.

```
POST /api/returns/{returnId}/deny
Content-Type: application/json

{
  "returnId": "{returnId}",
  "reason": "PolicyViolation",
  "message": "Unfortunately, this item is final sale and cannot be returned."
}
```

### Common Denial Reasons

| Reason Code | When to Use | Example Message |
|-------------|-------------|-----------------|
| `PolicyViolation` | Item is non-returnable (final sale, consumable) | "This item is final sale and cannot be returned." |
| `OutsideReturnWindow` | Manual override when automated check missed | "Your order was delivered more than 30 days ago." |
| `FraudSuspected` | Suspicious return pattern | "We're unable to process this return. Please contact us for assistance." |
| `Other` | Catch-all for edge cases | Provide a clear, empathetic explanation |

**Writing denial messages:** Be empathetic and actionable. The `Message` field is shown directly to the customer. Avoid technical jargon.

---

## 4. Submit Inspection Results

**When:** Return is in `Received` or `Inspecting` state.

```
POST /api/returns/{returnId}/inspection
Content-Type: application/json

{
  "returnId": "{returnId}",
  "results": [
    {
      "sku": "DOG-BOWL-01",
      "quantity": 2,
      "condition": "AsExpected",
      "conditionNotes": "Matches defect report, cracked base",
      "isRestockable": false,
      "disposition": "Dispose",
      "warehouseLocation": null
    },
    {
      "sku": "CAT-TOY-05",
      "quantity": 1,
      "condition": "AsExpected",
      "conditionNotes": "Intact packaging, unused",
      "isRestockable": true,
      "disposition": "Restockable",
      "warehouseLocation": "A-12-3"
    }
  ]
}
```

### Item Condition Values

| Value | Description | Typical Action |
|-------|-------------|----------------|
| `AsExpected` | Item matches what the customer described | Pass — proceed with disposition |
| `BetterThanExpected` | Item is in better condition than reported | Pass — may be sellable as new |
| `WorseThanExpected` | Item is in worse condition than reported (fraud indicator) | **Fail** — reject this item |
| `NotReceived` | Item was supposed to be in the package but wasn't | **Fail** — return to customer |

### Disposition Decision Values

| Value | Description | When to Use |
|-------|-------------|-------------|
| `Restockable` | Item can go back on the shelf | Good condition, original packaging |
| `Dispose` | Item must be discarded | Defective, damaged beyond resale |
| `Quarantine` | Item needs further review (safety concern) | Chemical odor, structural damage |
| `ReturnToCustomer` | Send the item back to the customer | Wrong item, or inspection failed |

### Three-Way Inspection Outcomes

- **All Pass:** Every item passes → `Completed`, full refund
- **All Fail:** Every item fails → `Rejected`, no refund
- **Mixed:** Some pass, some fail → `Completed` with partial refund (only passed items refunded)

### Example: Mixed Inspection (3 items, 2 pass, 1 fail)

```json
{
  "returnId": "...",
  "results": [
    {
      "sku": "DOG-BOWL-01",
      "quantity": 2,
      "condition": "AsExpected",
      "conditionNotes": "Confirmed defective",
      "isRestockable": false,
      "disposition": "Dispose",
      "warehouseLocation": null
    },
    {
      "sku": "CAT-TOY-05",
      "quantity": 1,
      "condition": "AsExpected",
      "conditionNotes": "Unused, intact",
      "isRestockable": true,
      "disposition": "Restockable",
      "warehouseLocation": "A-12-3"
    },
    {
      "sku": "DOG-LEASH-01",
      "quantity": 1,
      "condition": "WorseThanExpected",
      "conditionNotes": "Heavy wear, not as described by customer",
      "isRestockable": false,
      "disposition": "ReturnToCustomer",
      "warehouseLocation": null
    }
  ]
}
```

**Result:** DOG-BOWL-01 and CAT-TOY-05 refunded; DOG-LEASH-01 rejected. Customer sees partial refund.

---

## 5. "Other" Reason Review Checklist

When a return arrives in `Requested` (UnderReview) state with reason "Other":

1. **Read the customer's explanation** (in the return items data)
2. **Check order history** — is this a repeat returner?
3. **Evaluate against policy:**
   - Is the item returnable? (Not final sale, consumable, or hygiene product)
   - Is the explanation reasonable?
   - Is the return within the 30-day window? (Automated check handles this, but verify)
4. **Decision:**
   - **Approve** if policy allows → `POST /api/returns/{id}/approve`
   - **Deny** with empathetic message → `POST /api/returns/{id}/deny`

---

## 6. Escalation Path

| Situation | Action |
|-----------|--------|
| Return stuck in `Approved` for 25+ days | Contact customer about shipping deadline |
| Missing eligibility window (order delivered but no window) | Check if `ShipmentDelivered` event was published by Fulfillment BC |
| Customer disputes rejection | Escalate to CS supervisor (no automated re-open in Phase 2) |
| System error on any endpoint | Check Returns API logs, retry if transient |

---

*This runbook covers Phase 2 capabilities. Exchange workflows and RBAC are planned for Phase 3.*
