# Customer Experience

> **Source folder:** `src/Customer Experience/`
> **Status:** Implemented
> **Most recent material milestone:** M47.0 — Storefront order-confirmation timeline (Slice 5)
> **Dossier depth:** S2 — full

## Purpose

Customer Experience is the customer-facing surface of CritterSupply. It contains the Blazor storefront (`Storefront.Web`) and the backend-for-frontend that composes data from multiple BCs into views the storefront renders (`Storefront.Api`). The BFF holds no domain state — it queries Shopping, Orders, Product Catalog, and Customer Identity over HTTP, subscribes to integration events from Shopping, Orders, Payments, Inventory, Fulfillment, and Returns over RabbitMQ, and pushes per-customer updates to connected browsers via SignalR.

## Aggregates

Not applicable — this BC is a Backend-for-Frontend and owns no domain aggregates. Domain ownership lives in the BCs whose events Customer Experience subscribes to. See ADR 0013 (SignalR migration) and the BFF-pattern background in `docs/skills/bff-realtime-patterns.md`.

## Commands

The BFF exposes command shapes that the storefront posts to and that delegate to the owning BC over HTTP at handler time. Commands are colocated with their Wolverine.HTTP handlers under `src/Customer Experience/Storefront.Api/Commands/`. FluentValidation is wired in `Program.cs#L59`.

### Cart commands (delegate to Shopping BC via `IShoppingClient`)

- `InitializeCart(CustomerId)` — creates a cart for the authenticated customer. Handler: `src/Customer Experience/Storefront.Api/Commands/InitializeCart.cs#L21` (route `POST /api/storefront/carts/initialize`).
- `AddItemToCart(CartId, Sku, Quantity)` — adds an item to the cart. Handler: `src/Customer Experience/Storefront.Api/Commands/AddItemToCart.cs#L26` (route `POST /api/storefront/carts/{cartId}/items`).
- `ChangeItemQuantity(CartId, Sku, NewQuantity)` — changes line-item quantity. Handler: `src/Customer Experience/Storefront.Api/Commands/ChangeItemQuantity.cs#L26` (route `PUT /api/storefront/carts/{cartId}/items/{sku}/quantity`).
- `RemoveItemFromCart(CartId, Sku)` — removes a line item. Handler: `src/Customer Experience/Storefront.Api/Commands/RemoveItemFromCart.cs#L24` (route `DELETE /api/storefront/carts/{cartId}/items/{sku}`).
- `InitiateCheckout(CartId)` — converts the cart into a checkout. Handler: `src/Customer Experience/Storefront.Api/Commands/InitiateCheckout.cs#L21` (route `POST /api/storefront/carts/{cartId}/checkout`).

### Checkout commands (delegate to Orders BC via `IOrdersClient`)

- `ProvideCheckoutShippingAddress(CheckoutId, Address fields)` — records the shipping address on the checkout. Handler: `src/Customer Experience/Storefront.Api/Commands/ProvideCheckoutShippingAddress.cs#L11` (route `POST /api/storefront/checkouts/{checkoutId}/shipping-address`).
- `SelectCheckoutShippingMethod(CheckoutId, ShippingMethod, ShippingCost)` — records the shipping-method selection. Handler: `src/Customer Experience/Storefront.Api/Commands/SelectCheckoutShippingMethod.cs#L11` (route `POST /api/storefront/checkouts/{checkoutId}/shipping-method`).
- `ProvideCheckoutPaymentMethod(CheckoutId, PaymentMethodToken)` — attaches the payment-method token. Handler: `src/Customer Experience/Storefront.Api/Commands/ProvideCheckoutPaymentMethod.cs#L11` (route `POST /api/storefront/checkouts/{checkoutId}/payment-method`).
- `CompleteCheckout(CheckoutId)` — completes the checkout, returning the new `OrderId`. Handler: `src/Customer Experience/Storefront.Api/Commands/CompleteCheckout.cs#L21` (route `POST /api/storefront/checkouts/{checkoutId}/complete`).

### Address commands (delegate to Customer Identity BC via `ICustomerIdentityClient`)

- `AddCustomerAddress(CustomerId, Address fields)` — adds an address to the customer's address book during checkout. Handler: `src/Customer Experience/Storefront.Api/Commands/AddCustomerAddress.cs#L11` (route `POST /api/storefront/customers/{customerId}/addresses`).

## Domain events

Not applicable — Customer Experience does not emit domain events.

## Composition map

For each upstream BC, the BFF either subscribes to integration events over RabbitMQ, queries over HTTP, or both. The 6 upstream BCs identified in CONTEXTS.md are covered first; two additional event sources wired in code (Inventory, Returns) are covered last.

### From `Shopping`

