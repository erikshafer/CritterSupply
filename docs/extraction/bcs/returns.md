# Returns

> **Source folder:** `src/Returns/`
> **Status:** Implemented
> **Most recent material milestone:** M47.0 — Cross-product exchange end-to-end (Returns ↔ Inventory ↔ Payments ↔ Storefront)
> **Dossier depth:** S2 — full

## Purpose

Returns owns the post-delivery customer journey for items being sent back. A return request runs through eligibility checking, manual or auto approval, physical receipt at the warehouse, inspection (pass / fail / mixed), and then either a refund or an exchange. Exchanges may be same-SKU or cross-product; cross-product exchanges with a price difference trigger a delta capture or partial refund through Payments and a replacement-stock reservation through Inventory. The eligibility window is 30 days from delivery confirmation (`ReturnEligibilityWindow.ReturnWindowDays`, `src/Returns/Returns/ReturnProcessing/ReturnEligibilityWindow.cs#L18`).

## Aggregates

### `Return`

- **Stream ID:** UUID v7. Generated inside the request handler at stream start (`Guid.CreateVersion7()`, `src/Returns/Returns/ReturnProcessing/RequestReturn.cs#L115`) and used as the Marten event-stream id (`session.Events.StartStream<Return>(returnId, requested)`, `src/Returns/Returns/ReturnProcessing/RequestReturn.cs#L139`). The `ReturnId` is also used as the cross-product exchange `ReservationId` in the Inventory choreography per ADR 0061 (`ReleaseExchangeReservation.ReservationId == ReturnId`, `src/Returns/Returns/Integration/ExchangeDeltaCaptureFailedHandler.cs#L121`).
- **Key state (drives invariants):**
  - `Status: ReturnStatus` — drives every command guard (`Before` checks in each command handler).
  - `Type: ReturnType` (`Refund` or `Exchange`) — branches `SubmitInspectionHandler` and `ShipReplacementItemHandler` (`src/Returns/Returns/ReturnProcessing/SubmitInspection.cs#L89`).
  - `IsCrossProductExchange: bool` — set by `Apply(CrossProductExchangeRequested)` (`src/Returns/Returns/ReturnProcessing/Return.cs#L197`); guards every Inventory/Payments choreography handler in `Returns.Integration`.
  - `AdditionalPaymentCaptured: bool` and `AdditionalPaymentAmount: decimal?` — track captured delta state for compensation (`src/Returns/Returns/ReturnProcessing/Return.cs#L212`, used by inspection-rejection refund branch in `SubmitInspection.cs#L109-L120`).
  - `ReplacementInventoryId: Guid?` — captured on `ReplacementReservationConfirmed`; consumed when releasing the held reservation on cancellation/rejection (`src/Returns/Returns/ReturnProcessing/Return.cs#L224`, `src/Returns/Returns/Integration/ExchangeDeltaCaptureFailedHandler.cs#L117`).
  - `PriceDifference: decimal?` — set by `Apply(ExchangeApproved)` and `Apply(ExchangePriceDifferenceCalculated)`; positive value flags a cheaper-replacement partial refund in `ShipReplacementItemHandler` (`src/Returns/Returns/ReturnProcessing/ShipReplacementItem.cs#L71`).
  - `ShipByDeadline: DateTimeOffset?` — used to schedule the `ExpireReturn` self-message (`src/Returns/Returns/ReturnProcessing/ApproveExchange.cs#L166`, `RequestReturn.cs#L178`).
  - `IsTerminal` derived flag — true for `Denied`, `Completed`, `Rejected`, `Expired`, `Cancelled` (`src/Returns/Returns/ReturnProcessing/Return.cs#L42-L44`).
- **Lifecycle stages.** The active lifecycle is 10 states drawn from `ReturnStatus` (`src/Returns/Returns/ReturnProcessing/ReturnStatus.cs#L3-L21`). Two further enum values, `LabelGenerated` and `InTransit`, are declared with the comment `// Phase 2 — carrier integration` and are never assigned by any handler in the current code; they are not counted among the 10 active states (matches the CONTEXTS.md figure).
  1. **Requested** — initial state on `ReturnRequested` from `RequestReturnHandler`. Refunds with reason `Other` and all exchanges remain here pending CS review (`src/Returns/Returns/ReturnProcessing/RequestReturn.cs#L160`, `L142`).
  2. **Approved** — entered on `ReturnApproved` (refund auto-approval or `ApproveReturn`) or `ExchangeApproved` (`Return.cs#L83`, `L157`). Auto-approved refunds skip directly here from `RequestReturnHandler` for non-`Other` reasons (`RequestReturn.cs#L164-L175`).
  3. **Denied** — terminal. Entered on `ReturnDenied` (CS denial) or `ExchangeDenied` (CS denial or replacement out of stock per ADR 0061) (`Return.cs#L92`, `L165`; `ReplacementReservationOutcomeHandler.cs#L109`).
  4. **Received** — physical package logged at the warehouse on `ReturnReceived` from `ReceiveReturnHandler` (`Return.cs#L100`, `ReceiveReturn.cs#L41`).
  5. **Inspecting** — entered on `InspectionStarted` (explicit `StartInspection` call or implicit from `SubmitInspectionHandler` when status is still `Received`) (`Return.cs#L106`, `SubmitInspection.cs#L76`). For an exchange, `InspectionPassed` keeps the status at `Inspecting` to await `ShipReplacementItem` rather than completing (`Return.cs#L113-L129`).
  6. **ExchangeShipping** — entered on `ExchangeReplacementShipped` from `ShipReplacementItemHandler` (`Return.cs#L173`, `ShipReplacementItem.cs#L74`).
  7. **Completed** — terminal. Entered on `InspectionPassed` (refund), `InspectionMixed` (partial refund), or `ExchangeCompleted` (`Return.cs#L113-L129`, `L138`, `L180`).
  8. **Rejected** — terminal. Entered on `InspectionFailed` or `ExchangeRejected` (`Return.cs#L131`, `L187`).
  9. **Expired** — terminal. Entered on `ReturnExpired` from the scheduled `ExpireReturn` message (`Return.cs#L147`, `ExpireReturn.cs#L40`).
  10. **Cancelled** — terminal. Entered on `ExchangeCancelled` when a cross-product exchange's additional-payment delta capture fails downstream in Payments per ADR 0062 / M47.0 Slice 4 (`Return.cs#L233`, `ExchangeDeltaCaptureFailedHandler.cs#L92`). Distinct from `Denied` (up-front refusal at request time) and `Rejected` (failed inspection).
