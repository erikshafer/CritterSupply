# Fraud Detection Patterns for Returns BC

**Last Updated:** 2026-03-13
**Status:** Reference Architecture — Patterns for Future Implementation
**Target Audience:** Developers implementing fraud detection features in Cycle 28+

---

## Purpose

This document describes **detection patterns and integration points** for a future Fraud Detection BC that will monitor return behaviors and flag suspicious activity. Returns BC currently has **no fraud detection logic** — this document is forward-looking guidance for when fraud detection becomes a priority.

---

## Core Fraud Vectors

### 1. Serial Returners (Wardrobing / Rental Fraud)

**Pattern:** Customer repeatedly purchases items, uses them briefly, then returns for full refund.

**Detection Signals:**
- High return frequency: >30% of orders returned within rolling 90-day window
- Return timing patterns: Consistent returns 25-29 days after delivery (just before window closes)
- Same item SKUs returned multiple times by same customer
- High-value items with pristine condition returns (suggests minimal use)

**Mitigation:**
- Restocking fee escalation: 0% (first 2 returns) → 10% (returns 3-5) → 25% (6+ returns)
- Account restrictions: Suspend return eligibility after 10 returns in 90 days pending manual review

### 2. Refund Abuse (Item Switching)

**Pattern:** Customer returns a different item than originally purchased (e.g., buys new electronics, returns broken old unit).

**Detection Signals:**
- Serial number mismatch between purchased item and returned item
- Condition discrepancy: "New" item purchased, "Damaged" or "Used" item returned
- Weight mismatch: Returned package weight differs significantly from shipped weight
- Inspection rejection rate: Customer's returns frequently fail inspection

**Mitigation:**
- Enhanced inspection protocols: Photograph serial numbers for high-value items (>$200)
- Return denial for serial number mismatches
- Account flagging: 3+ inspection failures → permanent return restriction

### 3. Chargeback + Return Double-Dipping

**Pattern:** Customer initiates chargeback with credit card company AND files return request, attempting double refund.

**Detection Signals:**
- Active chargeback exists for order (Payments BC publishes `ChargebackInitiated`)
- Return requested for same order within 14 days of chargeback
- Customer has history of chargebacks (external fraud score from payment processor)

**Mitigation:**
- Auto-deny returns for orders with active chargebacks
- Escalate to fraud investigation team
- Share chargeback patterns with Payments BC for merchant dispute evidence

### 4. Friendly Fraud (False "Never Arrived" Claims)

**Pattern:** Customer claims shipment never arrived, requests refund, but carrier shows "Delivered" status.

**Detection Signals:**
- Return reason: "Never received" but Fulfillment BC shows `ShipmentDelivered` event
- Signature on delivery confirmation (high-value shipments)
- Same delivery address has multiple "never arrived" claims across different customers (package theft ring)

**Mitigation:**
- Require photo proof of delivery for high-value items
- Deny return if carrier shows "delivered" + signature
- Recommend customer file police report for stolen package (insurance claim)

### 5. Return Fraud Rings (Coordinated Abuse)

**Pattern:** Multiple accounts with shared attributes (same address, same payment method, same IP) all exhibit high return rates.

**Detection Signals:**
- Shared shipping address across 5+ accounts with >50% return rate
- Shared payment instrument (last 4 digits + issuer)
- Same IP address at checkout time across multiple accounts
- Coordinated timing: All accounts place orders within 24 hours, return within 48 hours

**Mitigation:**
- Graph-based account linking (Neo4j / entity resolution)
- Block return eligibility for all linked accounts if fraud score exceeds threshold
- Escalate to law enforcement if monetary threshold exceeded ($10K+ coordinated theft)

---

## Proposed Fraud Detection BC Architecture

### Event Subscriptions

Fraud Detection BC would subscribe to these integration messages:

