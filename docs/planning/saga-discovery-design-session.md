# Saga Discovery & Design Session

> **Date:** 2026-03-29
> **Status:** 📋 Research Complete — Recommendation Ready
> **Participants:** Principal Architect, Product Owner
> **Scope:** Review the existing Orders saga and identify additional bounded contexts that would benefit from a Wolverine saga without forcing contrived examples

---

## Executive Summary

CritterSupply's existing Orders saga remains a strong reference implementation for Wolverine's saga pattern. It demonstrates durable state, message correlation, delayed timeouts, optimistic concurrency, compensation, and the Decider pattern in a realistic order lifecycle.

The review surfaced one important caveat before adding more saga examples: the Orders BC already listens to Returns exchange-related messages, but the `Order` saga does not currently handle those message types. That gap should be addressed before expanding saga coverage, because it affects real exchange/refund behavior rather than documentation polish.

Beyond Orders, the team found:

1. **Strongly accepted candidate:** **Returns BC — Cross-Product Exchange Additional Payment Coordination**
2. **Conditional secondary candidate:** **Vendor Portal — Change Request Review Lifecycle**, but only if the product roadmap explicitly includes SLA/timeout escalation. Without that requirement, the current document-based workflow is sufficient and a saga would be gratuitous.

Rejected candidates were also reviewed so the rationale is clear, not just the conclusions.

---

## 1. Orders BC Saga Review

### Assessment

**Verdict:** Healthy reference implementation, but overdue for a targeted refresh in one area.

### Why it is healthy

The current `Order` saga is still the repository's best Wolverine saga example because it already demonstrates:

- document-backed saga state with Marten
- Wolverine message correlation by `OrderId`
- optimistic concurrency via numeric revisions
- delayed timeout handling (`ReturnWindowExpired`)
- compensation for cancellation, payment failure, and stock failure
- idempotency guards for duplicate or late-arriving messages
- separation of concerns between `PlaceOrderHandler`, `Order`, and `OrderDecider`

That makes it a technically credible and product-recognizable process manager.

### What needs refresh

The saga has a **real exchange-related integration gap**:

- Returns publishes exchange-specific messages to `orders-returns-events`
- Orders listens to that queue
- the `Order` saga has no handlers for the exchange-specific messages now flowing from Returns

This is not just a missing example. It means cross-product exchange scenarios are not fully coordinated through Orders today.

### Recommendation

Do **not** block all future saga work on a full Orders refactor.  
Do **fix the exchange-message gap first**, because it affects the credibility of Orders as the canonical reference.

---

## 2. Accepted Candidate

## Returns BC — Cross-Product Exchange Additional Payment Coordination

### Business process name

**Exchange Upgrade Payment Collection**

### Process description

This workflow covers the case where a customer returns one item but wants a more expensive replacement. The exchange cannot complete until the price difference is collected, and the replacement should not ship until payment succeeds. If payment fails or times out, the system needs a durable, explicit way to cancel or compensate the exchange rather than silently leaving it in limbo.

This is a real retail workflow, not a demo-only variation. Customer service, finance, and warehouse staff would all recognize it as a named operational process.

### Start condition

- `ExchangeAdditionalPaymentRequired`
- Practically, this begins when an approved exchange determines the replacement costs more than the original item

### Key state fields

The saga would need to track at least:

- `ReturnId`
- `OrderId`
- `CustomerId`
- `AmountDue`
- whether additional payment was captured
- whether the exchange has timed out
- current workflow status (awaiting payment, paid, failed, cancelled, completed)

### Terminal states

- **Completed:** additional payment captured and replacement shipment allowed/completed
- **Failed:** payment attempt definitively failed
- **Compensated/Cancelled:** payment never arrived or timed out, so the exchange is cancelled or falls back to a refund-only outcome

### Messages that drive it

Likely core messages:

- starts with `ExchangeAdditionalPaymentRequired`
- advances on payment success / failure messages from Payments
- completes on exchange completion
- cancels or compensates on a scheduled timeout if payment never arrives

### Why a saga is the right fit

A simple handler chain is not enough here because the workflow:

- spans multiple bounded contexts over time
- must wait for an external financial confirmation
- needs durable state between approval and payment confirmation
- needs a timeout path
- needs compensation rather than silent inconsistency

The current alternative is effectively "hope the right next message arrives and no one ships too early." That is exactly the kind of coordination problem sagas are meant to solve.

### Product justification

