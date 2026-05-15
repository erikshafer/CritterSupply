# Payments

> **Source folder:** `src/Payments/`
> **Status:** Implemented
> **Most recent material milestone:** M47.0 — Cross-product exchange Payments choreography (delta capture / partial refund)
> **Dossier depth:** S2 — full

## Purpose

Payments owns the financial-transaction lifecycle for orders and for cross-product return exchanges. It authorizes and captures payments against an external payment gateway (`IPaymentGateway`, `src/Payments/Payments/Processing/IPaymentGateway.cs`), records failures, and issues refunds. For exchanges that change the customer's price — a more-expensive replacement triggers a delta capture; a cheaper replacement triggers a partial refund — Payments piggybacks on the existing `Payment` aggregate using a deterministically derived stream id so that redelivered requests resolve to the same stream (`src/Payments/Payments/Processing/ExchangePaymentIds.cs`).

## Aggregates

### `Payment`

- **Stream ID:** UUID v7 generated at handler entry for primary order payments (`src/Payments/Payments/Processing/RequestPayment.cs#L35`, `src/Payments/Payments/Processing/AuthorizePayment.cs#L35` — both call `Guid.CreateVersion7()`). For cross-product exchange delta captures, the stream id is a UUID v5 derived from the `ReturnId` under a fixed namespace (`src/Payments/Payments/Processing/ExchangePaymentIds.cs#L40` — `ComputeDeltaPaymentId(returnId)`). The deterministic id is what allows at-least-once redeliveries of `Returns.ExchangeAdditionalPaymentRequired` to land on the same stream and re-emit the prior reply rather than double-charging (`src/Payments/Payments/Processing/CaptureExchangeDeltaHandler.cs#L82`–`L122`).
- **Key state:** `OrderId`, `CustomerId`, `Amount`, `Currency`, `PaymentMethodToken`, `Status` (`PaymentStatus`), `TransactionId`, `AuthorizationId`, `AuthorizationExpiresAt`, `FailureReason`, `IsRetriable`, `TotalRefunded`, computed `RefundableAmount = Amount - TotalRefunded` (`src/Payments/Payments/Processing/Payment.cs#L8`–`L28`).
- **Lifecycle stages:** `Pending` → `Authorized` | `Captured` | `Failed`, then `Captured` → `Refunded` once `TotalRefunded >= Amount` (`src/Payments/Payments/Processing/PaymentStatus.cs`, transitions in `Payment.Apply(...)` at `src/Payments/Payments/Processing/Payment.cs#L47`–`L78`). `Authorized` carries an authorization-expiry seven days after the authorization timestamp and is rejected on capture if expired (`src/Payments/Payments/Processing/Payment.cs#L52`, `src/Payments/Payments/Processing/CapturePayment.cs#L59`–`L65`). `Refunded` is set only when the cumulative refunded amount reaches the captured amount; partial refunds keep the aggregate in `Captured` (`src/Payments/Payments/Processing/Payment.cs#L73`–`L78`).
- **File:** `src/Payments/Payments/Processing/Payment.cs`.

## Commands

All commands target the `Payment` aggregate. Two are aggregate-creating (`StartStream`); two operate on an existing aggregate via Wolverine's aggregate-handler workflow (`[WriteAggregate]`).

**External / saga-driven commands** (issued by callers outside Payments — Orders saga, Backoffice, or HTTP):

