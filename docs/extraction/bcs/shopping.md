# Shopping

> **Source folder:** `src/Shopping/`
> **Status:** Implemented
> **Most recent material milestone:** M30.1 — Shopping ↔ Promotions coupon integration
> **Stub depth:** S1 — to be deepened in S2

## Purpose

Shopping owns the customer's pre-purchase cart lifecycle. A customer adds items to a cart, changes quantities, removes items, applies and removes coupons, and ultimately initiates checkout — at which point the cart hands off to Orders. Prices are captured at the moment an item is added to the cart and held there until the cart is cleared or checked out, so the customer sees the same price they first saw on the product page.

## Top-level structure

### Aggregates

- `Cart` — stream ID: UUID v7 (natural; one stream per cart).

### Commands

- `InitializeCart`
- `AddItemToCart`
- `RemoveItemFromCart`
- `ChangeItemQuantity`
- `ClearCart`
- `ApplyCouponToCart`
- `RemoveCouponFromCart`
- `InitiateCheckout`

### Domain events

- `CartInitialized`
- `ItemAdded`
- `ItemRemoved`
- `ItemQuantityChanged`
- `CartCleared`
- `CartAbandoned`
- `CouponApplied`
- `CouponRemoved`
- `CheckoutInitiated`

### Projections

- `Cart` snapshot — inline, keyed by `CartId` (Marten `Snapshot<Cart>` registered in `Shopping.Api/Program.cs`).

### Integration events

- `Shopping.ItemAdded` — publishes
- `Shopping.ItemRemoved` — publishes
- `Shopping.ItemQuantityChanged` — publishes
- `Shopping.CouponApplied` — publishes
- `Shopping.CouponRemoved` — publishes
- `Shopping.CheckoutInitiated` — publishes
- `Shopping.CartCheckoutCompleted` — publishes

### HTTP / API surface (one line)

`Shopping.Api` exposes cart commands and a cart-read query for the storefront BFF and serves as the integration boundary for cart events into RabbitMQ.

### Frontend surface (if applicable)

Not applicable — no frontend in this BC.

### Identity / auth posture (if applicable)

Consumes identity from Customer Identity (cookie session for storefront callers; JWT bearer for Backoffice / Vendor Portal callers).

## Prior event modeling

None on file.

## ADRs

- ADR 0001 — Checkout Migration to Orders BC
- ADR 0017 — Price Freeze at Add-to-Cart (Not Checkout)

## Source citations (S1 stub)

- `src/Shopping/`
- `src/Shared/Messages.Contracts/Shopping/`
- `CONTEXTS.md` (section: `Shopping`)
- `docs/decisions/0001-checkout-migration-to-orders.md`
- `docs/decisions/0017-price-freeze-at-add-to-cart.md`