- **File:** `src/Returns/Returns/ReturnProcessing/Return.cs`. Domain events: `src/Returns/Returns/ReturnProcessing/ReturnEvents.cs`.

The Returns BC also persists one Marten document outside the event-sourced aggregate — `ReturnEligibilityWindow` — keyed by `OrderId`, populated by the `Fulfillment.ShipmentDelivered` handler, and read by `RequestReturnHandler` to gate eligibility (`src/Returns/Returns/ReturnProcessing/ReturnEligibilityWindow.cs#L1-L25`, `src/Returns/Returns/Integration/ShipmentDelivered.cs#L17-L37`).

## Commands

All ten command records live in `src/Returns/Returns/ReturnProcessing/`. Nine are externally initiated (HTTP `POST` endpoints under `/api/returns/...`); one (`ExpireReturn`) is saga-internal — scheduled by the BC against itself via `IMessageBus.ScheduleAsync` (`src/Returns/Returns/ReturnProcessing/RequestReturn.cs#L178`, `ApproveExchange.cs#L166`).

**Externally initiated (HTTP `POST`):**

- `RequestReturn` — Customer requests a return or exchange. Validates eligibility window, branches on reason for auto-approval, and starts the Return event stream. Handler: `src/Returns/Returns/ReturnProcessing/RequestReturn.cs#L72-L211` (route `POST /api/returns`).
- `ApproveReturn` — CS agent approves a return that is in `Requested` (manual review for reason `Other`). Handler: `src/Returns/Returns/ReturnProcessing/ApproveReturn.cs#L24-L70` (route `POST /api/returns/{returnId}/approve`).
- `DenyReturn` — CS agent denies a return in `Requested` with reason + customer-facing message. Handler: `src/Returns/Returns/ReturnProcessing/DenyReturn.cs#L26-L71` (route `POST /api/returns/{returnId}/deny`).
- `ReceiveReturn` — Warehouse logs physical receipt; transitions `Approved → Received`. Handler: `src/Returns/Returns/ReturnProcessing/ReceiveReturn.cs#L24-L65` (route `POST /api/returns/{returnId}/receive`).
- `StartInspection` — Inspector explicitly starts inspection (`Received → Inspecting`). Handler: `src/Returns/Returns/ReturnProcessing/StartInspection.cs#L24-L57` (route `POST /api/returns/{returnId}/inspection/start`).
- `SubmitInspection` — Inspector submits per-line `InspectionLineResult` rows; handler partitions into all-pass / all-fail / mixed for refunds, and pass / fail for exchanges (with M47.0 Slice 4 captured-delta refund branch). Handler: `src/Returns/Returns/ReturnProcessing/SubmitInspection.cs#L48-L225` (route `POST /api/returns/{returnId}/inspection`).
- `ApproveExchange` — CS agent approves an exchange; calculates price difference, opens the cross-product branch, requests replacement reservation from Inventory, schedules `ExpireReturn`, publishes `ExchangeApproved`. Handler: `src/Returns/Returns/ReturnProcessing/ApproveExchange.cs#L32-L180` (route `POST /api/returns/{returnId}/approve-exchange`).
- `DenyExchange` — CS agent denies an exchange. Handler: `src/Returns/Returns/ReturnProcessing/DenyExchange.cs#L25-L70` (route `POST /api/returns/{returnId}/deny-exchange`).
- `ShipReplacementItem` — Warehouse ships the replacement after exchange inspection passes; appends `ExchangeReplacementShipped` + `ExchangeCompleted` and, for cheaper-replacement cases, requests a Payments partial refund (M47.0 Slice 2). Handler: `src/Returns/Returns/ReturnProcessing/ShipReplacementItem.cs#L34-L122` (route `POST /api/returns/{returnId}/ship-replacement`).

**Saga-internal / scheduled:**

- `ExpireReturn` — Scheduled message handler that transitions an `Approved` return whose `ShipByDeadline` has elapsed to `Expired`. No HTTP route; dispatched via `bus.ScheduleAsync(new ExpireReturn(returnId), shipByDeadline)` from `RequestReturnHandler` (auto-approved refund path) and `ApproveExchangeHandler`. Handler: `src/Returns/Returns/ReturnProcessing/ExpireReturn.cs#L23-L57`.

S1 stub count: 10 commands. S2 enumeration: 10. Reconciled.

## Domain events

All 21 events live in `src/Returns/Returns/ReturnProcessing/ReturnEvents.cs`. Grouped by lifecycle phase. Counts exclude the value records `ReturnLineItem`, `ExchangeRequest`, and `InspectionLineResult`, which are payload shapes carried inside events rather than events themselves.

**Request:**

- `ReturnRequested` — Customer (or proxy CS agent) initiated a return or exchange request; carries the return type and, for exchanges, the `ExchangeRequest` payload. File: `ReturnEvents.cs#L21-L28`.

**Approval / Denial:**

- `ReturnApproved` — Refund return approved (auto- or CS-driven); carries the estimated refund, restocking fee, and ship-by deadline. `ReturnEvents.cs#L30-L35`.
- `ReturnDenied` — Refund return denied with customer-facing reason + message. `ReturnEvents.cs#L37-L41`.
- `ExchangeApproved` — Exchange approved after CS review; carries `PriceDifference` and ship-by deadline. `ReturnEvents.cs#L98-L102`.
- `ExchangeDenied` — Exchange denied (CS refusal or Inventory-driven "replacement out of stock"). `ReturnEvents.cs#L107-L111`.

