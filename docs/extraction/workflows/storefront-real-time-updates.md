# Storefront real-time updates

> **Status:** Active
> **Type:** SignalR fan-out (Customer Experience consumes upstream integration events; pushes typed messages over a single hub)
> **Initiating actor:** System (upstream BC events)
> **BCs involved:** Customer Experience (subscriber + hub) ← Shopping, Orders, Payments, Inventory, Fulfillment, Returns
> **Most recent material milestone:** M47.0 — Slice 5 (in-session timeline + cross-product exchange surface)

## Purpose

Customer Experience runs a Blazor WASM storefront backed by a BFF API that exposes a single SignalR hub mounted at `/hub/storefront`. The BFF subscribes to lifecycle integration events from six upstream BCs across three RabbitMQ queues (`storefront-notifications`, `storefront-fulfillment-events`, `storefront-returns-events`, all `ProcessInline`); each notification handler produces an in-process Wolverine message implementing `IStorefrontWebSocketMessage`; Wolverine's SignalR transport routes the message to the hub, which targets the `customer:{customerId}` group on the connection enrolled via the `customerId` query string. Five typed message channels (cart, order-status, shipment-status, return-status, exchange-payment) drive live storefront UI updates.

## Actors and triggers

- **Initiating actor:** System (upstream BC events)
- **Trigger:** Any of 23 inbound integration events across Shopping, Orders, Payments, Inventory, Fulfillment, Returns (see Channels below)
- **Prerequisite state:** Customer is signed in and connected to `/hub/storefront?customerId={customerId}` (typically the storefront page is open); `StorefrontHub.OnConnectedAsync` enrolls the connection into the `customer:{customerId}` group

## Trace (per-channel template)

| Step | BC | Command / Event / Edge | Resulting state / output | Dossier reference |
|------|----|------------------------|--------------------------|-------------------|
| 1 | Upstream BC | Publishes a lifecycle integration event as part of its own workflow | RabbitMQ delivery to one of `storefront-notifications`, `storefront-fulfillment-events`, `storefront-returns-events` | `bcs/customer-experience.md#integration-events` |
| 2 | Customer Experience | A `Storefront/Notifications/*Handler` (one per inbound event type) consumes the event; constructs a typed `IStorefrontWebSocketMessage` carrying `CustomerId` for group targeting | In-process Wolverine message produced | `bcs/customer-experience.md#composition-map`, `bcs/customer-experience.md#integration-events` (Subscribed) |
| 3 | Customer Experience | Wolverine's SignalR transport routes the in-process message to `StorefrontHub` (`Program.cs#L66-L73`, marker interface `IStorefrontWebSocketMessage` with `Guid CustomerId`) | Routing dispatched | `bcs/customer-experience.md#signalr-channels` |
| 4 | Customer Experience | `StorefrontHub` pushes the message to the `customer:{customerId}` group (per-customer group from `OnConnectedAsync` connection enrolment) | WebSocket frame delivered to all connections for that customer | `bcs/customer-experience.md#signalr-channels` |
| 5 | Storefront WASM | Subscriber on the page (e.g. `Cart.razor`, `OrderConfirmation.razor`) handles the typed message; mapping helpers under `Storefront.Web/RealTime/StorefrontStatusMapper.cs` translate to customer-facing copy and colour chips | Live UI update | `bcs/customer-experience.md#signalr-channels` per channel |

## Channels (5 typed message families)

### Cart updates (channel: `CartUpdated`)
- **Triggered by:** Shopping `ItemAdded`, `ItemRemoved`, `ItemQuantityChanged`
- **Handlers:** `Item{Added,Removed,QuantityChanged}Handler` under `src/Customer Experience/Storefront/Notifications/`
- **Storefront pages updated:** `Cart.razor` line items, header cart badge

### Order-status updates (channel: `OrderStatusChanged`)
- **Triggered by:** Orders `OrderPlaced` (`"Placed"`); Payments `PaymentAuthorized` (`"PaymentAuthorized"`); Inventory `ReservationConfirmed` (`"InventoryReserved"`)
- **Handlers:** `OrderPlacedHandler`, `PaymentAuthorizedHandler`, `ReservationConfirmedHandler`
- **Storefront pages updated:** `OrderConfirmation.razor` status chip

