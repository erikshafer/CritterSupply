# Coupon redemption recording

> **Status:** Active (Phase-1)
> **Type:** Choreography (in-process Wolverine + 1 HTTP edge)
> **Initiating actor:** Customer
> **BCs involved:** Shopping, Promotions
> **Most recent material milestone:** M40.0 — Promotions DCB; ADR 0058 supersession

## Purpose

After a coupon has been validated and applied to a cart (see `coupon-and-discount-application.md`), the customer's redemption of that coupon needs to be permanently recorded against the `Coupon` aggregate so per-customer and global redemption caps can be enforced. The recording is choreographed in-process: the `RedeemCoupon` command appends a `CouponRedeemed` event on the `Coupon` stream, and `RecordPromotionRedemptionHandler` reacts to the event by recording the redemption on the `Promotion` stream as well.

## Actors and triggers

- **Initiating actor:** Customer (at checkout / order placement time)
- **Trigger:** A `RedeemCoupon` command sent to Promotions over the in-process Wolverine bus from the Shopping post-checkout path
- **Prerequisite state:** A `Coupon` aggregate exists in Promotions (UUID v5 from `promotions:coupon:{code.ToUpperInvariant()}`); a `Promotion` exists; the coupon has been applied to a cart and the cart has progressed past `InitiateCheckout`

## Trace

| Step | BC | Command / Event / Edge | Resulting state / output | Dossier reference |
|------|----|------------------------|--------------------------|-------------------|
| 1 | Shopping | At post-checkout time, dispatches `Promotions.RedeemCoupon` command in-process | Command on the Wolverine local bus | `bcs/promotions.md#integration-events` |
| 2 | Promotions | `RedeemCouponHandler` loads the `Coupon` DCB boundary (`EventTagQuery` + `[BoundaryModel]` + `IEventBoundary<CouponRedemptionState>`), evaluates redemption limits, appends `CouponRedeemed` to the `Coupon` stream | DCB-bounded append; `CouponRedeemed` returned in `OutgoingMessages` for the next in-process step | `bcs/promotions.md#aggregates`, `bcs/promotions.md#dcb-confirmation` |
| 3 | Promotions | `RecordPromotionRedemptionHandler` consumes `CouponRedeemed` via in-process Wolverine choreography; appends `PromotionRedemptionRecorded` to the `Promotion` stream | Per-promotion counters updated | `bcs/promotions.md#integration-events` |
| 4 | Promotions | `CouponLookupViewProjection` and `PromotionRedemptionProjection` update inline | Read-side reflects new redemption counts | `bcs/promotions.md#projections` |

## Projections and views

- `CouponLookupView` (Promotions) — keyed by code; serves `ValidateCoupon`. Updated on `CouponRedeemed`. Dossier: `bcs/promotions.md#projections`.
- `PromotionRedemptionView` (Promotions) — per-promotion redemption counters. Updated on `PromotionRedemptionRecorded`. Dossier: `bcs/promotions.md#projections`.

## Compensation paths

### Failure: DCB concurrency exception (`DcbConcurrencyException`)
- **Compensating action:** Wolverine retry policy at `Promotions/Program.cs:86-89` retries the `RedeemCouponHandler` until success or exhaustion.
- **Resulting state:** Eventually consistent; the redemption is recorded once.
- **BCs involved in compensation:** Promotions.

### Failure: Coupon over redemption limit at DCB-evaluation time
- **Compensating action:** Handler returns a validation error; no `CouponRedeemed` appended; choreography terminates at step 2.
- **Resulting state:** Cart's `CouponApplied` event remains in the customer's order history but no redemption is recorded.
- **BCs involved in compensation:** Promotions only — no compensating event is dispatched back to Shopping or Orders.

