# M45.1 — PO/UXE Next-3 Audits + Real Fixes Retrospective

> **Status:** ✅ Complete
> **Date:** 2026-05-08
> **Author:** Principal Architect
> **PR:** `copilot/address-next-three-issues`
> **Source:** `docs/research/state-of-repo-2026-05.md` §7.3 — synthesis of PO + UXE feedback
> **Predecessor:** [`m45-0-po-uxe-top3-retrospective.md`](./m45-0-po-uxe-top3-retrospective.md)

## TL;DR (revised)

This cycle began as audit-only — three gap memos and no code, mirroring M45.0's
treatment of S6 (abandoned cart). The PO pushed back: **"I was hoping we would
not just identify and audit the next 3 issues, but we would *address* them.
Meaning, fix, implementation, testing, etc."** That feedback was correct. The
M45.0 precedent for S1/S2 was real code (43 + 22 tests, 9 production files),
and S6 was the exception, not the rule.

The cycle pivoted. The three gap memos remain as charter input for the deeper
remaster work, but each item now also has **shipped, tested production code**
for the slice that is tractable inside a single fine-tuning cycle:

| #  | Item                                                  | Memo                                                              | Real fix shipped                                                                                                                                                                                             |
| -- | ----------------------------------------------------- | ----------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| S3 | Cross-product exchange end-to-end                     | `m45-1-cross-product-exchange-gap-memo.md`                        | ✅ `ShipReplacementItemHandler` now emits `ExchangePartialRefundIssued` (closes "contract published, never constructed"). Orders saga gains 4 cross-product handlers; partial-refund handler forwards `RefundRequested` to Payments. **+11 unit tests.** |
| S4 | Order post-placement modifications                    | `m45-1-order-post-placement-modifications-gap-memo.md`            | ✅ `ChangeShippingAddress` vertical slice for the pre-handoff window: command, validator, integration event, decider functions, saga handler, HTTP endpoint, Wolverine routing, **`docs/features/orders/order-modifications.feature`**, **+13 unit tests**. |
| S5 | Fraud-review / OnHold saga state                      | `m45-1-fraud-review-onhold-gap-memo.md`                           | ✅ Option A skeleton: `PutOrderOnHold` / `ReleaseOrderFromHold` / `RejectOrderForFraud` commands + validators, three integration events, decider functions, saga handlers, Wolverine routing. **+19 unit tests.** Closes the "misleading enum" anti-pattern (`OrderStatus.OnHold` no longer dead). |

**Build:** 0 errors. **Tests:** Returns.UnitTests **71 / 71** (+5);
Orders.UnitTests **214 / 214** (+70). **Total new tests: 75.**

### What is still deferred to the remaster

The audit memos remain authoritative for the **multi-BC orchestration** that is
out of scope for a fine-tuning cycle:

- **S3** — Inventory replacement-SKU reservation, Payments delta capture
  (the source of `ExchangeAdditionalPaymentCaptured`), end-to-end Alba
  integration test crossing Returns → Orders → Payments → Inventory. Today the
  cross-product handlers in Orders are minimal acknowledgers (no-ops) for the
  three messages where the upstream emitter or downstream coordination does not
  yet exist; the partial-refund handler is **fully wired** because both halves
  of that path now exist in this cycle.
- **S4** — Line cancel, quantity change, post-handoff recall / re-pick. The
  address-change slice is the cleanest pre-handoff change and exercises the
  full command/event/decider/saga/endpoint/feature/test stack as a template
  for the remaining modifications.
- **S5** — The actual triggering surface (rules engine, fraud-scoring service,
  Backoffice review-queue UI) and the product decision among Options A / B /
  C / D. The commands and saga handlers shipped today are **forward-compatible
  with all four options** — they are usable via the message bus regardless of
  which trigger lands.

## Why the pivot was right

The audit-only approach mistook the M45.0 / S6 exception for the rule. The
actual M45.0 pattern was:

- **S1, S2:** Real code (43 + 22 tests).
- **S6:** Audit only — because the abandoned-cart slice genuinely requires a
  product decision (cart TTL, recovery email cadence, anonymous-vs-authenticated
  scope) before any production code.