### Shipment-status updates (channel: `ShipmentStatusChanged`)
- **Triggered by:** Fulfillment `BackorderCreated`, `DeliveryAttemptFailed`, `ReturnToSenderInitiated`, `ShipmentDelivered`, `ShipmentHandedToCarrier`, `ShipmentLostInTransit`, `TrackingNumberAssigned`
- **Handlers:** `BackorderCreatedHandler`, `DeliveryAttemptFailedHandler`, `ReturnToSenderInitiatedHandler`, `ShipmentDeliveredHandler`, `ShipmentHandedToCarrierHandler`, `ShipmentLostInTransitHandler`, `TrackingNumberAssignedHandler`
- **Mapping:** `StorefrontStatusMapper.cs#L41-L52` — status-string + tracking number
- **Storefront pages updated:** `OrderConfirmation.razor` shipment chip + tracking link

### Return-status updates (channel: `ReturnStatusChanged`)
- **Triggered by:** Returns `ReturnRequested`, `ReturnReceived`, `ReturnApproved`, `ReturnDenied`, `ReturnRejected`, `ReturnCompleted`, `ReturnExpired`, `ExchangeCancelled`
- **Handlers:** Per-event handlers under `Storefront/Notifications/`
- **Mapping:** `StorefrontStatusMapper.cs#L88-L111` — status + customer-facing message
- **Storefront pages updated:** `OrderConfirmation.razor` + `OrderHistory.razor` return-status chip and the in-memory MudTimeline (see memory note — `OrderConfirmation.razor` renders an in-session `MudTimeline` with `data-testid='order-activity-timeline'` driven by SignalR → `OnSseEvent` → `AppendTimeline`; reuses `GetStatusColor` for chip colours)

### Exchange-payment updates (channel: `ReturnExchangePaymentChanged`)
- **Triggered by:** Returns `ExchangeAdditionalPaymentCaptured` (`PaymentKind = "Capture"`), Returns `ExchangePartialRefundIssued` (`PaymentKind = "Refund"`)
- **Handlers:** `ExchangeAdditionalPaymentCapturedHandler`, `ExchangePartialRefundIssuedHandler`
- **Mapping:** `StorefrontStatusMapper.BuildExchangePaymentMessage` (`StorefrontStatusMapper.cs#L134`) — customer-facing copy
- **Storefront pages updated:** `OrderConfirmation.razor` exchange-payment chip

## Projections and views

- **No persisted projection** in Customer Experience for the real-time channels. The hub is push-only; storefront pages render from a combination of (a) initial HTTP load against the BFF (which delegates to Shopping / Orders / Catalog / Customer Identity) and (b) in-session SignalR-driven appends.
- **In-session timeline (M47.0 / Slice 5):** `OrderConfirmation.razor` accumulates a `MudTimeline` in component memory. The timeline is in-memory only; persistence across page refreshes is deferred (would need a Storefront BFF projection + history endpoint). Memory note.

## Compensation paths

### Failure: WebSocket connection drops
- **Compensating action:** WASM client auto-reconnects (`HubConnection.WithAutomaticReconnect()`); on reconnect, the client receives subsequent push events. **In-session timeline state is lost** — see Declared vs. implemented.
- **Resulting state:** Real-time updates resume; pre-disconnect timeline entries are not replayed.

### Failure: Customer not connected when an upstream event occurs
- **Compensating action:** None — the hub fans out only to active connections. The customer sees the new state on next HTTP page load (rendered from the originating BC).
- **Resulting state:** No push; full page reload picks up the change.

### Failure: Handler exception during `ProcessInline` consumption
- **Compensating action:** Wolverine retry policies; failed messages may land in `wolverine_dead_letters` in the BFF's schema and be visible to `backoffice-operations-health.md`. Per `bcs/customer-experience.md#integration-events`, all 3 queues are `ProcessInline` — no durable inbox on these subscriptions; failure handling is per-handler.
- **Resulting state:** Possible missed push; full page reload eventually corrects.

## Variants and edge cases

### Inventory + Returns subscriptions are not in CONTEXTS.md
Both subscriptions are wired in code but not listed in CONTEXTS.md's Customer Experience integration table. Dossier source: `bcs/customer-experience.md#composition-map` sections "From Inventory" and "From Returns".