- `RequestPayment` — single-shot capture against a payment-method token; starts a new `Payment` stream and emits `PaymentInitiated` plus either `PaymentCaptured` or `PaymentFailed` in the same stream-start. Handler: `src/Payments/Payments/Processing/RequestPayment.cs#L28`. Validator at `src/Payments/Payments/Processing/RequestPayment.cs#L15`.
- `AuthorizePayment` — first phase of a two-phase auth/capture flow; starts a new `Payment` stream and emits `PaymentInitiated` plus either `PaymentAuthorized` (with seven-day expiry) or `PaymentFailed`. Handler: `src/Payments/Payments/Processing/AuthorizePayment.cs#L28`.
- `CapturePayment` — captures a previously authorized `Payment` for the full or a smaller amount. `Before` rejects when the payment is not `Authorized`, the authorization has expired, or the capture amount exceeds the authorized amount (`src/Payments/Payments/Processing/CapturePayment.cs#L41`–`L76`). Handler appends `PaymentCaptured` or `PaymentFailed` to the existing stream and publishes the matching saga-reply integration event. Handler: `src/Payments/Payments/Processing/CapturePayment.cs#L83`.
- `RequestRefund` — issues a refund against a captured `Payment`. `Before` rejects when the payment is not `Captured` or the requested amount exceeds the remaining `RefundableAmount` (`src/Payments/Payments/Processing/RequestRefund.cs#L28`–`L54`). On gateway success, appends `PaymentRefunded` and publishes `RefundCompleted`; on gateway failure, publishes `RefundFailed` and appends no event. Handler: `src/Payments/Payments/Processing/RequestRefund.cs#L26`.

**Cross-product-exchange message-handlers** (added under ADR 0062, M47.0/S2 and S4). These are triggered by inbound integration messages, not by command records local to this BC, so they do not increase the Payments command-record count even though they extend the BC's behavioural surface (see "Reconciliation note" below):

- `CaptureExchangeDeltaHandler` — listens for `Messages.Contracts.Returns.ExchangeAdditionalPaymentRequired` and starts (or finds) the deterministic delta-capture `Payment` stream, then replies with `Payments.ExchangeDeltaCaptured` or `Payments.ExchangeDeltaCaptureFailed`. Handler: `src/Payments/Payments/Processing/CaptureExchangeDeltaHandler.cs#L74`. Per-stream idempotency at `#L90`–`L122`.
- `IssueExchangePartialRefundHandler` — listens for `Messages.Contracts.Payments.ExchangePartialRefundRequested` and appends a `PaymentRefunded` (tagged with `ReturnId`) to the original `Payment` stream for the order, then publishes `Payments.ExchangePartialRefundIssued`. Handler: `src/Payments/Payments/Processing/IssueExchangePartialRefundHandler.cs#L37`. Idempotency check by `ReturnId` against the existing event stream at `#L62`–`L79`.
- `RefundExchangeDeltaHandler` — listens for `Messages.Contracts.Payments.RefundExchangeDeltaRequested` and refunds the captured delta on the deterministic delta-capture `Payment` stream, replying with the same `Payments.ExchangePartialRefundIssued` contract. Handler: `src/Payments/Payments/Processing/RefundExchangeDeltaHandler.cs#L48`.

## Domain events

All five domain events are persisted on the `Payment` event stream. The aggregate's `Apply` methods at `src/Payments/Payments/Processing/Payment.cs#L47`–`L78` are the authoritative state transitions.

- `PaymentInitiated` — payment processing has started for an order; carries `PaymentId`, `OrderId`, `CustomerId`, `Amount`, `Currency`, `PaymentMethodToken`, `InitiatedAt`. Always the first event on a stream (`Payment.Create`, `src/Payments/Payments/Processing/Payment.cs#L30`). File: `src/Payments/Payments/Processing/PaymentInitiated.cs`.
- `PaymentAuthorized` — the gateway has held funds against the payment method without capturing them; sets a seven-day authorization expiry on the aggregate. File: `src/Payments/Payments/Processing/PaymentAuthorized.cs`.
- `PaymentCaptured` — the gateway has moved funds; carries the gateway `TransactionId`. File: `src/Payments/Payments/Processing/PaymentCaptured.cs`.
- `PaymentFailed` — gateway authorization or capture has failed; carries `FailureReason` and an `IsRetriable` flag the Order saga reads to decide retry vs. cancel. File: `src/Payments/Payments/Processing/PaymentFailed.cs`.
- `PaymentRefunded` — a refund has been applied against the payment; carries `RefundAmount`, the running `TotalRefunded`, the gateway `RefundTransactionId`, and an optional `ReturnId` used as the cross-product-exchange idempotency key (`src/Payments/Payments/Processing/PaymentRefunded.cs#L20`–`L26`). The aggregate flips to `Refunded` only when cumulative refunds reach the captured amount. File: `src/Payments/Payments/Processing/PaymentRefunded.cs`.

