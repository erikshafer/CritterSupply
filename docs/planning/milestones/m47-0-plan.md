# M47.0 — Cross-Product Exchange End-to-End

> **Status:** ✅ Complete (5 slices). See [`m47-0-closeout.md`](./m47-0-closeout.md).
> **Date opened:** 2026-05-08
> **Date closed:** 2026-05-11
> **Source:** `docs/planning/milestones/m45-1-cross-product-exchange-gap-memo.md`
> **Carryover from:** M46.0 retrospective ("What we explicitly did not do")

## Purpose

Land the cross-BC choreography that makes
`docs/features/returns/cross-product-exchange.feature` end-to-end
implementable. The Returns side has been complete since M45.1; what
remains is replacement-SKU reservation (Inventory), additional-payment
delta capture (Payments), refund issuance (Payments), and the
compensation paths for failure modes — plus the Orders saga state needed
to keep an Order from closing while an exchange is in flight.

The gap memo names eight "What is missing" rows; M47.0 closes them
across **four ordered slices**, each landable in a single session.

## Slice plan

| # | Slice | BCs touched | Unflags Gherkin |
|---|-------|-------------|-----------------|
| **1** | **Inventory replacement reservation** *(this PR)* | Inventory + Returns | "replacement out of stock" |
| 2 | Payments delta capture + refund | Payments + Returns | "additional payment is captured", "$25 additional payment is refunded" |
| 3 | Orders saga "exchange in flight" state | Orders | "Order remains open until exchange completes" (implicit) |
| 4 | Compensation + end-to-end test sweep | Returns + cross-cutting | "Payment capture fails — exchange cancelled", remaining `@pending` |

The slice ordering follows the gap memo's "lowest-risk staging" (option 3):
Inventory first because it is the only single-BC slice and resolving
it does not pre-commit any ADR-grade decisions about Payments contracts
or saga state shape.

---

## Slice 1 — Inventory replacement reservation

### Scope

- **Returns** publishes a `ReserveReplacementForExchange` integration
  message when `ApproveExchange` runs the cross-product branch.
- **Inventory** consumes it, attempts to reserve via the existing
  `ProductInventory` reservation lifecycle (reused — see ADR 0061),
  replies with `ReplacementReserved` or `ReplacementReservationFailed`.
- **Returns** consumes the reply. On failure, the Return is denied with
  reason `"Replacement out of stock"` (idempotent — only if still in
  `Approved`).
- The "Cross-product exchange denied — replacement out of stock"
  scenario in `cross-product-exchange.feature` loses its `@pending` tag,
  and the matching skipped placeholder in
  `CrossProductExchangePendingTests.cs` is removed.

### Out of scope for Slice 1

- Routing-aware warehouse selection for exchange replacements (default
  is `WH-01` per ADR 0061).
- Releasing the replacement reservation when the exchange is
  rejected / cancelled — that is **Slice 4** territory because it
  requires Payments-failure / inspection-rejection paths to land first.
- Any Orders saga changes — the existing M45.1 acknowledger handlers
  are sufficient for this slice.
- Any Payments work.

### Architectural decisions captured

- ADR 0061 — replacement reservation reuses `ProductInventory`
  reservation lifecycle; choreography between Returns and Inventory; no
  Orders saga involvement; default warehouse `WH-01`.

### Deliverables

- Three integration contracts in `src/Shared/Messages.Contracts/Inventory/`:
  `ReserveReplacementForExchange.cs`, `ReplacementReserved.cs`,
  `ReplacementReservationFailed.cs`.
- One handler in Inventory:
  `Inventory.Management.ReserveReplacementForExchangeHandler`.
- One handler in Returns:
  `Returns.ReturnProcessing.ReplacementReservationOutcomeHandler`.
- Wiring in `Inventory.Api/Program.cs`: subscribe to
  `inventory-returns-events`; publish `ReplacementReserved` /
  `ReplacementReservationFailed` to `returns-inventory-events`.
- Wiring in `Returns.Api/Program.cs`: subscribe to
  `returns-inventory-events`; publish `ReserveReplacementForExchange`
  to `inventory-returns-events`.
- Modification to `ApproveExchangeHandler`: in the cross-product branch,
  also publish `ReserveReplacementForExchange`.
- Inventory integration tests covering: sufficient stock, insufficient
  stock, missing inventory, idempotency under at-least-once redelivery.
- Returns integration test covering: receipt of
  `ReplacementReservationFailed` denies the exchange (idempotent).
- Removed `@pending` tag from the matching scenario.
- Removed `Out_Of_Stock_Replacement_Denies_Exchange` skipped placeholder.

### Slice 1 acceptance criteria

1. `dotnet build` succeeds with 0 errors.
2. Full Inventory integration suite passes (existing + new).
3. Full Returns integration suite passes (existing + new).
4. The "Cross-product exchange denied — replacement out of stock"
   scenario in `cross-product-exchange.feature` no longer carries the
   `@pending` tag, and the corresponding skipped test in
   `CrossProductExchangePendingTests.cs` is gone.
5. The four other `@pending` scenarios remain `@pending` (their
   Slice 2 / 3 / 4 dependencies are still missing).

---

## Slices 2–4 (placeholder; expanded as each is started)

### Slice 2 — Payments delta capture + refund

Adds Payments handlers for `ExchangeAdditionalPaymentRequired`
(captures delta, emits `ExchangeAdditionalPaymentCaptured`) and for
inspection-rejection-after-capture (refunds delta). Adds Returns
commands `RecordAdditionalPaymentCaptured` /
`RecordPartialRefundIssued` so the `Return` aggregate's status
transitions are driven by Payments callbacks rather than implicit. Will
require its own ADR on whether Payments grows an `ExchangePayment`
aggregate or piggybacks on `Payment`.

### Slice 3 — Orders saga "exchange in flight"

Replaces the M45.1 acknowledger `Handle()` methods in the Order
aggregate with real saga state. Adds explicit "exchange in flight"
gating so the Order saga cannot close while an exchange is mid-flow.
Will require its own ADR on orchestration ownership (Returns vs Orders
saga) — recommendation in the gap memo is "Returns owns Returns-internal
state; Orders saga owns 'is this Order closeable?' via `ActiveReturnIds`."

### Slice 4 — Compensation + end-to-end sweep

Adds the `ExchangeCancelled` event + handler for
"additional-payment-capture fails" and "inspection fails after
additional payment". Adds end-to-end Alba/Reqnroll scenario crossing
all four BCs. Removes the remaining `@pending` tags and the matching
skipped placeholders. Releases the Slice 1 replacement reservation in
the rejection / cancellation paths.

---

## Cross-cutting deliverables (touched across all slices)

- ADR 0061 (Slice 1) and follow-on ADRs per slice.
- `CONTEXTS.md` updated when integration edges change (Slice 1: new
  Returns ↔ Inventory edge for replacement reservation; Slice 2: new
  Returns ↔ Payments edges; etc.).
- A milestone retrospective per slice
  (`m47-0-slice-N-retrospective.md`) following the M46.0 pattern.
