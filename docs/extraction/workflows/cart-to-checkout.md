# Cart-to-checkout

> **Status:** Active
> **Type:** Choreography
> **Initiating actor:** Customer
> **BCs involved:** Customer Experience, Shopping, Orders
> **Most recent material milestone:** M47.0 — Storefront real-time and cross-product exchange

## Purpose

A customer assembles a cart of catalog items in the storefront and submits it for purchase. Cart contents are owned by the Shopping BC and handed off to the Orders BC at checkout time via a single integration message, which starts the Order saga. The customer-facing surface is composed by the Customer Experience BFF and updated in real time as cart mutations are published.

## Actors and triggers

- **Initiating actor:** Customer (anonymous session or authenticated)
- **Trigger:** `POST /api/carts/{cartId}/checkout` on the Shopping BC, ultimately routed through the Customer Experience BFF cart endpoints
- **Prerequisite state:** A non-empty, non-terminal `Cart` aggregate exists in Shopping (created by `InitializeCart`, populated by `AddItemToCart` / `ChangeItemQuantity` / `ApplyCouponToCart`)

## Trace

| Step | BC | Command / Event / Edge | Resulting state / output | Dossier reference |
|------|----|------------------------|--------------------------|-------------------|
| 1 | Customer Experience | Cart endpoints (`POST /api/cart/items` etc) delegate to Shopping via `IShoppingClient` | HTTP proxy to Shopping cart commands | `bcs/customer-experience.md#cart-endpoints-delegate-to-shopping` |
| 2 | Shopping | `InitializeCart` → `CartInitialized` (UUID v7 stream) | `Cart` stream created (Customer or anonymous Session scope) | `bcs/shopping.md#commands` |
| 3 | Shopping | `AddItemToCart` (re-prices via Pricing HTTP), `ChangeItemQuantity`, `RemoveItemFromCart`, `ApplyCouponToCart` (validates via Promotions HTTP), `ClearCart` | Inline `Cart` snapshot updated; one of `ItemAdded` / `ItemQuantityChanged` / `ItemRemoved` / `CouponApplied` / `CouponRemoved` integration events published to RabbitMQ queue `storefront-notifications` | `bcs/shopping.md#integration-events` |
| 4 | Customer Experience | Subscribes to the 5 Shopping cart events on `storefront-notifications` | `CartUpdated` SignalR message pushed to `customer:{customerId}` group on `StorefrontHub` | `bcs/customer-experience.md#channel-cart-updates`, `bcs/customer-experience.md#subscribed-consolidated` |
| 5 | Shopping | `InitiateCheckout` — generates new `CheckoutId` (UUID v7), appends `CheckoutInitiated` (terminal Cart event), publishes `Messages.Contracts.Shopping.CheckoutInitiated` to queue `orders-checkout-initiated` | Cart transitions to terminal; integration message in flight | `bcs/shopping.md#commands`, `bcs/shopping.md#integration-events` |
| 6 | Orders | `CheckoutInitiatedHandler` consumes from `orders-checkout-initiated` | Opens a new `Checkout` aggregate stream (ADR 0001) | `bcs/orders.md#subscribes`, `bcs/orders.md#aggregates` |
| 7 | Orders | Checkout step endpoints (`SetShippingAddress`, `SetShippingMethod`, `SetPaymentMethod`, `PlaceOrder`) drive the Checkout aggregate through to its terminal `CheckoutCompleted` event, which is republished as `Shopping.CartCheckoutCompleted` | Triggers the Order saga (separate workflow) | `bcs/orders.md#checkout-step-endpoints`, `bcs/orders.md#publishes` |

The handoff between Cart and Checkout aggregates is the seam that completes the cart-to-checkout workflow. Everything from `CartCheckoutCompleted` onward is the Order saga (see `order-saga.md`).

## Projections and views

- `Cart` inline snapshot (owned by Shopping) — served by `GET /api/carts/{cartId}` via `AggregateStreamAsync`; rendered into `CartResponse` (derives `TotalAmount`, `AppliedDiscount`, `DiscountedTotal`) at query time. Dossier reference: `bcs/shopping.md#projections`.
- `Checkout` inline snapshot (owned by Orders) — served by the Orders checkout-step endpoints during the BFF-mediated checkout funnel. Dossier reference: `bcs/orders.md#aggregates`.
- Storefront cart view (rendered in `Cart.razor`, owned by Customer Experience) — composed live from Shopping's `GET /api/carts/{cartId}` and updated in real time by the SignalR `CartUpdated` channel. Dossier reference: `bcs/customer-experience.md#cart`.

## Compensation paths

The cart-to-checkout workflow is choreographic and short-lived; compensation is limited to two failure points before the saga begins.

### Failure: `AddItemToCart` Pricing validation rejects unknown SKU or non-Published price status
- **Compensating action:** HTTP request rejected at `ValidateAsync` (handler returns 400/404); no `ItemAdded` event is appended.
- **Resulting state:** Cart unchanged.
- **BCs involved in compensation:** Shopping (single-BC short-circuit).

### Failure: `ApplyCouponToCart` Promotions validation rejects coupon
- **Compensating action:** HTTP request rejected at `ValidateAsync`; no `CouponApplied` event is appended.
- **Resulting state:** Cart unchanged.
- **BCs involved in compensation:** Shopping.

