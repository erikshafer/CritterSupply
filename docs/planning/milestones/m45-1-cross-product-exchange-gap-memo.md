# M45.1 — Cross-Product Exchange End-to-End Gap Memo

> **Status:** Charter input — partially implemented (Returns side complete; cross-BC choreography missing)
> **Date:** 2026-05-08
> **Author:** Principal Architect (M45.1 cycle)
> **Source:** PO sign-off in `docs/research/state-of-repo-2026-05.md` §7.1 / §7.3, item S3
> **Audience:** Whoever scopes the **Returns remaster** (and, by coupling, the **Orders remaster** + a small **Inventory** + **Payments** slice)

## Purpose

The PO asked us to **verify the atomic "approve exchange + reserve replacement +
capture delta" path actually exists vs. is feature-file-only**. The verification
result is documented here so the next planning session does not re-derive it.
This memo does **not** propose an implementation; it documents the gap and a
recommended landing.

## Findings

The audit conclusion is unambiguous: **the Returns side of cross-product
exchange is implemented. The cross-BC choreography that makes it functionally
end-to-end is feature-file-only.**

### What exists (Returns BC, fully wired)

| Concern                                                                         | State                                                                                                                            |
| ------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| `ApproveExchange` command + handler                                             | ✅ `src/Returns/Returns/ReturnProcessing/ApproveExchange.cs`                                                                       |
| Domain events: `ExchangeApproved`, `CrossProductExchangeRequested`, `ExchangePriceDifferenceCalculated`, `ExchangeAdditionalPaymentRequired`, `ExchangeReplacementShipped`, `ExchangeCompleted` | ✅ All defined in `src/Returns/Returns/ReturnProcessing/ReturnEvents.cs` and applied by the `Return` aggregate                     |
| Replacement-ship handler with completion                                        | ✅ `src/Returns/Returns/ReturnProcessing/ShipReplacementItem.cs`                                                                   |
| Integration messages in `Messages.Contracts/Returns/`                           | ✅ `CrossProductExchangeRequested`, `ExchangeAdditionalPaymentRequired`, `ExchangeAdditionalPaymentCaptured`, `ExchangePartialRefundIssued`, `ExchangeApproved`, `ExchangeReplacementShipped`, `ExchangeCompleted` |
| `PublishMessage<>()` registrations to `orders-returns-events` and `storefront-returns-events` | ✅ `src/Returns/Returns.Api/Program.cs` lines 117–143 (4 cross-product messages × 2 queues = 8 routes)                            |
| Gherkin coverage                                                                | ✅ `docs/features/returns/cross-product-exchange.feature` — 9 scenarios (happy path × 3 price-difference branches, denial × 2, inspection-failure × 2, edge × 2) |
| Returns-side unit + integration tests                                           | ✅ `tests/Returns/Returns.UnitTests/ExchangeWorkflowTests.cs`, `tests/Returns/Returns.Api.IntegrationTests/ExchangeWorkflowEndpointTests.cs` |

### What is missing (cross-BC choreography)

| Concern                                                                          | State                                                                                                                                                                                                                                                                  |
| -------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Inventory reservation for the replacement SKU**                                | ❌ `grep -rn "ReplacementReserv\|ReserveReplacement" src/` returns zero results. The `cross-product-exchange.feature` scenarios "And the replacement is in stock" / "And the replacement item is out of stock" have no enforcement path — Returns approves blind.        |
| **Orders saga consumes `CrossProductExchangeRequested`**                         | ❌ Orders subscribes to `orders-returns-events` (`Orders.Api/Program.cs:122`) and the saga handles `ReturnRequested` / `ReturnCompleted` / `ReturnDenied` (`Order.cs` lines 493 / 510 / 544) — **but no handler for `CrossProductExchangeRequested`, `ExchangeAdditionalPaymentRequired`, `ExchangeAdditionalPaymentCaptured`, or `ExchangePartialRefundIssued`**. Wolverine will log "no handler" on every message. |
| **Payments BC captures the additional payment delta**                            | ❌ No subscriber to `ExchangeAdditionalPaymentRequired` anywhere in `src/Payments/`. The integration message is published but consumed by no one — the "And the additional payment is captured" Gherkin step has no implementing code path.                              |
| **`ExchangeAdditionalPaymentCaptured` emitter**                                  | ❌ Defined as an integration contract and registered for publication, but `grep -rn "new.*ExchangeAdditionalPaymentCaptured" src/` returns zero results outside the contract itself. Nothing emits it; nothing transitions the `Return` aggregate based on it.            |
| **`ExchangePartialRefundIssued` emitter**                                        | ❌ Same — defined and routed to publish, but never constructed. The "And a $20.00 partial refund is issued to the original payment method" Gherkin step is unimplemented.                                                                                               |
| **Compensation when "Additional payment capture fails — exchange cancelled"**    | ❌ The Gherkin scenario exists (`cross-product-exchange.feature` line ~99–103) but there is no `ExchangeCancelled` (or equivalent) command/event for the payment-failure compensation path. `ApproveExchangeHandler` schedules `ExpireReturn` but treats the additional-payment failure as out of band. |
| **Refund of the additional payment when inspection fails**                       | ❌ "And the $25.00 additional payment is refunded to the customer" Gherkin step has no implementing handler. `SubmitInspection` rejects the return on failure but does not orchestrate any cross-BC payment reversal.                                                  |
| **End-to-end integration test exercising Returns → Orders saga → Payments → Inventory** | ❌ The Returns integration tests stop at the Returns BC boundary. There is no Alba scenario that asserts the saga progressed, the additional payment was captured, or the replacement reservation was created.                                                          |

### Customer-visible impact

The PO flagged exchange flows as a top-line capability. In production:

- A customer approved for a "more expensive" exchange would see the Storefront UI
  acknowledge `ExchangeAdditionalPaymentRequired` (the message reaches Storefront
  via the publish to `storefront-returns-events`) but **never be charged** —
  there is no handler to invoke the Payments BC.
- A customer approved for a "cheaper" exchange would never receive their partial
  refund — `ExchangePartialRefundIssued` is never emitted.
- The replacement ship from `ShipReplacementItemHandler` would write
  `ExchangeReplacementShipped`, but the warehouse has **no formal stock
  reservation** for the replacement SKU. Whatever is on the shelf at the time of
  shipping is whatever is on the shelf — no eventing-driven hold.

In a fine-tuning cycle the right framing is: **the Gherkin defines the
contract, Returns owns the orchestration of its own state, and the cross-BC
hand-offs that make it real are unimplemented**.

## Why this was deferred from M45.1

This work spans three additional bounded contexts (Orders saga, Payments,
Inventory). A minimum-viable end-to-end implementation would touch:

1. **Inventory** — a `ReserveReplacementForExchange` command (or extend the
   existing reservation API) keyed by `ReturnId`, with a `ReplacementReserved`
   integration event back. Decide whether the existing `ProductInventory`
   reservation flow is reused or whether exchange holds are a separate path
   (Inventory remaster S2 / S3 introduced first-class reservation lifecycle —
   the natural answer is to reuse it, with a different `ReservationKind`).
2. **Orders saga** — handlers for `CrossProductExchangeRequested` (issue the
   Inventory reservation), `ExchangeAdditionalPaymentRequired` (issue a
   `CapturePayment` to Payments), `ExchangeAdditionalPaymentCaptured` /
   `ExchangePartialRefundIssued` (forward to Returns to advance the aggregate).
   New saga state for "exchange in flight" so the Order doesn't terminate too
   early.
3. **Payments** — a handler for `ExchangeAdditionalPaymentRequired` (capture
   delta + emit `ExchangeAdditionalPaymentCaptured`), and a handler for
   exchange-rejection (refund the captured delta). Consider whether a
   dedicated `ExchangePayment` aggregate is warranted vs. piggybacking on the
   existing Payment aggregate.
4. **Returns** — a `RecordAdditionalPaymentCaptured` and a
   `RecordPartialRefundIssued` command pair so the `Return` aggregate's status
   transitions are driven by Payments callbacks rather than implicit.
5. A new `ExchangeCancelled` (or `ExchangeAborted`) event + command pair to
   handle the `cross-product-exchange.feature` scenario "Additional payment
   capture fails — exchange cancelled".
6. End-to-end Alba integration test crossing all four BCs (which means the
   integration test fixture for Returns needs Orders, Payments, and Inventory
   stubs — or this becomes a Reqnroll BDD-driven scenario across the
   `cross-product-exchange.feature`).

That is a **multi-session arc**, not a single fix.

## Recommended placement

This work most naturally lives in:

1. **A Returns remaster** *(if/when Returns is selected for remastering)*. The
   PO's stated preference per §7.1 is to do this first because it carries the
   most coupling debt. Cross-product exchange end-to-end is the highest-leverage
   slice of any Returns remaster — it is what closes the gap between "we have a
   Returns BC" and "we can support exchange operations".
2. **A coordinated Returns + Orders mini-cycle** (without a full remaster). The
   missing pieces are mechanical wiring more than design — the Returns side
   already encodes the decisions (price difference, payment requirement,
   partial refund), and what is needed is the cross-BC choreography. This is
   the smallest-scope option, but it constrains the Orders remaster's choices
   on saga state shape.
3. **As pre-work for the Returns remaster charter** — fix the Inventory
   reservation gap first (so the warehouse actually has stock when
   `ShipReplacementItem` is called), then layer the Payments / Orders saga
   wiring as part of the remaster. This is the lowest-risk staging.

## Acceptance criteria for "complete"

When this gets picked up, "done" means:

- For each of the 9 scenarios in `cross-product-exchange.feature`, there is a
  passing Reqnroll step definition or equivalent integration test.
- `ExchangeAdditionalPaymentCaptured` and `ExchangePartialRefundIssued` are
  emitted by Payments (not just defined as contracts).
- Inventory reserves the replacement SKU before `ApproveExchange` succeeds —
  if no stock, the exchange is `ExchangeDenied` with reason
  "Replacement out of stock".
- The Orders saga has explicit state for "exchange in flight" so order
  closure cannot race the exchange.
- An `ExchangeCancelled` (or equivalent) compensation event handles the
  payment-capture-failure and inspection-failure-after-additional-payment
  scenarios.
- The four "no handler" log entries from Orders disappear (Wolverine confirms
  routing).

## Out of scope for this memo

- Whether to use a saga in Returns to orchestrate the cross-BC calls vs. let
  Orders own the orchestration. Both are defensible; the choice belongs to the
  remaster ADR.
- Whether Payments grows an `ExchangePayment` aggregate or reuses the existing
  `Payment` aggregate with a `PaymentKind` discriminator.
- Whether the replacement-SKU reservation reuses the standard
  `ProductInventory` flow or grows a new `ExchangeHold` reservation kind.
- The customer experience for "your exchange has been approved, awaiting your
  shipment" notifications — that is Correspondence work that should follow
  whichever path is chosen here.

## Out of scope for this memo *(but worth flagging)*

The Returns side already publishes 4 cross-product exchange messages to
`storefront-returns-events`. The Storefront BC's notification-handler set
(audited in M45.0 / S1) does not appear to surface these — when this work is
picked up, expect a parallel small UXE pass on `OrderConfirmation.razor` (or
the Returns detail page) so the customer sees the additional-payment-required
prompt and the partial-refund confirmation.

---

*End of memo.*