- **Subscribes to** (RabbitMQ): `ItemAdded`, `ItemRemoved`, `ItemQuantityChanged` — handlers: `src/Customer Experience/Storefront/Notifications/ItemAddedHandler.cs#L14`, `ItemRemovedHandler.cs#L14`, `ItemQuantityChangedHandler.cs#L14`. Each handler re-queries Shopping for the up-to-date cart and emits a `CartUpdated` SignalR message.
- **Queries** (HTTP): `GET /api/carts/{cartId}` (cart view); `POST /api/carts`, `POST /api/carts/{cartId}/items`, `DELETE /api/carts/{cartId}/items/{sku}`, `PUT /api/carts/{cartId}/items/{sku}/quantity`, `DELETE /api/carts/{cartId}` (clear), `POST /api/carts/{cartId}/checkout`. Client: `src/Customer Experience/Storefront.Api/Clients/ShoppingClient.cs`.
- **Maintains read model:** none — cart state is fetched live from Shopping at composition time.

### From `Orders`

- **Subscribes to** (RabbitMQ): `OrderPlaced` — handler: `src/Customer Experience/Storefront/Notifications/OrderPlacedHandler.cs#L11`. Emits an `OrderStatusChanged` SignalR message with status `"Placed"`.
- **Queries** (HTTP): `GET /api/checkouts/{checkoutId}`, `GET /api/orders/{orderId}`, `GET /api/orders?customerId={customerId}` (paged + summary forms); plus the four checkout commands listed above (`POST /api/checkouts/{checkoutId}/shipping-address`, `…/shipping-method`, `…/payment-method`, `…/complete`). Client: `src/Customer Experience/Storefront.Api/Clients/OrdersClient.cs`.
- **Maintains read model:** none — checkout and order state are fetched live from Orders.

### From `Fulfillment`

- **Subscribes to** (RabbitMQ): `BackorderCreated`, `DeliveryAttemptFailed`, `ReturnToSenderInitiated`, `ShipmentDelivered`, `ShipmentHandedToCarrier`, `ShipmentLostInTransit`, `TrackingNumberAssigned` — handlers: `src/Customer Experience/Storefront/Notifications/{Backorder,DeliveryAttemptFailed,ReturnToSenderInitiated,ShipmentDelivered,ShipmentHandedToCarrier,ShipmentLostInTransit,TrackingNumberAssigned}Handler.cs`. Each handler emits a `ShipmentStatusChanged` SignalR message with a status discriminator that `StorefrontStatusMapper.MapShipmentStatus` translates into customer-facing copy (`src/Customer Experience/Storefront.Web/RealTime/StorefrontStatusMapper.cs#L41`).
- **Queries** (HTTP): none — the BFF does not call Fulfillment directly. Shipment context is delivered exclusively through the integration-event payloads.
- **Maintains read model:** none.

### From `Payments`

- **Subscribes to** (RabbitMQ): `PaymentAuthorized` — handler: `src/Customer Experience/Storefront/Notifications/PaymentAuthorizedHandler.cs#L13`. Emits an `OrderStatusChanged` SignalR message with status `"PaymentAuthorized"`. The handler carries a `Guid.Empty` placeholder for `CustomerId` and an inline `// TODO` noting that the customer lookup against Orders is not yet wired (`PaymentAuthorizedHandler.cs#L17`); this is the only handler in the set that currently routes to the empty group.
- **Queries** (HTTP): none.
- **Maintains read model:** none.

### From `Product Catalog`

- **Subscribes to** (RabbitMQ): none.
- **Queries** (HTTP): `GET /api/products/{sku}`, `GET /api/products?page={page}&pageSize={pageSize}`. Client: `src/Customer Experience/Storefront.Api/Clients/CatalogClient.cs`. Used by the product-listing composition (`Storefront/Composition/ProductListingView.cs`) and the cart-view composition (`Storefront/Composition/CartView.cs`) to enrich line items with product name and image URL.
- **Maintains read model:** none — product data is fetched live from Catalog at composition time.

### From `Customer Identity`

- **Subscribes to** (RabbitMQ): none.
- **Queries** (HTTP): `GET /api/customers/{customerId}/addresses`, `POST /api/customers/{customerId}/addresses`, `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me`. Client: `src/Customer Experience/Storefront.Api/Clients/CustomerIdentityClient.cs`. The `Storefront.Web` server-side login endpoint also calls `POST /api/auth/login` directly to mint the cookie session (`src/Customer Experience/Storefront.Web/Program.cs#L83`).
- **Maintains read model:** none — the customer's saved addresses are fetched live for the checkout view (`Storefront/Composition/CheckoutView.cs`).

### From `Inventory` (subscription wired in code; not in CONTEXTS.md integration table)

- **Subscribes to** (RabbitMQ): `ReservationConfirmed` — handler: `src/Customer Experience/Storefront/Notifications/ReservationConfirmedHandler.cs#L13`. Emits an `OrderStatusChanged` SignalR message with status `"InventoryReserved"`. Carries the same `Guid.Empty` `CustomerId` placeholder noted on the Payments handler (`ReservationConfirmedHandler.cs#L17`).
- **Queries** (HTTP): none.
- **Maintains read model:** none.

### From `Returns` (subscription wired in code; not in CONTEXTS.md integration table)

