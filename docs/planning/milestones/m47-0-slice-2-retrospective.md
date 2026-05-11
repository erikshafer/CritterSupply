# M47.0 / Slice 2 Retrospective — Payments Delta Capture & Partial Refund

**Date:** 2026-05-11
**Slice goal:** Close `m45-1-cross-product-exchange-gap-memo.md` rows #3, #4, #5 — Payments BC actually moves the money for cross-product exchanges in both directions (delta capture for more-expensive replacements; partial refund for cheaper replacements).
**Status:** ✅ Complete (happy paths). Slice 4 still owns customer-visible cancellation paths.
**Pairing:** Principal Software Architect ↔ QA Engineer ping-pong (one defect found, one round of fixes, all green).

---

## What Landed

### New code

**Payments BC**
- `Payments.Processing.ExchangePaymentIds` — UUID-v5 helper for the deterministic delta-capture `PaymentId`
- `Payments.Processing.CaptureExchangeDeltaHandler` — handles `Returns.ExchangeAdditionalPaymentRequired`; piggybacks on the existing `Payment` aggregate; idempotent on redelivery (re-emits success/failure reply, never calls gateway twice)
- `Payments.Processing.IssueExchangePartialRefundHandler` — handles `Payments.ExchangePartialRefundRequested`; appends `PaymentRefunded` (tagged with `ReturnId`) to the original `Payment` stream
- `Payments.Processing.PaymentRefunded` — added optional `ReturnId` field for refund idempotency keying (default null preserves all existing behaviour)
- `Payments.Api/Program.cs` — first-ever RabbitMQ wiring for the Payments BC: listens on `payments-returns-events`, publishes to `returns-payments-events`

