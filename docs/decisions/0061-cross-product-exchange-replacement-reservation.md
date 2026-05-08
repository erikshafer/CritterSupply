# ADR 0061 — Cross-Product Exchange Replacement Reservation

**Status:** Accepted
**Date:** 2026-05-08
**Milestone:** M47.0 — Cross-Product Exchange End-to-End (Slice 1)

---

## Context

`docs/planning/milestones/m45-1-cross-product-exchange-gap-memo.md` documented
that the Returns BC's cross-product exchange flow was approving exchanges
**blind** — no formal hold on the replacement SKU's inventory was placed
before `ApproveExchange` succeeded. The "And the replacement is in stock" /
"And the replacement item is out of stock" Gherkin guards in
`docs/features/returns/cross-product-exchange.feature` had no enforcement
code path. In production, whatever stock was on the shelf at the time
`ShipReplacementItem` ran was whatever shipped — there was no eventing-driven
hold protecting the customer's exchange from being lost to a concurrent order.

This ADR records the choices made in **M47.0 / Slice 1** to close that gap.

The gap memo (§"Out of scope for this memo") explicitly deferred two
ADR-grade decisions to the implementation:

1. Does the replacement-SKU reservation reuse the standard
   `ProductInventory` reservation flow, or does it grow a new
   `ExchangeHold` reservation kind / aggregate?
2. Does the **Returns** BC own the cross-BC choreography for replacement
   reservation, or does the **Orders saga** own it?

Both decisions are made here so subsequent slices (Payments delta capture,
Orders saga state for "exchange in flight", end-to-end compensation) inherit
a stable foundation.

---

## Decision

### 1. Reuse `ProductInventory`'s existing reservation lifecycle

The replacement-SKU hold reuses the existing reservation flow on
`ProductInventory` (`StockReserved` → `ReservationCommitted` /
`ReservationReleased` / `ReservationExpired`). The `Return.Id` is used as
the `ReservationId`. The original `Order.Id` from the return is used as
the `OrderId` on the reservation record, keeping the existing
`ReservationOrderIds` invariant satisfied.

A new integration-message *entry point* is added — the new
`ReserveReplacementForExchange` integration message and its handler in
`Inventory.Management.ReserveReplacementForExchangeHandler` — but the
underlying domain event (`StockReserved`) and aggregate state are
unchanged.

**No new `ReservationKind` discriminator is added** in this slice.
Reservation purpose (order vs. exchange-hold) is currently inferable from
context (the integration-message entry point that produced it), and the
operations-monitoring views did not surface a need to discriminate. If
`ReservationKind` becomes necessary later — for example, to give exchange
holds a different default expiry, or to surface them separately on the
`AlertFeedView` — it can be added as a non-breaking field on `StockReserved`
with a `Kind = Order` default for backward compatibility.

### 2. Choreography between Returns and Inventory; no Orders saga involvement

The replacement reservation is a **direct two-party choreography** between
Returns and Inventory:

- Returns publishes `ReserveReplacementForExchange` to a new
  `inventory-returns-events` queue when the cross-product exchange branch
  of `ApproveExchange` runs.
- Inventory consumes it, attempts the reservation, and replies on a new
  `returns-inventory-events` queue with either `ReplacementReserved` or
  `ReplacementReservationFailed`.
- Returns consumes the reply. On failure, it appends `ExchangeDenied` with
  reason `"Replacement out of stock"` (idempotent — only if the Return is
  still in `Approved` status). On success, no aggregate state change is
  required for this slice; the customer-visible flow is unchanged.

The Orders saga is **not involved** in this slice. The saga does not need
to know about replacement reservation outcomes to maintain its own
correctness — it tracks `ActiveReturnIds`, and the existing M45.1
acknowledger handlers for `CrossProductExchangeRequested` already prevent
premature order closure. Adding saga state for "exchange in flight" is
deferred to Slice 3 (Orders saga remaster), where it will be designed
together with the Payments delta-capture flow.

### 3. Default warehouse `WH-01` for replacement holds

Slice 1 hardcodes `WH-01` as the warehouse the replacement reservation is
placed against. This matches the legacy `OrderPlacedHandler` pattern that
Inventory's M43.0 / Slice 12 carryover is still living with — it is
deliberately *not* worse than the existing baseline, but it is not better
either. The Fulfillment routing engine (M41.0 remaster) does not have an
"exchange replacement" entry point yet; routing-aware warehouse selection
for exchange replacements is a Returns + Fulfillment co-deliverable that
belongs in the Returns remaster charter.

---

## Rationale

### Why reuse the reservation lifecycle?

- The mechanics are identical: hold *N* units of SKU at warehouse *W*,
  decrement available, restore on release/expiry. There is nothing about
  an exchange hold that a normal reservation does not already model.
- A separate `ExchangeHold` aggregate would duplicate every test
  (idempotency, expiry, release on commit, commit-after-pick) without
  adding semantic value.
- The operational monitoring views (`AlertFeedView`,
  `NetworkInventorySummaryView`, `BackorderImpactView`) already surface
  reservations correctly — a separate aggregate would require either
  parallel projection logic or a unified read model that re-introduced
  the discrimination problem at the read layer.
- Optimistic concurrency, the Wolverine DLQ policy on
  `ConcurrencyException`, and the existing test infrastructure
  (`InventoryStreamId.Compute`, `Reservations` invariants, deterministic
  UUID v5 stream identity) all carry over for free.

### Why not orchestrate via the Orders saga?

- The cross-BC interaction here is one request and one reply — the
  archetypal choreography case. There is no multi-step coordination that
  needs centralized state.
