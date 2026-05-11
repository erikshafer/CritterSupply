# M47.0 / Slice 4 Retrospective — Cross-Product Exchange Compensation Paths

**Date:** 2026-05-11
**Slice goal:** Close the two remaining `@pending` Gherkin scenarios in `docs/features/returns/cross-product-exchange.feature` and release the Slice 1 inventory reservation on cancellation/rejection paths.
**Status:** ✅ Complete. Cross-product exchange feature is no longer carrying `@pending` debt.
**Pairing:** PSA + UXE + QAE + FEE planning round; PSA + FEE single implementation wave; QAE single test wave with one ping-pong (idempotency guard ordering — see "Ping-pong note" below).

---

## What Landed

### Returns BC

**`src/Returns/Returns/ReturnProcessing/ReturnStatus.cs`**
- New terminal `ReturnStatus.Cancelled` value (added to `IsTerminal` set).

**`src/Returns/Returns/ReturnProcessing/ReturnEvents.cs`** — two new domain events:
- `ExchangeCancelled(ReturnId, Reason, Message, CancelledAt)` — terminal transition.
- `ReplacementReservationConfirmed(ReturnId, InventoryId, Sku, WarehouseId, Quantity, ReservedAt)` — captures the Inventory stream id on the aggregate so the Returns BC never has to re-derive it.

**`src/Returns/Returns/ReturnProcessing/Return.cs`**
- New `ReplacementInventoryId : Guid?` field on the aggregate.
- New `Apply(ExchangeCancelled)` and `Apply(ReplacementReservationConfirmed)` methods.

**`src/Returns/Returns/Integration/ReplacementReservationOutcomeHandler.cs`**
- `Handle(ReplacementReserved)` upgraded from no-op marker to a real handler that appends `ReplacementReservationConfirmed`. Idempotent against redelivery (only appends when `ReplacementInventoryId` is null).

**`src/Returns/Returns/Integration/ExchangeDeltaCaptureFailedHandler.cs`**
- Upgraded from log-only stub to full cancellation handler:
  - `FetchForWriting<Return>` + idempotency guards (missing aggregate / not cross-product / already captured / not in `Approved`).
  - Appends `ExchangeCancelled`; aggregate transitions to `Cancelled`.
  - Publishes `Returns.ExchangeCancelled` integration message.
  - Publishes `Inventory.ReleaseExchangeReservation` when `ReplacementInventoryId` is recorded.
  - Preserves the Slice 2 structured warning log line.

**`src/Returns/Returns/ReturnProcessing/SubmitInspection.cs`**
- Exchange-rejection branch additionally publishes `Payments.RefundExchangeDeltaRequested` when `IsCrossProductExchange && AdditionalPaymentCaptured` and `Inventory.ReleaseExchangeReservation` when `ReplacementInventoryId` is recorded.

**`src/Returns/Returns.Api/Program.cs`**
- Outbound: `Returns.ExchangeCancelled` → `orders-returns-events` and `storefront-returns-events`.
- Outbound: `Payments.RefundExchangeDeltaRequested` → `payments-returns-events`.
- Outbound: `Inventory.ReleaseExchangeReservation` → `inventory-returns-events`.

### Payments BC

**`src/Payments/Payments/Processing/RefundExchangeDeltaHandler.cs`** *(new)*
- Pure static handler that targets the deterministic delta-payment stream (`ExchangePaymentIds.ComputeDeltaPaymentId(returnId)`).
- Refunds via gateway; appends `PaymentRefunded` tagged with `ReturnId`.
- Reuses the existing `Payments.ExchangePartialRefundIssued` reply contract — Storefront / Orders / Backoffice already render it (no new downstream wiring).
- Idempotent under at-least-once: re-emits the reply on redelivery without double-charging the gateway.

### Inventory BC

**`src/Shared/Messages.Contracts/Inventory/ReleaseExchangeReservation.cs`** *(new)*
- New integration contract `(InventoryId, ReservationId, Reason)` so cross-BC reservation release does not need an HTTP shape.

**`src/Inventory/Inventory/Management/ReleaseExchangeReservationHandler.cs`** *(new)*
- Compound handler (Load → Handle) that releases the reservation on the `ProductInventory` aggregate.
- **Fully idempotent against missing reservations** — when the reservation has already been released or expired, the handler is a silent no-op (Returns compensation may race the ExpireReservation timer).
- Publishes the existing `Inventory.ReservationReleased` integration event so downstream consumers (Backoffice dashboards) see the same shape as a regular order-side release.

### Storefront BC

**`src/Customer Experience/Storefront/Notifications/ExchangeCancelledHandler.cs`** *(new)*
- Pure static `Handle()` consuming `Messages.Contracts.Returns.ExchangeCancelled`.
- Emits `SignalRMessage<ReturnStatusChanged>` with `NewStatus = "Cancelled"` scoped to `customer:{CustomerId}`. Carries the verbatim PO-approved Gherkin copy in `Details`.

### Storefront.Web (Blazor)

**`src/Customer Experience/Storefront.Web/RealTime/StorefrontStatusMapper.cs`**
- New `"Cancelled"` case in `BuildReturnMessage` — surfaces the verbatim integration-message copy when present, falls back to a safe generic line otherwise.

**`src/Customer Experience/Storefront.Web/Components/Pages/OrderConfirmation.razor`**
- `GetStatusColor` adds `"Return: Cancelled" → Color.Error` (UXE direction: cancellation is a "things broke" outcome, render in Error rather than the generic Return: warning).
- `GetDisplayStatus` adds `"Return: Cancelled" → "Exchange Cancelled"` (friendlier label).

### Gherkin / Documentation