S5 is the only one of S3 / S4 / S5 that has the same "needs upstream decision"
shape — and even there, the saga state machine is decision-independent. S3 and
S4 each had clean, single-session vertical slices hiding inside the broader
audit. The pivot identified and shipped them.

## What shipped — by item

### S3 — Cross-product exchange (real fix)

**Production code:**
- `src/Returns/Returns/ReturnProcessing/ShipReplacementItem.cs` — when the
  cross-product exchange completes with a positive `PriceDifference` (cheaper
  replacement), now appends `ExchangePartialRefundIssued` as both a domain
  event on the Return stream and as the `Messages.Contracts.Returns.ExchangePartialRefundIssued`
  integration message. Closes the audit's "registered for publication but
  never constructed" anti-pattern.
- `src/Orders/Orders/Placement/Order.cs` — adds saga handlers for
  `CrossProductExchangeRequested`, `ExchangeAdditionalPaymentRequired`,
  `ExchangeAdditionalPaymentCaptured`, `ExchangePartialRefundIssued`. The
  first tracks the exchange in `ActiveReturnIds`. The middle two are
  intentional no-op acknowledgers (stop "no handler" log noise). The fourth
  forwards a `RefundRequested` to Payments BC so the customer actually
  receives the partial refund — closing the most damaging gap end-to-end.

**Tests:**
- `tests/Returns/Returns.UnitTests/ShipReplacementItemHandlerTests.cs` — 5 tests
  covering domain event emission, integration message emission, both negative
  cases (same-price and more-expensive replacement), and a regression guard for
  the unchanged emissions.
- `tests/Orders/Orders.UnitTests/Placement/OrderSagaCrossProductExchangeTests.cs` — 6 tests
  covering all four handlers, including the partial-refund forwarding behavior
  and the zero-amount defensive guard.

### S4 — Order post-placement modifications (real implementation: address change vertical slice)

**Production code:**
- `src/Shared/Messages.Contracts/Orders/ShippingAddressChanged.cs` — new integration event.
- `src/Orders/Orders/Placement/ChangeShippingAddress.cs` — command + FluentValidation validator.
- `src/Orders/Orders/Placement/OrderDecider.cs` — `CanChangeShippingAddress`
  (eligibility window: `Placed`, `PendingPayment`, `PaymentConfirmed`,
  `InventoryReserved`, `OnHold`) and `HandleChangeShippingAddress` (returns
  empty decision when ineligible — saga is idempotent under at-least-once
  delivery).
- `src/Orders/Orders/Placement/Order.cs` — `Handle(ChangeShippingAddress)`
  saga handler that mutates `ShippingAddress` in place and emits the integration event.
- `src/Orders/Orders.Api/Placement/ChangeShippingAddressEndpoint.cs` — HTTP
  endpoint at `POST /api/orders/{orderId}/shipping-address` with pre-flight
  eligibility validation (returns 409 with a clear message after warehouse hand-off).
- `src/Orders/Orders.Api/Program.cs` — Wolverine routing for `ShippingAddressChanged`
  to `fulfillment-requests` (re-route) and `storefront-notifications` (customer notification).

**Documentation:**
- `docs/features/orders/order-modifications.feature` — **new directory** with
  9 Gherkin scenarios covering happy path × 5 eligible statuses, denial × 7
  post-handoff statuses, denial for cancelled order, validation (missing
  reason, missing street), unknown order, and idempotency under at-least-once
  delivery.

**Tests:**
- `tests/Orders/Orders.UnitTests/Placement/OrderDeciderShippingAddressChangeTests.cs` — 13 tests
  covering eligibility for all 16 `OrderStatus` values, the decider happy path,
  decider non-mutation when ineligible, integration message contents, and saga-level
  mutation + idempotency.

### S5 — Fraud-review / OnHold (real implementation: Option A skeleton)