| Event | Publisher | Purpose |
|-------|-----------|---------|
| `ReturnRequested` | Returns BC | Trigger fraud score calculation at return initiation |
| `ReturnReceived` | Returns BC | Compare physical item attributes (weight, serial number) |
| `ReturnCompleted` | Returns BC | Track refund amounts and customer lifetime return value |
| `ReturnRejected` | Returns BC | Increment customer inspection failure counter |
| `OrderPlaced` | Orders BC | Track order frequency and high-value purchases |
| `ShipmentDelivered` | Fulfillment BC | Cross-reference delivery confirmation with "never arrived" claims |
| `ChargebackInitiated` | Payments BC | Flag chargeback + return double-dip attempts |
| `RefundIssued` | Payments BC | Track lifetime refund amounts per customer |

### Published Events

| Event | Subscribers | Purpose |
|-------|-------------|---------|
| `FraudScoreCalculated` | Returns BC, Orders BC | Real-time fraud score for active return/order |
| `AccountFlagged` | All BCs | Customer banned from returns/orders due to fraud |
| `FraudInvestigationOpened` | Returns BC, Customer Experience BC | Manual review required before return approval |

### Fraud Scoring Model

**Simple Rule-Based Model (MVP):**

```csharp
public sealed record FraudScore
{
    public Guid CustomerId { get; init; }
    public int Score { get; init; } // 0-100 (0 = safe, 100 = definite fraud)
    public IReadOnlyList<string> Flags { get; init; } // ["HIGH_RETURN_RATE", "SERIAL_NUMBER_MISMATCH"]
    public FraudRiskLevel RiskLevel => Score switch
    {
        < 30 => FraudRiskLevel.Low,
        < 60 => FraudRiskLevel.Medium,
        < 85 => FraudRiskLevel.High,
        _ => FraudRiskLevel.Critical
    };
}

public enum FraudRiskLevel
{
    Low,    // Auto-approve returns
    Medium, // Apply higher restocking fees
    High,   // Manual review required
    Critical // Auto-deny + escalate to fraud team
}
```

**Scoring Factors:**

| Factor | Weight | Condition |
|--------|--------|-----------|
| Return frequency | +25 | >30% of orders returned in 90 days |
| Inspection failures | +20 | 3+ rejected returns |
| Chargeback history | +30 | Active chargeback OR 2+ historical chargebacks |
| Serial number mismatch | +40 | Returned item serial ≠ purchased item serial |
| Account age | -10 | Account >2 years old with clean history |
| High lifetime value | -15 | Spent >$5K, <10% return rate historically |

**Machine Learning (Future):**
- Train gradient boosting model (XGBoost) on historical return outcomes
- Features: Customer tenure, order frequency, return timing patterns, product categories, dollar amounts
- Label: Binary classification (fraud / not fraud) based on manual investigation outcomes
- Update model quarterly with new fraud investigation data

---

## Integration Points with Returns BC

### 1. Pre-Approval Fraud Check

**Location:** `POST /api/returns/request` endpoint
**Implementation:**

```csharp
public static class RequestReturnEndpoint
{
    public static async Task<IResult> Handle(
        RequestReturn command,
        IFraudDetectionClient fraudClient, // HTTP client to Fraud Detection BC
        IDocumentSession session,
        CancellationToken ct)
    {
        // ... load eligibility window ...

        // NEW: Query fraud score before approving
        var fraudScore = await fraudClient.GetFraudScoreAsync(command.CustomerId, ct);

        if (fraudScore.RiskLevel == FraudRiskLevel.Critical)
        {
            return Results.BadRequest(new
            {
                Error = "ReturnDenied",
                Reason = "Your account has been flagged for suspicious activity. Please contact support."
            });
        }

        if (fraudScore.RiskLevel == FraudRiskLevel.High)
        {
            // Auto-deny OR queue for manual review (depends on policy)
            // For now: Apply higher restocking fee as deterrent
            var restockingFee = command.EstimatedRefundAmount * 0.25m; // 25% fee
            // ... continue with elevated fee ...
        }

        // ... proceed with normal return request logic ...
    }
}
```

### 2. Post-Inspection Verification

**Location:** `CompleteReturnHandler` (admin action)
**Implementation:**

