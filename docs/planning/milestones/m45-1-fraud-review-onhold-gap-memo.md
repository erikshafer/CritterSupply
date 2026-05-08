# M45.1 — Fraud-Review / OnHold Saga State Gap Memo

> **Status:** Charter input — enum exists, semantics do not
> **Date:** 2026-05-08
> **Author:** Principal Architect (M45.1 cycle)
> **Source:** PO sign-off in `docs/research/state-of-repo-2026-05.md` §7.1 / §7.3, item S5
> **Audience:** Whoever scopes the **Orders remaster** (and a future Risk / Trust BC if the team chooses Option C below)

## Purpose

The PO flagged "**Fraud-review / OnHold saga state on Orders** — absent" as
charter input for the Orders remaster. This memo documents the verification
result. The Orders README (`src/Orders/Orders.Api/README.md` lines 376–386 and
the "Future" row at line 430) **already self-flags this as a known gap with
four explicit options**; this memo elevates that footnote to the planning
surface so the next planning session weighs it deliberately.

## Findings

The audit conclusion: **the `OnHold` enum value exists and is treated as a
"cancellable" status, but no transition into `OnHold` exists anywhere in the
codebase. It is a state placeholder, not a state.**

| Concern                                                                  | State                                                                                                                                                                                                                                |
| ------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `OrderStatus.OnHold` enum value                                          | ✅ Defined (`src/Orders/Orders/Placement/OrderStatus.cs:32` — comment: "Flagged for fraud review or inventory issues.")                                                                                                                |
| `CanBeCancelled(OnHold) == true`                                         | ✅ Tested (`tests/Orders/Orders.UnitTests/Placement/OrderDeciderCancellationTests.cs:53`) — an `OnHold` order CAN be cancelled. This guard is the only *behavior* attached to `OnHold` anywhere in the codebase.                       |
| Anything that **transitions to** `OnHold`                                | ❌ `grep -rn "OrderStatus.OnHold\|Status = OrderStatus.OnHold\|= OnHold" src/` returns zero results outside the enum definition itself. No saga handler, no `OrderHeldOnHold` event reducer, no command that puts an order on hold.    |
| `FraudFlagged` / `OrderHeldOnHold` / `PutOrderOnHold` command or event   | ❌ None defined. `grep -rn -i "FraudFlagged\|HeldOnHold\|PutOrderOnHold\|FlagOrderForReview" src/` returns zero results.                                                                                                                |
| Fraud-scoring integration (Stripe Radar / equivalent)                    | ❌ None. `src/Payments/Payments/PaymentGateway/` has no scoring hook. The Payments aggregate captures and authorizes but does not surface a risk score.                                                                                |
| External fraud rules engine                                              | ❌ Not present.                                                                                                                                                                                                                       |
| README acknowledgement                                                   | ✅ **Strong.** `src/Orders/Orders.Api/README.md`: line 101 "⚠️ OnHold state planned (fraud detection)"; lines 376–386 "Q2: What flags an order for fraud review (OnHold state)?" with four named options (A: Manual / B: Internal rules / C: External scoring / D: No fraud review — current); line 430 "OnHold state (fraud detection): All orders process automatically — Future." |
| Free-text "Fraud detected" cancellation reason                           | ⚠️ Used in tests (`OrderDeciderCancellationTests.cs:206-218`, `Backoffice/.../OrderCancellationTests.cs:59`) and in `Promotions/CouponRedemptionTests.cs:152` for coupon revocation. This is the **current substitute for fraud handling**: a CS agent suspects fraud → cancels the order → free-texts "Fraud" in the reason. There is no analytical or workflow signal beyond the string. |
| Backoffice "review queue" surface for OnHold orders                      | ❌ Not present. The Backoffice Dashboard surfaces `AlertFeedView` for failed payments / etc. but has no "orders awaiting fraud review" surface.                                                                                       |

In other words: today's "fraud handling" is a CS agent reading a payment-
failure alert (which surfaces correctly via `AlertFeedView` —
`tests/Backoffice/.../AlertFeedViewTests.cs:195` proves the path), making a
human judgment call, and cancelling the order with `Reason: "Fraud detected"`.
There is no automated detection, no queue, and no `OnHold` workflow despite
the enum suggesting otherwise.

## Why this matters in practice

The `OnHold` enum is **misleading**. A reviewer reading
`src/Orders/Orders/Placement/OrderStatus.cs` would reasonably assume that
fraud-review is a real saga state with transitions in and out. It is not —
and the test suite's `[InlineData(OrderStatus.OnHold)]` reinforces the
illusion. The combination is worse than no enum value at all because it
implies coverage that does not exist.

The PO's reframing (§7.1: "absent") is the correct read.

## Why this was deferred from M45.1

The Orders README at lines 376–386 already enumerates the design space — and
the choice between Options A–D is **a product decision, not an engineering
one**:

| Option | Description                                                                                                                                  | Cost  | Effectiveness |
| ------ | -------------------------------------------------------------------------------------------------------------------------------------------- | ----- | ------------- |
| A      | **Manual review queue** — every order with `total > $X` or shipping/billing mismatch goes to CS. Synchronous block.                          | Med   | Med           |
| B      | **Internal rules engine** — velocity, geography, payment method, item profile. Rules in code or DB.                                          | Med   | Med-High      |
| C      | **External fraud scoring** — Stripe Radar, Sift, Riskified. Score returned at payment authorization; high-score orders move to OnHold.       | Low-Med (vendor cost) | High          |
| D      | **Status quo** — no fraud review; chargebacks via Payments.                                                                                  | Zero  | None          |