## Projections

- `Payment` snapshot — **inline** snapshot of the `Payment` aggregate, keyed by `PaymentId` (`Guid Id`); source events: all five domain events listed above. Registered at `src/Payments/Payments.Api/Program.cs#L42` (`opts.Projections.Snapshot<Payment>(SnapshotLifecycle.Inline)`). Serves the `GET /api/payments/{paymentId}` endpoint, the `GET /api/payments?orderId={id}` endpoint, and the `[WriteAggregate] Payment` and `Payment? payment` parameter loads inside the `CapturePayment`, `RequestRefund`, `CaptureExchangeDeltaHandler`, `IssueExchangePartialRefundHandler`, and `RefundExchangeDeltaHandler` flows.

## Integration events

All integration-event records live under `src/Shared/Messages.Contracts/Payments/`. Outbound publishing of the three cross-product-exchange replies is wired to the `returns-payments-events` RabbitMQ queue at `src/Payments/Payments.Api/Program.cs#L105`–`L110`; the inbound `payments-returns-events` queue is the single subscription point for the cross-product-exchange requests at `src/Payments/Payments.Api/Program.cs#L102`.

### Saga replies — drive the Order saga forward

- `PaymentAuthorized` — fields: `PaymentId: Guid, OrderId: Guid, Amount: decimal, AuthorizationId: string, AuthorizedAt: DateTimeOffset, ExpiresAt: DateTimeOffset`. Publisher → subscriber: `Payments` → `Orders`. Emitted from `AuthorizePaymentHandler` (`src/Payments/Payments/Processing/AuthorizePayment.cs#L86`). File: `src/Shared/Messages.Contracts/Payments/PaymentAuthorized.cs`.
- `PaymentCaptured` — fields: `PaymentId: Guid, OrderId: Guid, Amount: decimal, TransactionId: string, CapturedAt: DateTimeOffset`. Publisher → subscriber: `Payments` → `Orders`. Emitted from `RequestPaymentHandler` (`src/Payments/Payments/Processing/RequestPayment.cs#L84`) and `CapturePaymentHandler` (`src/Payments/Payments/Processing/CapturePayment.cs#L128`). File: `src/Shared/Messages.Contracts/Payments/PaymentCaptured.cs`.
- `PaymentFailed` — fields: `PaymentId: Guid, OrderId: Guid, FailureReason: string, IsRetriable: bool, FailedAt: DateTimeOffset`. Publisher → subscriber: `Payments` → `Orders`. Emitted from the failure branches of `RequestPaymentHandler`, `AuthorizePaymentHandler`, and `CapturePaymentHandler`. File: `src/Shared/Messages.Contracts/Payments/PaymentFailed.cs`.
- `RefundCompleted` — fields: `PaymentId: Guid, OrderId: Guid, Amount: decimal, TransactionId: string, RefundedAt: DateTimeOffset`. Publisher → subscriber: `Payments` → `Orders`. Emitted from `RequestRefundHandler` (`src/Payments/Payments/Processing/RequestRefund.cs#L94`). File: `src/Shared/Messages.Contracts/Payments/RefundCompleted.cs`.
- `RefundFailed` — fields: `PaymentId: Guid, OrderId: Guid, FailureReason: string, FailedAt: DateTimeOffset`. Publisher → subscriber: `Payments` → `Orders`. Emitted from the failure branch of `RequestRefundHandler` (`src/Payments/Payments/Processing/RequestRefund.cs#L74`). File: `src/Shared/Messages.Contracts/Payments/RefundFailed.cs`.
- `RefundRequested` — fields: `OrderId: Guid, Amount: decimal, Reason: string, RequestedAt: DateTimeOffset`. Publisher → subscriber: `Orders` → `Payments` (record lives under `Messages.Contracts.Payments` because Payments owns the contract surface). Emitted by Orders' decider/saga (`src/Orders/Orders/Placement/OrderDecider.cs#L176, L394, L608, L784`, `src/Orders/Orders/Placement/Order.cs#L601`). File: `src/Shared/Messages.Contracts/Payments/RefundRequested.cs`.