- **Subscribes to** (RabbitMQ): `ReturnRequested`, `ReturnReceived`, `ReturnApproved`, `ReturnDenied`, `ReturnRejected`, `ReturnCompleted`, `ReturnExpired` (return-status flow) and `ExchangeAdditionalPaymentCaptured`, `ExchangePartialRefundIssued`, `ExchangeCancelled` (cross-product exchange flow) — handlers: `src/Customer Experience/Storefront/Notifications/Return{Requested,Received,Approved,Denied,Rejected,Completed,Expired}Handler.cs` and `Exchange{AdditionalPaymentCaptured,PartialRefundIssued,Cancelled}Handler.cs`. Status-flow handlers emit `ReturnStatusChanged`; the two payment-flow handlers emit `ReturnExchangePaymentChanged` (`src/Customer Experience/Storefront/RealTime/StorefrontEvent.cs#L80`); `ExchangeCancelledHandler` emits `ReturnStatusChanged` with status `"Cancelled"` and forwards the producer-supplied verbatim copy in `Details` (`ExchangeCancelledHandler.cs#L24`).
- **Queries** (HTTP): none.
- **Maintains read model:** none.

## Read models / projections

None. `Storefront.Api` registers a Marten document store with schema `storefront` (`src/Customer Experience/Storefront.Api/Program.cs#L18`), but no projection types are configured against it and no documents are written by handlers in the BC. View composition is request-time HTTP fan-out into the in-process `CartView`, `CheckoutView`, and `ProductListingView` records under `src/Customer Experience/Storefront/Composition/`.

## SignalR channels

The BFF exposes a single hub mounted at `/hub/storefront` (`src/Customer Experience/Storefront.Api/Program.cs#L204`). Wolverine's SignalR transport routes any in-process message implementing `IStorefrontWebSocketMessage` to the hub (`Program.cs#L66-L73`); the marker interface defines `Guid CustomerId` for group targeting (`src/Customer Experience/Storefront/RealTime/IStorefrontWebSocketMessage.cs#L7`). The hub assigns each connection to a single per-customer group on `OnConnectedAsync` based on the `customerId` query string (`src/Customer Experience/Storefront.Api/StorefrontHub.cs#L19-L25`).

There is one hub class and one group naming scheme; the channels below differ by the message type Wolverine routes through it.

### Channel: cart updates

- **Group:** `customer:{customerId}`
- **Messages pushed:** `CartUpdated(CartId, CustomerId, ItemCount, TotalAmount, OccurredAt)` (`src/Customer Experience/Storefront/RealTime/StorefrontEvent.cs#L21`).
- **Source events that trigger push:** Shopping `ItemAdded`, `ItemRemoved`, `ItemQuantityChanged` — handlers in `src/Customer Experience/Storefront/Notifications/Item{Added,Removed,QuantityChanged}Handler.cs`.
- **Hub:** `src/Customer Experience/Storefront.Api/StorefrontHub.cs`.

### Channel: order-status updates

- **Group:** `customer:{customerId}`
- **Messages pushed:** `OrderStatusChanged(OrderId, CustomerId, NewStatus, OccurredAt)` (`StorefrontEvent.cs#L31`).
- **Source events that trigger push:** Orders `OrderPlaced` (status `"Placed"`); Payments `PaymentAuthorized` (status `"PaymentAuthorized"`); Inventory `ReservationConfirmed` (status `"InventoryReserved"`).
- **Hub:** `src/Customer Experience/Storefront.Api/StorefrontHub.cs`.

### Channel: shipment-status updates

- **Group:** `customer:{customerId}`
- **Messages pushed:** `ShipmentStatusChanged(ShipmentId, OrderId, CustomerId, NewStatus, TrackingNumber, OccurredAt)` (`StorefrontEvent.cs#L40`).
- **Source events that trigger push:** Fulfillment `BackorderCreated`, `DeliveryAttemptFailed`, `ReturnToSenderInitiated`, `ShipmentDelivered`, `ShipmentHandedToCarrier`, `ShipmentLostInTransit`, `TrackingNumberAssigned`. Status-string mapping: `src/Customer Experience/Storefront.Web/RealTime/StorefrontStatusMapper.cs#L41-L52`.
- **Hub:** `src/Customer Experience/Storefront.Api/StorefrontHub.cs`.

### Channel: return-status updates

- **Group:** `customer:{customerId}`
- **Messages pushed:** `ReturnStatusChanged(ReturnId, OrderId, CustomerId, NewStatus, Details, OccurredAt)` (`StorefrontEvent.cs#L52`).
- **Source events that trigger push:** Returns `ReturnRequested`, `ReturnReceived`, `ReturnApproved`, `ReturnDenied`, `ReturnRejected`, `ReturnCompleted`, `ReturnExpired`, `ExchangeCancelled`. Status-and-message mapping: `src/Customer Experience/Storefront.Web/RealTime/StorefrontStatusMapper.cs#L88-L111`.
- **Hub:** `src/Customer Experience/Storefront.Api/StorefrontHub.cs`.

