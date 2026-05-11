# ADR 0062 — Cross-Product Exchange Payments Choreography

**Status:** Accepted (PO sign-off pending — see §Open Items)
**Date:** 2026-05-11
**Milestone:** M47.0 — Cross-Product Exchange End-to-End (Slice 2)

---

## Context

ADR 0061 closed the **inventory** half of the cross-product exchange gap memo
(`docs/planning/milestones/m45-1-cross-product-exchange-gap-memo.md`) — the
Returns BC asks the Inventory BC for a replacement-SKU hold and the exchange
is denied if no stock can be reserved.

The memo's other open seam is the **payments** half:

- **Row #4 (`ExchangeAdditionalPaymentCaptured` emitter):** the contract was
  defined and routed for publication, but no handler ever constructed it. The
  customer was told the exchange was approved; nothing ever charged the
  upcharge. (M45.1's `ApproveExchangeHandler` only emits
  `ExchangeAdditionalPaymentRequired`; nothing replied.)
- **Row #5 (`ExchangePartialRefundIssued` emitter):** M45.1 / S3 added a
  placeholder where `ShipReplacementItemHandler` constructed the public
  `Messages.Contracts.Returns.ExchangePartialRefundIssued` directly, but no
  refund actually moved through any payment gateway — it was a "publish into
  the void" (Orders saga forwarded a `Payments.RefundRequested` that no
  handler in Payments BC consumed).

The S2 fix needs three intertwined decisions:

1. **Where does the delta capture live** — a separate `ExchangePayment`
   aggregate, or piggyback on the existing `Payment` aggregate?
2. **Who orchestrates** — the Orders saga (centralised), or direct
   Returns ↔ Payments choreography?
3. **How is idempotency keyed** under at-least-once redelivery of
   `ExchangeAdditionalPaymentRequired` / `ExchangePartialRefundRequested`?

This ADR records each decision and one explicit deviation from the original
M35.0 / S4 Gherkin design that needs Product Owner sign-off before broad
production rollout.

---

## Decision

### 1. Piggyback on the existing `Payment` aggregate

The delta capture starts a **new** `Payment` event stream (with its own
`PaymentInitiated` / `PaymentCaptured` / `PaymentFailed` history) rather than
appending events to the original order's `Payment` stream and rather than
introducing an `ExchangePayment` aggregate.

**Rationale:**

- Reuses the existing `Apply()` / status / refund-tracking semantics of
  `Payment` end to end — the Slice 4 refund/compensation paths get to use
  `RefundableAmount` and the existing `RequestRefund` handler with no new
  code.
- A new aggregate would need its own snapshot, projection, and refund
  bookkeeping — pure code duplication for what is structurally the same
  concept (an authorise/capture/refund lifecycle against a tokenised payment
  method).
- Keeping the original order's `Payment` stream untouched preserves the
  "one stream per gateway transaction" mental model used everywhere else
  in CritterSupply. Mixing capture-of-delta events into the same stream
  would tangle the original order's totals.

The partial-refund path is the inverse — it appends a `PaymentRefunded`
event onto the **original** order's `Payment` stream because semantically
that is what is happening (the original capture is being partially refunded
by the price difference).

### 2. Direct Returns ↔ Payments choreography (no Orders saga involvement)

The four-message conversation flows directly between Returns and Payments
on a pair of dedicated RabbitMQ queues:

| Direction | Queue | Messages |
|-----------|-------|----------|
| Returns → Payments | `payments-returns-events` | `Returns.ExchangeAdditionalPaymentRequired`, `Payments.ExchangePartialRefundRequested` |
| Payments → Returns | `returns-payments-events` | `Payments.ExchangeDeltaCaptured`, `Payments.ExchangeDeltaCaptureFailed`, `Payments.ExchangePartialRefundIssued` |