```csharp
public static class CompleteReturnHandler
{
    public static async Task<IResult> Handle(
        CompleteReturn command,
        IFraudDetectionClient fraudClient,
        IDocumentSession session,
        CancellationToken ct)
    {
        // ... load return aggregate ...

        // NEW: Report inspection outcome to Fraud Detection BC
        await fraudClient.ReportInspectionOutcomeAsync(new InspectionOutcome(
            ReturnId: command.ReturnId,
            CustomerId: returnAggregate.CustomerId,
            InspectionPassed: true,
            SerialNumberMatch: command.Items.All(i => i.SerialNumberMatches),
            ConditionMatch: command.Items.All(i => i.ConditionMatch)
        ), ct);

        // ... proceed with refund issuance ...
    }
}
```

### 3. Real-Time Fraud Alerting

**Location:** Wolverine integration message handler
**Implementation:**

```csharp
public static class AccountFlaggedHandler
{
    public static async Task Handle(
        Messages.Contracts.FraudDetection.AccountFlagged message,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Load all active returns for this customer
        var activeReturns = await session.Query<Return>()
            .Where(r => r.CustomerId == message.CustomerId)
            .Where(r => r.Status == ReturnStatus.Approved || r.Status == ReturnStatus.Received)
            .ToListAsync(ct);

        // Auto-deny all active returns
        foreach (var returnAggregate in activeReturns)
        {
            returnAggregate.Deny("AccountFlagged", $"Your account has been suspended due to {message.Reason}");
        }

        // Persist changes
        foreach (var returnAggregate in activeReturns)
        {
            session.Store(returnAggregate);
        }
    }
}
```

---

## Monitoring & Metrics

**Key Metrics to Track:**

1. **Return Fraud Rate:** `(Confirmed Fraud Returns / Total Returns) * 100`
   - Target: <2%
   - Alert if exceeds 5%

2. **False Positive Rate:** `(Legitimate Returns Flagged / Total Flagged Returns) * 100`
   - Target: <10%
   - Monitor customer complaints about wrongful denials

3. **Average Investigation Time:** Time from `FraudInvestigationOpened` to resolution
   - Target: <48 hours
   - SLA breach if >72 hours

4. **Fraud Loss Prevention:** Dollar amount saved by denying fraudulent returns
   - Calculate: `SUM(denied_return_refund_amounts WHERE fraud_confirmed = true)`

5. **Customer Impact:** Legitimate customers incorrectly denied
   - Track complaints to customer support about fraud flags
   - Immediate escalation path for false positives

**Dashboard Alerts:**

- Spike in fraud score distribution (>15% of returns flagged as High/Critical)
- New fraud pattern detected (e.g., sudden surge in serial number mismatches)
- Fraud ring identified (5+ linked accounts with coordinated behavior)

---

## Privacy & Compliance Considerations

### GDPR / CCPA Compliance

- **Data Minimization:** Only collect fraud signals necessary for detection (no excessive surveillance)
- **Right to Explanation:** Customers can request explanation of why return was denied
- **Data Retention:** Fraud scores archived after 3 years (or per regulatory requirement)
- **Transparency:** Privacy policy must disclose fraud detection practices

### Anti-Bias Safeguards

- **No demographic profiling:** Fraud scoring must not use protected characteristics (race, gender, location as proxy for income)
- **Regular audits:** Quarterly review of false positive rates by customer segment
- **Appeal process:** Customers can dispute fraud flags via customer support escalation

### Legal Coordination

- **Law enforcement:** Share fraud evidence with police only when threshold exceeded ($10K+ organized fraud ring)
- **Merchant rights:** Document fraud patterns for chargeback dispute evidence
- **Terms of Service:** Update ToS to explicitly prohibit serial returns and wardrobing

---

## References

- CONTEXTS.md — Returns BC integration contracts
- Cycle 27 Retrospective — Returns BC Phase 3 lessons learned
- Industry Fraud Reports:
  - National Retail Federation: "Return Fraud & Abuse Survey" (2024)
  - Appriss Retail: "Consumer Returns in the Retail Industry" (2024 report)

---

**Next Steps (Future Cycles):**

1. **Cycle 28:** Implement basic rule-based fraud scoring in new Fraud Detection BC
2. **Cycle 29:** Integrate Returns BC with Fraud Detection via HTTP client + integration messages
3. **Cycle 30:** Build fraud investigation dashboard for customer support team
4. **Cycle 31+:** Train ML model on historical fraud investigation outcomes