### Cross-product-exchange Payments↔Returns choreography (ADR 0062)

- `ExchangeAdditionalPaymentRequired` — inbound from Returns. Fields: `ReturnId: Guid, OrderId: Guid, CustomerId: Guid, AmountDue: decimal, RequiredAt: DateTimeOffset`. Publisher → subscriber: `Returns` → `Payments`. File: `src/Shared/Messages.Contracts/Returns/ExchangeAdditionalPaymentRequired.cs`. Drives `CaptureExchangeDeltaHandler`.
- `ExchangePartialRefundRequested` — inbound from Returns (cheaper-replacement happy path). Fields: `ReturnId: Guid, OrderId: Guid, CustomerId: Guid, RefundAmount: decimal, RequestedAt: DateTimeOffset`. Publisher → subscriber: `Returns` → `Payments`. File: `src/Shared/Messages.Contracts/Payments/ExchangePartialRefundRequested.cs`. Drives `IssueExchangePartialRefundHandler`.
- `RefundExchangeDeltaRequested` — inbound from Returns (Slice 4 inspection-rejection compensation). Fields: `ReturnId: Guid, OrderId: Guid, CustomerId: Guid, RefundAmount: decimal, RequestedAt: DateTimeOffset`. Publisher → subscriber: `Returns` → `Payments`. File: `src/Shared/Messages.Contracts/Payments/RefundExchangeDeltaRequested.cs`. Drives `RefundExchangeDeltaHandler`.
- `ExchangeDeltaCaptured` — outbound reply on success of the delta capture. Fields: `ReturnId: Guid, OrderId: Guid, PaymentId: Guid, AmountCaptured: decimal, Currency: string, TransactionId: string, CapturedAt: DateTimeOffset`. Publisher → subscriber: `Payments` → `Returns`. File: `src/Shared/Messages.Contracts/Payments/ExchangeDeltaCaptured.cs`. Emitted at `src/Payments/Payments/Processing/CaptureExchangeDeltaHandler.cs#L94`, `#L203`.
- `ExchangeDeltaCaptureFailed` — outbound reply on failure of the delta capture. Fields: `ReturnId: Guid, OrderId: Guid, AmountDue: decimal, Currency: string, Reason: string, IsRetriable: bool, FailedAt: DateTimeOffset`. Publisher → subscriber: `Payments` → `Returns`. File: `src/Shared/Messages.Contracts/Payments/ExchangeDeltaCaptureFailed.cs`. Emitted at `src/Payments/Payments/Processing/CaptureExchangeDeltaHandler.cs#L107`, `#L154`, `#L189`.
- `ExchangePartialRefundIssued` — outbound reply for both partial-refund flows (cheaper-replacement happy path and Slice 4 delta refund). Fields: `ReturnId: Guid, OrderId: Guid, OriginalPaymentId: Guid, RefundAmount: decimal, Currency: string, TransactionId: string, IssuedAt: DateTimeOffset`. Publisher → subscriber: `Payments` → `Returns`. File: `src/Shared/Messages.Contracts/Payments/ExchangePartialRefundIssued.cs`. Emitted at `src/Payments/Payments/Processing/IssueExchangePartialRefundHandler.cs#L70`, `#L106` and `src/Payments/Payments/Processing/RefundExchangeDeltaHandler.cs#L74`, `#L111`.

## Sagas / orchestration