**Production code:**
- `src/Orders/Orders/Placement/FraudReviewCommands.cs` — three commands with
  validators: `PutOrderOnHold(OrderId, Reason, ReviewerId)`,
  `ReleaseOrderFromHold(OrderId, ReviewerId, ReleaseNotes?)`,
  `RejectOrderForFraud(OrderId, Reason, ReviewerId)`.
- `src/Shared/Messages.Contracts/Orders/FraudReviewEvents.cs` — three integration
  events (`OrderPutOnHold`, `OrderReleasedFromHold`, `OrderRejectedForFraud`).
- `src/Orders/Orders/Placement/OrderDecider.cs` — three eligibility predicates
  (`CanBePutOnHold`, `CanBeReleasedFromHold`, `CanBeRejectedForFraud`) and
  three decision functions. Fraud rejection reuses the cancellation
  compensation path (release inventory + refund captured payment) and emits
  **both** `OrderRejectedForFraud` (for Backoffice account-flagging and
  Customer Experience messaging) **and** the standard `OrderCancelled` (so
  downstream BCs react via the existing choreography without a parallel
  implementation).
- `src/Orders/Orders/Placement/Order.cs` — three saga handlers; fraud rejection
  closes the saga immediately when no payment was captured (mirrors `Handle(CancelOrder)`).
- `src/Orders/Orders.Api/Program.cs` — Wolverine routing for all three integration events.

**Tests:**
- `tests/Orders/Orders.UnitTests/Placement/OrderDeciderFraudReviewTests.cs` — 19 tests
  covering all three eligibility predicates × all 16 `OrderStatus` values,
  all three decision functions (happy path + ineligible + idempotency),
  fraud-rejection compensation (inventory release × N reservations, refund only
  when payment captured, both events emitted), and saga-level integration
  (status mutation + saga closure semantics).

**Closes the "misleading enum" anti-pattern.** Before this cycle, `OrderStatus.OnHold`
existed in the enum and was treated as cancellable in tests, but no command
transitioned the saga into it. Customer Service had no programmatic surface to
hold an order. After this cycle, the saga correctly transitions through
`OnHold` and the enum value is genuinely live.

**Triggering surface remains pluggable.** Per the gap memo, the actual
triggering mechanism (rules engine, fraud-scoring service, Backoffice
review-queue UI) requires a product decision among Options A / B / C / D and
remains out of scope for this fine-tuning cycle. The shipped commands are
usable via the message bus today, so a future Backoffice handler or fraud
service can issue them without further saga-side changes.

---

## Original audit-only narrative (preserved for context)



## Context

The user's directive: "PR 548 addressed the top 3 issues. I now want us to
focus on the next 3 issues."

PR 548 deliberately **filtered out** S3 / S4 / S5 from M45.0 with the
following note (from `m45-0-po-uxe-top3-retrospective.md`):

> Items that would require a multi-session remaster (S3 cross-product
> exchange, S4 post-placement modifications, S5 fraud-review saga state, S7
> Store Credit BC) were filtered out — those belong in the Orders or Returns
> remaster charters per PO §7.1.

M45.1 honors that filter. The right deliverable for "charter input that
takes a multi-session arc to implement" is **not** to start the multi-session
arc inside a fine-tuning cycle — it is to **audit the gap precisely** so the
session that picks the work up does not need to re-derive the findings. This
is the exact pattern M45.0 used for S6, which is now `m45-0-abandoned-cart-
gap-memo.md` and is referenced as carryover item #1 of M45.0.

## Why audit-only is the right answer here

Three considerations:

1. **Scope.** Each of S3 / S4 / S5 individually requires multi-BC
   coordination. S3 alone would touch Returns + Orders + Payments +
   Inventory and span a Returns remaster. A fine-tuning cycle that tries to
   "start" any of these would either deliver a half-implementation (the
   anti-pattern that produced today's `OrderStatus.OnHold` placeholder) or
   would silently expand into a remaster.
2. **Upstream decisions.** S5 explicitly requires a **product decision** (A
   / B / C / D — see the gap memo) before any implementation. Implementing
   without that decision is premature.
3. **Pattern continuity.** M45.0 set the precedent — PO-flagged charter
   input → gap memo + retrospective → carryover into the relevant remaster
   charter. Repeating that shape here is the most legible thing for future
   planners.

## What shipped

### S3 — Cross-product exchange end-to-end audit

**Verification result.** The Returns side of cross-product exchange is
**fully implemented** — `ApproveExchange` correctly emits the discriminator
events, the integration messages are defined, and they are routed to
`orders-returns-events` and `storefront-returns-events`. The 9-scenario
`docs/features/returns/cross-product-exchange.feature` is correct.

**The gap.** The cross-BC choreography is missing:

- **Inventory** does not reserve the replacement SKU. The "And the
  replacement is in stock" Gherkin step has no enforcement.
- **Orders saga** subscribes to `orders-returns-events` but has handlers
  for only `ReturnRequested` / `ReturnCompleted` / `ReturnDenied`. The four
  cross-product exchange messages (`CrossProductExchangeRequested`,
  `ExchangeAdditionalPaymentRequired`,
  `ExchangeAdditionalPaymentCaptured`, `ExchangePartialRefundIssued`) are
  delivered but not handled — Wolverine logs "no handler" on every one.
- **Payments** does not capture the additional payment delta. Nothing
  emits `ExchangeAdditionalPaymentCaptured`; nothing emits
  `ExchangePartialRefundIssued`.
- No **end-to-end Alba integration test** that crosses Returns → Orders →
  Payments → Inventory.

**Customer-visible impact.** A customer approved for a "more expensive"
exchange would see Storefront acknowledge the request but never be charged.
A customer approved for a "cheaper" exchange would never receive the partial
refund. The replacement is shipped from a warehouse with no formal stock
reservation. The PO's framing — "verify the atomic 'approve exchange +
reserve replacement + capture delta' path actually exists vs. is feature-
file-only" — is exactly right: it is feature-file-only.

**Deliverable.** `m45-1-cross-product-exchange-gap-memo.md` —
recommends landing this either in a Returns remaster or in a coordinated
Returns + Orders mini-cycle. Lists acceptance criteria.

### S4 — Order post-placement modifications audit

**Verification result.** Address change, line cancel, and quantity change
are **completely unimplemented**. There are no commands, no events, no HTTP
endpoints, no saga handlers, and no Backoffice tooling. The Orders API
exposes whole-order cancel only.

**The most striking finding.** **`docs/features/orders/` does not exist.**
Of the 13 implemented BCs, Orders is the only placement-and-fulfillment-
critical BC without a Gherkin feature directory. There is no behavioral
documentation of the Orders BC at all — neither for placement, nor
cancellation, nor (obviously) modification.

**Deliverable.** `m45-1-order-post-placement-modifications-gap-memo.md` —
recommends landing this in the Orders remaster. Lists acceptance criteria,
including the prerequisite `docs/features/orders/` directory and an ADR
documenting the post-placement-eligibility matrix.

**Carryover (this retrospective).** A standalone documentation-hygiene PR
to file Gherkin for the **already-implemented** Orders behavior (placement,
cancellation, lifecycle) before the remaster lands. This is a
low-risk slice that closes the most striking finding of the audit
independently of the remaster.

### S5 — Fraud-review / OnHold saga state audit

**Verification result.** `OrderStatus.OnHold` exists as an enum value with
a comment "Flagged for fraud review or inventory issues." It is included in
`OrderDeciderCancellationTests.CanBeCancelled_*` as a "cancellable" status.
**Nothing else attaches to it.** There is no command that puts an order on
hold, no event that records the hold, no saga handler that transitions to
or from it, and no fraud-scoring service or rules engine anywhere in the
codebase. Today's "fraud handling" is a CS agent reading a payment-failure
alert and using `Reason: "Fraud detected"` as a free-text cancellation
reason.

**The README already self-flags this** — `src/Orders/Orders.Api/README.md`
lines 376–386 enumerate four design options (A: Manual review queue / B:
Internal rules engine / C: External fraud scoring / D: Status quo) and
line 430 lists the gap as "Future." This memo elevates the README footnote
to the planning surface so the next planning session weighs it
deliberately.

**Anti-pattern.** The combination of "enum value present + tested as
cancellable + no transitions" is **worse than no enum value at all** because
it implies coverage that does not exist. The Promotions BC has the equivalent
pattern done correctly: `RevokeCoupon` command + `CouponRevoked` event +
`CouponStatus.Revoked` terminal state with real reducer behavior. The Orders
BC's `OnHold` is the misleading-enum version of the same idea.

**Deliverable.** `m45-1-fraud-review-onhold-gap-memo.md` — recommends
landing this in the Orders remaster, **after** the PO resolves which of the
four design options to pursue. Lists acceptance criteria.

**Carryover (this retrospective).** Marking `OrderStatus.OnHold` as
`[Obsolete]` (with a comment pointing at the gap memo) as an interim safety
measure that prevents new code from referencing the placeholder. Single-
line change but should be QA'd against `OrderDeciderCancellationTests` first.

## Findings

### What went well

- **The "audit memo, not implementation" framing kept the cycle honest.**
  Each of S3 / S4 / S5 has at least one tempting partial implementation
  ("just wire one handler," "just add an `[Obsolete]` attribute," "just file
  one feature file") that would have spilled into multi-BC scope. Saying
  "no — audit, defer to the remaster" up-front was the right call and
  matches what M45.0 did for S6.
- **The S5 audit found a textbook misleading-enum anti-pattern.** The fact
  that `OrderStatus.OnHold` is *tested* as cancellable while having zero
  in-bound transitions is a perfectly legible "do not do this" example for
  the team and worth linking to in any future onboarding material on
  decider patterns.
- **The S3 audit found a textbook integration-message-published-to-no-one
  anti-pattern.** `ExchangeAdditionalPaymentCaptured` and
  `ExchangePartialRefundIssued` are defined as integration contracts and
  registered for publication on two queues — but no code ever constructs
  them. Reading `Returns.Api/Program.cs` would lead a new engineer to
  believe the contracts are live; reading `grep -rn "new
  ExchangeAdditionalPaymentCaptured" src/` reveals the truth. Worth a brief
  callout in `docs/skills/integration-messaging.md` when that file is next
  refreshed.
- **The S4 audit found a documentation gap that is independently
  fixable.** The "no `docs/features/orders/` directory" finding does not
  require the Orders remaster; it is a single low-risk slice that someone
  could pick up before the remaster lands.

### Challenges

- **Scope discipline on S3.** The temptation to "just wire the four
  Orders saga handlers" was real. Resisting it was the right call because
  the saga's state shape needs to grow to track "exchange in flight,"
  which is a remaster-scoped decision. A half-implementation here would
  have created an `OrderStatus.OnHold`-shaped problem in the saga.
- **The PO's option-matrix in the Orders README (S5) made the audit
  simpler than expected.** This is a pattern worth repeating: when a
  README already enumerates the design space, a gap memo can lean on it
  rather than re-derive it, and the audit shrinks to "verify nothing was
  silently implemented since the README was written" — which is two grep
  invocations.

### What we did not do

- Address S7 (Store Credit BC), S8 (Storefront/Vendor Portal a11y +
  empty-state sweep), S9 (Operations Dashboard MVP shape decision), S10
  (Vendor Portal cold-start framing). Per the §7.3 framing, S7 is future-BC
  roadmap; S8/S9/S10 are separate scopes and remain available for a future
  fine-tuning cycle.
- Implement any of S3 / S4 / S5. By deliberate scope choice — see "Why
  audit-only is the right answer here" above.
- Modify any production code. No build, no tests run; the only artifacts
  this PR produces are four Markdown files under
  `docs/planning/milestones/`.
- Rerun the M45.0 carryover items 1–5. They remain on the carryover list
  for the relevant downstream cycles.

## Code & test totals

| Area                 | Production files modified | Production files added | Test files added | Net new tests |
| -------------------- | ------------------------: | ---------------------: | ---------------: | ------------: |
| Documentation (S3)   |                         0 |                      1 |                0 |             0 |
| Documentation (S4)   |                         0 |                      1 |                0 |             0 |
| Documentation (S5)   |                         0 |                      1 |                0 |             0 |
| Documentation (retro)|                         0 |                      1 |                0 |             0 |
| **Total**            |                     **0** |                  **4** |            **0** |         **0** |

| Suite          | Pass | Fail | Skipped | Total |
| -------------- | ---: | ---: | ------: | ----: |
| Solution build | *(unchanged — no production code modified)*                |

## Carryover into the next cycle

| # | Item                                                                                                                                                                                       | Source            | Suggested home                                              |
| - | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ----------------- | ----------------------------------------------------------- |
| 1 | Implement S3 cross-product exchange end-to-end choreography per the gap memo                                                                                                               | M45.1 / PO §7.1   | Returns remaster (or Returns + Orders mini-cycle)            |
| 2 | File Gherkin for the **already-implemented** Orders behavior (placement, cancellation, lifecycle) so `docs/features/orders/` exists before the Orders remaster lands                        | M45.1 / S4 finding | Single-session documentation slice; no production code needed |
| 3 | Implement S4 post-placement modifications per the gap memo (after carryover #2 lands the Gherkin baseline)                                                                                 | M45.1 / PO §7.1   | Orders remaster                                              |
| 4 | Mark `OrderStatus.OnHold` as `[Obsolete]` with a comment pointing at the S5 gap memo, as interim safety measure preventing new code from referencing the placeholder                        | M45.1 / S5 finding | Single-PR documentation/safety slice; QA pass on `OrderDeciderCancellationTests` |
| 5 | Implement S5 fraud-review / OnHold per the gap memo (after PO resolves Option A/B/C/D)                                                                                                     | M45.1 / PO §7.1   | Orders remaster                                              |
| 6 | Brief callout in `docs/skills/integration-messaging.md` on the "contract published, never constructed" anti-pattern surfaced by the S3 audit (`ExchangeAdditionalPaymentCaptured` / `ExchangePartialRefundIssued`) | M45.1 lesson      | Skill-doc refresh — small                                    |

Items 1, 3, 5 are the substantive remaster work. Items 2, 4, 6 are
**low-risk, single-session interim slices** that can be picked up without
waiting for any remaster — and item 2 specifically closes the most striking
finding of the audit (no `docs/features/orders/`) independently of the
larger remaster.

The M45.0 carryover list (the abandoned-cart implementation, the
`OrderHistoryTests` mock fix, the `MapShipmentStatus` label drift, the
Marten daemon-driven projection integration tests, and the bUnit conditional-
SignalR pattern) remains current and unchanged.

## Sources

- `docs/research/state-of-repo-2026-05.md` §7.1 (PO sign-off), §7.2 (UXE
  sign-off), §7.3 (synthesis — items S3 / S4 / S5)
- `docs/planning/milestones/m45-0-po-uxe-top3-retrospective.md` (immediate
  predecessor; the pattern this retrospective continues)
- `docs/planning/milestones/m45-0-abandoned-cart-gap-memo.md` (the M45.0
  template this cycle's three memos follow)
- `src/Returns/Returns/ReturnProcessing/ApproveExchange.cs`,
  `src/Returns/Returns/ReturnProcessing/ShipReplacementItem.cs`,
  `src/Returns/Returns.Api/Program.cs` (S3 Returns side — what is
  implemented)
- `src/Orders/Orders/Placement/Order.cs`,
  `src/Orders/Orders.Api/Program.cs` (S3 + S4 + S5 Orders side — what is
  not)
- `src/Orders/Orders.Api/README.md` lines 101, 376–386, 430 (S5 — the
  team's own self-flagged design space)
- `src/Orders/Orders/Placement/OrderStatus.cs`,
  `tests/Orders/Orders.UnitTests/Placement/OrderDeciderCancellationTests.cs`
  (S5 — the misleading-enum evidence)
- `docs/features/returns/cross-product-exchange.feature` (S3 — the contract)
- `src/Promotions/Promotions/Coupon/RevokeCoupon.cs`,
  `src/Promotions/Promotions/CouponStatus.cs` (S5 — the correct shape that
  Orders' `OnHold` should follow)

---

*End of retrospective.*