### Channel: exchange-payment updates

- **Group:** `customer:{customerId}`
- **Messages pushed:** `ReturnExchangePaymentChanged(ReturnId, OrderId, CustomerId, PaymentKind, PaymentId, Amount, Currency, PaymentReference, OccurredAt)` (`StorefrontEvent.cs#L80`).
- **Source events that trigger push:** Returns `ExchangeAdditionalPaymentCaptured` (PaymentKind `"Capture"`), Returns `ExchangePartialRefundIssued` (PaymentKind `"Refund"`). Mapping to customer-facing copy: `StorefrontStatusMapper.BuildExchangePaymentMessage` (`StorefrontStatusMapper.cs#L134`).
- **Hub:** `src/Customer Experience/Storefront.Api/StorefrontHub.cs`.

## Integration events

Customer Experience is exclusively a subscriber on the integration bus. All in-process messages it produces are typed as `IStorefrontWebSocketMessage` and routed to SignalR rather than to RabbitMQ; Program.cs registers no `PublishMessage` rule for any message contract under `Messages.Contracts` (`src/Customer Experience/Storefront.Api/Program.cs#L56-L98`).

### Subscribed (consolidated)

| Source BC | Event | Handler |
|---|---|---|
| Shopping | `ItemAdded` | `Storefront/Notifications/ItemAddedHandler.cs` |
| Shopping | `ItemRemoved` | `Storefront/Notifications/ItemRemovedHandler.cs` |
| Shopping | `ItemQuantityChanged` | `Storefront/Notifications/ItemQuantityChangedHandler.cs` |
| Orders | `OrderPlaced` | `Storefront/Notifications/OrderPlacedHandler.cs` |
| Payments | `PaymentAuthorized` | `Storefront/Notifications/PaymentAuthorizedHandler.cs` |
| Inventory | `ReservationConfirmed` | `Storefront/Notifications/ReservationConfirmedHandler.cs` |
| Fulfillment | `BackorderCreated` | `Storefront/Notifications/BackorderCreatedHandler.cs` |
| Fulfillment | `DeliveryAttemptFailed` | `Storefront/Notifications/DeliveryAttemptFailedHandler.cs` |
| Fulfillment | `ReturnToSenderInitiated` | `Storefront/Notifications/ReturnToSenderInitiatedHandler.cs` |
| Fulfillment | `ShipmentDelivered` | `Storefront/Notifications/ShipmentDeliveredHandler.cs` |
| Fulfillment | `ShipmentHandedToCarrier` | `Storefront/Notifications/ShipmentHandedToCarrierHandler.cs` |
| Fulfillment | `ShipmentLostInTransit` | `Storefront/Notifications/ShipmentLostInTransitHandler.cs` |
| Fulfillment | `TrackingNumberAssigned` | `Storefront/Notifications/TrackingNumberAssignedHandler.cs` |
| Returns | `ReturnRequested` | `Storefront/Notifications/ReturnRequestedHandler.cs` |
| Returns | `ReturnReceived` | `Storefront/Notifications/ReturnReceivedHandler.cs` |
| Returns | `ReturnApproved` | `Storefront/Notifications/ReturnApprovedHandler.cs` |
| Returns | `ReturnDenied` | `Storefront/Notifications/ReturnDeniedHandler.cs` |
| Returns | `ReturnRejected` | `Storefront/Notifications/ReturnRejectedHandler.cs` |
| Returns | `ReturnCompleted` | `Storefront/Notifications/ReturnCompletedHandler.cs` |
| Returns | `ReturnExpired` | `Storefront/Notifications/ReturnExpiredHandler.cs` |
| Returns | `ExchangeAdditionalPaymentCaptured` | `Storefront/Notifications/ExchangeAdditionalPaymentCapturedHandler.cs` |
| Returns | `ExchangePartialRefundIssued` | `Storefront/Notifications/ExchangePartialRefundIssuedHandler.cs` |
| Returns | `ExchangeCancelled` | `Storefront/Notifications/ExchangeCancelledHandler.cs` |

Subscriptions land on three RabbitMQ queues — `storefront-notifications`, `storefront-fulfillment-events`, and `storefront-returns-events` — all configured `ProcessInline` (`src/Customer Experience/Storefront.Api/Program.cs#L88-L97`).

### Published

None. The BC publishes no messages to `Messages.Contracts`. The only outbound integration traffic is HTTP queries and commands issued through the `IShoppingClient`, `IOrdersClient`, `ICustomerIdentityClient`, and `ICatalogClient` clients.

## Sagas / orchestration

Not applicable — this BC is not a saga orchestrator.

## HTTP / API surface

`Storefront.Api` exposes Wolverine.HTTP endpoints discovered from the `Storefront.Api` assembly (`src/Customer Experience/Storefront.Api/Program.cs#L62`). The SignalR hub is mounted at `/hub/storefront` with antiforgery disabled on the negotiate handshake (`Program.cs#L204`).