Not applicable — this BC is not a saga orchestrator. Payments participates in the Order saga (owned by Orders) and in the cross-product-exchange Payments↔Returns choreography (ADR 0062).

- **Saga-driving events Payments produces** (consumed by the Order saga): `PaymentAuthorized`, `PaymentCaptured`, `PaymentFailed`, `RefundCompleted`, `RefundFailed`.
- **Saga-driving events Payments consumes** (issued by the Order saga): `RefundRequested` is the integration message Orders emits when an order is cancelled or fails after capture (`src/Orders/Orders/Placement/OrderDecider.cs#L176`); the local equivalent issued via HTTP is `RequestRefund`.
- **Choreography events Payments produces** (consumed by Returns): `ExchangeDeltaCaptured`, `ExchangeDeltaCaptureFailed`, `ExchangePartialRefundIssued`.
- **Choreography events Payments consumes** (issued by Returns): `ExchangeAdditionalPaymentRequired`, `ExchangePartialRefundRequested`, `RefundExchangeDeltaRequested`.

## HTTP / API surface

`Payments.Api` exposes the local payment / refund commands via Wolverine HTTP (`src/Payments/Payments.Api/Program.cs#L192`), plus two read endpoints. Schema isolation: `payments` (`src/Payments/Payments.Api/Program.cs#L38` via `Constants.Payments`).

- `POST /api/requestpayment` — single-shot capture; mapped from `RequestPayment` via Wolverine HTTP discovery. Handler: `src/Payments/Payments/Processing/RequestPayment.cs#L28`. Auth: protected by the `Backoffice` JWT scheme registered at `src/Payments/Payments.Api/Program.cs#L118`.
- `POST /api/authorizepayment` — first-phase authorization; mapped from `AuthorizePayment`. Handler: `src/Payments/Payments/Processing/AuthorizePayment.cs#L28`. Auth: as above.
- `POST /api/capturepayment` — capture a previously authorized payment; mapped from `CapturePayment`. Handler: `src/Payments/Payments/Processing/CapturePayment.cs#L83`. Auth: as above.
- `POST /api/requestrefund` — issue a refund against a captured payment; mapped from `RequestRefund`. Handler: `src/Payments/Payments/Processing/RequestRefund.cs#L26`. Auth: as above.
- `GET /api/payments/{paymentId}` — fetch a single payment via `AggregateStreamAsync<Payment>`; returns `PaymentResponse` or 404. Handler: `src/Payments/Payments.Api/Processing/GetPaymentEndpoint.cs#L20`. Auth: `[Authorize(Policy = "FinanceClerk")]`.
- `GET /api/payments?orderId={id}` — fetch all payments for an order via `session.Query<Payment>().Where(p => p.OrderId == orderId)`. Handler: `src/Payments/Payments.Api/OrderPayments/GetPaymentsForOrderEndpoint.cs#L55`. Auth: `[Authorize(Policy = "CustomerService")]`. Consumed by Backoffice for customer-service workflows (the source comment at `src/Payments/Payments.Api/OrderPayments/GetPaymentsForOrderEndpoint.cs#L43`–`L44` names this consumer).

The two read endpoints are the only HTTP-callable surface registered under explicit `[Authorize(...)]` policies; the four Wolverine command endpoints inherit the `Backoffice` JWT scheme via `app.UseAuthentication()` / `app.UseAuthorization()` (`src/Payments/Payments.Api/Program.cs#L184`–`L185`).

The cross-product-exchange handlers are message-only — they are reached via the RabbitMQ `payments-returns-events` queue listener at `src/Payments/Payments.Api/Program.cs#L102`, not via HTTP.

## Frontend surface

Not applicable — no frontend in this BC.

## Identity / auth posture

