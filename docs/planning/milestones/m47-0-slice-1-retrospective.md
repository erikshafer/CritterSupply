# M47.0 / Slice 1 — Retrospective

> **Slice:** Inventory replacement reservation for cross-product exchange
> **Date completed:** 2026-05-08
> **Plan:** [`m47-0-plan.md`](./m47-0-plan.md)
> **ADR:** [`0061-cross-product-exchange-replacement-reservation.md`](../../decisions/0061-cross-product-exchange-replacement-reservation.md)
> **Source gap:** [`m45-1-cross-product-exchange-gap-memo.md`](./m45-1-cross-product-exchange-gap-memo.md), "What is missing" row #1

## Outcome

The cross-product exchange flow now places a real hold on the replacement
SKU when `ApproveExchange` runs the cross-product branch. If the
Inventory BC reports the replacement is unavailable, the Returns BC
denies the exchange with a customer-facing
"Replacement item currently unavailable" message — closing the
"Returns approves blind" gap that has existed since the M35.0
cross-product feature first shipped.

The matching `@pending` tag and the matching skipped placeholder test
are gone, replaced by an active scenario in the feature file and an
active integration test in the Returns suite.

## What landed

### Code

| Layer | File | Purpose |
|---|---|---|
| Contracts | `src/Shared/Messages.Contracts/Inventory/ReserveReplacementForExchange.cs` | Returns → Inventory request |
| Contracts | `src/Shared/Messages.Contracts/Inventory/ReplacementReserved.cs` | Inventory → Returns success reply |
| Contracts | `src/Shared/Messages.Contracts/Inventory/ReplacementReservationFailed.cs` | Inventory → Returns failure reply |
| Inventory | `src/Inventory/Inventory/Management/ReserveReplacementForExchangeHandler.cs` | New entry point reusing the standard `ProductInventory` reservation lifecycle |
| Inventory | `src/Inventory/Inventory.Api/Program.cs` | Listen `inventory-returns-events`, publish replies on `returns-inventory-events` |
| Returns | `src/Returns/Returns/ReturnProcessing/ApproveExchange.cs` | Cross-product branch additionally publishes `ReserveReplacementForExchange` |
| Returns | `src/Returns/Returns/ReturnProcessing/ReturnsExchangeDefaults.cs` | Centralized default warehouse constant (`WH-01`) |
| Returns | `src/Returns/Returns/Integration/ReplacementReservationOutcomeHandler.cs` | Consumes both replies; on failure denies the exchange (idempotent) |
| Returns | `src/Returns/Returns.Api/Program.cs` | Listen `returns-inventory-events`, publish requests on `inventory-returns-events` |

### Tests

| Suite | File | Tests |
|---|---|---|
| Inventory integration | `tests/Inventory/Inventory.Api.IntegrationTests/Management/ReserveReplacementForExchangeTests.cs` | 4 |
| Returns integration | `tests/Returns/Returns.Api.IntegrationTests/ReplacementReservationOutcomeTests.cs` | 4 |

Full Inventory suite: **115 / 115 passed.** Full Returns suite:
**48 passed**, with the previously-skipped
`Out_Of_Stock_Replacement_Denies_Exchange` placeholder removed (other
unrelated `@pending` and `CrossBcSmokeTests` skips are unchanged).

### Docs

| File | Change |
|---|---|
| `docs/decisions/0061-cross-product-exchange-replacement-reservation.md` | New ADR — reuse vs new aggregate, choreography vs orchestration, default warehouse |
| `docs/planning/milestones/m47-0-plan.md` | New milestone plan with the 4-slice arc |
| `docs/features/returns/cross-product-exchange.feature` | `@pending` removed from row-#1 scenario, replaced with "closed in M47.0/S1" annotation |
| `tests/Returns/Returns.Api.IntegrationTests/CrossProductExchangePendingTests.cs` | Matching skipped placeholder removed |
| `CONTEXTS.md` | Returns ↔ Inventory edge added to both sections |

## Decisions worth surfacing

