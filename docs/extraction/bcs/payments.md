# Payments

> **Source folder:** `src/Payments/`
> **Status:** Implemented
> **Most recent material milestone:** M47.0 — Cross-product exchange Payments choreography (delta capture / partial refund)
> **Stub depth:** S1 — to be deepened in S2

## Purpose

Payments owns the financial-transaction lifecycle for orders and for cross-product return exchanges. It authorizes and captures payments against an external payment gateway, records failures, and issues refunds. For exchanges that change the customer's price (a more-expensive replacement triggers a delta capture; a cheaper replacement triggers a partial refund), Payments piggybacks on the existing `Payment` aggregate using a deterministically derived stream id so that redelivered requests resolve to the same stream.

## Top-level structure

### Aggregates

- `Payment` — stream ID: UUID v7 for primary order payments; UUID v5 derived from the `ReturnId` (`ExchangePaymentIds.ComputeDeltaPaymentId`) for cross-product exchange delta captures.

### Commands

- `RequestPayment`
- `AuthorizePayment`
- `CapturePayment`
- `RequestRefund`

### Domain events

- `PaymentInitiated`
- `PaymentAuthorized`
- `PaymentCaptured`
- `PaymentFailed`
- `PaymentRefunded`

### Projections

- `Payment` snapshot — inline, keyed by `PaymentId` (`Payments.Api/Program.cs`).

### Integration events

- `Payments.PaymentAuthorized` — publishes
- `Payments.PaymentCaptured` — publishes
- `Payments.PaymentFailed` — publishes
- `Payments.RefundCompleted` — publishes
- `Payments.RefundFailed` — publishes
- `Payments.ExchangeDeltaCaptured` — publishes
- `Payments.ExchangeDeltaCaptureFailed` — publishes
- `Payments.ExchangePartialRefundIssued` — publishes
- `Payments.RefundRequested` — bidirectional helper contract
- `Payments.RefundExchangeDeltaRequested` — bidirectional helper contract
- `Payments.ExchangePartialRefundRequested` — subscribes
- `Returns.ExchangeAdditionalPaymentRequired` — subscribes

### HTTP / API surface (one line)

`Payments.Api` exposes the payment / refund commands consumed by the Order saga and (for delta captures) by the Returns BC, plus a payment-read query.

### Frontend surface (if applicable)

Not applicable — no frontend in this BC.

### Identity / auth posture (if applicable)

JWT Bearer (Backoffice scheme); callers are server-side BCs and Backoffice administrators.

## Prior event modeling

- `docs/planning/saga-discovery-design-session.md`

## ADRs

- ADR 0010 — Stripe Payment Gateway Integration with Webhook-Driven Event Handling
- ADR 0062 — Cross-Product Exchange Payments Choreography

## Source citations (S1 stub)

- `src/Payments/`
- `src/Shared/Messages.Contracts/Payments/`
- `CONTEXTS.md` (section: `Payments`)
- `docs/decisions/0010-stripe-payment-gateway-integration.md`
- `docs/decisions/0062-cross-product-exchange-payments-choreography.md`
- `docs/planning/saga-discovery-design-session.md`