- **Scheme:** JWT Bearer, single scheme `Backoffice` issued by the Backoffice authority at `https://localhost:5249` in development (`src/Payments/Payments.Api/Program.cs#L118`–`L135`).
- **Policies:** `CustomerService` (roles `CustomerService`, `OperationsManager`, `SystemAdmin`), `FinanceClerk` (roles `FinanceClerk`, `OperationsManager`, `SystemAdmin`), `OperationsManager` (roles `OperationsManager`, `SystemAdmin`) — all defined at `src/Payments/Payments.Api/Program.cs#L138`–`L157`.
- **Roles:** `CustomerService`, `FinanceClerk`, `OperationsManager`, `SystemAdmin` (read from the `role` claim — `src/Payments/Payments.Api/Program.cs#L133`).
- **Source:** `src/Payments/Payments.Api/Program.cs` (auth + policies).

The note at `src/Payments/Payments.Api/Program.cs#L116`–`L117` records that Payments does not register the Vendor JWT issuer because no vendor-facing endpoint exists in this BC.

## Tests as behavioral evidence

- Gherkin features: none under `docs/features/payments/` (the directory does not exist). The cross-product-exchange Gherkin scenarios that drive Payments behaviour from the customer side live under `docs/features/returns/cross-product-exchange.feature` and are referenced from the Payments BC source at `src/Shared/Messages.Contracts/Payments/ExchangeDeltaCaptureFailed.cs` and `src/Shared/Messages.Contracts/Payments/RefundExchangeDeltaRequested.cs`.

- Integration tests: `tests/Payments/Payments.Api.IntegrationTests/`
  - `Processing/AuthorizationFlowTests.cs` — 8 tests covering the two-phase auth/capture flow end-to-end (authorize success, authorize failure, capture authorized, capture-not-authorized rejection, expired authorization, partial capture, double capture, capture-on-failed payment).
  - `Processing/PaymentFlowTests.cs` — 3 tests covering the single-shot `RequestPayment` flow (success → captured, gateway failure → failed, retriable failure path).
  - `Processing/RefundFlowTests.cs` — 7 tests covering `RequestRefund` against captured payments (full refund, partial refund, refund-on-uncaptured rejection, over-refund rejection, gateway failure, repeated partial refunds, refund-on-failed-payment rejection).
  - `Processing/CaptureExchangeDeltaHandlerTests.cs` — 4 tests covering the cross-product-exchange delta-capture handler (success, no-original-payment failure, gateway failure, idempotent re-delivery on the deterministic stream id).
  - `Processing/IssueExchangePartialRefundHandlerTests.cs` — 3 tests covering the cheaper-replacement partial-refund handler (success, missing-original silent return, idempotent re-delivery via `ReturnId` on the original `Payment` stream).
  - `Processing/RefundExchangeDeltaHandlerTests.cs` — 3 tests covering the Slice 4 delta-refund compensation handler (success against the deterministic delta-capture stream, missing-delta silent return, idempotent re-delivery).
  - `Processing/GetPaymentsForOrderEndpointTests.cs` — 5 tests covering `GET /api/payments?orderId={id}` (single payment, multiple payments, no payments, refunded payment surfaced, mixed-status set).
  - `Processing/GetPaymentNotFoundTests.cs` — 1 test covering 404 on `GET /api/payments/{paymentId}` for an unknown id.
  - `Processing/CountingPaymentGateway.cs` — test double `IPaymentGateway` that counts gateway invocations; used by the cross-product-exchange handler tests to assert single-call semantics under redelivery.
  - Suite fixture: `TestFixture.cs`, `IntegrationTestCollection.cs`.

- Unit tests: `tests/Payments/Payments.UnitTests/Processing/`
  - `RequestPaymentValidatorTests.cs` — 5 example-based validation tests for `RequestPayment.RequestPaymentValidator`.
  - `RequestPaymentValidatorPropertyTests.cs` — 1 FsCheck property-based validation test for `RequestPayment`.
  - `RequestRefundValidatorPropertyTests.cs` — 3 FsCheck property-based validation tests for `RequestRefund` (rejects empty `PaymentId`, rejects non-positive amounts, omnibus rejection of invalid commands).
  - `PaymentCapturePropertyTests.cs` — 2 FsCheck property-based tests on the `Payment` aggregate's capture transitions.