- Pulling the Orders saga into a *replacement reservation* flow would
  make every cross-product exchange depend on the Orders saga being
  responsive, even though the Orders saga has no business decision to
  make about the reservation outcome.
- The Returns aggregate already owns the relevant business state (does
  this exchange have a replacement available?) and the relevant
  compensation (deny the exchange if not). Asking the Orders saga to
  forward Inventory's reply back to Returns would just add a hop without
  changing the outcome.
- The existing M45.1 acknowledger `Handle(CrossProductExchangeRequested)`
  on the Order aggregate is sufficient to keep the Order from closing
  prematurely — that behavior is preserved unchanged.

### Why default to `WH-01`?

- Adding routing-aware warehouse selection for exchange replacements
  requires a new entry point on the Fulfillment routing engine, which is
  out of slice scope.
- The legacy `WH-01` default already exists in the codebase; using the
  same value here does not regress any production behavior.
- The default is in one place
  (`ReturnsExchangeDefaults.ReplacementWarehouseId`) and is a single-line
  edit when Fulfillment grows the appropriate routing surface.

---

## Consequences

### Positive

- The "replacement out of stock" Gherkin scenario in
  `cross-product-exchange.feature` becomes end-to-end implementable — its
  `@pending` tag is removed in this slice, and the matching skipped
  placeholder in `CrossProductExchangePendingTests.cs` is deleted.
- No changes to Marten projections, the Inventory aggregate, or the
  Orders saga — the slice's blast radius is limited to two BCs and three
  new integration-message contracts.
- Subsequent slices inherit a working reservation flow that can be
  released by `ReleaseReservation` if Slices 2 / 4 need to compensate
  (e.g., refund-on-rejection has to release the hold).

### Negative / Accepted Limitations

- Replacement reservations all land at `WH-01`. If `WH-01` is out of
  stock for the replacement SKU but `WH-02` has it, the exchange will be
  denied where a routing-aware system would have approved it. **This is a
  documented, deliberate limitation**, addressed in the Returns remaster
  charter.
- No `ReservationKind` discriminator — operational tooling that wants to
  distinguish exchange holds from order reservations cannot do so today.
  Acceptable because no operational tooling currently needs this.
- The Returns aggregate does not surface `ReplacementReservationStatus`
  on its read model — a customer / CS-agent UI cannot show "approved,
  awaiting reservation" as a distinct state from "approved (reserved)".
  The existing `Approved` state covers both; this is acceptable for
  Slice 1 because `ApproveExchange` is a CS-agent operation and the
  reservation request fires synchronously from the same handler.

### Follow-on work this enables

- Slice 2 (Payments delta capture) can release the replacement
  reservation if the additional payment fails — same release semantics
  as any other reservation.
- Slice 3 (Orders saga "exchange in flight") can subscribe to
  `ReplacementReserved` / `ReplacementReservationFailed` if the saga
  later needs to gate order closure on reservation outcome.
- The Returns + Fulfillment remaster can replace the `WH-01` default
  with a `RouteReplacementRequested` → `ReplacementWarehouseSelected`
  pre-step, without changing the Inventory contract.

---

## Alternatives considered

### A. New `ExchangeHold` reservation kind on `ProductInventory`

- Add `ReservationKind { Order, ExchangeHold }` discriminator on
  `StockReserved` and the `Reservations` dictionary value type.
- **Rejected** because no projection or downstream consumer needs the
  discrimination today, and adding it touches every reservation test.
  Cheap to add later if a real consumer emerges.

### B. Separate `ExchangeHold` aggregate

- New event-sourced aggregate per (ReturnId, Sku, WarehouseId) keyed by
  `ReturnId`, with its own `Held → Released → Consumed` lifecycle.
- **Rejected** as duplication. The mechanics are identical to reservation;
  a new aggregate would duplicate idempotency tests, expiry scheduling,
  and projection plumbing without semantic gain.

### C. Orders saga orchestrates the reservation

- `CrossProductExchangeRequested` (already published to Orders) would
  trigger the saga to issue `ReserveReplacementForExchange`, await
  `ReplacementReserved` / `ReplacementReservationFailed`, then forward
  the outcome to Returns.
- **Rejected** for this slice. The saga has no business decision to make
  about the outcome — it would be a pure forwarder. The hop adds latency,
  failure surface, and saga state without changing the result.
  Re-evaluate when Slice 3 introduces "exchange in flight" saga state for
  Payments delta-capture coordination.

### D. Synchronous in-process call from Returns to Inventory

- `ApproveExchange` directly invokes Inventory's reservation handler
  via an HTTP client.
- **Rejected** as a BC-boundary violation and a synchronous-coupling
  anti-pattern that would block the CS-agent's HTTP response on cross-BC
  network availability.

---

## References

- `docs/planning/milestones/m45-1-cross-product-exchange-gap-memo.md`
  ("What is missing" row #1 — the gap this ADR's decision closes).
- `docs/planning/milestones/m46-0-retrospective.md` ("What we explicitly
  did not do" — the deferral that motivated this ADR).
- `docs/planning/milestones/m47-0-plan.md` (the milestone plan).
- ADR 0060 — Inventory BC Remaster Rationale (the lifecycle whose
  reservation flow is being reused).
- `src/Inventory/Inventory/Management/ReserveStock.cs` (the existing
  HTTP-driven reservation handler; mirror logic).
- `src/Inventory/Inventory/Management/StockReservationRequestedHandler.cs`
  (the existing integration-message reservation handler; closer mirror).