The Orders saga still **subscribes** to the existing
`Returns.ExchangeAdditionalPaymentCaptured` / `Returns.ExchangePartialRefundIssued`
public messages (re-emitted by Returns after Payments replies), but its
handlers are pure no-op acknowledgers — they exist to keep Wolverine quiet
on the saga's subscription, not to drive any cross-BC work.

**Rationale:**

- Mirrors ADR 0061's choice for the inventory half. Ownership consistency
  matters more than "one orchestrator to rule them all" here — the Returns
  BC is the only place that knows the lifecycle constraints (price
  difference direction, exchange status gating, replacement reservation
  state) needed to decide whether to issue the request.
- Avoids inflating the Orders saga with five new branches just so it can
  forward each message — the saga would learn nothing from the messages it
  did not already know.
- Keeps the Orders saga a strict acknowledger of *outcomes*, not a coordinator
  of *flows*. Slice 3 will add the saga state for "exchange in flight" but
  that is read-only state on top of these choreography signals.

### 3. Deterministic delta-capture `PaymentId` for idempotency

`Payments.Processing.ExchangePaymentIds.ComputeDeltaPaymentId(returnId)`
derives the new delta-capture `Payment.Id` as a UUID-v5 of the `ReturnId`
under a fixed namespace. Two redeliveries of the same
`ExchangeAdditionalPaymentRequired` resolve to the same `Payment.Id`,
the second `CaptureExchangeDeltaHandler` invocation finds an existing
`Payment` document at that id, and re-emits the success / failure reply
**without** calling the gateway a second time and **without** appending
duplicate events.

For the partial-refund path, idempotency is keyed on the `ReturnId`
field added to `PaymentRefunded`. The `IssueExchangePartialRefundHandler`
scans the original `Payment` stream for an existing `PaymentRefunded`
event tagged with the incoming `ReturnId` before calling the gateway.
The new field is optional and defaults to `null`, so pre-M47 events
deserialise safely without migration.

**Rationale:**

- Mirrors the Slice 1 inventory pattern of "re-emit the success reply on
  redelivery, never silently drop, never call the side-effecting collaborator
  twice".
- Avoids a separate "Payments idempotency" document or table — the existing
  Marten event store IS the idempotency log.
- UUID-v5 over a fixed namespace keeps the derivation pure (no clock, no
  randomness), so test assertions can compute the expected id from a known
  `ReturnId` without coupling to handler internals.

### 4. Asymmetric "re-emit on duplicate" between Payments and Returns sides

The Payments-side handlers (`CaptureExchangeDeltaHandler`,
`IssueExchangePartialRefundHandler`) **do** re-emit their public reply on a
duplicate delivery — the gateway is not called a second time, but the reply
contract is published again so a Returns-side redelivery loss never strands
the choreography.

The Returns-side handlers (`ExchangeDeltaCapturedHandler`,
`ExchangePartialRefundIssuedHandler`) take the inverse stance — once the
domain event has been appended to the Return stream, redeliveries are pure
no-ops; they do **not** republish the public
`Returns.ExchangeAdditionalPaymentCaptured` /
`Returns.ExchangePartialRefundIssued` a second time.

This is deliberate. The Returns-side public republish is an
order-of-operations signal to Storefront / Backoffice / Orders saga, not an
ack the Payments side waits on. Re-emitting it on every redelivery would
spam SignalR clients and dashboards with duplicate "your refund has been
issued" notifications without adding any reliability — Storefront / Orders
are themselves at-least-once consumers and their own subscription redelivery
covers a missed first emission.

The asymmetry mirrors `ReplacementReservationOutcomeHandler` (Slice 1) on
the Returns side and `RequestPayment` (legacy) on the Payments side. If a
future BC needs the at-least-once republish from Returns, it should be
added explicitly, not by removing the guard here.

---

## Public Contract Changes

The two pre-existing `Messages.Contracts.Returns` integration messages were
extended to carry currency + payment-method reference so downstream
consumers (Storefront, Backoffice, Notifications) can format and link
without re-querying Payments:

- `ExchangeAdditionalPaymentCaptured` adds `Guid PaymentId`, `string Currency`,
  `string PaymentReference`.
- `ExchangePartialRefundIssued` adds `Guid OriginalPaymentId`, `string Currency`,
  `string TransactionId`.

These were green-field fields (no production consumers shipped against the
M45.1 placeholder — the messages were never actually constructed for
`ExchangeAdditionalPaymentCaptured`, and the M45.1 `ExchangePartialRefundIssued`
emission was only consumed by the Orders saga's now-deprecated
`RefundRequested` forwarder), so the addition is non-breaking.

Four new contracts were added to `Messages.Contracts.Payments/`:

- `ExchangeDeltaCaptured` (success reply, Payments → Returns)
- `ExchangeDeltaCaptureFailed` (failure reply, Payments → Returns; Slice 2
  handler is a structured-warning stub — Slice 4 turns it into the
  customer-visible cancellation path)
- `ExchangePartialRefundRequested` (request, Returns → Payments)
- `ExchangePartialRefundIssued` (success reply, Payments → Returns)

---

## Deviation From M35.0 / S4 Gherkin (PO sign-off needed)

The M35.0 / S4 Gherkin scenario "Cross-product exchange — replacement
costs more" reads (paraphrased): customer requests exchange → CS approves →
**customer is presented an authorisation interstitial and provides
payment** → upcharge is captured.

Slice 2 collapses the "customer provides payment" step into the original
approval. The customer's existing payment method (the one used for the
original order) is reused for the delta capture; no UI interstitial is
shown.

**Why this deviation:**

- The original order's payment-method token is already on file in the
  `Payment` aggregate; reusing it gives an "approve once, captured
  automatically when ready" UX that matches how every other CritterSupply
  cross-BC flow (subscription renewals, reorders) works.
- The interstitial would require a customer-facing approval surface that
  Slice 2 does not have time to design and build, and that Slice 3
  (Storefront timeline integration) would need to also coordinate with.
- Card-on-file with implicit reuse for a customer-initiated exchange is
  industry-standard. The customer's intent is clear (they asked for the
  exchange and were quoted the price difference at approval time).

**Why this needs PO sign-off:**

- Some payment regulations (PSD2 SCA, certain card-network rules) treat a
  reused payment method for a *new* charge as a separate transaction that
  may need a fresh authentication challenge. The Slice 2 stub gateway does
  not exercise this, but a real gateway integration may.
- The Gherkin specification is the documented customer experience; this
  ADR should not be the sole place the deviation lives.

**Required PO actions before broad production exposure:**