No `@pending` or `@wip` scenarios are referenced from the test sources reviewed.

## ADRs

- **ADR 0010** — Stripe Payment Gateway Integration with Webhook-Driven Event Handling. Established `IPaymentGateway` as the strategy seam (currently satisfied by `StubPaymentGateway` at `src/Payments/Payments/Processing/StubPaymentGateway.cs`, registered at `src/Payments/Payments.Api/Program.cs#L160`) and committed Payments to a two-phase authorize/capture model aligned with the Order saga. File: `docs/decisions/0010-stripe-payment-gateway-integration.md`.
- **ADR 0062** — Cross-Product Exchange Payments Choreography. Established that Payments piggybacks on the existing `Payment` aggregate for cross-product exchange flows (no separate `ExchangePayment` aggregate), that delta-capture stream ids are deterministic UUID v5 from `ReturnId`, that partial-refund idempotency keys live on `PaymentRefunded.ReturnId`, and that Returns and Payments coordinate directly via the `payments-returns-events` / `returns-payments-events` queues without Orders saga involvement. Added the delta-capture path (`CaptureExchangeDeltaHandler`, `ExchangeDeltaCaptured`, `ExchangeDeltaCaptureFailed`), the cheaper-replacement partial-refund path (`IssueExchangePartialRefundHandler`, `ExchangePartialRefundRequested`, `ExchangePartialRefundIssued`), and the inspection-rejection delta-refund path (`RefundExchangeDeltaHandler`, `RefundExchangeDeltaRequested`). File: `docs/decisions/0062-cross-product-exchange-payments-choreography.md`.

## Prior event modeling

- `docs/planning/saga-discovery-design-session.md` — session that identified Returns (cross-product exchange additional-payment coordination) as the highest-value next saga and documented the integration events Payments produces and consumes for that workflow (lines 64–134 of the document name `ExchangeAdditionalPaymentRequired` and the payment success / failure feedback loop). Informed the contract and idempotency design later codified in ADR 0062.

## Reconciliation note

S1 recorded the Payments BC at 1 aggregate / 5 events / 4 commands. S2 reconciliation against the source confirms: 1 aggregate (`Payment`), 5 domain events (`PaymentInitiated`, `PaymentAuthorized`, `PaymentCaptured`, `PaymentFailed`, `PaymentRefunded`), 4 command records (`RequestPayment`, `AuthorizePayment`, `CapturePayment`, `RequestRefund`).

The ADR 0062 work (M47.0/S2 and S4) added behaviour without adding command records or domain-event records: the three new handlers (`CaptureExchangeDeltaHandler`, `IssueExchangePartialRefundHandler`, `RefundExchangeDeltaHandler`) are dispatched on inbound integration messages (`Returns.ExchangeAdditionalPaymentRequired`, `Payments.ExchangePartialRefundRequested`, `Payments.RefundExchangeDeltaRequested`), and the existing `PaymentRefunded` event was extended with an optional `ReturnId` field rather than replaced. The integration-event surface grew (six new contracts under `src/Shared/Messages.Contracts/Payments/`), but those are not domain events on the `Payment` stream. Aggregate, domain-event, and command counts therefore match S1 exactly.

## Source citations (S2 full)

