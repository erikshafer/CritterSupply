# ADR 0017: Price Freeze at Add-to-Cart (Not Checkout)

**Status:** ✅ Accepted

**Date:** 2026-03-08

**Context:** Pricing BC Phase 1 — When is price snapshot captured for order processing?

---

## Context

The Pricing BC introduces server-authoritative pricing to close the critical security gap in Shopping BC where clients can supply arbitrary `UnitPrice` values. A key question arises: **when is the price frozen for an order?**

Two competing approaches exist:

1. **Add-to-cart freeze:** Price captured when `AddItemToCart` command executes
2. **Checkout freeze:** Price captured when `PlaceOrder` command executes (requires synchronous Pricing BC call during checkout)

CONTEXTS.md currently states *"price-at-checkout immutability"* but the Pricing BC event modeling document recommends add-to-cart freeze. This ADR resolves the contradiction.

### Current State (Before Pricing BC)

- Shopping BC accepts client-supplied `UnitPrice` in `AddItemToCart` command
- **Security vulnerability:** Client can submit any price (e.g., $0.01 for a $50 product)
- No price validation occurs
- Cart line items store the client-supplied price

### Industry Standards

- **Amazon:** Price locks at add-to-cart, displays "price changed" banner if price updates before checkout
- **Shopify:** Price locks at add-to-cart, cart refresh updates to new price
- **WooCommerce:** Price locks at add-to-cart, cart displays "price updated" notice

---

## Decision

**Price is frozen at add-to-cart time, not checkout time.**

When `AddItemToCart` is executed:
1. Shopping BC calls `IPricingClient.GetCurrentPriceAsync(sku)` synchronously
2. Authoritative price from Pricing BC overwrites client-supplied price
3. Price is stored in `CartLineItem` (immutable once added)
4. **Cart price TTL:** 1 hour (configurable via `appsettings.json: CartPriceTtl`)

After TTL expiry:
- BFF queries Pricing BC for fresh prices before displaying cart
- If price changed → Shopping BC publishes `PriceRefreshed` event
- UI displays notification: *"Price updated for [Product Name]: was $24.99, now $21.99"*

**No synchronous Pricing BC call during checkout.** Orders BC receives `CheckoutLineItem[]` with pre-frozen prices from Shopping BC.

---

## Rationale

### Why Add-to-Cart Freeze?

**1. Eliminates Temporal Coupling**

If Pricing BC is unavailable at checkout time:
- ❌ Checkout freeze: Order placement fails (Pricing BC outage blocks revenue)
- ✅ Add-to-cart freeze: Order placement succeeds (price already captured)

**2. Industry Standard**

Major e-commerce platforms freeze at add-to-cart. Users expect this behavior:
- Add item → see price in cart → price stays same during checkout (unless explicit refresh)
- If price changes, user is notified and can decide whether to proceed

**3. Simpler Implementation**

- ❌ Checkout freeze: Synchronous HTTP call to Pricing BC in Order saga's critical path
- ✅ Add-to-cart freeze: Single sync call when adding item, async refresh on TTL expiry

**4. Better Availability**

Pricing BC unavailability impacts:
- ❌ Checkout freeze: Cannot place orders (critical path blocked)
- ✅ Add-to-cart freeze: Cannot add NEW items to cart, but can still checkout with existing cart

**5. Matches User Mental Model**

Users perceive the cart as a *"snapshot of what I'm buying"*. Price should not change silently. Add-to-cart freeze with explicit refresh notifications aligns with user expectations.

---

## Consequences

### Positive

✅ **Better availability:** Pricing BC outage doesn't block checkout
✅ **Simpler code:** No synchronous Pricing call in Order saga
✅ **Matches industry standards:** Amazon, Shopify, WooCommerce all freeze at add-to-cart
✅ **Clear user expectations:** Cart displays locked-in price, notifications on refresh
✅ **Closes security gap:** Client-supplied prices rejected immediately at add-to-cart

### Negative

⚠️ **Price drift:** Price can change between add-to-cart and checkout

**Mitigation:**
- Cart price TTL (1 hour default) limits drift window
- `PriceRefreshed` event triggers UI notification
- BFF queries fresh prices before displaying cart (post-TTL)
- User explicitly acknowledges price change before proceeding

⚠️ **Flash sale edge case:** User adds item at $19.99, flash sale ends at $24.99, cart still shows $19.99 for up to 1 hour

**Mitigation:**
- TTL limits exposure window
- Business decision: honor locked-in price (better customer experience) vs force refresh (protects margin)
- Configurable TTL allows business to tune based on margin sensitivity

---

## Alternatives Considered

### Alternative 1: Checkout-Time Freeze

**Pattern:** Synchronous Pricing BC call in `PlaceOrder` command handler

**Pros:**
- ✅ Price is always current at checkout time
- ✅ No TTL complexity
- ✅ No drift edge cases

**Cons:**
- ❌ Temporal coupling: Pricing BC outage blocks checkout (revenue loss)
- ❌ Latency: Extra HTTP round-trip in critical checkout path
- ❌ Complexity: Retry logic, circuit breakers, fallback strategies required
- ❌ New failure mode: "Pricing BC unavailable, please try again" during checkout (bad UX)

**Why rejected:** Availability and simplicity benefits of add-to-cart freeze outweigh "always current" benefit. Cart price refresh + TTL provides sufficient freshness.

---

### Alternative 2: Hybrid (Add-to-Cart + Checkout Verification)

**Pattern:** Freeze at add-to-cart, re-check at checkout, reject if price increased >5%

**Pros:**
- ✅ Protects against large drift (flash sale ends, price spikes)
- ✅ Most carts proceed without re-check (price unchanged)