### Failure: Customer abandons cart after `InitiateCheckout` but before `CartCheckoutCompleted`
- **Compensating action:** None currently wired. The `CartAbandoned` event record and `Cart.Apply(CartAbandoned)` exist but no production code path appends them (unit-test coverage only). The Checkout aggregate in Orders has no time-bounded close; cart-to-checkout handoffs that stall produce orphan Checkout streams.
- **Resulting state:** Cart in terminal `Closed` state (set by `CheckoutInitiated`); Checkout in indeterminate non-terminal state.
- **BCs involved in compensation:** None — declared-but-unimplemented in code.

## Variants and edge cases

### Anonymous-session cart
The `InitializeCart` command accepts either `CustomerId` (authenticated) or `SessionId` (guest). Cart endpoints carry no `[Authorize]` attribute. The handoff to Orders still publishes `CheckoutInitiated` with a nullable `CustomerId`. Dossier: `bcs/shopping.md#commands`.

### Repeated `AddItemToCart` for the same SKU
`Cart.Apply(ItemAdded)` sums quantity rather than appending a duplicate line item. The SignalR push is still one `CartUpdated` per `ItemAdded` event. Dossier: `bcs/shopping.md#aggregates` and `bcs/shopping.md#integration-events`.

## BCs and roles

- **Customer Experience** — BFF surface; renders the storefront cart page (`Cart.razor`, `OrderConfirmation.razor`); proxies cart commands to Shopping; subscribes to Shopping's cart events and pushes `CartUpdated` SignalR messages to the customer's group. Dossier: `bcs/customer-experience.md`.
- **Shopping** — Owns the `Cart` event-sourced aggregate; emits the five cart-mutation integration events and the terminal `CheckoutInitiated` handoff event. Dossier: `bcs/shopping.md`.
- **Orders** — Consumes `CheckoutInitiated` from queue `orders-checkout-initiated`; opens the `Checkout` aggregate; runs the checkout funnel; terminates with `CartCheckoutCompleted` (which begins the Order saga, see `order-saga.md`). Dossier: `bcs/orders.md`.

## Tests as behavioral evidence

- **Gherkin features:**
  - `docs/features/customer-experience/cart-real-time-updates.feature` — covers SSE/SignalR push for the five cart integration events.
  - `docs/features/customer-experience/checkout-flow.feature` — covers the customer journey from cart through `InitiateCheckout` and onward to Orders pickup.
- **Integration tests (Alba):**
  - `tests/Shopping/Shopping.Api.IntegrationTests/Cart/CartLifecycleTests.cs` — 9 tests covering the end-to-end lifecycle from `InitializeCart` to `InitiateCheckout` and `Cleared` terminations.
  - `tests/Shopping/Shopping.Api.IntegrationTests/Cart/InitiateCheckoutHttpTests.cs` — 5 tests including verification that `Messages.Contracts.Shopping.CheckoutInitiated` is produced.
  - `tests/Customer Experience/Storefront.Api.IntegrationTests/` — proxy-endpoint coverage.
- **Unit tests:** `tests/Shopping/Shopping.UnitTests/Cart/CartApplyTests.cs` (23 tests across each `Apply` overload, including `CartAbandoned`) and `CartCreateTests.cs` (9 tests).
- **`@pending` / `@wip` / `@future`:** none in Shopping's test surface.

## ADRs

- **ADR 0001** — Checkout Migration to Orders BC. Established that the `Checkout` aggregate lives in Orders and that Shopping hands off via `CheckoutInitiated`. File: `docs/decisions/0001-checkout-migration-to-orders.md`.
- **ADR 0017** — Price Freeze at Add-to-Cart (Not Checkout). Unit price written into `CartLineItem` is the Pricing BC price at add-to-cart time, held immutable for the cart's life. The ADR's `PriceFrozenAt` field and TTL-driven `PriceRefreshed` flow are not present in `CartLineItem.cs` or in any Shopping handler today. File: `docs/decisions/0017-price-freeze-at-add-to-cart.md`.

## Declared vs. implemented

- **Declared shape:** ADR 0017 describes `PriceFrozenAt` timestamps and a TTL-driven price-refresh flow on cart line items.
- **Implemented shape:** `CartLineItem` carries `UnitPrice` only; no `PriceFrozenAt`; no refresh flow.
- **Gap:** Stale-price detection is absent; the `ItemAdded` price persists for the lifetime of the cart without time-bound revalidation.

- **Declared shape:** `CartAbandoned` event record exists with an `Apply` overload on the `Cart` aggregate.
- **Implemented shape:** No production code path appends `CartAbandoned`; only `CartApplyTests` exercises the `Apply` overload.
- **Gap:** Cart-abandonment workflows are declared in the type system but not instantiated.

## Source citations

- Dossier sections referenced: `bcs/shopping.md#commands`, `bcs/shopping.md#integration-events`, `bcs/shopping.md#projections`, `bcs/shopping.md#aggregates`, `bcs/orders.md#subscribes`, `bcs/orders.md#aggregates`, `bcs/orders.md#checkout-step-endpoints`, `bcs/orders.md#publishes`, `bcs/customer-experience.md#cart-endpoints-delegate-to-shopping`, `bcs/customer-experience.md#subscribed-consolidated`, `bcs/customer-experience.md#channel-cart-updates`, `bcs/customer-experience.md#cart`.
- Gherkin: `docs/features/customer-experience/cart-real-time-updates.feature`, `docs/features/customer-experience/checkout-flow.feature`.
- ADRs: `docs/decisions/0001-checkout-migration-to-orders.md`, `docs/decisions/0017-price-freeze-at-add-to-cart.md`.