### Cart endpoints (delegate to Shopping)

- `GET /api/storefront/carts/{cartId}` — cart view, composed from Shopping cart + Catalog product details. Handler: `src/Customer Experience/Storefront.Api/Queries/GetCartView.cs#L12`. Auth: anonymous on the BFF route; consumer pages enforce cookie auth on the storefront side.
- `POST /api/storefront/carts/initialize` — initialize cart for `customerId`. Handler: `Commands/InitializeCart.cs#L21`.
- `POST /api/storefront/carts/{cartId}/items` — add item. Handler: `Commands/AddItemToCart.cs#L26`.
- `PUT /api/storefront/carts/{cartId}/items/{sku}/quantity` — change quantity. Handler: `Commands/ChangeItemQuantity.cs#L26`.
- `DELETE /api/storefront/carts/{cartId}/items/{sku}` — remove item. Handler: `Commands/RemoveItemFromCart.cs#L24`.
- `POST /api/storefront/carts/{cartId}/checkout` — initiate checkout. Handler: `Commands/InitiateCheckout.cs#L21`.

### Checkout endpoints (delegate to Orders)

- `GET /api/storefront/checkouts/{checkoutId}` — checkout view, composed from Orders checkout + Customer Identity saved addresses. Handler: `Queries/GetCheckoutView.cs#L12`.
- `POST /api/storefront/checkouts/{checkoutId}/shipping-address` — record shipping address. Handler: `Commands/ProvideCheckoutShippingAddress.cs#L11`.
- `POST /api/storefront/checkouts/{checkoutId}/shipping-method` — record shipping method. Handler: `Commands/SelectCheckoutShippingMethod.cs#L11`.
- `POST /api/storefront/checkouts/{checkoutId}/payment-method` — attach payment-method token. Handler: `Commands/ProvideCheckoutPaymentMethod.cs#L11`.
- `POST /api/storefront/checkouts/{checkoutId}/complete` — complete checkout, returning new `OrderId`. Handler: `Commands/CompleteCheckout.cs#L21`.

### Order endpoints (delegate to Orders)

- `GET /api/storefront/orders` — order history for the authenticated customer. Handler: `Queries/GetOrderHistory.cs#L10`.
- `GET /api/storefront/orders/{orderId}` — order view. Handler: `Queries/GetOrderView.cs#L10`.

### Product endpoints (delegate to Catalog)

- `GET /api/storefront/products?category={category}&page={page}&pageSize={pageSize}` — product listing. Handler: `Queries/GetProductListing.cs#L12`.

### Customer endpoints (delegate to Customer Identity)

- `POST /api/storefront/customers/{customerId}/addresses` — add address to the customer's address book. Handler: `Commands/AddCustomerAddress.cs#L11`.

### Real-time hub

- `/hub/storefront` — SignalR hub. Connection is parameterised by `?customerId={guid}` query string; the hub assigns the connection to the `customer:{customerId}` group on connect. Class: `src/Customer Experience/Storefront.Api/StorefrontHub.cs`.

CORS is enabled on `Storefront.Api` so that browsers in the `Storefront.Web` origin can complete the negotiate handshake and the WebSocket upgrade against a different port (`Program.cs#L36-L53`, `Program.cs#L188`).

## Frontend surface

`Storefront.Web` is a Blazor Server application using `AddInteractiveServerComponents()` and MudBlazor (`src/Customer Experience/Storefront.Web/Program.cs#L13`, `Program.cs#L17`). Cookie authentication is configured server-side with a 7-day sliding expiration and the cookie name `CritterSupply.Auth` (`Program.cs#L20-L30`). The login flow posts credentials to a server-mapped `POST /api/auth/login` endpoint that calls Customer Identity and signs in the cookie principal (`Program.cs#L77-L118`). The browser SignalR client (`wwwroot/js/signalr-client.js`) connects directly to `Storefront.Api`'s hub URL using `transport: WebSockets` and `skipNegotiation: true` (`signalr-client.js#L25-L33`), and translates Wolverine's CloudEvents envelope into a flattened `{eventType, …data}` shape consumed by `.NET` callbacks (`signalr-client.js#L46-L86`).

### `Home`

- Route: `/`
- Auth: anonymous
- BFF endpoints: none
- SignalR channels: none
- File: `src/Customer Experience/Storefront.Web/Components/Pages/Home.razor`

### `Products`

- Route: `/products`
- Auth: anonymous
- BFF endpoints: `GET /api/storefront/products` (listing); add-to-cart issues `POST /api/storefront/carts/initialize` and `POST /api/storefront/carts/{cartId}/items`
- SignalR channels: none
- File: `src/Customer Experience/Storefront.Web/Components/Pages/Products.razor`

### `Cart`