This is backlog-worthy because it creates observable business state:

- customer service needs to know whether the exchange is waiting on payment
- finance needs to know whether the extra charge succeeded
- warehouse needs to know whether the replacement is cleared to ship
- customers need visible status rather than a vague "under review"

### Owner input still needed

Before implementation, product should decide:

- whether the customer is auto-charged immediately or first explicitly notified/asked to confirm
- what happens when the additional payment fails
- whether a cheaper replacement refund is issued only after inspection/ship completion or earlier

---

## 3. Conditional Secondary Candidate

## Vendor Portal — Change Request Review Lifecycle

### Business process name

**Vendor Product Content Update Review**

### Process description

Vendors submit changes to product descriptions, images, or data corrections. The catalog team reviews the request, may ask for more information, and eventually approves, rejects, or the vendor withdraws the request. This is a real marketplace workflow with operator-visible state and obvious reporting value.

### Start condition

- `SubmitChangeRequest`

### Key state fields

The existing workflow already tracks most of the right state:

- request ID
- vendor tenant
- SKU and change type
- submitted by user
- request status
- review question / additional info loop
- created, submitted, and resolved timestamps

If converted into a saga, it would additionally need durable timeout/escalation state.

### Terminal states

- **Completed:** approved
- **Failed:** rejected
- **Cancelled:** withdrawn
- **Superseded:** replaced by a newer active request

### Messages that drive it

Representative drivers:

- starts on `SubmitChangeRequest`
- advances on `AdditionalInfoRequested`
- re-advances on `ProvideAdditionalInfo`
- completes on approval or rejection messages
- could terminate on a timeout/escalation message if review stalls too long

### Why this is only a conditional candidate

Both the technical and product review agree that this is a real business workflow, but **it only justifies a saga if CritterSupply wants to model review SLA enforcement or timeout escalation**.

Without an overdue-review requirement, the existing document-based workflow is already understandable and sufficient. Adding a saga purely for variety would weaken the reference architecture instead of strengthening it.

### When it becomes a true saga candidate

Accept this as a future saga only if the roadmap includes behavior such as:

- "review must complete within X days"
- "needs-more-info responses expire after X days"
- "stale requests escalate automatically"
- "vendors can see overdue review state"

With those requirements, a saga becomes justified. Without them, it should stay as-is.

---

## 4. Rejected Candidates

## Vendor Identity — Invitation Lifecycle

### Why it was rejected

This is a legitimate business capability, but not a strong saga example.

- It is mostly a single-BC administrative workflow
- expiry is timestamp-based rather than a meaningful multi-step orchestration
- there is no substantive compensation chain
- the observable states are simple (`Pending`, `Accepted`, `Revoked`, `Expired`)

Using a saga here would add ceremony without teaching anything valuable about Wolverine process coordination.

## Standard Returns Refund Workflow

### Why it was rejected

The non-exchange return flow is already well represented as an event-sourced aggregate plus downstream messaging. It has lifecycle state, but it does not need an additional saga of its own because the more complex cross-BC financial coordination already belongs in Orders or in a dedicated exchange-payment process.

## Other bounded contexts briefly considered

- **Promotions / Pricing:** interesting timing and approval concerns exist, but the currently implemented code does not provide a cleaner, more realistic saga candidate than Returns
- **Fulfillment:** multi-warehouse routing is promising, but still more roadmap implication than present-day workflow
- **Correspondence:** retry and delivery tracking matter, but the current message lifecycle does not yet present a better teaching example than the accepted Returns candidate

---

## 5. Recommended Next Actions

1. **Fix the Orders exchange-message gap first** so the canonical saga remains trustworthy.
2. **Prioritize a Returns saga design** for cross-product exchange additional-payment coordination.
3. Decide whether CritterSupply wants a **second, human-review-oriented saga example**:
   - if yes, define SLA/escalation behavior for Vendor Portal change requests
   - if no, keep that workflow document-based
4. Update saga-facing documentation after any implementation so the teaching material stays aligned with code.

---

## 6. Final Recommendation

If CritterSupply adds only **one** more saga, it should be in **Returns** for cross-product exchange payment coordination. It is technically justified, product-meaningful, and exposes saga concepts that Orders alone does not emphasize strongly enough.

If CritterSupply wants **two** saga examples, the second should be **Vendor Portal change request review**, but only after explicitly adding timeout/escalation requirements. Otherwise, it should remain a document workflow and be listed as intentionally non-saga.
