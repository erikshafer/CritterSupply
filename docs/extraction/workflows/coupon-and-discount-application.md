# Coupon-and-discount application

> **Status:** Active
> **Type:** Choreography (synchronous HTTP only)
> **Initiating actor:** Customer
> **BCs involved:** Shopping, Pricing, Promotions
> **Most recent material milestone:** M40.0 — Promotions DCB

## Purpose

A customer enters a coupon code or adds an item to their cart. Shopping fetches the authoritative price from Pricing and (when a coupon is applied) asks Promotions to validate the coupon and calculate the discount against the cart's line items. All three steps run synchronously over HTTP at cart-mutation time; no integration events flow between Shopping ↔ Pricing or Shopping ↔ Promotions on this workflow.

## Actors and triggers

- **Initiating actor:** Customer (anonymous session or authenticated)
- **Trigger:** Either `POST /api/carts/{cartId}/items` (Pricing lookup) or `POST /api/carts/{cartId}/apply-coupon` (Promotions validation + discount calculation) on the Shopping BC
- **Prerequisite state:** A non-terminal `Cart` aggregate exists in Shopping; for coupon application, the cart must contain at least one line item.

## Trace

| Step | BC | Command / Event / Edge | Resulting state / output | Dossier reference |
|------|----|------------------------|--------------------------|-------------------|
| 1 | Shopping | `AddItemToCart` handler `ValidateAsync` calls `IPricingClient.GetPriceAsync(sku)` | Pricing returns `BasePrice` + status; non-`Published` status rejected at HTTP layer | `bcs/shopping.md#commands`, `bcs/shopping.md#http-api-surface` |
| 2 | Pricing | `GET /api/pricing/products?skus={sku}` served by `Pricing.Api` reads | Returns current `CurrentPriceView` projection rows | `bcs/pricing.md#read`, `bcs/pricing.md#projections` |
| 3 | Shopping | On success, `AddItemToCart` appends `ItemAdded` carrying the Pricing-returned `UnitPrice` (frozen for the cart's life per ADR 0017) | Cart updated; `Messages.Contracts.Shopping.ItemAdded` published | `bcs/shopping.md#commands`, `bcs/shopping.md#integration-events` |
| 4 | Shopping | `ApplyCouponToCart` handler `ValidateAsync` calls `IPromotionsClient.ValidateCouponAsync(code)` | Promotions returns valid/invalid; invalid rejected at HTTP layer | `bcs/shopping.md#commands` |
| 5 | Promotions | `GET /api/promotions/coupons/{code}/validate` reads `CouponLookupView` (O(1) keyed by code) | Returns validation result + coupon descriptor | `bcs/promotions.md#query-consumed-by-shopping`, `bcs/promotions.md#projections` |
| 6 | Shopping | If valid, calls `IPromotionsClient.CalculateDiscountAsync(coupon, lineItems)` | Promotions returns `TotalDiscount` against the cart's line items | `bcs/shopping.md#commands` |
| 7 | Promotions | `POST /api/promotions/discounts/calculate` reads `Promotion` / `Coupon` aggregates via Marten | Returns computed `TotalDiscount` | `bcs/promotions.md#query-consumed-by-shopping` |
| 8 | Shopping | Appends `CouponApplied(CouponCode, DiscountAmount)`; publishes `Messages.Contracts.Shopping.CouponApplied` | Cart updated; CX SignalR `CartUpdated` push downstream | `bcs/shopping.md#integration-events` |

Recording of the coupon redemption (separate from validation/calculation) is a different workflow — see `coupon-redemption-recording.md`.

## Projections and views

- `CurrentPriceView` (owned by Pricing) — inline projection over `ProductPrice` stream; served by `GET /api/pricing/products?skus=...`. Dossier: `bcs/pricing.md#projections`.
- `CouponLookupView` (owned by Promotions) — `MultiStreamProjection<CouponLookupView, string>` keyed by coupon code for O(1) validation reads. Dossier: `bcs/promotions.md#projections`.

## Compensation paths

### Failure: Pricing returns non-`Published` status or 404
- **Compensating action:** `AddItemToCart.ValidateAsync` rejects at HTTP layer (4xx); no `ItemAdded` event appended.
- **Resulting state:** Cart unchanged.
- **BCs involved in compensation:** Shopping (request-time short-circuit).

### Failure: Promotions reports coupon invalid
- **Compensating action:** `ApplyCouponToCart.ValidateAsync` rejects at HTTP layer (4xx); no `CouponApplied` event appended.
- **Resulting state:** Cart unchanged.
- **BCs involved in compensation:** Shopping.

### Failure: Pricing or Promotions HTTP endpoint is unreachable / times out
- **Compensating action:** None currently wired. `IPricingClient` and `IPromotionsClient` calls propagate exceptions; no fallback price, no fallback discount, no circuit breaker is documented in the dossiers.
- **Resulting state:** HTTP request fails; cart unchanged.
- **BCs involved in compensation:** None — failure visible only at the HTTP layer.

## Variants and edge cases

### Removing a coupon
`RemoveCouponFromCart` does not call Promotions; it appends `CouponRemoved(CartId, CouponCode, RemovedAt)` and publishes the corresponding integration event. The recorded discount is dropped from the cart inline. Dossier: `bcs/shopping.md#commands`.

### Quantity change after coupon applied
`ChangeItemQuantity` does not re-call Promotions; the previously calculated `DiscountAmount` on `CouponApplied` is not refreshed. No documented re-evaluation path exists in code.

## BCs and roles

- **Shopping** — Cart owner; invokes both downstream BCs synchronously at command-validation time. Dossier: `bcs/shopping.md`.
- **Pricing** — Read-side over `CurrentPriceView`; serves SKU price lookups via HTTP. Does not publish published-price integration events (3 outbound contracts declared, 0 wired). Dossier: `bcs/pricing.md`.
- **Promotions** — DCB-event-sourced; serves coupon validation and discount calculation via HTTP. Does not publish coupon-validation events. Dossier: `bcs/promotions.md`.

## Tests as behavioral evidence

- **Gherkin features:** none specific to coupon validation; `docs/features/customer-experience/cart-real-time-updates.feature` covers the SignalR side after `CouponApplied`.
- **Integration tests (Alba):**
  - `tests/Shopping/Shopping.Api.IntegrationTests/Cart/CouponOperationsTests.cs` — 11 tests covering apply/remove flows and the Promotions `ValidateAsync` rejection path (uses Promotions HTTP stub).
  - `tests/Shopping/Shopping.Api.IntegrationTests/Cart/AddItemToCartValidationTests.cs` — 8 tests including unknown-SKU and non-`Published` Pricing status (uses Pricing HTTP stub).
- **Test stubs:** `tests/Shopping/Shopping.Api.IntegrationTests/Stubs/` provides Pricing and Promotions HTTP client stubs. Real cross-BC integration coverage is not present; Pricing and Promotions are tested independently.

## ADRs

- **ADR 0016** — UUID v5 for natural-key stream IDs (Pricing uses `pricing:{SKU}`). File: `docs/decisions/0016-uuid-v5-for-natural-key-stream-ids.md`.
- **ADR 0017** — Price Freeze at Add-to-Cart (Not Checkout). File: `docs/decisions/0017-price-freeze-at-add-to-cart.md`.

## Declared vs. implemented

- **Declared shape (CONTEXTS.md line 234):** "Shopping → publishes — Published prices consumed by carts" implies Pricing publishes a `PricePublished` / `PriceUpdated` integration event that Shopping subscribes to.
- **Implemented shape:** Pricing declares 3 outbound contracts (`PricePublished`, `PriceUpdated`, `VendorPriceSuggestionSubmitted`) under `Messages.Contracts.Pricing/`; **none are wired** to a publisher route in `Pricing.Api/Program.cs`. Shopping consumes prices via synchronous HTTP `GET /api/pricing/products?skus=...` only.
- **Gap:** The CONTEXTS.md narrative of asynchronous price flow over RabbitMQ does not exist in code. Dossier source: `bcs/pricing.md#contextsmd-drift` (item 1).

- **Declared shape (CONTEXTS.md line 237):** "Bulk pricing uses a saga with approval workflow."
- **Implemented shape:** No saga in Pricing; only a `BulkPricingJobId` correlation slot on `PriceChanged`. Dossier source: `bcs/pricing.md#contextsmd-drift` (item 4).
- **Gap:** Bulk pricing approval workflow is documented but not present in code.

## Source citations

- Dossier sections referenced: `bcs/shopping.md#commands`, `bcs/shopping.md#http-api-surface`, `bcs/shopping.md#integration-events`, `bcs/pricing.md#read`, `bcs/pricing.md#projections`, `bcs/pricing.md#contextsmd-drift`, `bcs/promotions.md#query-consumed-by-shopping`, `bcs/promotions.md#projections`.
- ADRs: `docs/decisions/0016-uuid-v5-for-natural-key-stream-ids.md`, `docs/decisions/0017-price-freeze-at-add-to-cart.md`.