- Route: `/cart`
- Auth: `[Authorize]` (cookie required; `src/Customer Experience/Storefront.Web/Components/Pages/Cart.razor#L2`)
- BFF endpoints: `GET /api/storefront/carts/{cartId}`, `PUT /api/storefront/carts/{cartId}/items/{sku}/quantity`, `DELETE /api/storefront/carts/{cartId}/items/{sku}`, `POST /api/storefront/carts/{cartId}/checkout`
- SignalR channels: subscribes to `customer:{customerId}` and consumes `cart-updated` messages to refresh the cart drawer in real time (`Cart.razor#L218-L219`)
- File: `src/Customer Experience/Storefront.Web/Components/Pages/Cart.razor`

### `Checkout`

- Route: `/checkout`
- Auth: `[Authorize]` (`Checkout.razor#L2`)
- BFF endpoints: `GET /api/storefront/checkouts/{checkoutId}`; the four checkout-step `POST` endpoints listed under HTTP / API surface; `POST /api/storefront/customers/{customerId}/addresses` when adding a new shipping address from the wizard
- SignalR channels: none on this page; real-time order-status surfaces on `OrderConfirmation`
- File: `src/Customer Experience/Storefront.Web/Components/Pages/Checkout.razor`

### `OrderConfirmation`

- Route: `/order-confirmation/{OrderId:guid}`
- Auth: anonymous on the route attribute; the page reads the `CustomerId` claim from `AuthenticationStateProvider` and only subscribes to SignalR if a claim is present (`OrderConfirmation.razor#L165-L179`)
- BFF endpoints: `GET /api/storefront/orders/{orderId}`
- SignalR channels: subscribes to `customer:{customerId}` and consumes `order-status-changed`, `shipment-status-changed`, `return-status-changed`, `return-exchange-payment-changed` to drive the order-status timeline (`OrderConfirmation.razor#L225-L226`); status copy is rendered via `StorefrontStatusMapper`
- File: `src/Customer Experience/Storefront.Web/Components/Pages/OrderConfirmation.razor`

### `OrderHistory`

- Route: `/orders`
- Auth: `[Authorize]` (`OrderHistory.razor#L2`)
- BFF endpoints: `GET /api/storefront/orders`
- SignalR channels: none
- File: `src/Customer Experience/Storefront.Web/Components/Pages/OrderHistory.razor`

### `Account`

- Route: `/account`
- Auth: `[Authorize]` (`Account.razor#L2`)
- BFF endpoints: reads cookie principal claims rendered into the page; address management surfaces are reached from `Checkout`
- SignalR channels: none
- File: `src/Customer Experience/Storefront.Web/Components/Pages/Account.razor`

### `Login`

- Route: `/login`
- Auth: anonymous
- BFF endpoints: `POST /api/auth/login` on `Storefront.Web` itself (`src/Customer Experience/Storefront.Web/Program.cs#L77`), which calls Customer Identity `POST /api/auth/login` and signs in the cookie
- SignalR channels: none
- File: `src/Customer Experience/Storefront.Web/Components/Pages/Login.razor`

### `NotFound`

- Route: `/not-found` (also rendered via `UseStatusCodePagesWithReExecute("/not-found", …)`, `Storefront.Web/Program.cs#L66`)
- Auth: anonymous
- BFF endpoints: none
- SignalR channels: none
- File: `src/Customer Experience/Storefront.Web/Components/Pages/NotFound.razor`

### `Error`

- Route: `/Error`
- Auth: anonymous
- BFF endpoints: none
- SignalR channels: none
- File: `src/Customer Experience/Storefront.Web/Components/Pages/Error.razor`

### Layout / cross-page surface

The `InteractiveAppBar` layout component subscribes to `customer:{customerId}` on every authenticated page render and consumes `cart-updated` to drive the cart-badge count in the top app bar (`src/Customer Experience/Storefront.Web/Components/Layout/InteractiveAppBar.razor#L96-L101`). The `AddAddressDialog` MudBlazor dialog (`Components/Pages/AddAddressDialog.razor`) is invoked from `Checkout` and posts to `POST /api/storefront/customers/{customerId}/addresses`. `ReconnectModal` (`Components/Layout/ReconnectModal.razor`) presents Blazor-Server reconnect state to the user.

## Identity / auth posture