**`docs/features/returns/cross-product-exchange.feature`**
- Removed `@pending` from "Cross-product exchange with additional payment rejected — refund payment difference".
- Removed `@pending` from "Additional payment capture fails — exchange cancelled".
- Both pinned to a doc comment naming the implementing handler so future readers can navigate directly.

**`tests/Returns/Returns.Api.IntegrationTests/CrossProductExchangePendingTests.cs`** — *deleted*. Both Skip-marked Facts are now exercised by real integration tests.

---

## New tests (QA wave)

**`tests/Payments/Payments.Api.IntegrationTests/Processing/RefundExchangeDeltaHandlerTests.cs`** *(new)* — 3 tests:
- `RefundExchangeDeltaHandler_with_captured_delta_emits_ExchangePartialRefundIssued_against_delta_stream` — happy path, asserts the refund is applied to the delta stream (not the original payment).
- `RefundExchangeDeltaHandler_redelivery_does_not_double_refund_and_re_emits_reply` — idempotency.
- `RefundExchangeDeltaHandler_without_delta_stream_is_silent_no_op` — defensive precondition guard.

**`tests/Returns/Returns.Api.IntegrationTests/InspectionRejectionRefundsDeltaTests.cs`** *(new)* — 2 tests:
- `SubmitInspection_failure_with_captured_delta_publishes_refund_request_and_release` — closes the Path B Gherkin scenario.
- `SubmitInspection_failure_without_captured_delta_does_not_publish_refund_request` — guards against accidental refund publishing on non-captured exchanges.

**`tests/Returns/Returns.Api.IntegrationTests/PaymentsChoreographyHandlersTests.cs`** — replaced the Slice 2 stub-shape test with 3 new tests:
- `ExchangeDeltaCaptureFailedHandler_transitions_return_to_Cancelled_and_publishes_cancellation` — closes the Path A Gherkin scenario.
- `ExchangeDeltaCaptureFailedHandler_releases_held_replacement_reservation_when_known` — Slice 1 reservation release.
- `ExchangeDeltaCaptureFailedHandler_idempotent_on_redelivery` — at-least-once safety.

**`tests/Customer Experience/Storefront.Api.IntegrationTests/SignalRNotificationTests.cs`** — 1 new test:
- `ExchangeCancelled_Handler_ReturnsCustomerScopedReturnStatusChanged`.

**`tests/Customer Experience/Storefront.Web.UnitTests/RealTime/StorefrontStatusMapperCancelledTests.cs`** *(new)* — 3 tests covering verbatim copy, null fallback, empty fallback.

### Test results

- Returns.Api.IntegrationTests: **56 passed**, 6 skipped (pre-existing), 0 failed.
- Payments.Api.IntegrationTests: **34 passed**, 0 failed.
- Inventory.Api.IntegrationTests: **115 passed**, 0 failed (verifies `ReleaseExchangeReservationHandler` did not regress the rest of Inventory).
- Returns.UnitTests: **71 passed**, 0 failed.
- Storefront.Api.IntegrationTests: **1 new test passed**.
- Storefront.Web.UnitTests: **97 passed, 5 pre-existing OrderHistoryTests failures unrelated to Slice 4** (need separate ticket).

---

## Ping-pong note (PSA ↔ QAE)

QAE's first run of `RefundExchangeDeltaHandler_redelivery_does_not_double_refund_and_re_emits_reply` failed with "no `ExchangePartialRefundIssued` received on second delivery". Root cause: `Payment.Apply(PaymentRefunded)` flips `Status` to `Refunded` once `TotalRefunded >= Amount`. Because the delta payment has `Amount == RefundAmount == 25`, the very first refund moved Status away from `Captured`. The handler's preconditions then short-circuited on the redelivery without re-emitting the reply.

PSA fix (single round of ping-pong): reorder the guards in `RefundExchangeDeltaHandler` so the idempotency check (lookup by `ReturnId` in the existing `PaymentRefunded` events) runs **before** the `Status == Captured` guard. The Slice 2 `IssueExchangePartialRefundHandler` does not exhibit this because it refunds against an original payment with `Amount > RefundAmount`, so Status stays `Captured`.

QAE re-ran: 3/3 green. No further iterations needed.

---

## Carry-forwards (out of scope for Slice 4)

These were explicitly scoped out at the planning round and remain open:

- **End-to-end Reqnroll scenario covering Returns → Inventory → Payments → Storefront** for both compensation paths. Per-BC integration tests give us coverage of each seam; an E2E adds runtime cost without proving anything new at this slice.
- **Backoffice timeline for cross-product exchange events** (see Slice 3 retro carry-forward).
- **Persisted return-history view** (see Slice 3 retro carry-forward).
- **MudTimeline component for `OrderConfirmation.razor`** (see Slice 3 retro carry-forward).
- **`OrderHistoryTests` x5 pre-existing failures** in Storefront.Web.UnitTests — discovered while running regression suite; unrelated to Slice 4 scope. Needs a separate ticket.

---

## Closes

- ✅ `docs/features/returns/cross-product-exchange.feature` "Cross-product exchange with additional payment rejected — refund payment difference"
- ✅ `docs/features/returns/cross-product-exchange.feature` "Additional payment capture fails — exchange cancelled"
- ✅ M47.0 plan carry-forward: "Releases the Slice 1 replacement reservation in the rejection / cancellation paths"
- ✅ M47.0 / Slice 2 retro carry-forward: "ExchangeDeltaCaptureFailed compensation path is a TODO"

The cross-product exchange flow now has zero `@pending` Gherkin scenarios and zero acknowledged hand-wave gaps in the BC seams.