**Returns BC**
- `Returns.Integration.ExchangeDeltaCapturedHandler` — appends `ExchangeAdditionalPaymentCaptured` domain event + republishes the public `Returns.ExchangeAdditionalPaymentCaptured` integration message to the existing fan-out
- `Returns.Integration.ExchangeDeltaCaptureFailedHandler` — Slice 2 stub: structured warning log via `ILogger<ExchangeDeltaCaptureFailedLog>` so stranded exchanges are discoverable in operational logs (UXE condition #2). Customer-visible cancellation deferred to Slice 4
- `Returns.Integration.ExchangePartialRefundIssuedHandler` — appends `ExchangePartialRefundIssued` domain event + republishes the public message
- `Returns.ReturnProcessing.ShipReplacementItem` — refactored: no longer constructs `ExchangePartialRefundIssued` directly. Now publishes `Payments.ExchangePartialRefundRequested` to trigger the Payments-side handler
- `Returns.Api/Program.cs` — listens on `returns-payments-events`, publishes new `Payments.ExchangePartialRefundRequested` + the existing `Returns.ExchangeAdditionalPaymentRequired` (now also routed to `payments-returns-events`)

**Orders BC**
- `Orders.Placement.Order.Handle(Returns.ExchangePartialRefundIssued)` — became a no-op acknowledger. Was forwarding `Payments.RefundRequested`; would have **double-refunded** the customer once the new path landed (Payments now refunds directly via the choreography). Regression-guarded by a unit test

**Public contracts**
- `Messages.Contracts.Payments.ExchangeDeltaCaptured` (new)
- `Messages.Contracts.Payments.ExchangeDeltaCaptureFailed` (new)
- `Messages.Contracts.Payments.ExchangePartialRefundRequested` (new)
- `Messages.Contracts.Payments.ExchangePartialRefundIssued` (new)
- `Messages.Contracts.Returns.ExchangeAdditionalPaymentCaptured` extended with `PaymentId`, `Currency`, `PaymentReference` (UXE condition #1)
- `Messages.Contracts.Returns.ExchangePartialRefundIssued` extended with `OriginalPaymentId`, `Currency`, `TransactionId` (UXE condition #1)

### New tests (QA Engineer wave)

**Payments BC** — `tests/Payments/Payments.Api.IntegrationTests/Processing/`
- `CountingPaymentGateway` — wraps `StubPaymentGateway` with call counting; lets the fixture assert "the gateway was hit exactly once on redelivery"
- `CaptureExchangeDeltaHandlerTests` — 4 tests: happy path, idempotent redelivery, no-original-payment failure (NonRetriable), declining-gateway failure
- `IssueExchangePartialRefundHandlerTests` — 3 tests: happy path, idempotent redelivery, no-original-payment silent return

**Returns BC** — `tests/Returns/Returns.Api.IntegrationTests/PaymentsChoreographyHandlersTests.cs`
- 4 tests: delta-captured happy path, delta-captured idempotency, partial-refund-issued happy path (initially failed — see Defect #1 below), failed-capture state-isolation

**Test fixture extensions**
- `Payments.Api.IntegrationTests.TestFixture` — registers `CountingPaymentGateway` over the production `StubPaymentGateway`; `CleanAllDocumentsAsync` now also calls `DeleteAllEventDataAsync` (event streams used to leak between tests, breaking deterministic-id idempotency assertions)

### Documentation

- **ADR 0062** — `docs/decisions/0062-cross-product-exchange-payments-choreography.md`. Captures the four key choices: (1) piggyback on `Payment` aggregate, (2) direct Returns ↔ Payments choreography (no Orders saga involvement), (3) deterministic delta `PaymentId` + `ReturnId`-tagged `PaymentRefunded` for idempotency, (4) deliberate Returns-side / Payments-side asymmetry on "re-emit on duplicate". Names the **no-authorisation-interstitial deviation** explicitly with a "PO sign-off needed before broad production exposure" callout (UXE condition #3 first half)
- **`docs/features/returns/cross-product-exchange.feature`** — unflagged the two `@pending` scenarios for cheaper-replacement and more-expensive-replacement; revised the more-expensive scenario to match the implemented "reuse original payment method" UX (no interstitial step) (UXE condition #3 second half)
- **`tests/Returns/Returns.Api.IntegrationTests/CrossProductExchangePendingTests.cs`** — deleted the two `[Fact(Skip=…)]` placeholders for cheaper-replacement and more-expensive-replacement; left the two genuinely-Slice-4 placeholders (capture-failure cancellation, inspection-rejection refund) with updated Skip reasons referencing M47.0 / S2 vs Slice 4
- **`CONTEXTS.md`** — added the Returns ↔ Payments edge to both the Payments and Returns BC entries

---

## How It Went

### What worked

- **The Slice 1 ADR template made Slice 2 cheap.** ADR 0061's structure (Decision / Rationale per choice + Alternatives Considered + Consequences) was the right shape for Slice 2's payment-side mirror. Wrote ADR 0062 in 30 minutes.
- **UXE pre-flight review caught three real things.** The `Currency` + `PaymentReference` extension to public messages, the structured warning log on the failed-capture stub, and the no-interstitial deviation flag all materially improved the slice. None would have been caught by post-hoc review.
- **PSA ↔ QA ping-pong cycle was tight.** One PSA wave (~2hr), one QA wave (~3hr including standing up the Payments BC's first-ever integration test fixture from scratch), one defect, one ~10-min PSA fix, all green. No second QA wave needed.
- **Idempotency-by-deterministic-id pattern carried over cleanly.** Same shape as Slice 1's inventory `ProductInventory` reservation lookup. The team is now familiar enough with this pattern that "what's the idempotency key?" is the first question asked, not the last.
- **`PaymentRefunded.ReturnId` as an optional field** turned out to be the cleanest possible additive change. No event migration; pre-M47 events deserialise with `null`; new code branches on `!= null` for cross-product-exchange refunds only. Worth remembering for future "I need to tag an existing event with new metadata" situations.

### What broke

- **Defect #1 (PSA → caught by QA in test #10).** `ExchangePartialRefundIssuedHandler` used `aggregate.FinalRefundAmount is not null` as the idempotency guard. But `ShipReplacementItemHandler` already sets `FinalRefundAmount` via `ExchangeCompleted.PriceDifferenceRefund` as a *precondition* of triggering the partial refund — so the guard fired on every realistic delivery. The handler appended nothing and republished nothing. Real-world impact would have been: Storefront / Backoffice / Orders never see the public `Returns.ExchangePartialRefundIssued` integration message; the M45.1 "constructed but no money moved (visibly)" anti-pattern reincarnates in a slightly different form.
  - **Fix:** Scan the Return event stream for an existing `ExchangePartialRefundIssued` domain event (mirrors the Payments-side `PaymentRefunded.ReturnId` lookup pattern). Test #10 now passes without modification.
  - **Lesson:** When choosing an idempotency key for an event, be specific to the event itself. Reusing a field that the broader workflow also sets risks coincidental collision with an unrelated state precondition.

### Trade-offs accepted

- **Returns-side handlers do NOT re-emit on duplicate** while Payments-side handlers DO. This is intentional asymmetry: the Payments-side reply is a reliability signal the Returns side waits on, whereas the Returns-side public republish is a notification the downstream consumers are themselves at-least-once on. Asymmetry documented in ADR 0062. Caught by QA Observation #1 — folded into the ADR as a deliberate design choice with stated rationale.
- **`ExchangeDeltaCaptureFailed` is a structured-log stub.** Slice 4 owns the customer-visible "exchange cancelled" path. Today, a delta-capture failure leaves the Return aggregate in `Approved` with a held inventory reservation and a structured warning. Manual ops intervention (deny the return + release the reservation) is required to unblock. Acceptable as an interim under the assumption that the gateway failure rate against `tok_success_*` tokens in production is near zero.
- **No automated log-capture assertion** on the warning stub. The Returns BC test fixture doesn't yet wire `Microsoft.Extensions.Logging.Testing.FakeLogger`. QA recommended a future slice add this — small standalone task. For Slice 2, the substitute `ExchangeDeltaCaptureFailedHandler_does_not_mutate_state` test exercises the marker type and guards against accidental mutation. Tracked.

### Things to remember next slice

- **Slice 3 will need to consume the new public-message fields.** Storefront timeline UI should display the `Currency` + `PaymentReference` (delta capture) and `Currency` + `TransactionId` (refund) — that's why we added them. UXE: please confirm display copy for these.
- **Slice 4's compensation paths get easier because of Slice 2's idempotency keys.** A `RefundExchangeDelta` command can re-target the existing delta-capture `Payment` stream by computing `ExchangePaymentIds.ComputeDeltaPaymentId(returnId)` deterministically. No "where did we capture the delta?" lookup needed.
- **PO sign-off on the no-interstitial deviation** is the only unresolved doc item from this slice. Tracked in ADR 0062 §Open Items. Until then, the path is feature-flag-friendly — the `CaptureExchangeDeltaHandler` is the single switch point.

---

## Numbers

- **Production code:** 19 files changed, 879 insertions / 122 deletions (PSA wave 1 commit). Plus the post-QA defect-fix commit (~30 lines).
- **Test code:** ~600 LoC across 4 new files (Payments × 3, Returns × 1) + fixture wiring.
- **Tests passing post-slice:** 71 Returns Unit + 213 Orders Unit + 31 Payments IT + 52 Returns IT (+ 10 unrelated pre-existing skips).
- **Tests removed:** 2 `[Fact(Skip=…)]` placeholders deleted (the two scenarios this slice closed).
- **Tests modified for shape changes:** 2 (`ShipReplacementItemHandlerTests`, `OrderSagaCrossProductExchangeTests`) — both now reflect the M47.0 / S2 reality.
- **Defects found by QA, fixed by PSA:** 1 (`ExchangePartialRefundIssuedHandler` short-circuit).

---

## Carry-Forward to Slice 3 / Slice 4

| Item | Owner | Notes |
|------|-------|-------|
| PO sign-off on no-interstitial UX deviation | PO + UXE | Tracked in ADR 0062 §Open Items |
| Storefront timeline displays the new public-message fields | UXE + frontend | New fields: `PaymentId` / `Currency` / `PaymentReference` (capture) and `OriginalPaymentId` / `Currency` / `TransactionId` (refund) |
| Customer-visible cancellation when delta capture fails | Slice 4 PSA | `ExchangeDeltaCaptureFailedHandler` becomes the trigger; release inventory reservation; mark Return as Cancelled |
| Refund of additional payment on inspection rejection | Slice 4 PSA | `SubmitInspection` reject path emits `RefundAdditionalPayment` when `IsCrossProductExchange && AdditionalPaymentCaptured` |
| Log-capture helper in Returns IT fixture | QA / future slice | Allow assertions on `ExchangeDeltaCaptureFailedLog` warnings |
| Reporting roll-up for "total paid by customer for order X" | Future | Now needs to traverse original Payment + delta-capture Payment streams |
