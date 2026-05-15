# Returns

> **Source folder:** `src/Returns/`
> **Status:** Implemented
> **Most recent material milestone:** M47.0 — Cross-product exchange end-to-end (Returns ↔ Inventory ↔ Payments ↔ Storefront)
> **Stub depth:** S1 — to be deepened in S2

## Purpose

Returns owns the post-delivery customer journey for items being sent back. A return request goes through eligibility checking, approval or denial, physical receipt, inspection (pass / fail / mixed), and then either a refund or an exchange. Exchanges may be same-SKU or cross-product; cross-product exchanges with a price difference trigger a delta capture or partial refund through Payments and a replacement reservation through Inventory. The eligibility window is 30 days post-delivery.

## Top-level structure

### Aggregates

- `Return` — stream ID: UUID v7.

### Commands

- `RequestReturn`
- `ApproveReturn`
- `DenyReturn`
- `ReceiveReturn`
- `StartInspection`
- `SubmitInspection`
- `ApproveExchange`
- `DenyExchange`
- `ShipReplacementItem`
- `ExpireReturn`

### Domain events

- `ReturnRequested`, `ReturnApproved`, `ReturnDenied`, `ReturnReceived`, `ReturnExpired`
- `InspectionStarted`, `InspectionPassed`, `InspectionFailed`, `InspectionMixed`
- `ExchangeApproved`, `ExchangeDenied`, `ExchangeReplacementShipped`, `ExchangeCompleted`, `ExchangeRejected`, `ExchangeCancelled`
- `CrossProductExchangeRequested`, `ExchangePriceDifferenceCalculated`, `ExchangeAdditionalPaymentRequired`, `ExchangeAdditionalPaymentCaptured`, `ExchangePartialRefundIssued`
- `ReplacementReservationConfirmed`

### Projections

- `Return` snapshot — inline, keyed by `ReturnId` (`Returns.Api/Program.cs`).

### Integration events

- `Returns.ReturnRequested`, `ReturnApproved`, `ReturnDenied`, `ReturnReceived`, `ReturnCompleted`, `ReturnRejected`, `ReturnExpired` — publishes
- `Returns.ExchangeRequested`, `ExchangeApproved`, `ExchangeDenied`, `ExchangeRejected`, `ExchangeCompleted`, `ExchangeReplacementShipped`, `ExchangeCancelled`, `ExchangePartialRefundIssued` — publishes
- `Returns.CrossProductExchangeRequested`, `ExchangeAdditionalPaymentRequired`, `ExchangeAdditionalPaymentCaptured` — publishes
- `Inventory.ReplacementReserved` / `ReplacementReservationFailed` — subscribes
- `Payments.ExchangeDeltaCaptured` / `ExchangeDeltaCaptureFailed` / `ExchangePartialRefundIssued` — subscribes
- `Fulfillment.ShipmentDelivered` — subscribes (eligibility window start)

### HTTP / API surface (one line)

`Returns.Api` exposes return-request commands, inspection commands, and the return read model consumed by the storefront BFF and Backoffice.

### Frontend surface (if applicable)

Not applicable — no frontend in this BC.

### Identity / auth posture (if applicable)

JWT Bearer (Backoffice + Vendor schemes).

## Prior event modeling

None on file.

## ADRs

- ADR 0061 — Cross-Product Exchange Replacement Reservation
- ADR 0062 — Cross-Product Exchange Payments Choreography

## Source citations (S1 stub)

- `src/Returns/`
- `src/Shared/Messages.Contracts/Returns/`
- `CONTEXTS.md` (section: `Returns`)
- `docs/decisions/0061-cross-product-exchange-replacement-reservation.md`
- `docs/decisions/0062-cross-product-exchange-payments-choreography.md`
