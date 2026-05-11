# M47.0 — Closeout

**Status:** ✅ Complete (5 slices)
**Date opened:** 2026-05-08
**Date closed:** 2026-05-11
**Plan:** [`m47-0-plan.md`](./m47-0-plan.md)

## What M47.0 was

M47.0 closed the cross-product exchange end-to-end gap memo
(`m45-1-cross-product-exchange-gap-memo.md`) by landing the cross-BC
choreography that makes `docs/features/returns/cross-product-exchange.feature`
end-to-end implementable for the customer surface. Five slices.

## Slice outcomes

| # | Slice | Closes |
|---|-------|--------|
| 1 | Inventory replacement reservation (Returns ↔ Inventory choreography) | "Replacement out of stock" `@pending` scenario; ADR 0061 |
| 2 | Payments delta capture + partial refund (Returns ↔ Payments choreography) | "Cheaper replacement → $20 refund", "More expensive replacement → $25 captured" `@pending` scenarios; ADR 0062 |
| 3 | Storefront real-time surface for cross-product-exchange payment metadata | New `return-exchange-payment-changed` SignalR discriminator; new mapper helper; new UI dispatch case |
| 4 | Cross-product exchange compensation paths | "Additional payment capture fails — exchange cancelled" + "Refund difference on inspection rejection" `@pending` scenarios; new `ReturnStatus.Cancelled` terminal; reservation release on cancellation |
| 5 | In-session activity timeline + test-debt cleanup | Slice 3 carry-forward (MudTimeline on `OrderConfirmation.razor`); Slice 4 carry-forward (5 pre-existing OrderHistoryTests failures) |

## Final state of `cross-product-exchange.feature`

- 0 `@pending` scenarios remaining.
- 0 acknowledged hand-wave gaps in the BC seams (Returns, Inventory, Payments, Storefront all wired).
- The `ReturnStatus` state machine has a real terminal `Cancelled` value with a customer-visible end-to-end path (Slice 4).

## ADRs landed

- ADR 0061 — Cross-product exchange replacement reservation (Slice 1)
- ADR 0062 — Cross-product exchange Payments choreography (Slice 2)

## What M47.0 explicitly did **not** do

These items were explicitly out of scope at planning rounds and remain
open. Each warrants its own slice (and most need their own ADR) because
none can be shoe-horned into a single session without cutting corners.

### 1. Persisted return-history view (Storefront BFF)

**What:** Today the customer's `OrderConfirmation` page renders a real-time
activity timeline (M47.0 / Slice 5) that lives only in browser memory.
A page refresh wipes it. To survive refresh / cross-device viewing, the
Storefront BFF needs:

- A persisted projection of return-related events keyed by `OrderId` (or `ReturnId`).
- A new HTTP endpoint (`GET /api/storefront/orders/{orderId}/activity`).
- UI changes in `OrderConfirmation.razor` to seed `_timeline` from that endpoint on page load and de-duplicate against incoming SignalR events.

**Why deferred:** Materially changes the Storefront BFF (new projection,
new HTTP shape, new contract surface). Worth its own slice and an ADR
on whether the projection lives in Storefront or whether Storefront
delegates to a Returns BC history endpoint.

**Recommended next slice owner:** M48.0 / Storefront BFF.

### 2. Backoffice timeline for cross-product exchange events

**What:** Backoffice operators can't see exchange progress in their UI.
The Returns BC publishes `ExchangeAdditionalPaymentCaptured`,
`ExchangePartialRefundIssued`, and `ExchangeCancelled` to
`storefront-returns-events` only — Backoffice is not a subscriber.

**Why deferred:** Different BC, different UI, different routing. Touches
`Returns.Api/Program.cs` (add `backoffice-returns-events` outbound
routing) plus new Backoffice handlers and a new Backoffice page.
Roughly equivalent in scope to M47.0 / Slice 3 but for a different
audience.

**Recommended next slice owner:** M48.0 / Backoffice.

### 3. End-to-end Reqnroll scenario covering Returns → Inventory → Payments → Storefront

**What:** A single Reqnroll scenario that walks the full cross-product
exchange happy path across all four BCs in a single test process.

**Why deferred:** Per-BC integration tests (Slice 1 Inventory, Slice 2
Payments, Slice 4 Returns compensation) and Storefront SignalR tests
(Slice 3 + Slice 4) already cover each seam. An E2E adds runtime cost
without proving anything new about the seams. Slice 4 made this call
explicitly; Slice 5 confirmed it.

**Recommended next slice owner:** Optional. Only adds value if a future
incident demonstrates a cross-BC interaction the per-BC tests missed.
Tracked here for completeness, not as a blocker.

## Statistics across the milestone

- 5 slices, all completed in single sessions, no slice deferred or split.
- 2 ADRs landed (0061, 0062).
- 5 retrospectives (`m47-0-slice-{1..5}-retrospective.md`).
- 5 `@pending` Gherkin scenarios closed (in slices 1, 2, 4, 4, and the existing-acknowledger no-op work in slice 4).
- ~20 new integration / handler / unit tests across Returns, Payments, Inventory, Storefront.Api, Storefront.Web.UnitTests.
- 0 production-code defects found by QAE across slices 3, 4, 5; one ping-pong on Slice 4 (idempotency-guard ordering in `RefundExchangeDeltaHandler`).

## What unblocks future cycles

With M47.0 closed:

- Returns BC has a complete state machine for cross-product exchanges (no terminal-state gaps).
- Customers see real-time updates for the cross-product-exchange happy and sad paths.
- Inventory and Payments are real choreography participants, not stub acknowledgers.
- The Storefront UI has both the most-recent-update banner and the in-session activity timeline; persistence is the next obvious step rather than a foundational change.

The next logical cycle is **persistence + cross-audience visibility**
(items 1 and 2 above), which together would let M48.0 ship
"customers and operators both see the full history of any return,
across page refreshes and across devices." That's a coherent cycle
framing rather than three separate carry-forwards.