The product / business decision (which option, what threshold, who reviews,
what SLA) is upstream of the implementation.

A minimum-viable implementation (Option B or Option C, after the product
decision is made) would touch:

1. A `RiskScoringService` interface with stub + production implementations
   (matching the `PaymentGateway` / `EmailProvider` pattern).
2. A `PutOrderOnHold(OrderId, Reason, RiskScore?)` command, an
   `OrderHeldOnHold` event, and the corresponding saga reducer that
   transitions out of `PaymentConfirmed` / `InventoryReserved` rather than
   continuing to `InventoryCommitted` / `Fulfilling`.
3. A `ReleaseOrderFromHold(OrderId, ReviewerId)` command + event for the CS
   agent to clear the hold.
4. A `RejectOrderForFraud(OrderId, ReviewerId)` command + event that
   compensates inventory + refunds payment.
5. A Backoffice dashboard surface — a "review queue" view (likely a Marten
   document projection over `OrderHeldOnHold` minus `OrderReleasedFromHold`
   minus `OrderRejectedForFraud`) and the action endpoints.
6. Wiring from Payments — when the gateway returns a risk signal (Option C)
   or when the internal rules fire (Option B), emit a
   `RiskAssessmentCompleted` integration event the Orders saga consumes.
7. Gherkin in `docs/features/orders/` (which does not yet exist — see the
   M45.1 post-placement-modifications memo) covering the four lifecycle
   transitions: `→ OnHold`, `→ Released`, `→ RejectedForFraud`, `→ Cancelled
   from OnHold` (the last one already partly handled by `CanBeCancelled`).
8. An ADR documenting the option chosen and why.

That is a **multi-session arc** plus an upstream product decision.

## Recommended placement

This work most naturally lives in:

1. **The Orders remaster**, *after* the product decision (Option A / B / C)
   is made. The remaster is the natural home because adding a real `OnHold`
   saga state changes the saga's transition table — the remaster would
   formalize the table anyway.
2. **A future Risk / Trust BC** if the team selects Option C and the scoring
   integration grows beyond a single command-side hook (e.g., reputation
   scoring across orders, customer trust scoring, automated dispute defense).
   This is the long-horizon option and overkill for v1.

The pragmatic recommendation: **defer until the Orders remaster, treat the
product decision (Option A vs. B vs. C) as a charter input the PO can
resolve before the remaster starts**. Until then, **the misleading
`OrderStatus.OnHold` enum value should either be removed (cleanest) or
flagged with a `[Obsolete]` attribute and a comment pointing at this memo
(safest, since `OrderDeciderCancellationTests` references it).** The
intermediate option — keeping the enum and adding an ADR pointer — is
documented as carryover #4 in the M45.1 retrospective; this memo does not
implement it because the right time is when the remaster decides which
option to pursue.

## Acceptance criteria for "complete"

When this gets picked up, "done" means:

- An ADR records the chosen option (A / B / C) and the threshold(s).
- `OrderHeldOnHold`, `OrderReleasedFromHold`, and `OrderRejectedForFraud`
  events exist with corresponding saga reducers.
- A Backoffice "review queue" surface exists with the action endpoints.
- For Option C: the chosen scoring vendor's webhook is wired (with a stub
  for development matching the `PaymentGateway` pattern).
- For Option A / B: the rules engine or the manual-review threshold is
  documented in code / config in a single, testable place.
- Gherkin coverage of the four `OnHold` lifecycle transitions + the
  cancellation-from-OnHold path that is already permitted today.
- An end-to-end Alba integration test that walks: payment authorized →
  scoring flags → saga moves to `OnHold` → CS releases → saga resumes from
  `PaymentConfirmed`.
- The `[Obsolete]` attribute (if added per "Out of scope" below) is removed
  and the misleading-enum risk is closed.

## Out of scope for this memo

- The product decision (A / B / C). Belongs to the PO + a brief stakeholder
  discussion, not to engineering.
- The choice of scoring vendor (if Option C). Belongs to a procurement /
  technical-evaluation discussion downstream of the product decision.
- The CS-agent UI for the review queue. The PO is the right voice on
  reviewer ergonomics; engineering should expect to iterate after a first
  cut.
- Marking `OrderStatus.OnHold` `[Obsolete]` *now* as an interim safety
  measure. Plausible but should be a separate single-line PR with QA pass on
  the `OrderDeciderCancellationTests` to confirm no test breaks. Filed as
  carryover #4 below.

## Out of scope for this memo *(but worth flagging)*

The Promotions BC has the equivalent pattern done correctly: `RevokeCoupon`
is an explicit command, `CouponRevoked` is an explicit event, and
`CouponStatus.Revoked` is a real terminal state with reducer behavior
(`src/Promotions/Promotions/Coupon/RevokeCoupon.cs`,
`src/Promotions/Promotions/Coupon/CouponRevoked.cs`,
`src/Promotions/Promotions/CouponStatus.cs:11`). The Orders BC's `OnHold` is
the **anti-pattern** version of the same idea: the enum value exists, the
event and command do not. The Promotions implementation is a reasonable
shape to copy when the Orders remaster picks this up.

---

*End of memo.*
