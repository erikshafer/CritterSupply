# M45.0 — Abandoned-Cart Recovery Gap Memo

> **Status:** Charter input — not implemented
> **Date:** 2026-05-08
> **Author:** Principal Architect (M45.0 cycle)
> **Source:** PO sign-off in `docs/research/state-of-repo-2026-05.md` §7.1, item S6
> **Audience:** Whoever picks up the next Shopping or Correspondence remaster, or scopes a Customer Engagement BC

## Purpose

The PO asked us to **verify that Correspondence consumes Shopping events for
abandoned-cart recovery**. The verification result is documented here so future
sessions don't re-derive the gap. This memo does **not** propose an
implementation; that decision belongs to the cycle that adopts the work.

## Findings

The audit conclusion is unambiguous: **abandoned-cart recovery is not
implemented end-to-end**. Specifically:

| Concern                                                  | State                                                                                                                                  |
| -------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| `Shopping.Cart.CartAbandoned` domain event               | ✅ Defined (`src/Shopping/Shopping/Cart/CartAbandoned.cs`)                                                                              |
| `Cart.Apply(CartAbandoned)` reducer                      | ✅ Implemented (`src/Shopping/Shopping/Cart/Cart.cs:71`) — sets terminal `Abandoned` status                                             |
| Unit-test coverage of the reducer                        | ✅ 3 tests in `tests/Shopping/Shopping.UnitTests/Cart/CartApplyTests.cs:263-298`                                                         |
| **Anything that emits `CartAbandoned`**                  | ❌ **Nothing** — `grep -rn "new CartAbandoned" src/` returns zero results outside the test files                                         |
| Integration message in `Messages.Contracts/Shopping/`    | ❌ Not defined                                                                                                                          |
| Shopping `Program.cs` `PublishMessage<CartAbandoned>()`  | ❌ Not registered (publish list at `src/Shopping/Shopping.Api/Program.cs:87-99` covers only the 6 cart-mutation events)                  |
| Correspondence subscriber                                | ❌ No handler under `src/Correspondence/Correspondence/Messages/` consumes any cart event; the four queues subscribed in `Program.cs:96-105` are `correspondence-{orders,fulfillment,returns,payments}-events` |
| Background scheduler / TTL job                           | ❌ Not implemented                                                                                                                      |

The Shopping README (`src/Shopping/Shopping.Api/README.md` lines 41, 119, 184,
302) **already declares this as a known gap** ("⚠️ not yet implemented"). This
memo elevates the README footnote to a charter input so it gets weighed
against other work during the next planning session.

## Why this was deferred from M45.0

M45.0's scope was three reliability fixes for already-implemented behavior.
Implementing abandoned-cart recovery is a **new product feature**, not a fix.
A minimum-viable implementation would touch:

1. A scheduled command (`AbandonStaleCarts` or per-cart TTL via Wolverine
   `ScheduleAsync`). Requires injectable `TimeProvider` and a deterministic
   "cart is stale" rule (industry standard: 30 min anonymous / 30 days
   authenticated — see Shopping README §302).
2. A cart-state inspection — either a Marten query against active carts or a
   per-cart timer scheduled on `ItemAdded`/`ItemQuantityChanged` and rolled
   forward on subsequent activity.
3. Adding `Messages.Contracts/Shopping/CartAbandoned.cs` integration message,
   `PublishMessage<>()` registration, and a `correspondence-shopping-events`
   queue.
4. A `Correspondence/Messages/CartAbandonedHandler.cs` and an
   `AbandonedCartRecoveryEmailTemplate` with one-click "resume your cart"
   deep-link.
5. BDD scenarios in `docs/features/shopping/` covering the timer-resets-on-
   activity guarantee, dedupe (one email per abandonment, not per item event),
   and post-abandonment cleanup.
6. Integration tests with a virtual `TimeProvider` to keep the suite fast.

That is a **multi-session arc**, not a single fix.

## Recommended placement

This work most naturally lives in one of three future cycles, in order of
fit:

1. **A Shopping BC remaster** *(if/when the Shopping BC is selected for
   remastering)*. The cart lifecycle, including abandonment, is the most
   natural piece of scope to land alongside a remaster. Pre-DCB Shopping
   already needs idiomatic `[BoundaryModel]` work; cart abandonment is a
   green-field add inside that arc.
2. **A Correspondence "lifecycle expansion" cycle**. Correspondence already
   subscribes to four BC queues; adding a fifth (`correspondence-shopping-
   events`) is a low-risk extension. This is the smallest-scope option and
   gets the customer-facing email shipped quickest.
3. **A Customer Engagement BC** *(future greenfield)*. If the company ever
   builds a marketing-automation BC, abandonment-recovery is its
   highest-leverage first slice. This is the long-horizon option and
   probably overkill for a single email trigger.

The PO's preference (§7.1) is to address this **after** the Orders +
Returns remasters, so option 2 is the most likely landing spot.

## Acceptance criteria for "complete"

When this gets picked up, "done" means:

- A `CartAbandoned` event is appended to the cart stream by a deterministic
  scheduling rule (timer, scheduled command, or comparable mechanism).
- An integration `CartAbandoned` is published, and a Correspondence handler
  consumes it idempotently (same cart abandoned + retried delivery → one
  email).
- A BDD scenario verifies the timer-reset-on-activity guarantee.
- The Shopping README's "⚠️ not yet implemented" footnotes are removed.
- A short retrospective + the relevant ADR (decision: timer mechanism)
  is filed.

## Out of scope for this memo

- The choice between per-cart timers vs. periodic sweep (see Shopping README
  §302 for the trade-off discussion).
- Whether to send via email, push, SMS, or all three (Correspondence already
  has email + SMS provider abstractions; channel choice is product-not-
  architecture).
- Customer-preference / opt-out wiring (separate concern).

---

*End of memo.*