**Receipt:**

- `ReturnReceived` — Physical package logged at the warehouse. `ReturnEvents.cs#L43-L45`.

**Inspection:**

- `InspectionStarted` — Inspector began the per-line condition assessment. `ReturnEvents.cs#L47-L50`.
- `InspectionPassed` — All inspected lines passed; carries the final refund + restocking fee. For a refund, drives transition to `Completed`; for an exchange, holds at `Inspecting` to await `ShipReplacementItem`. `ReturnEvents.cs#L61-L66`.
- `InspectionFailed` — All inspected lines failed condition / disposition checks; transition to `Rejected`. `ReturnEvents.cs#L68-L72`.
- `InspectionMixed` — Some lines passed and some failed; partial refund issued for passed items. `ReturnEvents.cs#L78-L84`.

**Replacement (exchange happy path):**

- `ExchangeReplacementShipped` — Replacement parcel handed to carrier; carries `ShipmentId` + `TrackingNumber`. `ReturnEvents.cs#L116-L120`.
- `ExchangeCompleted` — Exchange completed end-to-end; carries optional `PriceDifferenceRefund` for cheaper replacements. `ReturnEvents.cs#L126-L129`.
- `ExchangeRejected` — Exchange rejected because the original item failed inspection. `ReturnEvents.cs#L134-L137`.

**Cross-product exchange branch (M25.2, M35.0, M47.0):**

- `CrossProductExchangeRequested` — Original SKU and replacement SKU differ; flags the aggregate for the cross-product code paths. `ReturnEvents.cs#L147-L154`.
- `ExchangePriceDifferenceCalculated` — Records original total, replacement total, and signed price difference at approval time. `ReturnEvents.cs#L160-L165`.
- `ExchangeAdditionalPaymentRequired` — Replacement is more expensive; customer owes the delta. `ReturnEvents.cs#L170-L173`.
- `ExchangeAdditionalPaymentCaptured` — Payments BC confirmed the delta capture (M47.0 Slice 2). Appended by `ExchangeDeltaCapturedHandler`. `ReturnEvents.cs#L178-L182`.
- `ExchangePartialRefundIssued` — Payments BC confirmed the partial refund for a cheaper-replacement exchange (M47.0 Slice 2). Appended by `ExchangePartialRefundIssuedHandler`. `ReturnEvents.cs#L188-L191`.
- `ReplacementReservationConfirmed` — Inventory BC confirmed the replacement-stock hold (M47.0 Slice 4); persists the Inventory stream id on the Return so subsequent compensation paths can release the hold. `ReturnEvents.cs#L200-L206`.

**Terminal / compensation:**

- `ReturnExpired` — Approved return aged past `ShipByDeadline` without receipt; appended by `ExpireReturnHandler`. `ReturnEvents.cs#L86-L88`.
- `ExchangeCancelled` — M47.0 Slice 4. Cross-product exchange cancelled because Payments could not capture the additional-payment delta; transitions the aggregate to `Cancelled` (terminal). Appended by `ExchangeDeltaCaptureFailedHandler`. `ReturnEvents.cs#L214-L218`.

S1 stub count: 21 events. S2 enumeration: 21. Reconciled.

## Projections

The Returns BC has one inline event-sourced snapshot and one Marten document, both registered in `src/Returns/Returns.Api/Program.cs#L43-L55`.