**Cons:**
- ❌ Still requires synchronous Pricing call at checkout (reintroduces temporal coupling)
- ❌ Complexity: What threshold? 5%? 10%? Per-category? Hardcoded or configurable?
- ❌ User friction: Order rejected at final step (poor UX)

**Why rejected:** Adds complexity without eliminating temporal coupling. TTL + refresh notifications provide sufficient drift protection.

---

### Alternative 3: No TTL, Prices Never Refresh

**Pattern:** Price locked at add-to-cart, never updated, even if cart sits for days

**Pros:**
- ✅ Simplest implementation (no TTL logic, no refresh)
- ✅ Predictable (user always sees original price)

**Cons:**
- ❌ Extreme drift: Cart added Monday at $24.99, checkout Friday after price dropped to $19.99, user pays higher price
- ❌ User frustration: "I see it's $19.99 now but my cart says $24.99?"
- ❌ Margin exposure: Flash sale at $9.99 ends, carts linger for weeks honoring old price

**Why rejected:** Drift exposure too high. TTL provides balance between stability and freshness.

---

## Implementation

### Shopping BC Changes (Issue #214)

**AddItemToCartHandler:**
```csharp
public static async Task<Result> Handle(
    AddItemToCart command,
    IPricingClient pricingClient,
    IDocumentSession session)
{
    // Fetch authoritative price from Pricing BC
    var currentPrice = await pricingClient.GetCurrentPriceAsync(command.Sku);
    if (currentPrice is null)
        return new ValidationError($"SKU {command.Sku} does not have a price set.");

    // Client-supplied UnitPrice is IGNORED - use server price
    var cart = await session.LoadAsync<Cart>(command.CartId);
    var lineItem = new CartLineItem(
        command.Sku,
        command.Quantity,
        currentPrice.BasePrice,  // ← Authoritative price from Pricing BC
        DateTimeOffset.UtcNow);  // PriceFrozenAt for TTL checks

    // ... append ItemAddedToCart event
}
```

**IPricingClient Interface:**
```csharp
public interface IPricingClient
{
    Task<CurrentPriceView?> GetCurrentPriceAsync(string sku, CancellationToken ct = default);
}
```

**Registered in DI:**
```csharp
builder.Services.AddHttpClient<IPricingClient, PricingClient>(client =>
{
    client.BaseAddress = new Uri("http://localhost:5242");  // Pricing.Api
});
```

---

### Cart Price TTL Configuration

**appsettings.json (Shopping.Api):**
```json
{
  "CartPriceTtl": "01:00:00"  // 1 hour default
}
```

**CartLineItem Changes:**
```csharp
public sealed record CartLineItem(
    string Sku,
    int Quantity,
    decimal UnitPrice,
    DateTimeOffset PriceFrozenAt);  // ← New field for TTL tracking
```

---

### BFF Refresh Logic (Customer Experience)

**GetCartView Query:**
```csharp
public static async Task<CartView> Handle(
    GetCartView query,
    IDocumentSession session,
    IPricingClient pricingClient,
    TimeSpan cartPriceTtl)
{
    var cart = await session.LoadAsync<Cart>(query.CartId);

    // Check if any line item prices are stale (> TTL)
    var now = DateTimeOffset.UtcNow;
    var staleItems = cart.LineItems
        .Where(item => now - item.PriceFrozenAt > cartPriceTtl)
        .ToList();

    if (staleItems.Any())
    {
        // Fetch fresh prices from Pricing BC
        var skus = staleItems.Select(i => i.Sku).ToList();
        var freshPrices = await pricingClient.GetBulkPricesAsync(skus);

        // Publish PriceRefreshed if price changed
        foreach (var item in staleItems)
        {
            var freshPrice = freshPrices.FirstOrDefault(p => p.Sku == item.Sku);
            if (freshPrice is not null && freshPrice.BasePrice != item.UnitPrice)
            {
                // Publish PriceRefreshed event → UI notification
                // (Event detail: old price, new price, sku)
            }
        }
    }

    return new CartView(/* ... */);
}
```

---

### PriceRefreshed Event

**Integration Event (Messages.Contracts.Shopping):**
```csharp
public sealed record PriceRefreshed(
    Guid CartId,
    string Sku,
    decimal OldPrice,
    decimal NewPrice,
    DateTimeOffset RefreshedAt);
```

**UI Notification (BFF SignalR):**
```
🔄 Price updated for Dog Food 5lb: was $24.99, now $21.99
```

---

## CONTEXTS.md Update Required

**Current wording (contradicts this ADR):**
> "Orders BC receives CheckoutLineItem[] at checkout time with **price-at-checkout immutability**."

**New wording:**
> "Orders BC receives CheckoutLineItem[] at checkout time with **price-at-add-to-cart immutability** (frozen when item added to cart). Cart price TTL (default: 1 hour) triggers refresh notifications via BFF if price changes."

---

## References

- **Event Modeling:** `docs/planning/pricing-event-modeling.md` — "The Price Snapshot for Orders" section
- **CONTEXTS.md:** Shopping BC and Orders BC integration notes (requires update)
- **Issue #214:** Shopping BC integration - server-authoritative pricing
- **Issue #191:** Messages.Contracts.Pricing namespace (includes PricePublished, PriceUpdated)
- **Skill:** `docs/skills/external-service-integration.md` — HTTP client patterns

---

## Open Question

**Cart price TTL configurability:** Should TTL be:
- (a) Global setting (all products same TTL)
- (b) Per-category setting (food 30min, furniture 24hrs)
- (c) Per-product setting (flash sale items 5min, regular 1hr)

**Phase 1 decision:** Global setting (simplest). Defer per-category/per-product to Phase 2+ if business need arises.

---

**This ADR supersedes the "price-at-checkout" language in CONTEXTS.md and establishes add-to-cart freeze as the canonical Pricing BC integration pattern.**