- Scheme on `Storefront.Web`: cookie-based session (`CookieAuthenticationDefaults.AuthenticationScheme`) — cookie name `CritterSupply.Auth`, `HttpOnly`, `SameSite=Lax`, 7-day sliding expiration (`src/Customer Experience/Storefront.Web/Program.cs#L20-L30`). The cookie is minted by the server-mapped `POST /api/auth/login` endpoint after a successful call to Customer Identity's `POST /api/auth/login`; the resulting `ClaimsPrincipal` carries `CustomerId`, `Email`, `Name`, `GivenName`, and `Surname` claims (`Storefront.Web/Program.cs#L97-L110`). Source of the underlying session contract: ADR 0012 (Customer Identity session-based authentication).
- Propagation to SignalR: each page that needs real-time updates reads the `CustomerId` claim from `AuthenticationStateProvider` and passes it as the `customerId` query-string parameter on the hub URL when calling `signalrClient.subscribe` (`Cart.razor#L218`, `OrderConfirmation.razor#L226`, `InteractiveAppBar.razor#L100`). The hub places the connection into `customer:{customerId}` on `OnConnectedAsync` (`StorefrontHub.cs#L19-L25`), and the `IStorefrontWebSocketMessage.CustomerId` value on each outgoing message determines which group Wolverine targets (`Program.cs#L66-L73`, `IStorefrontWebSocketMessage.cs#L7`). The Blazor cookie itself is not forwarded to the hub; group membership is established from the query string at connection time and the connection is then trusted for the lifetime of the WebSocket.
- Additional schemes registered on `Storefront.Api`: two JWT bearer schemes — `"Backoffice"` (authority `https://localhost:5249`, Backoffice Identity BC) and `"Vendor"` (authority `https://localhost:5240`, Vendor Identity BC) — both with `RoleClaimType = "role"` (`src/Customer Experience/Storefront.Api/Program.cs#L136-L162`). No endpoint in `Storefront.Api` currently applies an `[Authorize]` policy referencing either scheme; they are registered as a hosting prerequisite for cross-portal callers.

## Tests as behavioral evidence

### Gherkin features (`docs/features/customer-experience/`)

- `cart-real-time-updates.feature` — 17 scenarios covering cart-page SSE / SignalR push for add / remove / quantity-change, multi-tab and multi-customer isolation, reconnect behavior, single-stream multiplexing, and Catalog-detail rendering.
- `checkout-flow.feature` — 21 scenarios covering happy-path checkout with saved address, step navigation and persistence, address and shipping-method selection, payment-token submission, order placement triggering the Orders saga, order-confirmation real-time updates, error paths (empty cart, cart cleared mid-checkout, invalid token, declined payment), multi-BC composition, and three tagged scenarios under `@mobile` / `@accessibility`.
- `product-browsing.feature` — 24 scenarios covering listing, pagination, category filter, search, detail view, image gallery, add-to-cart from listing and detail, multi-BC composition, with two `@future` scenarios documenting stock-availability surfaces (`Display stock availability on product listing`, `Cannot add out-of-stock product to cart`) plus `@performance`, `@mobile`, and `@accessibility` tagged scenarios.
- `storefront-protected-routes.feature` — 3 `@auth` scenarios covering redirect-to-login on `/checkout` and `/cart` for anonymous users and redirect-to-cart on `/checkout` for authenticated users without an active cart.

No `@pending` or `@wip` scenarios are present in the feature set; the `@future` tag appears twice on `product-browsing.feature` (the two stock-availability scenarios).

### Integration tests (`tests/Customer Experience/Storefront.Api.IntegrationTests/`)

- `AddCustomerAddressTests.cs` — 3 tests: round-trip behavior of the address-add command and its delegation to the Customer Identity stub.
- `CartCommandTests.cs` — 11 tests: each cart command's HTTP route (initialize, add, change quantity, remove, initiate checkout) plus FluentValidation rejection cases.
- `CartViewCompositionTests.cs` — 3 tests: cart-view composition combining Shopping cart with Catalog product enrichment.
- `CartViewErrorHandlingTests.cs` — 5 tests: 404 / null / partial-failure handling when Shopping or Catalog returns no data.
- `CartWorkflowTests.cs` — 4 tests: end-to-end cart workflows across multiple commands.
- `CheckoutViewCompositionTests.cs` — 3 tests: checkout-view composition combining Orders checkout with Customer Identity saved addresses.
- `OrderHistoryTests.cs` — 2 tests: paged order-history retrieval through the Orders client.
- `ProductListingCompositionTests.cs` — 5 tests: product-listing composition from Catalog.
- `ProductListingErrorHandlingTests.cs` — 7 tests: error responses from Catalog mapped through the BFF.
- `SignalRNotificationTests.cs` — 15 tests: each integration-event handler emits the expected `IStorefrontWebSocketMessage` with the correct group target.
- `TestFixture.cs` — Alba/`WebApplicationFactory` host with stub clients (`Stubs/Stub{Shopping,Orders,Catalog,CustomerIdentity}Client.cs`).

### Component / unit tests (`tests/Customer Experience/Storefront.Web.UnitTests/`)

- `Components/Pages/AccountTests.cs` — 4 tests.
- `Components/Pages/HomeTests.cs` — 8 tests.
- `Components/Pages/LoginTests.cs` — 7 tests.
- `Components/Pages/NotFoundTests.cs` — 4 tests.
- `Components/Pages/OrderConfirmationTests.cs` — 25 tests covering the order-confirmation page and SignalR-driven status timeline.
- `Components/Pages/OrderConfirmationTimelineTests.cs` — 7 tests covering the timeline component lifecycle.
- `Components/Pages/OrderHistoryTests.cs` — 5 tests.
- `Components/Pages/ProductsTests.cs` — 10 tests.
- `RealTime/StorefrontEventReaderTests.cs` — 6 tests covering the defensive `JsonElement` accessors.
- `RealTime/StorefrontStatusMapperCancelledTests.cs` — 3 tests covering the M47.0 / Slice 4 `"Cancelled"` status copy path.
- `RealTime/StorefrontStatusMapperExchangePaymentTests.cs` — 12 tests covering the cross-product-exchange capture and refund copy paths and the unknown-`paymentKind` fallback.

