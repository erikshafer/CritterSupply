# Customer Experience

> **Source folder:** `src/Customer Experience/`
> **Status:** Implemented
> **Most recent material milestone:** M47.0 — Storefront order-confirmation timeline (Slice 5)
> **Stub depth:** S1 — to be deepened in S2

## Purpose

Customer Experience is the customer-facing surface of CritterSupply. It contains the Blazor storefront (`Storefront.Web`) and the backend-for-frontend that composes data from multiple BCs into views the storefront renders (`Storefront.Api`). The BFF holds no domain state — it queries Shopping, Orders, Product Catalog, Customer Identity, and Fulfillment over HTTP, subscribes to their integration events over RabbitMQ, and pushes updates to connected browsers via SignalR.

## Top-level structure

### Aggregates

Not applicable; this BC owns no domain aggregates. Customer Experience is a BFF and pure composition / notification relay.

### Commands

- BFF-local query and command shapes live in `Storefront.Api/Queries/` and `Storefront.Api/Commands/` (delegated to the owning BC over HTTP at handler time).

### Domain events

Not applicable — the BC does not emit domain events. It subscribes to integration events from upstream BCs and relays them to the SignalR hub.

### Projections

Not applicable — view composition is request-time HTTP fan-out (`Storefront/Composition/`: `CartView`, `CheckoutView`, `ProductListingView`).

### Integration events

- Subscribes to Shopping (`ItemAdded`, `ItemRemoved`, `ItemQuantityChanged`)
- Subscribes to Orders (`OrderPlaced`, `PaymentAuthorized` relay)
- Subscribes to Fulfillment (`ShipmentHandedToCarrier`, `TrackingNumberAssigned`, `ShipmentDelivered`, `DeliveryAttemptFailed`, `ReturnToSenderInitiated`, `ShipmentLostInTransit`, `BackorderCreated`)
- Subscribes to Returns (`ReturnRequested`, `ReturnReceived`, `ReturnApproved`, `ReturnDenied`, `ReturnRejected`, `ReturnCompleted`, `ReturnExpired`, `ExchangeAdditionalPaymentCaptured`, `ExchangeCancelled`, `ExchangePartialRefundIssued`)

### HTTP / API surface (one line)

`Storefront.Api` exposes the storefront's BFF query endpoints (cart, checkout, product listings) plus the SignalR hub at `/hub/storefront`.

### Frontend surface (if applicable)

`Storefront.Web` — Blazor Server storefront; pages include cart, checkout, product browse, and order-confirmation (with in-session activity timeline).

### Identity / auth posture (if applicable)

Cookie-based session (consumed from Customer Identity); JWT Bearer schemes for Backoffice / Vendor callers also registered on the BFF.

## Prior event modeling

None on file.

## ADRs

- ADR 0013 — Migrate from SSE to SignalR for Real-Time Communication
- ADR 0034 — Backoffice BFF Architecture (BFF pattern reference)
- ADR 0043 — Storefront Web Technology Options

## Source citations (S1 stub)

- `src/Customer Experience/`
- `src/Customer Experience/Storefront/Notifications/` (integration-event handler set)
- `src/Customer Experience/Storefront.Api/Program.cs` (handler discovery, SignalR wiring)
- `CONTEXTS.md` (section: `Customer Experience`)
- `docs/decisions/0013-signalr-migration-from-sse.md`
- `docs/decisions/0043-storefront-web-technology-options.md`