- `src/Payments/` (folder root)
- `src/Payments/Payments/Processing/Payment.cs`
- `src/Payments/Payments/Processing/PaymentInitiated.cs`
- `src/Payments/Payments/Processing/PaymentAuthorized.cs`
- `src/Payments/Payments/Processing/PaymentCaptured.cs`
- `src/Payments/Payments/Processing/PaymentFailed.cs`
- `src/Payments/Payments/Processing/PaymentRefunded.cs`
- `src/Payments/Payments/Processing/PaymentStatus.cs`
- `src/Payments/Payments/Processing/RequestPayment.cs`
- `src/Payments/Payments/Processing/AuthorizePayment.cs`
- `src/Payments/Payments/Processing/CapturePayment.cs`
- `src/Payments/Payments/Processing/RequestRefund.cs`
- `src/Payments/Payments/Processing/CaptureExchangeDeltaHandler.cs`
- `src/Payments/Payments/Processing/IssueExchangePartialRefundHandler.cs`
- `src/Payments/Payments/Processing/RefundExchangeDeltaHandler.cs`
- `src/Payments/Payments/Processing/ExchangePaymentIds.cs`
- `src/Payments/Payments/Processing/IPaymentGateway.cs`
- `src/Payments/Payments/Processing/StubPaymentGateway.cs`
- `src/Payments/Payments/Processing/PaymentResponse.cs`
- `src/Payments/Payments/Constants.cs`
- `src/Payments/Payments.Api/Program.cs`
- `src/Payments/Payments.Api/Processing/GetPaymentEndpoint.cs`
- `src/Payments/Payments.Api/OrderPayments/GetPaymentsForOrderEndpoint.cs`
- `src/Shared/Messages.Contracts/Payments/PaymentAuthorized.cs`
- `src/Shared/Messages.Contracts/Payments/PaymentCaptured.cs`
- `src/Shared/Messages.Contracts/Payments/PaymentFailed.cs`
- `src/Shared/Messages.Contracts/Payments/RefundCompleted.cs`
- `src/Shared/Messages.Contracts/Payments/RefundFailed.cs`
- `src/Shared/Messages.Contracts/Payments/RefundRequested.cs`
- `src/Shared/Messages.Contracts/Payments/ExchangeDeltaCaptured.cs`
- `src/Shared/Messages.Contracts/Payments/ExchangeDeltaCaptureFailed.cs`
- `src/Shared/Messages.Contracts/Payments/ExchangePartialRefundRequested.cs`
- `src/Shared/Messages.Contracts/Payments/ExchangePartialRefundIssued.cs`
- `src/Shared/Messages.Contracts/Payments/RefundExchangeDeltaRequested.cs`
- `src/Shared/Messages.Contracts/Returns/ExchangeAdditionalPaymentRequired.cs`
- `src/Orders/Orders/Placement/OrderDecider.cs` (`RefundRequested` emission sites)
- `tests/Payments/Payments.Api.IntegrationTests/Processing/AuthorizationFlowTests.cs`
- `tests/Payments/Payments.Api.IntegrationTests/Processing/PaymentFlowTests.cs`
- `tests/Payments/Payments.Api.IntegrationTests/Processing/RefundFlowTests.cs`
- `tests/Payments/Payments.Api.IntegrationTests/Processing/CaptureExchangeDeltaHandlerTests.cs`
- `tests/Payments/Payments.Api.IntegrationTests/Processing/IssueExchangePartialRefundHandlerTests.cs`
- `tests/Payments/Payments.Api.IntegrationTests/Processing/RefundExchangeDeltaHandlerTests.cs`
- `tests/Payments/Payments.Api.IntegrationTests/Processing/GetPaymentsForOrderEndpointTests.cs`
- `tests/Payments/Payments.Api.IntegrationTests/Processing/GetPaymentNotFoundTests.cs`
- `tests/Payments/Payments.Api.IntegrationTests/Processing/CountingPaymentGateway.cs`
- `tests/Payments/Payments.UnitTests/Processing/RequestPaymentValidatorTests.cs`
- `tests/Payments/Payments.UnitTests/Processing/RequestPaymentValidatorPropertyTests.cs`
- `tests/Payments/Payments.UnitTests/Processing/RequestRefundValidatorPropertyTests.cs`
- `tests/Payments/Payments.UnitTests/Processing/PaymentCapturePropertyTests.cs`
- `CONTEXTS.md` (section: `Payments`)
- `docs/decisions/0010-stripe-payment-gateway-integration.md`
- `docs/decisions/0062-cross-product-exchange-payments-choreography.md`
- `docs/planning/saga-discovery-design-session.md`