1. **Reuse `ProductInventory`, not a new `ExchangeHold` aggregate.**
   The mechanics (hold N units of SKU at warehouse W, release on
   commit/expiry) are identical to a normal reservation. A separate
   aggregate would have duplicated every test for no semantic gain.
   Documented in ADR 0061.

2. **No `ReservationKind` discriminator yet.** No projection or downstream
   consumer needs to distinguish exchange holds from order reservations
   today. If a real consumer emerges (e.g. operational tooling), it can
   be added as a non-breaking field on `StockReserved` with a default.

3. **Choreography between Returns and Inventory; no Orders saga involvement.**
   The interaction is one-request-one-reply and the Orders saga would be
   a pure forwarder. The existing M45.1 acknowledger handlers on the
   Order aggregate already prevent premature order closure.

4. **Idempotent re-emission on duplicate delivery.** When the Inventory
   handler sees a re-delivered `ReserveReplacementForExchange` whose
   `ReturnId` is already in the reservations dictionary, it re-emits a
   `ReplacementReserved` reply instead of just no-oping. This protects
   Returns against losing the first reply in its inbox without
   double-reserving on the aggregate side.

5. **Default `WH-01` is documented, not hidden.** The constant lives in
   `ReturnsExchangeDefaults.ReplacementWarehouseId` with a doc-comment
   pointing at the future Returns + Fulfillment routing entry point.

## What we explicitly did not do

- **No release of the replacement reservation on rejection / cancellation.**
  When inspection fails or the exchange is cancelled, the reserved stock
  remains held until expiry. This is **Slice 4** scope because the
  cancellation paths require Payments-failure (Slice 2) and
  Orders-saga-state (Slice 3) decisions to land first.
- **No routing-aware warehouse selection.** All replacement holds land
  at `WH-01` per the documented limitation in ADR 0061.
- **No Orders saga changes.** The M45.1 acknowledger handlers remain;
  the saga has no business decision to make about the reservation.
- **No surfacing of "approved-with-reservation-in-flight" as a distinct
  Return status.** The existing `Approved` status covers both states.
  A future slice can split if customer / CS-agent UX demands it.
- **No Payments work.** Slice 2.

## Risks the next slice should know about

- The default `WH-01` warehouse means an exchange can be denied where a
  routing-aware system would have approved it. **Documented**, not a
  bug — but expect the Returns + Fulfillment remaster to revisit.
- Reserved replacement stock will sit on the books until the
  `ExpireReservationHandler.ExpiryTimeout` elapses. Slice 4 owes the
  release path. If business pressure surfaces before Slice 4 lands, the
  workaround is the existing `ReleaseReservation` HTTP endpoint
  (operations can manually release a hold if a customer cancels).
- The Returns aggregate does not surface
  `ReplacementReservationStatus`. A read-side consumer that needs to
  distinguish "approved (still reserving)" from "approved (reserved)"
  would have to reconstruct it from the event stream. Acceptable today.

## Lessons / observations

- **Re-emit confirmations on idempotent re-delivery, don't silently
  drop.** Saved one round of "why did Returns get stranded?" debugging
  by writing the test before writing the handler.
- **Existing per-BC test fixtures are sufficient for cross-BC
  choreography testing.** Inventory tests assert "we publish what we
  said we would"; Returns tests assert "we react correctly to the reply
  contract". Neither needs a real cross-BC harness — the Wolverine
  routing wiring is what binds them in production. Per the existing
  memory: `tracked.Sent.SingleMessage<T>()` does work for messages
  with configured routes even when external transports are disabled.
- **One ADR, one slice.** Resisting the urge to bake decisions for
  Slices 2/3/4 into ADR 0061 kept it short and kept future slices from
  being over-constrained.

## Next slice (preview)

**Slice 2 — Payments delta capture + refund** (gap memo rows #3 / #4 / #5).
Adds Payments handlers for the cross-product price delta — both the
"replacement costs more" capture path and the "replacement costs less"
refund path. Will require its own ADR on whether Payments grows an
`ExchangePayment` aggregate or piggybacks on `Payment`.