### End-to-end tests (`tests/Customer Experience/Storefront.E2ETests/`)

- `Features/CheckoutFlowStepDefinitions.cs` and `Features/checkout-flow.feature.cs` — Reqnroll step definitions and generated bindings for the checkout-flow Gherkin feature, run through Playwright (`Hooks/PlaywrightHooks.cs`) against `Storefront.Web` with stub BFF clients.
- `Features/ProtectedRoutesStepDefinitions.cs` and `Features/storefront-protected-routes.feature.cs` — bindings for the protected-routes feature.
- `Features/order-history.feature.cs` — generated bindings for an order-history scenario set.
- `Pages/{CartPage,CheckoutPage,LoginPage,OrderConfirmationPage}.cs` — Playwright page objects.

## ADRs

- **ADR 0013** — Migrate from SSE to SignalR for Real-Time Communication. Replaces the prior server-sent-events transport with SignalR over WebSockets for storefront real-time updates and establishes the bidirectional foundation that this BC's hub uses today. File: `docs/decisions/0013-signalr-migration-from-sse.md`.
- **ADR 0034** — Backoffice BFF Architecture. Documents the BFF pattern as validated during Backoffice M32.0; referenced from this BC because the same pattern shape (composition + projections + real-time push) describes Customer Experience, though the Customer Experience BFF predates this ADR. File: `docs/decisions/0034-backoffice-bff-architecture.md`.
- **ADR 0043** — Storefront Web Technology Options Evaluation. Catalogues the current `Storefront.Web` technology choices (Blazor Server, cookie session, MudBlazor, SignalR-over-WebSockets) and evaluates alternatives. File: `docs/decisions/0043-storefront-web-technology-options.md`.

## Prior event modeling

None on file. The S2 dossier is the first formal modeling artifact for this BC.

## Source citations (S2 full)

- `src/Customer Experience/` (folder root)
- `src/Customer Experience/Storefront/Notifications/` (23 integration-event handlers)
- `src/Customer Experience/Storefront/Composition/` (`CartView`, `CheckoutView`, `ProductListingView`)
- `src/Customer Experience/Storefront/Clients/` (`IShoppingClient`, `IOrdersClient`, `ICustomerIdentityClient`, `ICatalogClient`)
- `src/Customer Experience/Storefront/RealTime/IStorefrontWebSocketMessage.cs`
- `src/Customer Experience/Storefront/RealTime/StorefrontEvent.cs`
- `src/Customer Experience/Storefront.Api/Program.cs` (handler discovery, RabbitMQ subscriptions, SignalR wiring, multi-issuer JWT registration, CORS)
- `src/Customer Experience/Storefront.Api/StorefrontHub.cs`
- `src/Customer Experience/Storefront.Api/Queries/` (5 query handlers)
- `src/Customer Experience/Storefront.Api/Commands/` (10 command handlers)
- `src/Customer Experience/Storefront.Api/Clients/` (HTTP client implementations)
- `src/Customer Experience/Storefront.Web/Program.cs` (cookie auth, login mapping)
- `src/Customer Experience/Storefront.Web/Components/Pages/` (10 Razor pages)
- `src/Customer Experience/Storefront.Web/Components/Layout/InteractiveAppBar.razor`
- `src/Customer Experience/Storefront.Web/RealTime/StorefrontStatusMapper.cs`
- `src/Customer Experience/Storefront.Web/wwwroot/js/signalr-client.js`
- `CONTEXTS.md` (section: `Customer Experience`)
- `docs/decisions/0013-signalr-migration-from-sse.md`
- `docs/decisions/0034-backoffice-bff-architecture.md`
- `docs/decisions/0043-storefront-web-technology-options.md`
- `docs/skills/bff-realtime-patterns.md`
- `docs/features/customer-experience/cart-real-time-updates.feature`
- `docs/features/customer-experience/checkout-flow.feature`
- `docs/features/customer-experience/product-browsing.feature`
- `docs/features/customer-experience/storefront-protected-routes.feature`
- `tests/Customer Experience/Storefront.Api.IntegrationTests/` (10 test classes + stubs + fixture)
- `tests/Customer Experience/Storefront.Web.UnitTests/` (8 page test classes + 3 RealTime test classes)
- `tests/Customer Experience/Storefront.E2ETests/` (Reqnroll + Playwright suite)
- `Messages.Contracts/Shopping/`, `Messages.Contracts/Orders/`, `Messages.Contracts/Payments/`, `Messages.Contracts/Inventory/`, `Messages.Contracts/Fulfillment/`, `Messages.Contracts/Returns/` (subscribed contracts)