### Failure: `RecordPromotionRedemptionHandler` (step 3) fails after `CouponRedeemed` is persisted
- **Compensating action:** Wolverine durable inbox / retry on the in-process choreography re-runs the handler. No documented compensating event reverses `CouponRedeemed`.
- **Resulting state:** Eventual consistency; if retries exhaust, `PromotionRedemptionRecorded` is not appended and the per-promotion counter drifts from the per-coupon counter.

## Variants and edge cases

### `OrderPlaced` handler is a Phase-1 no-op
Promotions subscribes to `Orders.OrderPlaced` via `OrderPlacedHandler`. Per the source XML-doc (cited in `bcs/promotions.md`), this handler returns empty `OutgoingMessages` as a Phase-1 no-op; the Phase-2 fan-out shape (deriving `RedeemCoupon` from `OrderPlaced.LineItems`'s coupon code) is documented but not implemented. The dossier surfaces this as an S1-vs-code drift: S1 framed `OrderPlaced` as the redemption-recording trigger; in code the trigger is the in-process `RedeemCoupon` dispatch from Shopping post-checkout.

### `RecordPromotionRedemption` command record retained for back-compat
The `RecordPromotionRedemption` command record + validator are defined and discoverable but no `Handle(RecordPromotionRedemption, ...)` exists. The handler class instead reacts to the `CouponRedeemed` event. ADR 0058 governs the supersession. Dossier: `bcs/promotions.md#routes-without-instantiator`.

## BCs and roles

- **Shopping** — Dispatches the `RedeemCoupon` command in-process at post-checkout time. Dossier: `bcs/shopping.md`.
- **Promotions** — Owns both the `Coupon` and `Promotion` aggregates; appends `CouponRedeemed` on the `Coupon` stream and `PromotionRedemptionRecorded` on the `Promotion` stream via in-process choreography. Canonical DCB usage in the codebase. Dossier: `bcs/promotions.md`.

## Tests as behavioral evidence

- **Gherkin features:** none directly cover the redemption-recording choreography.
- **Integration tests:** coverage of `RedeemCouponHandler` + `RecordPromotionRedemptionHandler` lives in the Promotions integration suite (see `bcs/promotions.md#tests-as-behavioral-evidence`).
- **`@pending` / `@wip` / `@future`:** `OrderPlacedHandler` Phase-2 fan-out is documented in source XML-doc as a forward-note, with no corresponding `@future` Gherkin scenario.

## ADRs

- **ADR 0058** — Promotions redemption choreography (RedeemCoupon → CouponRedeemed → RecordPromotionRedemption). Established that the back-compat `RecordPromotionRedemption` command is superseded by reacting to the `CouponRedeemed` event. File: `docs/decisions/0058-*.md` (cited via `bcs/promotions.md`).
- **ADR 0016** — UUID v5 stream IDs (Coupon stream: `promotions:coupon:{code.ToUpperInvariant()}`). File: `docs/decisions/0016-uuid-v5-for-natural-key-stream-ids.md`.

## Declared vs. implemented

- **Declared shape (S1 / CONTEXTS.md):** `RecordPromotionRedemption` flows in from Orders as an integration event triggered by `OrderPlaced`.
- **Implemented shape:** `OrderPlacedHandler` is a no-op; the redemption is triggered by the in-process `RedeemCoupon` dispatched from Shopping at post-checkout. `RecordPromotionRedemption` is a back-compat command record with no handler. ADR 0058 records the supersession.
- **Gap:** S1 framing and CONTEXTS.md prose still imply the `OrderPlaced` → `RecordPromotionRedemption` trigger; code uses the in-process choreography described above.

## Source citations

- Dossier sections referenced: `bcs/promotions.md#aggregates`, `bcs/promotions.md#dcb-confirmation`, `bcs/promotions.md#projections`, `bcs/promotions.md#integration-events`, `bcs/promotions.md#routes-without-instantiator`, `bcs/shopping.md`.
- ADRs: `docs/decisions/0058-*.md` (Promotions redemption supersession), `docs/decisions/0016-uuid-v5-for-natural-key-stream-ids.md`.