1. Confirm the no-interstitial UX is acceptable for the M47.0 release.
2. Decide whether a Storefront-side soft confirmation ("we will charge
   $X to your card ending in 4242 to ship the replacement") is required;
   if so, add it as a Slice 3 acceptance criterion.
3. Approve the Gherkin revision in
   `docs/features/returns/cross-product-exchange.feature` that collapses
   the explicit payment step.

Until PO sign-off lands, the path is feature-flag-friendly: the
`CaptureExchangeDeltaHandler` is the single switch point — a
configuration-driven guard (deferred from this ADR; trivial to add when
needed) could re-route the request to a manual-approval queue.

---

## Consequences

### Positive

- The M45.1 gap memo's "constructed but no money moved" anti-pattern is
  fully resolved for both the more-expensive and cheaper-replacement
  paths in the happy case.
- Payments BC owns gateway interaction end to end; Returns BC owns
  exchange-lifecycle state. Neither leaks into the other.
- Idempotency is provable by reading the Marten event store — the
  deterministic `Payment.Id` for delta captures and the `ReturnId`-tagged
  `PaymentRefunded` events form an audit trail that survives deployment
  rollbacks.
- The Orders saga's `Handle(ExchangePartialRefundIssued)` becomes a true
  no-op acknowledger, eliminating the M45.1 "double refund" foot-gun where
  Payments would be refunding once via the new path AND once via the old
  Orders → Payments `RefundRequested` forward (the forward never actually
  worked because Payments had no consumer for `RefundRequested`, but if it
  ever shipped a consumer in the future the bug would have surfaced).

### Negative

- A second `Payment` stream per cross-product exchange. Reporting that
  rolls up "total paid by customer for order X" needs to either follow
  the `OrderId` link on the delta-capture `Payment` or aggregate by
  the original `Payment` plus the delta — two queries instead of one.
  Slice 3 (Storefront integration) will surface this if it bites.
- Two new RabbitMQ queues in the Payments BC (Slice 1 added two for
  Inventory; this slice adds two for Returns). The fan-out is growing but
  remains O(BCs Payments talks to), not O(message types).
- The Payments BC now has cross-BC integration code (Returns request
  handlers) instead of being purely Order-facing. ADR 0062's rationale
  treats this as acceptable; future BCs (e.g. Subscriptions) will follow
  the same pattern.
- The Slice 4 cancellation paths still need to be built. Today, a delta
  capture failure leaves the Returns aggregate in `Approved` with a held
  inventory reservation and a structured warning log — manual ops
  intervention is required to deny the exchange and release the reservation.

---

## Alternatives Considered

### Alternative A — Centralise in the Orders saga

Have the Orders saga subscribe to all four Returns ↔ Payments contracts
and turn each into a fan-out command (`CapturePayment` / `RequestRefund`)
to Payments.

**Rejected because:** the saga already has 11 handlers; adding 5 more
that mirror what Returns + Payments could do directly bloats the
aggregate without adding behavior. The Inventory choreography (ADR 0061)
also bypasses the saga, so consistency favours direct Returns ↔ Payments.

### Alternative B — Separate `ExchangePayment` aggregate

Introduce a new `ExchangePayment` aggregate alongside `Payment`, with its
own status enum and refund tracking.

**Rejected because:** the lifecycle is identical to `Payment` (initiate
→ capture / fail; refund). The only difference is "the customer's intent
was an exchange, not a fresh order" — which is metadata, not structure.
A new aggregate would duplicate every Marten projection and every Slice 4
refund handler.

### Alternative C — Append delta capture events to the original `Payment` stream

Append a second `PaymentInitiated` + `PaymentCaptured` pair onto the
existing original `Payment` stream.

**Rejected because:** the `Payment.Apply` methods would have to learn
"when the second `PaymentInitiated` arrives, branch to a sub-state" —
the aggregate becomes a hidden saga. The `Amount` / `RefundableAmount`
math gets ambiguous (does refundable include or exclude the delta?).
Far cleaner to keep one stream per gateway transaction.

### Alternative D — Synchronous HTTP from Returns to Payments

Have `ApproveExchangeHandler` make a direct HTTP call to a Payments
endpoint and only emit the Returns events on success.

**Rejected because:** breaks the choreography pattern used everywhere
else in CritterSupply, couples the Returns API's request latency to
Payments + the gateway, and loses the durable inbox/outbox guarantees
that make at-least-once redelivery the correct mental model.

---

## Open Items

1. **PO sign-off** on the no-authorisation-interstitial UX deviation
   (see §Deviation above). Until granted, the Gherkin revision in
   `docs/features/returns/cross-product-exchange.feature` should be
   reviewed jointly by PO + UXE.
2. **Slice 4 cancellation paths.** Today
   `ExchangeDeltaCaptureFailedHandler` is a structured-warning stub.
   Slice 4 will turn it into a customer-visible "exchange cancelled"
   transition that releases the inventory reservation.
3. **Reporting roll-ups.** A future "total paid by customer for
   order X" query needs to traverse both the original `Payment` and
   any delta-capture `Payment` streams. Out of scope for M47.0 but
   should be tracked.