### Customer Experience publishes nothing on the integration bus
The BC is **exclusively a subscriber**. All in-process messages it produces are typed as `IStorefrontWebSocketMessage` and routed to SignalR rather than to RabbitMQ. No `PublishMessage` rule is registered (`Program.cs#L56-L98`). Dossier: `bcs/customer-experience.md#integration-events` (Published).

### Single hub, single per-customer group
The hub assigns each connection to exactly one `customer:{customerId}` group on `OnConnectedAsync` based on the `customerId` query string. There is one hub class and one group naming scheme; the channels above differ by the message type Wolverine routes through it.

### Anonymous WebSocket connection
The hub uses query-string `customerId` rather than an authenticated principal — this is the documented mechanism in `bcs/customer-experience.md#signalr-channels`. The auth posture for the BFF HTTP surface is separate (see `bcs/customer-experience.md#identity--auth-posture`).

## BCs and roles

- **Customer Experience** — Subscriber + SignalR hub. Owns 22 notification handlers, 5 channel message types, and the single hub. Dossier: `bcs/customer-experience.md`.
- **Shopping** — Publisher of cart events. Dossier: `bcs/shopping.md`.
- **Orders** — Publisher of `OrderPlaced` (origin of order-status push). Dossier: `bcs/orders.md`.
- **Payments** — Publisher of `PaymentAuthorized`. Dossier: `bcs/payments.md`.
- **Inventory** — Publisher of `ReservationConfirmed`. Dossier: `bcs/inventory.md`.
- **Fulfillment** — Publisher of the 7 shipment events. Dossier: `bcs/fulfillment.md`.
- **Returns** — Publisher of the 8 return + exchange-payment events. Dossier: `bcs/returns.md`.

## Tests as behavioral evidence

- **Integration tests:** `tests/Customer Experience/Storefront.Api.IntegrationTests/` exercises each notification handler with Alba + Testcontainers + SignalR Client transport (per memory note on `wolverine-signalr.md` patterns).
- **Component / unit tests:** `tests/Customer Experience/Storefront.Web.UnitTests/` covers the storefront page bindings; `OrderConfirmationTimelineTests.cs` covers the M47.0/Slice 5 timeline rendering.
- **End-to-end:** `tests/Customer Experience/Storefront.E2ETests/` covers cross-page flows with real Kestrel + Playwright.

## ADRs

- **(per the storefront timeline memory note)** Persisted timeline / history endpoint is documented as deferred to M48.0 (the current milestone).

## Declared vs. implemented

- **Declared shape (M47.0 closeout retro):** Persisted return-history (Storefront BFF projection + history endpoint) so the OrderConfirmation timeline survives a page refresh.
- **Implemented shape:** OrderConfirmation timeline is in-memory only; on refresh the timeline is empty until the next SignalR push.
- **Gap:** Persistence deferred. Memory note (M47.0 closeout). Forward-note for the storefront timeline workstream.

- **Declared shape (CONTEXTS.md):** Customer Experience integration table.
- **Implemented shape:** The Inventory and Returns subscriptions are present in code but absent from CONTEXTS.md.
- **Gap:** Documentation undercount; functional gap absent. Dossier source: `bcs/customer-experience.md#composition-map` (Inventory + Returns sections).

## Source citations

- Dossier sections referenced: `bcs/customer-experience.md#integration-events`, `bcs/customer-experience.md#signalr-channels`, `bcs/customer-experience.md#composition-map`, `bcs/customer-experience.md#tests-as-behavioral-evidence`, `bcs/orders.md#publishes`, `bcs/payments.md`, `bcs/inventory.md#published-by-inventory-outbound`, `bcs/fulfillment.md#integration-events`, `bcs/returns.md#outbound-return-lifecycle-publishes`, `bcs/shopping.md`.
- Memory notes: storefront timeline (`OrderConfirmation.razor` MudTimeline), M47.0 closeout, Storefront bunit fixture.
- Tests: `tests/Customer Experience/Storefront.Api.IntegrationTests/`, `tests/Customer Experience/Storefront.Web.UnitTests/`, `tests/Customer Experience/Storefront.E2ETests/`.
