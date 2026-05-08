# M45.1 — PO/UXE Next-3 Audits Retrospective

> **Status:** ✅ Complete
> **Date:** 2026-05-08
> **Author:** Principal Architect
> **PR:** `copilot/address-next-three-issues`
> **Source:** `docs/research/state-of-repo-2026-05.md` §7.3 — synthesis of PO + UXE feedback
> **Predecessor:** [`m45-0-po-uxe-top3-retrospective.md`](./m45-0-po-uxe-top3-retrospective.md)

## TL;DR

After M45.0 closed §7.3 items S1, S2, and S6 (the surgical-scope set),
M45.1 addressed the **next three** PO-flagged items — S3, S4, S5 — all of
which are explicitly framed in §7.3 as "**charter input** for the
Orders/Returns remasters." Following the M45.0 pattern for S6
(abandoned-cart), the deliverable for each item is a **gap audit memo** that
documents what exists vs. what is missing, recommends a placement, and lists
acceptance criteria for "complete" — so future planning sessions do not
re-derive the gap.

| #  | Item                                                  | Origin   | Outcome                                                                                                    | Memo                                                              |
| -- | ----------------------------------------------------- | -------- | ---------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| S3 | Cross-product exchange end-to-end audit               | PO §7.1  | ✅ **Audited.** Returns side complete; cross-BC choreography (Inventory reservation, Payments delta capture, Orders saga consumers, refund emitter) is feature-file-only. | `m45-1-cross-product-exchange-gap-memo.md`                        |
| S4 | Order post-placement modifications                    | PO §7.1  | ✅ **Audited.** Confirmed unimplemented and undocumented. `docs/features/orders/` directory does not exist; address-change / line-cancel / quantity-change have no commands, events, endpoints, or tests. | `m45-1-order-post-placement-modifications-gap-memo.md`            |
| S5 | Fraud-review / OnHold saga state                      | PO §7.1  | ✅ **Audited.** `OrderStatus.OnHold` enum exists and is treated as cancellable, but **nothing transitions into it**. Misleading-enum anti-pattern. README already enumerates four design options (A: Manual / B: Internal / C: External / D: status quo). | `m45-1-fraud-review-onhold-gap-memo.md`                           |

**No production code changed; no tests added or modified; no build impact.**
This was an audit-only cycle by deliberate design — the same shape M45.0
chose for S6.

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