- **`Return` snapshot** — Marten inline snapshot of the `Return` aggregate (`SnapshotLifecycle.Inline`). Keyed by the `Return` stream id (`Id`). Source events: all 21 listed above (the snapshot is materialised from the aggregate's `Apply` methods on every append). Indexed on `OrderId` for `GET /api/returns?orderId=...` queries (`Program.cs#L54-L55`). Served by `GetReturnHandler` (`/api/returns/{returnId}`) and `GetReturnsForOrderHandler` (`/api/returns?orderId&status`) in `src/Returns/Returns.Api/Queries/`.
- **`ReturnEligibilityWindow`** — Marten document (not event-sourced); identity = `Id` (= `OrderId`). Source: `Fulfillment.ShipmentDelivered` integration event handled by `ShipmentDeliveredHandler` (`src/Returns/Returns/Integration/ShipmentDelivered.cs#L13-L37`). Indexed on `CustomerId` and `WindowExpiresAt` (`Program.cs#L48-L51`). Read by `RequestReturnHandler` to gate the 30-day window (`RequestReturn.cs#L85-L102`). Acts as the persistent ACL between Fulfillment delivery confirmations and Returns eligibility.

No async daemon projections are registered; the async daemon runs in `Solo` mode for outbox processing (`Program.cs#L57`) but no Returns-side projection subscribes to it.

## Integration events

Grouped by direction. All Returns-owned contracts are under `src/Shared/Messages.Contracts/Returns/`. Inbound contracts referenced here live under their producing BC's namespace (`Messages.Contracts.Inventory`, `Messages.Contracts.Payments`, `Messages.Contracts.Fulfillment`). Routing is wired in `src/Returns/Returns.Api/Program.cs#L99-L227`.

### Outbound — return lifecycle (publishes)

Routed to `orders-returns-events`, `storefront-returns-events`, and `backoffice-return-*` queues (`Program.cs#L153-L227`).

- `Returns.ReturnRequested` — Return initiated. Top-level fields: `ReturnId: Guid, OrderId: Guid, CustomerId: Guid, RequestedAt: DateTimeOffset`. File: `src/Shared/Messages.Contracts/Returns/ReturnRequested.cs`.
- `Returns.ReturnApproved` — Refund return approved. Fields: `ReturnId: Guid, OrderId: Guid, CustomerId: Guid, EstimatedRefundAmount: decimal, RestockingFeeAmount: decimal, ShipByDeadline: DateTimeOffset, ApprovedAt: DateTimeOffset`. File: `Returns/ReturnApproved.cs`.
- `Returns.ReturnDenied` — Refund return denied. Fields: `ReturnId, OrderId, CustomerId, Reason: string, Message: string?, DeniedAt`. File: `Returns/ReturnDenied.cs`.
- `Returns.ReturnReceived` — Physical receipt logged. Fields: `ReturnId, OrderId, CustomerId, ReceivedAt`. File: `Returns/ReturnReceived.cs`.
- `Returns.ReturnCompleted` — Refund or mixed-inspection completed; Orders saga consumes to issue the refund and close, Inventory consumes to restock. Fields: `ReturnId, OrderId, CustomerId, FinalRefundAmount: decimal, Items: IReadOnlyList<ReturnedItem>, CompletedAt`. File: `Returns/ReturnCompleted.cs`. The `ReturnedItem` payload (`Returns/ReturnedItem.cs`) carries `Sku, Quantity, IsRestockable, WarehouseId, RestockCondition, RefundAmount, RejectionReason`.
- `Returns.ReturnRejected` — Inspection failed. Fields: `ReturnId, OrderId, CustomerId, Reason: string, Items: IReadOnlyList<ReturnedItem>, RejectedAt`. File: `Returns/ReturnRejected.cs`.
- `Returns.ReturnExpired` — Approved return expired. Fields: `ReturnId, OrderId, CustomerId, ExpiredAt`. File: `Returns/ReturnExpired.cs`.

### Outbound — exchange lifecycle (publishes)

- `Returns.ExchangeRequested` — Exchange initiated. Fields: `ReturnId, OrderId, CustomerId, ReplacementSku: string, ReplacementQuantity: int, ReplacementUnitPrice: decimal, RequestedAt`. File: `Returns/ExchangeRequested.cs`.
- `Returns.ExchangeApproved` — Fields: `ReturnId, OrderId, CustomerId, ReplacementSku, PriceDifference: decimal, ShipByDeadline, ApprovedAt`. File: `Returns/ExchangeApproved.cs`.
- `Returns.ExchangeDenied` — Fields: `ReturnId, OrderId, CustomerId, Reason, Message, DeniedAt`. File: `Returns/ExchangeDenied.cs`.
- `Returns.ExchangeReplacementShipped` — Fields: `ReturnId, OrderId, CustomerId, ShipmentId: string, TrackingNumber: string, ShippedAt`. File: `Returns/ExchangeReplacementShipped.cs`.
- `Returns.ExchangeCompleted` — Fields: `ReturnId, OrderId, CustomerId, PriceDifferenceRefund: decimal?, CompletedAt`. File: `Returns/ExchangeCompleted.cs`.
- `Returns.ExchangeRejected` — Fields: `ReturnId, OrderId, CustomerId, FailureReason: string, RejectedAt`. File: `Returns/ExchangeRejected.cs`.

### Outbound — cross-product exchange branch (publishes)

Routed in addition to the standard exchange queues, on `orders-returns-events` and `storefront-returns-events` (`Program.cs#L168-L210`).

- `Returns.CrossProductExchangeRequested` — Cross-product exchange flagged (different SKU). Fields: `ReturnId, OrderId, CustomerId, OriginalSku: string, ReplacementSku: string, OriginalUnitPrice: decimal, ReplacementUnitPrice: decimal, Quantity: int, RequestedAt`. File: `Returns/CrossProductExchangeRequested.cs`.
- `Returns.ExchangeAdditionalPaymentRequired` — Customer owes the delta. Fields: `ReturnId, OrderId, CustomerId, AmountDue: decimal, RequiredAt`. File: `Returns/ExchangeAdditionalPaymentRequired.cs`. Also routed to `payments-returns-events` so Payments receives the capture request (`Program.cs#L130-L131`).
- `Returns.ExchangeAdditionalPaymentCaptured` — Payments-side delta capture confirmed and republished by the Returns BC. Fields: `ReturnId, OrderId, CustomerId, PaymentId: Guid, AmountCaptured: decimal, Currency: string, PaymentReference: string, CapturedAt`. File: `Returns/ExchangeAdditionalPaymentCaptured.cs`.
- `Returns.ExchangePartialRefundIssued` — Payments-side partial refund confirmed and republished. Fields: `ReturnId, OrderId, CustomerId, OriginalPaymentId: Guid, RefundAmount: decimal, Currency: string, TransactionId: string, IssuedAt`. File: `Returns/ExchangePartialRefundIssued.cs`.
- `Returns.ExchangeCancelled` — M47.0 Slice 4. Cross-product exchange cancelled after delta-capture failure. Fields: `ReturnId, OrderId, CustomerId, Reason: string, Message: string, CancelledAt`. File: `Returns/ExchangeCancelled.cs`.

### Outbound — Inventory choreography (publishes)

Routed to `inventory-returns-events` (`Program.cs#L113-L114`, `L150-L151`). Owned by the `Inventory` namespace because the Inventory BC handles them, but produced by the Returns BC.

- `Inventory.ReserveReplacementForExchange` — Asks Inventory to place a hold on the replacement SKU at `WH-01` (`ReturnsExchangeDefaults.ReplacementWarehouseId`, `src/Returns/Returns/ReturnProcessing/ReturnsExchangeDefaults.cs#L13-L18`). Fields: `ReturnId, OrderId, CustomerId, ReplacementSku, WarehouseId, Quantity, RequestedAt`. File: `src/Shared/Messages.Contracts/Inventory/ReserveReplacementForExchange.cs`. Per ADR 0061, `ReturnId` doubles as the reservation key.
- `Inventory.ReleaseExchangeReservation` — Asks Inventory to release the held replacement reservation when an exchange is cancelled (Slice 4) or rejected (inspection failure with captured delta). Fields: `InventoryId: Guid, ReservationId: Guid, Reason: string`. File: `src/Shared/Messages.Contracts/Inventory/ReleaseExchangeReservation.cs`.

### Outbound — Payments choreography (publishes)

Routed to `payments-returns-events` (`Program.cs#L130-L144`).

- `Payments.ExchangePartialRefundRequested` — Cheaper-replacement partial refund request against the original Order payment. Fields: `ReturnId, OrderId, CustomerId, RefundAmount: decimal, RequestedAt`. File: `src/Shared/Messages.Contracts/Payments/ExchangePartialRefundRequested.cs`.
- `Payments.RefundExchangeDeltaRequested` — M47.0 Slice 4. Refund of a previously-captured additional-payment delta when an inspection rejects an exchange that already collected the delta. Targets the delta-capture `Payment` stream rather than the original Order payment. Fields: `ReturnId, OrderId, CustomerId, RefundAmount: decimal, RequestedAt`. File: `src/Shared/Messages.Contracts/Payments/RefundExchangeDeltaRequested.cs`.

### Inbound (subscribes)

- `Fulfillment.ShipmentDelivered` — Eligibility window start. Listened on `returns-fulfillment-events` (`Program.cs#L100-L101`). Handler: `src/Returns/Returns/Integration/ShipmentDelivered.cs`. Creates the `ReturnEligibilityWindow` document.
- `Inventory.ReplacementReserved` — Reply to `ReserveReplacementForExchange` (success). Listened on `returns-inventory-events` with durable inbox (`Program.cs#L106-L107`). Appends `ReplacementReservationConfirmed` to capture the Inventory stream id (`src/Returns/Returns/Integration/ReplacementReservationOutcomeHandler.cs#L48-L75`). Fields: `ReturnId, OrderId, InventoryId: Guid, Sku, WarehouseId, Quantity, ReservedAt`. File: `src/Shared/Messages.Contracts/Inventory/ReplacementReserved.cs`.
- `Inventory.ReplacementReservationFailed` — Reply to `ReserveReplacementForExchange` (failure). Same queue. Appends `ExchangeDenied` with reason `ReplacementOutOfStock` and republishes the public `Returns.ExchangeDenied` (`ReplacementReservationOutcomeHandler.cs#L77-L116`). Fields: `ReturnId, OrderId, Sku, WarehouseId, RequestedQuantity: int, AvailableQuantity: int, Reason: string, FailedAt`. File: `Inventory/ReplacementReservationFailed.cs`.
- `Payments.ExchangeDeltaCaptured` — Reply to the additional-payment capture. Listened on `returns-payments-events` with durable inbox (`Program.cs#L123-L124`). Appends `ExchangeAdditionalPaymentCaptured` and republishes `Returns.ExchangeAdditionalPaymentCaptured` (`src/Returns/Returns/Integration/ExchangeDeltaCapturedHandler.cs#L32-L62`). Fields: `ReturnId, OrderId, PaymentId, AmountCaptured, Currency, TransactionId, CapturedAt`. File: `Payments/ExchangeDeltaCaptured.cs`.
- `Payments.ExchangeDeltaCaptureFailed` — Reply on capture failure. Same queue. Appends `ExchangeCancelled` and emits `Returns.ExchangeCancelled` + `Inventory.ReleaseExchangeReservation` (`src/Returns/Returns/Integration/ExchangeDeltaCaptureFailedHandler.cs#L59-L124`). Fields: `ReturnId, OrderId, AmountDue, Currency, Reason, IsRetriable: bool, FailedAt`. File: `Payments/ExchangeDeltaCaptureFailed.cs`.
- `Payments.ExchangePartialRefundIssued` — Reply to the cheaper-replacement partial refund. Same queue. Appends `ExchangePartialRefundIssued` and republishes `Returns.ExchangePartialRefundIssued` (`src/Returns/Returns/Integration/ExchangePartialRefundIssuedHandler.cs#L30-L81`). Fields: `ReturnId, OrderId, OriginalPaymentId, RefundAmount, Currency, TransactionId, IssuedAt`. File: `Payments/ExchangePartialRefundIssued.cs`.

## Sagas / orchestration

The Returns BC owns the **cross-product exchange** orchestration end-to-end, with the Inventory BC and the Payments BC as participants. The orchestration is realised through the `Return` aggregate's state machine plus a small set of inbound integration handlers in `src/Returns/Returns/Integration/`; there is no separate Wolverine `Saga` document. The simple-refund and same-SKU-exchange flows are realised entirely through the same aggregate state machine and emit no orchestration messages.

ADR 0061 (M47.0 Slice 1) and ADR 0062 (M47.0 Slice 2 + Slice 4) name Returns as the orchestrator side of this flow.

### Participants and channels

- **Returns ↔ Inventory** — request/reply on the `inventory-returns-events` queue (Returns → Inventory) and `returns-inventory-events` queue (Inventory → Returns) (`Program.cs#L106-L114`, `L150-L151`).
- **Returns ↔ Payments** — request/reply on the `payments-returns-events` queue (Returns → Payments) and `returns-payments-events` queue (Payments → Returns) (`Program.cs#L123-L144`).

### Forward path — replacement reservation (Slice 1, ADR 0061)

1. `ApproveExchange` runs. When the original SKU and replacement SKU differ, `ApproveExchangeHandler` appends `ExchangeApproved`, `CrossProductExchangeRequested`, and `ExchangePriceDifferenceCalculated`, publishes `Returns.CrossProductExchangeRequested`, and emits `Inventory.ReserveReplacementForExchange` to `WH-01` (the default warehouse per `ReturnsExchangeDefaults.ReplacementWarehouseId`) (`src/Returns/Returns/ReturnProcessing/ApproveExchange.cs#L99-L145`).
2. Inventory replies with either `Inventory.ReplacementReserved` or `Inventory.ReplacementReservationFailed` on `returns-inventory-events`.
3. `ReplacementReservationOutcomeHandler` handles both (`src/Returns/Returns/Integration/ReplacementReservationOutcomeHandler.cs`):
   - On success — appends `ReplacementReservationConfirmed` to capture the Inventory stream id on the aggregate. Idempotent: skips if the aggregate already has a `ReplacementInventoryId` (`L61-L74`).
   - On failure — appends `ExchangeDenied` with reason code `ReplacementOutOfStock` and customer-facing message "Replacement item currently unavailable. Please request a refund or try again later." and republishes the public `Returns.ExchangeDenied` (`L77-L116`). Idempotent: only acts when the aggregate is still in `Approved`.

### Forward path — additional-payment delta capture (Slice 2, ADR 0062)

1. When `ApproveExchangeHandler` detects `priceDifference < 0` (replacement is more expensive), it appends `ExchangeAdditionalPaymentRequired` and publishes `Returns.ExchangeAdditionalPaymentRequired` (`ApproveExchange.cs#L148-L162`). The same contract is routed to `payments-returns-events` so Payments receives the capture request (`Program.cs#L130-L131`).
2. Payments replies with `Payments.ExchangeDeltaCaptured` on `returns-payments-events`.
3. `ExchangeDeltaCapturedHandler` appends `ExchangeAdditionalPaymentCaptured` and republishes the public `Returns.ExchangeAdditionalPaymentCaptured` (`src/Returns/Returns/Integration/ExchangeDeltaCapturedHandler.cs`). Idempotent: skips if `aggregate.AdditionalPaymentCaptured` is already true.

### Forward path — partial refund at exchange completion (Slice 2, ADR 0062)

1. `ShipReplacementItem` runs after exchange inspection passes. When `aggregate.PriceDifference > 0` (replacement is cheaper), `ShipReplacementItemHandler` emits `Payments.ExchangePartialRefundRequested` against the original Order payment (`src/Returns/Returns/ReturnProcessing/ShipReplacementItem.cs#L110-L118`).
2. Payments replies with `Payments.ExchangePartialRefundIssued` on `returns-payments-events`.
3. `ExchangePartialRefundIssuedHandler` appends `ExchangePartialRefundIssued` and republishes the public `Returns.ExchangePartialRefundIssued`. Idempotency check scans the stream for an existing `ExchangePartialRefundIssued` event because `aggregate.FinalRefundAmount` is already set by `ExchangeCompleted` and cannot serve as the dedupe key (`src/Returns/Returns/Integration/ExchangePartialRefundIssuedHandler.cs#L42-L63`).

### Compensation path — capture failure (Slice 4, ADR 0062)

1. Payments replies with `Payments.ExchangeDeltaCaptureFailed`.
2. `ExchangeDeltaCaptureFailedHandler` (`src/Returns/Returns/Integration/ExchangeDeltaCaptureFailedHandler.cs`):
   - Appends `ExchangeCancelled` with reason `PaymentCaptureFailed` and customer-facing message "Payment for price difference could not be processed. Exchange cancelled." (`L92-L98`). The aggregate transitions to terminal `Cancelled`.
   - Publishes `Returns.ExchangeCancelled` to Storefront / Orders / Backoffice (`L103-L109`).
   - If `ReplacementInventoryId` was recorded, publishes `Inventory.ReleaseExchangeReservation` to release the held stock (`L117-L123`). Skipped when the InventoryId was never recorded; the Inventory side is idempotent against missing reservations and an `ExpireReservation` timer would otherwise eventually clean up.
   - Idempotent: no-op when the aggregate is missing, not a cross-product exchange, has already captured the delta, or has already moved past `Approved`.

### Compensation path — inspection rejection with captured delta (Slice 4)

1. `SubmitInspection` runs against an exchange that has `aggregate.IsCrossProductExchange == true`, `aggregate.AdditionalPaymentCaptured == true`, and at least one failed inspection line.
2. `SubmitInspectionHandler` appends `ExchangeRejected`, publishes `Returns.ExchangeRejected`, then emits `Payments.RefundExchangeDeltaRequested` for the captured delta amount (`src/Returns/Returns/ReturnProcessing/SubmitInspection.cs#L109-L120`) and `Inventory.ReleaseExchangeReservation` if a `ReplacementInventoryId` is present (`L125-L132`).
3. Payments replies with `Payments.ExchangePartialRefundIssued` on the same `returns-payments-events` queue, handled as above.

### Saga-acknowledger relationship with the Order saga

Returns is a participant in the Orders BC's Order saga; it is the orchestrator only of the cross-product exchange flow described above. The Order saga consumes `Returns.ReturnRequested`, `ReturnReceived`, `ReturnCompleted`, `ReturnDenied`, `ReturnRejected`, `ReturnExpired`, `CrossProductExchangeRequested`, `ExchangeAdditionalPaymentRequired`, `ExchangeAdditionalPaymentCaptured`, `ExchangePartialRefundIssued`, and `ExchangeCancelled` (`Program.cs#L153-L181`).

## HTTP / API surface

`Returns.Api` is a Wolverine HTTP host (`MapWolverineEndpoints`, `src/Returns/Returns.Api/Program.cs#L330-L333`). Endpoints are discovered from `[WolverinePost]` / `[WolverineGet]` attributes on the handlers cited above.

**Return lifecycle commands** (handlers in `src/Returns/Returns/ReturnProcessing/`):

- `POST /api/returns` — Submit a return or exchange request. Handler: `RequestReturn.cs#L74`. Auth: `[Authorize]` (any authenticated principal under either configured JWT scheme).
- `POST /api/returns/{returnId}/approve` — CS approval of a refund return. Handler: `ApproveReturn.cs#L41`. Auth: `[Authorize]`.
- `POST /api/returns/{returnId}/deny` — CS denial of a refund return. Handler: `DenyReturn.cs#L43`. Auth: `[Authorize]`.
- `POST /api/returns/{returnId}/receive` — Warehouse receipt. Handler: `ReceiveReturn.cs#L41`. Auth: `[Authorize]`.
- `POST /api/returns/{returnId}/inspection/start` — Start inspection. Handler: `StartInspection.cs#L41`. Auth: `[Authorize]`.
- `POST /api/returns/{returnId}/inspection` — Submit inspection results. Handler: `SubmitInspection.cs#L65`. Auth: `[Authorize]`.
- `POST /api/returns/{returnId}/approve-exchange` — CS approval of an exchange. Handler: `ApproveExchange.cs#L70`. Auth: `[Authorize]`.
- `POST /api/returns/{returnId}/deny-exchange` — CS denial of an exchange. Handler: `DenyExchange.cs#L49`. Auth: `[Authorize]`.
- `POST /api/returns/{returnId}/ship-replacement` — Warehouse ships replacement. Handler: `ShipReplacementItem.cs#L58`. Auth: `[Authorize]`.

**Read endpoints** (handlers in `src/Returns/Returns.Api/Queries/`):

- `GET /api/returns/{returnId}` — Single Return read model. Handler: `GetReturn.cs#L29`. Auth: `[Authorize(Policy = "CustomerService")]`.
- `GET /api/returns?orderId&status` — Filtered list (max 100, ordered by `RequestedAt desc`). Handler: `GetReturnsForOrder.cs#L11`. Auth: `[Authorize(Policy = "CustomerService")]`.

**Operational endpoints:**

- `GET /api/v1/health` — Health check (Development only). `AllowAnonymous` (`Program.cs#L327`).
- `GET /api/v1/swagger.json` — Swagger document (Development only) (`Program.cs#L307-L315`).
- `GET /` — `301` redirect to `/api` (`Program.cs#L335-L339`).

**Consumers (drawn from CONTEXTS.md and verified against handlers):**

- Backoffice — `GET /api/returns` and `GET /api/returns/{returnId}` for the CS-agent return list and detail views; the same endpoints back the CS approve/deny/inspection actions.
- Customer Experience BFF — does not query Returns over HTTP; it consumes the published return-lifecycle integration events to drive the storefront return-status timeline (subscribed to 10 Returns events per the Customer Experience S2 dossier note).

## Frontend surface

Not applicable — no frontend in this BC. The customer-facing return initiation and return-status timeline are rendered by the Customer Experience BFF (Variant C dossier), which consumes Returns' integration events. The CS-agent surface is rendered by Backoffice, which calls the `/api/returns` endpoints above.

## Identity / auth posture

- **Schemes:** Two JWT bearer schemes registered in `src/Returns/Returns.Api/Program.cs#L236-L262` per ADR 0032 — `Backoffice` (issuer `https://localhost:5249`, the Backoffice Identity BC) and `Vendor` (issuer `https://localhost:5240`, the Vendor Identity BC). The default authentication scheme is `JwtBearerDefaults.AuthenticationScheme`.
- **Policies:** Five policies declared in `Program.cs#L265-L300` — `CustomerService` (Backoffice scheme, roles `CustomerService` / `OperationsManager` / `SystemAdmin`), `WarehouseClerk` (Backoffice scheme, roles `WarehouseClerk` / `OperationsManager` / `SystemAdmin`), `OperationsManager` (Backoffice scheme, roles `OperationsManager` / `SystemAdmin`), `VendorAdmin` (Vendor scheme, role `VendorAdmin`), `AnyAuthenticated` (either scheme).
- **Per-endpoint application:** the read endpoints use `[Authorize(Policy = "CustomerService")]` (`GetReturn.cs#L30`, `GetReturnsForOrder.cs#L11`). The command endpoints use bare `[Authorize]` (any authenticated principal under either scheme); finer-grained policy gating per command (warehouse vs CS) is not currently applied at the endpoint level.
- **Source files:** `src/Returns/Returns.Api/Program.cs` (auth registration `#L236-L300`, middleware `#L322-L323`); per-endpoint attributes co-located with each handler in `src/Returns/Returns/ReturnProcessing/` and `src/Returns/Returns.Api/Queries/`.

## Tests as behavioral evidence

**Gherkin features** (`docs/features/returns/`):

- `cross-product-exchange.feature` — 9 scenarios covering same-price, cheaper, more-expensive, out-of-stock, outside-window, inspection-rejection, captured-delta refund-on-rejection, expiration, and capture-failure-cancellation paths. The file's M46.0/A "honesty pass" comment block (`#L7-L18`) describes a `@pending` regime; no `@pending` or `@wip` tag remains on any current scenario (all 5 originally-pending scenarios were closed during M47.0 Slices 1, 2, and 4).
- `exchange-workflow.feature` — 8 scenarios for same-SKU exchange happy path and inspection branches.
- `return-eligibility.feature` — 10 scenarios for the 30-day window, delivery confirmation, and out-of-window denial.
- `return-expiration.feature` — 7 scenarios for the scheduled `ExpireReturn` path.
- `return-inspection.feature` — 7 scenarios for inspection outcomes (pass / fail / mixed).
- `return-request.feature` — 4 scenarios for request submission and auto-approval branching.

**Integration tests** (`tests/Returns/Returns.Api.IntegrationTests/`, Alba + Testcontainers via `TestFixture.cs`):

- `RequestReturnEndpointTests.cs` — 7 facts covering submission, eligibility-window enforcement, auto-approval branching, and exchange path entry.
- `ReturnLifecycleEndpointTests.cs` — 27 facts covering the full refund lifecycle through every command endpoint.
- `ExchangeWorkflowEndpointTests.cs` — 10 facts covering same-SKU and cross-product exchange happy paths and CS denial.
- `InspectionRejectionRefundsDeltaTests.cs` — 2 facts covering the M47.0 Slice 4 inspection-rejection captured-delta refund and reservation release.
- `ReplacementReservationOutcomeTests.cs` — 4 facts covering the inbound `Inventory.ReplacementReserved` / `ReplacementReservationFailed` handlers.
- `PaymentsChoreographyHandlersTests.cs` — 6 facts covering the inbound `Payments.ExchangeDeltaCaptured`, `ExchangeDeltaCaptureFailed`, and `ExchangePartialRefundIssued` handlers.
- `CrossBcSmokeTests/` — three multi-host pipeline test classes (`FulfillmentToReturnsPipelineTests.cs` 2 facts, `ReturnsToInventoryPipelineTests.cs` 2 facts, `ReturnsToOrdersPipelineTests.cs` 2 facts). Every fact in this folder carries `[Fact(Skip = "Blocked by Wolverine saga persistence issue — saga created via InvokeAsync() is not found by subsequent handlers in multi-host tests. See docs/wolverine-saga-persistence-issue.md")]`. The S1 stub's reference to `CrossProductExchangePendingTests` (M46.0/A skipped placeholders) does not appear under this name in the current code; the surviving multi-host skip-set is the `CrossBcSmokeTests` folder noted here.

**Unit tests** (`tests/Returns/Returns.UnitTests/`):

- `ReturnAggregateTests.cs` — 12 facts for `Apply()` transitions and `IsTerminal`.
- `ReturnLifecycleTests.cs` — 18 facts for end-to-end aggregate sequences.
- `ReturnCalculationTests.cs` — 10 facts for `CalculateEstimatedRefund` (restocking fee branches by reason).
- `ExchangeWorkflowTests.cs` — 13 facts for exchange-specific branches.
- `ShipReplacementItemHandlerTests.cs` — 5 facts for the partial-refund branch and event sequencing.
- `ReturnLineItemResponseTests.cs` — 6 facts for read-model mapping.

## ADRs

- **ADR 0061** — Cross-Product Exchange Replacement Reservation (M47.0 Slice 1). Established that the Returns BC requests a hold on the replacement SKU from the Inventory BC at exchange-approval time, that `ReturnId` doubles as the reservation id, and that the default replacement warehouse is `WH-01`. File: `docs/decisions/0061-cross-product-exchange-replacement-reservation.md`.
- **ADR 0062** — Cross-Product Exchange Payments Choreography (M47.0 Slices 2 and 4). Established that the Returns ↔ Payments delta-capture, partial-refund, and refund-on-rejection paths flow direct between the two BCs (no Order saga involvement), that `ReturnId` is the dedupe key on the Payments side, and that capture failure compensates by transitioning the Return to `Cancelled` and releasing the held replacement reservation. File: `docs/decisions/0062-cross-product-exchange-payments-choreography.md`.
- **ADR 0032** — Multi-issuer JWT authentication. Established the dual-scheme (`Backoffice` + `Vendor`) JWT setup used by `Returns.Api` and the policy set listed in the Identity / auth posture section above. File: `docs/decisions/0032-*.md` (referenced inline in `Program.cs#L235`, `L264`).

## Prior event modeling

None on file. The S2 dossier is the first formal modeling artifact for Returns. The aggregate's state machine and the cross-product exchange orchestration described above were derived directly from `src/Returns/Returns/ReturnProcessing/Return.cs`, `ReturnEvents.cs`, the four `Returns.Integration` handlers, and the Slice 1 / 2 / 4 retrospectives in `docs/planning/milestones/m47-0-*.md`.

## Source citations (S2 full)

- `src/Returns/` (folder root)
- `src/Returns/Returns/ReturnProcessing/Return.cs`
- `src/Returns/Returns/ReturnProcessing/ReturnEvents.cs`
- `src/Returns/Returns/ReturnProcessing/ReturnStatus.cs`
- `src/Returns/Returns/ReturnProcessing/ReturnEligibilityWindow.cs`
- `src/Returns/Returns/ReturnProcessing/ReturnsExchangeDefaults.cs`
- `src/Returns/Returns/ReturnProcessing/RequestReturn.cs`, `ApproveReturn.cs`, `DenyReturn.cs`, `ReceiveReturn.cs`, `StartInspection.cs`, `SubmitInspection.cs`, `ApproveExchange.cs`, `DenyExchange.cs`, `ShipReplacementItem.cs`, `ExpireReturn.cs`
- `src/Returns/Returns/Integration/ShipmentDelivered.cs`, `ReplacementReservationOutcomeHandler.cs`, `ExchangeDeltaCapturedHandler.cs`, `ExchangePartialRefundIssuedHandler.cs`, `ExchangeDeltaCaptureFailedHandler.cs`
- `src/Returns/Returns.Api/Program.cs`
- `src/Returns/Returns.Api/Queries/GetReturn.cs`, `GetReturnsForOrder.cs`
- `src/Shared/Messages.Contracts/Returns/` (all 17 outbound contracts)
- `src/Shared/Messages.Contracts/Inventory/ReserveReplacementForExchange.cs`, `ReplacementReserved.cs`, `ReplacementReservationFailed.cs`, `ReleaseExchangeReservation.cs`
- `src/Shared/Messages.Contracts/Payments/ExchangePartialRefundRequested.cs`, `ExchangePartialRefundIssued.cs`, `ExchangeDeltaCaptured.cs`, `ExchangeDeltaCaptureFailed.cs`, `RefundExchangeDeltaRequested.cs`
- `src/Shared/Messages.Contracts/Fulfillment/ShipmentDelivered.cs`
- `CONTEXTS.md` (section: `Returns`)
- `docs/decisions/0061-cross-product-exchange-replacement-reservation.md`
- `docs/decisions/0062-cross-product-exchange-payments-choreography.md`
- `docs/planning/milestones/m47-0-plan.md`, `m47-0-slice-1-retrospective.md`, `m47-0-slice-2-retrospective.md`, `m47-0-slice-4-retrospective.md`
- `docs/features/returns/cross-product-exchange.feature`, `exchange-workflow.feature`, `return-eligibility.feature`, `return-expiration.feature`, `return-inspection.feature`, `return-request.feature`
- `tests/Returns/Returns.Api.IntegrationTests/` (all suites listed above)
- `tests/Returns/Returns.UnitTests/` (all suites listed above)
