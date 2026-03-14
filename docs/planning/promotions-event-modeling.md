# Promotions BC: Event Modeling Workshop

**Date:** 2026-03-15
**Status:** 🟢 Stages 1–6 Complete
**Participants:** Principal Software Architect
**Related:** [pricing-promotions-domain-spike.md](spikes/pricing-promotions-domain-spike.md), [pricing-event-modeling.md](pricing-event-modeling.md), [CONTEXTS.md](../../CONTEXTS.md#promotions)
**Prerequisite:** Pricing BC Phase 1 ✅ Complete (Cycle 21-22)

---

## Purpose

This document captures the Principal Software Architect's raw brain dump for the Promotions BC event modeling workshop. It is Stage 1 of a multi-stage collaborative design process — the goal is to surface every system concern, technical constraint, and integration dependency before the team begins organizing events into a timeline.

**This is NOT a design spec.** It is an unfiltered list of everything the system needs to handle, enforce, and automate from the architecture perspective.

---

## Stage 1: Brain Dump — System Concerns

### 1. Aggregates and Consistency Boundaries

#### Aggregate 1: `Promotion` (Event-Sourced, One Per Promotion)

The Promotion aggregate is the central concept. It owns the lifecycle of a marketing offer — from draft creation through activation, optional pause, and eventual expiration or cancellation.

**Stream key decision:** UUID v7 (time-ordered, new) vs UUID v4 (random). Unlike Pricing's `ProductPrice` which uses UUID v5 from SKU (deterministic lookup), Promotions have no natural key. A promotion is created, not derived from external data. UUID v7 gives time-ordered streams which is useful for "show me the most recent promotions" queries. **Recommendation: UUID v7 for Promotion stream IDs.**

**State shape (proposed):**
- PromotionId (Guid)
- Name (string)
- Description (string)
- Status (enum: Draft, Scheduled, Active, Paused, Expired, Cancelled)
- DiscountType (enum: PercentageOff, FixedAmountOff, FreeShipping)
- DiscountValue (decimal — percentage 0-100 or fixed Money amount)
- DiscountApplication (enum: PerOrder, PerEligibleItem) — controls FixedAmountOff semantics
- Scope (enum: AllItems, ByCategory, BySpecificSkus)
- IncludedSkus (IReadOnlyList<string>)
- ExcludedSkus (IReadOnlyList<string>)
- IncludedCategories (IReadOnlyList<string>)
- AllowsStacking (bool)
- RequiresCouponCode (bool) — false means auto-applied when eligibility met
- RedemptionCap (int?) — null means unlimited
- CurrentRedemptionCount (int)
- MinimumOrderAmount (Money?) — "spend $50 to get 10% off"
- StartsAt (DateTimeOffset)
- EndsAt (DateTimeOffset)
- CreatedBy (Guid)
- CreatedAt (DateTimeOffset)

**Critical modeling question: Where does `DiscountValue` live?** If it's a percentage, `decimal` suffices. If it's a fixed amount ("$5 off"), we need `Money` value object. The Pricing BC already has `Money` — do we share it? Create a local copy? Reference the Pricing assembly? **Brain dump answer: Copy the Money VO into Promotions BC.** Don't create a cross-BC assembly dependency. Each BC should be independently deployable. The Money VO is 140+ lines with tests — worth duplicating for isolation.

**Lifecycle:**
```
Draft → Scheduled → Active → Expired (natural end)
Draft → Scheduled → Active → Paused → Active → Expired
Draft → Scheduled → Active → Cancelled (admin action)
Draft → Active (immediate activation, StartsAt ≤ now)
Draft → Cancelled (never activated)
```

**Open question:** Should Paused→Active require re-scheduling? Or does it just resume from wherever it was? The spike says "cannot be modified once Active (only Pause or Cancel)." But if paused, can the end date be extended? **This matters for the event model.**

#### Aggregate 2: `Coupon` (Event-Sourced, One Per Coupon Code)

A Coupon is a redeemable code tied to a Promotion. Separate aggregate from Promotion because:
1. Coupons have independent lifecycle (issued, redeemed, expired, revoked)
2. Coupon redemption count tracking is per-coupon, not per-promotion
3. High-traffic coupon validation needs its own concurrency boundary
4. Batch generation creates thousands of coupons — can't be part of Promotion aggregate stream

**Stream key decision:** The spike proposes `coupon-{couponCode}` as stream ID. This means the coupon code IS the natural key. UUID v5 from coupon code string (like Pricing uses for SKU)? Or just use the code as a string stream identity? **Marten supports `StreamIdentity.AsString` — but CritterSupply has standardized on `StreamIdentity.AsGuid`.** Could use UUID v5 from `promotions:{couponCode.ToUpperInvariant()}` to stay consistent with the Pricing BC pattern.

**Case sensitivity concern:** Coupon codes entered by customers. "SUMMER25" vs "summer25" vs "Summer25". Must normalize. UUID v5 with `.ToUpperInvariant()` handles this naturally if we use the Pricing BC pattern.

**State shape (proposed):**
- CouponCode (string) — normalized (uppercase, trimmed)
- CouponId (Guid) — UUID v5 from code, or stream-assigned
- PromotionId (Guid) — parent promotion
- BatchId (Guid?) — if generated in a batch
- Status (enum: Active, Redeemed, Expired, Revoked)
- MaxUses (int) — 1 for single-use, N for multi-use
- CurrentUseCount (int)
- CustomerRedemptions (IReadOnlyList<CouponRedemption>) — tracks who redeemed
- IssuedAt (DateTimeOffset)

**Embedded value:**
```csharp
public sealed record CouponRedemption(
    Guid CustomerId,
    Guid OrderId,
    DateTimeOffset RedeemedAt);
```

**Critical question: Multi-use coupons.** A coupon with `MaxUses = 100` means 100 different customers can use it. A coupon with `MaxUses = 1` is single-use. But what about "once per customer, unlimited total"? That's a different constraint — `MaxUsesPerCustomer` vs `MaxUsesTotal`. The spike models `MaxUses` as total. **I think we need both dimensions:**
- `MaxUsesTotal` (int?) — null = unlimited total
- `MaxUsesPerCustomer` (int) — default 1 (each customer can use it once)

This is a common real-world pattern: "WELCOME10 — 10% off your first order" has MaxUsesTotal=null, MaxUsesPerCustomer=1.

#### Aggregate 3: `CouponBatch` (Event-Sourced? Or Document? Or Part of Promotion?)

The spike mentions `GenerateCouponBatch` as a command on the Promotion aggregate. But generating 1,000 coupons as 1,000 events on the Promotion stream would bloat that stream significantly. **Options:**

1. **CouponBatch as a separate aggregate** — owns the batch generation lifecycle. Each coupon then gets its own stream. Batch tracks generation progress.
2. **CouponBatch as a Marten document** — simple CRUD, not event-sourced. Just tracks "batch X generated 1000 coupons for promotion Y."
3. **Inline on Promotion aggregate** — single `CouponBatchGenerated` event, then individual `CouponIssued` events on separate Coupon streams.

**Recommendation: Option 3 (from the spike) — `CouponBatchGenerated` on Promotion stream, individual `CouponIssued` events on per-Coupon streams.** The batch is a command that fans out. The Promotion aggregate just records "a batch was created" as one event. The individual coupons are their own aggregates.

**But wait — batch generation of 1,000+ coupons means 1,000+ aggregate creations in one handler.** This is a Wolverine throughput concern. Should it be a saga? A background job? Wolverine's `OutgoingMessages` pattern (return 1,000 messages that each create a coupon)? **This is a design discussion item.**

#### Non-Aggregate: Discount Calculation (Stateless Service / Pure Function)

Discount calculation is NOT an aggregate. It's a pure function:
```
CalculateDiscount(cart items, active promotions, coupon codes) → discount breakdown
```

This is called at:
1. Cart display time (Shopping BC asks Promotions BC "what discounts apply?")
2. Checkout time (Orders BC asks "final discount for this order")

**No state to store. No events to emit. Just computation.** This is a query endpoint on Promotions.Api.

---

### 2. Events Per Aggregate

#### Promotion Domain Events

```
PromotionCreated           — Draft created with all configuration
PromotionScopeRevised      — SKUs/categories changed (Draft only)
PromotionScheduled         — StartsAt set, waiting for activation time
PromotionActivated         — Goes live (manual or scheduled trigger)
PromotionPaused            — Temporarily suspended (admin action)
PromotionResumed           — Re-activated after pause
PromotionCancelled         — Permanently ended early (admin action)
PromotionExpired           — Natural end (date reached OR redemption cap hit)
PromotionRedemptionRecorded — An order used this promotion (tracks count)
PromotionEndDateExtended   — Admin pushed back the end date (while Active?)
PromotionDiscountModified  — Changed discount value (Draft only? Or Active too?)
```

**Open question: Can an Active promotion's discount value be changed?** The spike says no — "cannot be modified once Active." But business reality says "we set the wrong percentage, need to fix it NOW without cancelling and recreating." **Propose: `PromotionCorrected` event (like `PriceCorrected` in Pricing BC) — audit trail of the fix without pretending it didn't happen.** This follows the same pattern as Pricing BC's correction model.

#### Coupon Domain Events

```
CouponIssued               — Code created and ready for use
CouponReserved             — Customer applied to cart (soft hold)
CouponReservationReleased  — Customer removed from cart, or cart expired
CouponRedeemed             — Order placed with this coupon (hard commit)
CouponRevoked              — Admin killed it (fraud, error)
CouponExpired              — Parent promotion expired → all unredeemed coupons expire
```

**Important new event not in the spike: `CouponReserved`.** When a customer applies a coupon to a cart, there needs to be a soft reservation to prevent the same single-use coupon from being validated for two carts simultaneously. This is the double-soft-reservation race condition the spike identifies. A reservation with a TTL (e.g., 15 minutes, matching cart inactivity timeout) prevents the worst case.

**Counter-argument: Is CouponReserved over-engineering?** The spike says "let both validate succeed, second-to-commit gets regular price." That's a valid business policy that avoids the complexity of reservation management. **This is a product decision, not an architecture decision.** Surface it to the PO.

#### CouponBatch Events (if on Promotion stream)

```
CouponBatchGenerated       — Metadata about the batch (count, max uses per coupon)
```

Just one event on the Promotion stream. The individual coupons are their own streams.

---

### 3. Invariants (Per Aggregate)

#### Promotion Invariants

1. **Status transitions must follow valid paths** — Cannot go from Expired → Active. Cannot go from Cancelled → anything.
2. **StartsAt < EndsAt** — A promotion that ends before it starts is invalid.
3. **DiscountValue bounds:**
   - PercentageOff: 0 < value ≤ 100 (100% = free)
   - FixedAmountOff: value > 0 (and must be Money with currency)
   - FreeShipping: value irrelevant (discount is shipping cost)
4. **RedemptionCap enforcement** — CurrentRedemptionCount cannot exceed RedemptionCap. When equal → auto-expire.
5. **Cannot modify Active promotion** — Only Pause, Cancel, or Correct allowed once Active.
6. **Cannot modify Expired/Cancelled promotion** — Terminal states.
7. **Scope must be non-empty** — At least one SKU, category, or AllItems scope.
8. **Draft promotions cannot have redemptions** — No orders can use a Draft promotion.
9. **Coupon generation requires Draft or Scheduled status** — Cannot generate coupons for an expired promotion.
10. **RequiresCouponCode + no coupons generated = unusable promotion** — Validate at activation time: if RequiresCouponCode=true, at least one coupon must exist.

#### Coupon Invariants

1. **Cannot redeem beyond MaxUses** — UseCount < MaxUses to accept redemption.
2. **Cannot redeem after parent Promotion expired/cancelled** — Must check promotion status.
3. **Revoked/Expired coupons are terminal** — No further state changes.
4. **Code uniqueness** — Two coupons cannot share the same code (enforced by stream ID).
5. **Customer-level redemption limit** — If MaxUsesPerCustomer is enforced, check CustomerRedemptions list.

#### Cross-Aggregate Invariant (NOT enforceable atomically)

**"Discount cannot reduce price below MAP/floor"** — This requires querying Pricing BC's `CurrentPriceView` for the floor price, then ensuring `effectivePrice - discount ≥ floorPrice`. This CANNOT be enforced within Promotions BC's aggregate boundary because floor price data lives in Pricing BC.

**Options for enforcement:**
1. **At discount calculation time** (synchronous HTTP to Pricing BC) — clamp discount to never go below floor
2. **At promotion creation time** — reject promotions that *could* violate floor (but floor prices change independently!)
3. **Eventual consistency** — Promotions BC subscribes to `PriceUpdated` and maintains a local projection of floor prices. Uses this for validation. Stale by definition.

**Recommendation: Option 1 — enforce at calculation time.** The discount calculation endpoint should query Pricing BC for the floor price and clamp. This means the caller (Shopping or Orders) gets a discount that's already floor-safe. **This is a runtime constraint, not a design-time constraint.**

---

### 4. Concurrency Concerns

#### 4a. Redemption Cap Race Condition (CRITICAL)

**Scenario:** Promotion has `RedemptionCap = 100`, `CurrentRedemptionCount = 99`. Two orders placed simultaneously. Both handlers read count as 99, both increment to 100, cap is exceeded.

**The spike explicitly warns about this:**
> "The OrderWithPromotionPlaced handler that appends PromotionRedemptionRecorded MUST be configured with single-concurrency per PromotionId"

**Wolverine solution — Sequential local queue:**
```csharp
opts.LocalQueue("promotion-redemption").Sequential();
```
Route all `OrderWithPromotionPlaced` messages through this queue. Wolverine processes one at a time. No race condition.

**But wait** — sequential per queue means ALL promotion redemptions are serialized. If promotion A and promotion B both get redemptions simultaneously, promotion B waits for promotion A. This is fine if throughput is low (typical for e-commerce). But for a flash sale with thousands of orders per second, this becomes a bottleneck.

**Better solution — Sequential per PromotionId:**
```csharp
// Wolverine supports message-specific sequential processing
opts.Policies.ConfigureConventionalLocalRouting(x =>
{
    x.CustomizeQueues((type, queue) =>
    {
        if (type == typeof(RecordPromotionRedemption))
            queue.Sequential();
    });
});
```

Actually, the real Wolverine pattern for per-entity sequential processing is **the saga pattern** or using the aggregate's own Marten optimistic concurrency. Since `Promotion` is event-sourced with Marten, appending `PromotionRedemptionRecorded` to the Promotion stream will use Marten's optimistic concurrency (expected version check). If two handlers try to append simultaneously, one gets a `ConcurrencyException` and Wolverine's retry policy kicks in:

```csharp
opts.OnException<ConcurrencyException>()
    .RetryOnce()
    .Then.RetryWithCooldown(100.Milliseconds(), 250.Milliseconds())
    .Then.Discard();
```

**This is already the pattern used across the codebase** (Pricing, Orders, Fulfillment all have this). The optimistic concurrency + retry is the first line of defense. Sequential queue is the belt-and-suspenders option for truly high-traffic scenarios.

**Recommendation: Start with Marten optimistic concurrency + retry. Add sequential queue if load testing reveals issues.**

#### 4b. Coupon Double-Validation Race Condition

**Scenario:** Two customers simultaneously validate the same single-use coupon code. Both HTTP validation calls succeed (coupon is still Active). Both apply to their carts. One order commits, the other gets regular price.

**The spike's business policy is correct:**
1. Validation at cart-apply time is optimistic (HTTP call, best-effort)
2. Commitment at order placement time is authoritative (Coupon aggregate, pessimistic)
3. If coupon already redeemed when second order commits → order proceeds at regular price
4. Customer notification: "Your coupon was claimed by another shopper"

**Implementation:** The Coupon aggregate's `Apply(CouponRedeemed)` method checks `CurrentUseCount < MaxUses`. If violated, the handler returns an integration message to Orders BC indicating coupon was not applied. **Marten's optimistic concurrency handles the atomicity.**

#### 4c. Promotion Activation + Expiration Race

**Scenario:** Scheduled activation fires at exactly the same moment as scheduled expiration (StartsAt == EndsAt, or very close). Both Wolverine scheduled messages arrive simultaneously.

**Mitigation:** The aggregate's status-checking in `Apply()` methods prevents invalid transitions. If `PromotionActivated` arrives and status is already `Expired`, it's a no-op. Order of event application is deterministic within Marten's stream.

#### 4d. Concurrent Batch Generation

**Scenario:** Admin clicks "Generate 1000 coupons" twice. Two `GenerateCouponBatch` commands arrive.

**Mitigation:** The Promotion aggregate tracks batch IDs. The `Before()` handler can check if a batch is already in progress. Or use idempotency keys on the command.

---

### 5. Integration Points with Other BCs

#### 5a. Shopping BC → Promotions BC (Coupon Validation)

**Direction:** Shopping calls Promotions
**Pattern:** Synchronous HTTP (customer waiting for immediate feedback)
**Endpoint:** `POST /api/promotions/coupons/validate`
**Request:**
```csharp
public sealed record ValidateCoupon(
    string CouponCode,
    Guid CartId,
    Guid? CustomerId,
    IReadOnlyList<CartItem> CartItems);
```
**Response:**
```csharp
public sealed record CouponValidationResult(
    string CouponCode,
    bool IsValid,
    string? InvalidReason,
    Guid? PromotionId,
    string? PromotionName,
    DiscountType? DiscountType,
    decimal? DiscountValue,
    IReadOnlyList<string>? EligibleSkus);
```

**Why sync?** Customer types coupon code, clicks "Apply", expects instant feedback. Async message round-trip introduces UX lag that's unacceptable.

**Fallback:** If Promotions BC is down, coupon validation fails gracefully. Shopping BC shows "Unable to validate coupon, please try again." Cart is not affected. No degraded-price risk.

#### 5b. Shopping BC → Promotions BC (CouponApplied/CouponRemoved events)

**Direction:** Shopping publishes → Promotions subscribes
**Pattern:** Async via RabbitMQ
**Messages (already reserved in CONTEXTS.md):**
- `CouponApplied` — Shopping domain event becomes integration message
- `CouponRemoved` — Shopping domain event becomes integration message

**Purpose:** Promotions BC tracks which coupons are "in carts" for analytics/reservation. NOT for validation (that's sync HTTP).

**Open question:** Does Promotions BC actually need to know about coupons in carts? The spike mentions it for "soft reservation" but the business policy says "let both validate." **If we don't do soft reservations, Promotions BC doesn't need CouponApplied/CouponRemoved at all.** It only cares about `OrderWithPromotionPlaced` (final commitment).

**Recommendation: Defer CouponApplied/CouponRemoved subscription to Phase 2.** Phase 1: Promotions BC only cares about coupon validation (sync) and redemption recording (async from Orders). Simpler. Less integration surface.

#### 5c. Promotions BC → Shopping BC (PromotionActivated/PromotionExpired)

**Direction:** Promotions publishes → Shopping subscribes
**Pattern:** Async via RabbitMQ
**Messages (per CONTEXTS.md):**
- `PromotionActivated` → Shopping BC updates active promotions cache, displays badges
- `PromotionExpired` → Shopping BC removes badges, recalculates auto-applied discounts

**Customer Experience impact:** When a promotion activates, the storefront should show "Sale!" badges on eligible products and auto-apply discounts to carts with eligible items (if RequiresCouponCode=false). When it expires, the opposite.

**Shopping BC handler for PromotionActivated:**
1. Update a local `ActivePromotionsView` document (Marten document store)
2. For auto-apply promotions (RequiresCouponCode=false): Find all active carts with eligible items → append `PromotionApplied` domain event to each cart stream
3. Push SignalR notification to Customer Experience BFF → update UI

**Concern:** Finding "all active carts with eligible items" is potentially expensive. How many active carts exist? Thousands? **This might need to be a background process, not inline.** Wolverine cascading messages pattern: PromotionActivated → query carts → emit N individual `ApplyPromotionToCart` commands.

#### 5d. Shopping BC / Orders BC → Promotions BC (Discount Calculation)

**Direction:** Shopping/Orders calls Promotions
**Pattern:** Synchronous HTTP
**Endpoint:** `GET /api/promotions/calculate-discount`
**Request:**
```csharp
public sealed record CalculateDiscountRequest(
    Guid? CustomerId,
    IReadOnlyList<CartLineItem> Items,  // SKU, Quantity, UnitPrice
    IReadOnlyList<string>? CouponCodes,
    decimal ShippingCost);
```
**Response:**
```csharp
public sealed record DiscountBreakdown(
    decimal TotalDiscount,
    decimal DiscountedShippingCost,
    IReadOnlyList<LineItemDiscount> LineItemDiscounts,
    IReadOnlyList<AppliedPromotionSummary> AppliedPromotions);
```

**When is this called?**
1. **Cart display** — Shopping BC calls to show line-item discounts in the UI
2. **Checkout completion** — Orders BC calls to determine final effective prices

**Floor price enforcement** happens here. The calculate-discount endpoint queries Pricing BC's `CurrentPriceView` for floor prices and clamps discounts.

#### 5e. Orders BC → Promotions BC (Redemption Recording)

**Direction:** Orders publishes → Promotions subscribes
**Pattern:** Async via RabbitMQ
**Message:**
```csharp
public sealed record OrderWithPromotionPlaced(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<AppliedPromotion> AppliedPromotions,
    DateTimeOffset PlacedAt);

public sealed record AppliedPromotion(
    Guid PromotionId,
    string? CouponCode,
    decimal DiscountApplied);
```

**This is the authoritative commitment.** Promotions BC:
1. Appends `PromotionRedemptionRecorded` to Promotion aggregate (increments count)
2. If coupon used: Appends `CouponRedeemed` to Coupon aggregate
3. If RedemptionCap hit: Appends `PromotionExpired` to Promotion, publishes `PromotionExpired` integration message

**Sequential processing required:** This handler must be routed through a queue with concurrency control to prevent redemption cap races.

#### 5f. Promotions BC → Pricing BC (Floor Price Query)

**Direction:** Promotions calls Pricing
**Pattern:** Synchronous HTTP
**Endpoint:** `GET /api/pricing/{sku}` (already exists — returns CurrentPriceView)
**Purpose:** Floor price lookup during discount calculation
**Data needed:** `FloorPrice` from `CurrentPriceView`

**Caching consideration:** Floor prices change rarely. Could cache locally with a TTL (e.g., 5 minutes). Subscribe to `PriceUpdated` to invalidate cache. **Phase 1: direct HTTP call. Phase 2: local cache with event-driven invalidation.**

#### 5g. Promotions BC → Customer Experience BFF (Real-Time Updates)

**Direction:** Promotions publishes → CE subscribes
**Pattern:** Async via RabbitMQ → SignalR push to browser
**Messages:**
- `PromotionActivated` → CE shows "New deal!" toast / banner
- `PromotionExpired` → CE removes sale badges
- `CouponValidated`/`CouponRejected` → CE shows validation result in real-time

**This follows the existing CE pattern** — CE has `Storefront/Notifications/` handlers that receive RabbitMQ messages and push to SignalR. Same pattern as `OrderPlaced`, `ShipmentDispatched`, etc.

#### 5h. Admin Portal → Promotions BC (Command Interface)

**Direction:** Admin Portal sends commands → Promotions BC
**Pattern:** HTTP (Admin Portal is a BFF that routes commands)
**Commands:**
- CreatePromotion
- ActivatePromotion / SchedulePromotion
- PausePromotion / ResumePromotion
- CancelPromotion
- GenerateCouponBatch
- RevokeCoupon
- ExtendPromotionEndDate (new, not in spike)

**RBAC concern:** Admin Portal needs role-based access control. Who can create promotions? Who can activate them? The cycle plan mentions "RBAC ADR for Admin Portal to be authored during Cycle 29." **This ADR should define promotion-specific roles:**
- `PromotionManager` — full CRUD on promotions
- `PromotionViewer` — read-only access to promotion analytics
- `CouponManager` — generate batches, revoke coupons

**Phase 1 simplification:** No RBAC. Any authenticated admin can do anything. RBAC deferred to Admin Portal cycle.

#### 5i. Promotions BC ← Pricing BC (Price Change Notifications)

**Direction:** Pricing publishes → Promotions subscribes
**Pattern:** Async via RabbitMQ
**Messages:**
- `PriceUpdated` — A SKU's price changed

**Why Promotions cares:** If a FixedAmountOff promotion says "$5 off" but the SKU's price just dropped to $3, the discount calculation must clamp. **But this is enforced at calculation time, not at event time.** So does Promotions BC even need to subscribe to `PriceUpdated`?

**Use case 1: Local price cache invalidation** — If we cache floor prices for the discount calculation endpoint, `PriceUpdated` invalidates the cache.

**Use case 2: Promotion conflict detection** — "Alert: SKU DOG-FOOD-5LB price dropped to $4.99, but promotion SUMMER25 offers $5 off. This promotion would result in $0 effective price." This is a projection/read model concern, not an aggregate concern.

**Recommendation: Subscribe to `PriceUpdated` for cache invalidation (Phase 2) and conflict alerting (Phase 3). Not needed for Phase 1 if we always do live HTTP lookups.**

#### 5j. Orders BC Checkout Enhancement (Phase 3)

**Critical integration:** Currently, `CheckoutCompleted` captures `UnitPrice` as frozen-at-cart-add price (ADR 0017). Promotions BC adds a discount overlay. The checkout completion flow needs to:

1. Query Promotions BC: "What discounts apply to this cart?"
2. Apply discounts to line items (clamped by floor price)
3. Capture `EffectivePrice` (post-discount) per line item in `CheckoutCompleted`
4. If coupons used, include them in `OrderWithPromotionPlaced`

**ADR needed:** How does the `CheckoutCompleted` event change? Does it add `DiscountAmount` per line item? Or does it track `UnitPrice` (pre-discount) + `EffectivePrice` (post-discount) + `DiscountBreakdown`? **This affects the Orders BC contract — requires careful backward compatibility.**

**Proposed `CheckoutCompleted` evolution:**
```csharp
public sealed record CheckoutCompleted(
    Guid OrderId,
    Guid CheckoutId,
    Guid? CustomerId,
    IReadOnlyList<CheckoutLineItem> Items,  // existing
    IReadOnlyList<AppliedDiscount>? Discounts,  // NEW — nullable for backward compat
    decimal? TotalDiscount,  // NEW
    AddressSnapshot ShippingAddress,
    string ShippingMethod,
    decimal ShippingCost,
    string PaymentMethodToken,
    DateTimeOffset CompletedAt);
```

---

### 6. Scheduled / Automated Processes

#### 6a. Promotion Activation Scheduling

**When:** Promotion's `StartsAt` is in the future at creation time
**Mechanism:** Wolverine `ScheduleAsync` (same pattern as Returns BC `ExpireReturn`)
**Implementation:**
```csharp
// In CreatePromotionHandler or ActivatePromotionHandler:
if (promotion.StartsAt > DateTimeOffset.UtcNow)
{
    await bus.ScheduleAsync(
        new ActivatePromotion(promotion.PromotionId),
        promotion.StartsAt);
}
```
**Stale message guard:** When the scheduled message fires, the handler MUST check current promotion status. If promotion was cancelled or already activated, the scheduled message is a no-op. **This is the same pattern as `ScheduledPriceActivated` in Pricing BC (`ScheduleId` match check).**

**Implementation detail:** Store a `ScheduleId` (Guid) in the `PromotionScheduled` event. The scheduled message carries this `ScheduleId`. Handler checks `promotion.PendingScheduleId == message.ScheduleId` to discard stale messages. This prevents issues when a promotion is cancelled and re-created with different timing.

#### 6b. Promotion Expiration Scheduling

**When:** Promotion's `EndsAt` arrives
**Mechanism:** Wolverine `ScheduleAsync`
**Implementation:**
```csharp
// In ActivatePromotionHandler (when promotion goes Active):
await bus.ScheduleAsync(
    new ExpirePromotion(promotion.PromotionId, scheduleId),
    promotion.EndsAt);
```
**Same stale-message guard applies.** If promotion was already cancelled or paused, the expiry is a no-op.

#### 6c. Coupon Cascade Expiry

**When:** Parent promotion expires
**Mechanism:** Downstream handler, NOT inline
**Implementation:** `PromotionExpired` domain event triggers a cascading handler that queries all unredeemed coupons for that promotion and expires them.

**Concern:** If the promotion had 10,000 coupons, expiring them all inline in the `ExpirePromotionHandler` would create a massive transaction. **Fan out via Wolverine messages:**
```csharp
// In response to PromotionExpired event:
var unredeemedCoupons = await session.Query<CouponView>()
    .Where(c => c.PromotionId == evt.PromotionId && c.Status == CouponStatus.Active)
    .ToListAsync();

foreach (var coupon in unredeemedCoupons)
{
    outgoing.Add(new ExpireCoupon(coupon.CouponCode));
}
```
Each `ExpireCoupon` is an individual command that updates one Coupon aggregate. Wolverine processes them in parallel (or sequentially if configured).

#### 6d. Cart Promotion Recalculation (Triggered by PromotionActivated/Expired)

**When:** A promotion activates or expires, affecting items in active carts
**Mechanism:** Integration handler in Shopping BC
**Concern:** Finding affected carts requires a query against all active carts. This is a projection query, not an aggregate operation. The Shopping BC needs a projection of "carts with item X" to know which carts to update.

**Phase 1 simplification:** Don't auto-update carts. Customer sees updated prices on next page load (Customer Experience BFF re-queries). This avoids the complexity of cart-level push updates for Phase 1.

#### 6e. Promotion Analytics Snapshot (Future)

**When:** Periodic (hourly? daily?)
**Purpose:** Capture promotion performance snapshots for reporting
**Mechanism:** Wolverine scheduled recurring message or async daemon projection
**Deferred to:** Phase 3+ (Analytics BC prerequisite)

---

### 7. Projections / Read Models

#### 7a. `ActivePromotionsView` (Inline Projection)

**Purpose:** Fast lookup of currently active promotions for discount calculation
**Key:** PromotionId (Guid)
**Lifecycle:** Created on PromotionActivated, deleted on PromotionExpired/Cancelled
**Fields:**
- PromotionId, Name, DiscountType, DiscountValue, Scope, IncludedSkus, ExcludedSkus, IncludedCategories, AllowsStacking, RequiresCouponCode, StartsAt, EndsAt

**Why inline?** Zero-lag reads. The discount calculation endpoint needs real-time accuracy — if a promotion was just cancelled, the very next calculation call must reflect it.

**Query pattern:** "Give me all active promotions that apply to SKU X" → filter by scope, check included/excluded SKUs, check categories.

#### 7b. `CouponLookupView` (Inline Projection)

**Purpose:** Fast coupon validation (hot path during checkout)
**Key:** CouponCode (string, normalized uppercase)
**Lifecycle:** Created on CouponIssued, updated on CouponRedeemed/Revoked/Expired
**Fields:**
- CouponCode, PromotionId, Status, MaxUses, CurrentUseCount, IssuedAt

**Why inline?** Coupon validation is in the customer's critical path. Must be zero-lag.

**UUID v5 stream ID consideration:** If coupon streams use UUID v5 from the code, but the projection key is the code string, we need a `MultiStreamProjection<CouponLookupView, string>` (same pattern as Pricing BC's `CurrentPriceView`). Map Guid streams to string-keyed documents.

#### 7c. `PromotionSummaryView` (Async Projection)

**Purpose:** Marketing dashboard — campaign performance metrics
**Key:** PromotionId
**Fields:**
- PromotionId, Name, Status, DiscountType, DiscountValue
- TotalRedemptions, TotalDiscountAmount, UniqueCustomers
- CouponsIssued, CouponsRedeemed, CouponsExpired
- StartsAt, EndsAt, CreatedAt

**Why async?** Dashboard data doesn't need zero-lag. Seconds-old data is fine.

#### 7d. `CustomerRedemptionHistoryView` (Async Projection)

**Purpose:** "Has this customer already used this promotion/coupon?" — needed for per-customer limit enforcement
**Key:** Composite (CustomerId, PromotionId) or CustomerId with embedded list
**Fields:**
- CustomerId, PromotionId, CouponCode, OrderId, DiscountApplied, RedeemedAt

**Critical for validation:** When checking "can customer X use coupon Y?" the handler needs to know if customer X has already redeemed promotion Z. This projection provides that lookup.

**Concern:** Should this be inline (zero-lag, needed for validation accuracy) or async (eventual consistency risk)? **If a customer places two orders in quick succession with the same coupon, the async projection might not have caught up.** The Coupon aggregate's `CustomerRedemptions` list is the authoritative source, not this projection.

**Decision: Use the Coupon aggregate state for enforcement. Use this projection for analytics only. Make it async.**

#### 7e. `PromotionCalendarView` (Async Projection)

**Purpose:** Admin Portal — visual calendar of scheduled/active/expired promotions
**Key:** Date range index
**Fields:**
- PromotionId, Name, Status, StartsAt, EndsAt, DiscountType, DiscountValue

**Why async?** Admin dashboard, not customer-facing.

#### 7f. `SkuPromotionEligibilityView` (Inline Projection)

**Purpose:** "What promotions currently apply to SKU X?" — needed for storefront badge display and discount calculation
**Key:** SKU (string)
**Fields:**
- Sku, IReadOnlyList<ActivePromotionSummary> (PromotionId, Name, DiscountType, DiscountValue)

**Multi-event source:** Built from PromotionActivated (add to eligible SKUs), PromotionExpired/Cancelled (remove), PromotionScopeRevised (update eligibility).

**Concern:** For `Scope.AllItems` promotions, EVERY SKU is eligible. Do we create a document per SKU? Or a special "all items" flag? **This needs design attention.**

**Alternative approach:** Don't build a per-SKU projection. Instead, query `ActivePromotionsView` and filter by scope at query time. For Phase 1 with a small number of active promotions, this is fine. Denormalize to per-SKU projection when performance requires it.

---

### 8. Cross-BC Queries (Sync HTTP vs Async Messages)

| Query | Direction | Pattern | Rationale |
|-------|-----------|---------|-----------|
| Validate coupon code | Shopping → Promotions | **Sync HTTP** | Customer waiting for instant feedback |
| Calculate discount breakdown | Shopping/Orders → Promotions | **Sync HTTP** | Needed for cart display and checkout finalization |
| Get floor price for SKU | Promotions → Pricing | **Sync HTTP** | Part of discount calculation (clamp to floor) |
| Get active promotions for storefront | CE BFF → Promotions | **Sync HTTP** | Page load query for badge display |
| Record redemption | Orders → Promotions | **Async RabbitMQ** | Order is already placed, no customer waiting |
| Promotion activated/expired | Promotions → Shopping | **Async RabbitMQ** | Background update, eventual consistency OK |
| Promotion activated/expired | Promotions → CE BFF | **Async RabbitMQ** | SignalR push to browser, not blocking |
| Price updated (cache invalidation) | Pricing → Promotions | **Async RabbitMQ** | Background invalidation |
| Coupon applied to cart | Shopping → Promotions | **Async RabbitMQ** (if needed) | Analytics only, not blocking |

**Key principle:** Sync HTTP for customer-facing latency-sensitive operations. Async RabbitMQ for everything else.

---

### 9. Wolverine-Specific Patterns

#### 9a. Compound Handler Pattern (Before/Validate/Load/Handle)

**Applies to:** Every command handler in Promotions BC

**Example: CreatePromotionHandler:**
```csharp
public static class CreatePromotionHandler
{
    // FluentValidation runs first (via opts.UseFluentValidation())

    // Before: Additional business rule checks
    public static ProblemDetails Before(CreatePromotion command)
    {
        if (command.StartsAt >= command.EndsAt)
            return new ProblemDetails { Detail = "Promotion must end after it starts", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    // Handle: Create aggregate, return events + outgoing messages
    [WolverinePost("/api/promotions")]
    public static (IStartStream, OutgoingMessages) Handle(CreatePromotion command)
    {
        var promotionId = Guid.CreateVersion7();
        var created = new PromotionCreated(/* ... */);
        var outgoing = new OutgoingMessages();

        // If StartsAt is future, schedule activation
        if (command.StartsAt > DateTimeOffset.UtcNow)
        {
            outgoing.ScheduleLocally(
                new ActivatePromotion(promotionId, Guid.NewGuid()),
                command.StartsAt);
        }

        return (MartenOps.StartStream<Promotion>(promotionId, created), outgoing);
    }
}
```

#### 9b. WriteAggregate for Existing Aggregates

**Applies to:** ActivatePromotion, PausePromotion, CancelPromotion, RecordRedemption

```csharp
public static class ActivatePromotionHandler
{
    public static ProblemDetails Before(
        ActivatePromotion command,
        Promotion? promotion)
    {
        if (promotion is null)
            return new ProblemDetails { Detail = "Promotion not found", Status = 404 };
        if (promotion.Status != PromotionStatus.Draft && promotion.Status != PromotionStatus.Scheduled)
            return new ProblemDetails { Detail = $"Cannot activate from {promotion.Status}", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/promotions/{promotionId}/activate")]
    public static (Events, OutgoingMessages) Handle(
        ActivatePromotion command,
        [WriteAggregate] Promotion promotion)
    {
        var activated = new PromotionActivated(promotion.PromotionId, command.ActivatedBy, DateTimeOffset.UtcNow);
        var events = new Events(activated);

        var outgoing = new OutgoingMessages();
        // Publish integration message
        outgoing.Add(new Messages.Contracts.Promotions.PromotionActivated(
            promotion.PromotionId,
            promotion.Name,
            promotion.DiscountType.ToString(),
            promotion.DiscountValue,
            promotion.StartsAt,
            promotion.EndsAt));

        // Schedule expiration
        var scheduleId = Guid.NewGuid();
        outgoing.ScheduleLocally(
            new ExpirePromotion(promotion.PromotionId, scheduleId),
            promotion.EndsAt);

        return (events, outgoing);
    }
}
```

#### 9c. Scheduled Messages (Activation + Expiration)

**Pattern:** Same as Returns BC `ExpireReturn` and Pricing BC `ScheduledPriceActivated`

Two scheduled messages per promotion lifecycle:
1. `ActivatePromotion` — fires at `StartsAt`
2. `ExpirePromotion` — fires at `EndsAt`

Both use the stale-message guard pattern (ScheduleId matching).

#### 9d. ConcurrencyException Retry Policy

**Standard CritterSupply pattern (already in every BC):**
```csharp
opts.OnException<ConcurrencyException>()
    .RetryOnce()
    .Then.RetryWithCooldown(100.Milliseconds(), 250.Milliseconds())
    .Then.Discard();
```

**Applies to:** Redemption recording (race condition mitigation), coupon redemption, any aggregate mutation.

#### 9e. Durable Outbox for Integration Messages

**Standard pattern:**
```csharp
opts.Policies.AutoApplyTransactions();
opts.Policies.UseDurableLocalQueues();
opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
```

Ensures `PromotionActivated`, `PromotionExpired`, `CouponValidated`, `DiscountCalculated` messages survive process restarts.

#### 9f. RabbitMQ Queue Configuration

**Proposed queues:**
```csharp
// Promotions BC listens
opts.ListenToRabbitQueue("promotions-order-events").ProcessInline();  // OrderWithPromotionPlaced
opts.ListenToRabbitQueue("promotions-pricing-events").ProcessInline(); // PriceUpdated (Phase 2)

// Promotions BC publishes
opts.PublishMessage<Messages.Contracts.Promotions.PromotionActivated>()
    .ToRabbitQueue("promotion-events");
opts.PublishMessage<Messages.Contracts.Promotions.PromotionExpired>()
    .ToRabbitQueue("promotion-events");
```

**Shopping BC listens:**
```csharp
opts.ListenToRabbitQueue("shopping-promotion-events").ProcessInline();
```

**CE BFF listens:**
```csharp
opts.ListenToRabbitQueue("storefront-promotion-events").ProcessInline();
```

#### 9g. Handler Discovery

```csharp
opts.Discovery.IncludeAssembly(typeof(Promotion).Assembly);
```

Wolverine auto-discovers handlers in the Promotions domain assembly.

---

### 10. Polecat Migration Considerations

#### 10a. Current ADR Status

ADR 0026 proposes Polecat for Vendor Identity, Vendor Portal, Customer Identity, and Customer Experience — NOT Pricing or Promotions. The spike mentions Polecat as a "Tier 2 candidate" for Pricing/Promotions but the actual ADR targets different BCs.

#### 10b. Design for Infrastructure Agnosticism

**The Promotions BC MUST be designed to work with either Marten (PostgreSQL) or Polecat (SQL Server).** This means:
1. Aggregates use `IDocumentSession` (shared interface between Marten and Polecat via JasperFx.Events)
2. Domain events are plain sealed records with no Marten-specific attributes
3. Projections use the `MultiStreamProjection<T, TId>` base class (shared)
4. No PostgreSQL-specific JSONB queries in handler code

**Practical implication:** Build on Marten first (proven, stable). If Polecat reaches 1.0+ and `WolverineFx.Polecat` integration exists, migration should be a `Program.cs` configuration change, not a code rewrite.

#### 10c. EF Core Projection Suitability

The spike argues that `CouponRedemptionLog` and `PromotionSummary` are "relational queries at heart." This is true — date-range filtering, aggregation, joins. If Polecat is adopted, these projections could be EF Core read models projected from the event stream.

**Phase 1 recommendation:** Use Marten inline/async projections (proven pattern). Document which projections are candidates for EF Core read models in a future Polecat migration.

#### 10d. Schema Isolation

**Standard CritterSupply pattern:**
```csharp
opts.DatabaseSchemaName = "promotions";
```

Promotions BC gets its own PostgreSQL schema. Isolated from all other BCs. Same shared database instance (per docker-compose), different schema (like every other BC).

---

### 11. Additional System Concerns

#### 11a. Stacking Rules Engine

**The hardest problem in Promotions.** When multiple promotions apply to the same cart:
- Which ones can combine?
- What order are they applied in?
- Does "percentage off" stack on original price or already-discounted price?

**Phase 1 simplification: No stacking.** Highest discount wins. `AllowsStacking = false` for all promotions. A single "best discount" is applied per line item.

**Phase 2:** `AllowsStacking = true` allows combining. Stacking order: percentage-off first (on original price), then fixed-amount-off on the result. Free shipping is always additive.

**Phase 3:** Custom stacking rules, priority ordering, "stackable within same campaign, not across campaigns."

**This MUST be a phased approach.** The stacking engine is where most e-commerce promotion systems become legacy nightmares.

#### 11b. Fraud Prevention Patterns

**Concerns:**
1. Automated coupon scraping — bots testing coupon codes rapidly
2. Coupon abuse — same customer with multiple accounts
3. Promotion exploitation — adding/removing items to manipulate eligibility

**Phase 1 mitigations:**
- Rate limiting on coupon validation endpoint (IP-based)
- MaxUsesPerCustomer enforcement on Coupon aggregate
- Logging of validation attempts for post-hoc analysis

**Phase 2+:**
- Customer Identity BC integration — detect multi-account abuse
- Pattern detection projection — flag suspicious redemption patterns
- Integration with Analytics BC for anomaly detection

#### 11c. Testing Strategy

**Unit tests:**
- Promotion aggregate Apply methods (state transitions, invariant enforcement)
- Coupon aggregate Apply methods
- Discount calculation pure function (various scenarios)
- FluentValidation validators for all commands
- Money VO (if copied from Pricing BC, copy tests too)

**Integration tests (Alba + TestContainers):**
- Create → Activate → Expire promotion lifecycle
- Coupon validation happy path + error cases
- Discount calculation with floor price clamping (requires Pricing BC in test)
- Redemption recording via RabbitMQ
- Scheduled activation/expiration (Wolverine scheduled message testing)
- Concurrent redemption (ConcurrencyException + retry)

**Cross-BC integration tests:**
- Shopping BC → Promotions BC coupon validation HTTP call
- Orders BC → Promotions BC discount calculation + redemption recording
- Promotions BC → Pricing BC floor price lookup

**BDD candidates (Reqnroll):**
- "Customer applies valid coupon to cart and sees discount"
- "Customer applies expired coupon and sees error message"
- "Promotion expires and cart discount is removed"
- "Two customers race for last single-use coupon"

#### 11d. Port Allocation

CritterSupply uses specific port ranges per BC (see CLAUDE.md port allocation table). Current assignments end at Correspondence (5248). Next available ports:

| BC | Port | Status |
|---|---|---|
| Correspondence | 5248 | ✅ Assigned (Cycle 28) |
| **Promotions** | **5249** | 📋 **Proposed** (Cycle 29) |

**Proposed:** Promotions.Api → port **5249** (next sequential port per CLAUDE.md convention — increment by 1 for each new BC)

#### 11e. Project Structure

Following CritterSupply conventions:
```
src/
├── Promotions/
│   ├── Promotions/                    # Domain project
│   │   ├── Promotions/               # Promotion aggregate folder
│   │   │   ├── Promotion.cs          # Aggregate record + Apply methods
│   │   │   ├── PromotionCreated.cs   # Domain events (one per file)
│   │   │   ├── PromotionActivated.cs
│   │   │   ├── ...
│   │   │   ├── CreatePromotionHandler.cs
│   │   │   ├── CreatePromotionValidator.cs
│   │   │   ├── ActivatePromotionHandler.cs
│   │   │   └── ...
│   │   ├── Coupons/                   # Coupon aggregate folder
│   │   │   ├── Coupon.cs
│   │   │   ├── CouponIssued.cs
│   │   │   ├── ...
│   │   │   ├── ValidateCouponHandler.cs
│   │   │   └── ...
│   │   ├── Discounts/                 # Discount calculation (stateless)
│   │   │   ├── CalculateDiscountHandler.cs
│   │   │   └── DiscountBreakdown.cs
│   │   ├── Money.cs                   # Copied from Pricing BC
│   │   ├── Constants.cs
│   │   └── AssemblyAttributes.cs
│   └── Promotions.Api/               # API project
│       ├── Program.cs
│       ├── appsettings.json
│       └── Properties/
│           └── launchSettings.json
├── Shared/
│   └── Messages.Contracts/
│       └── Promotions/                # NEW: Integration messages
│           ├── PromotionActivated.cs
│           ├── PromotionExpired.cs
│           ├── CouponValidated.cs
│           ├── CouponRejected.cs
│           ├── DiscountCalculated.cs
│           └── OrderWithPromotionPlaced.cs
```

#### 11f. Money VO Strategy

**The spike proposed sharing Money VO.** But CritterSupply's architecture principle is: **each BC is independently deployable.**

**Options:**
1. **Copy Money VO into Promotions** — duplication, but zero coupling
2. **Shared kernel package** — extract Money into a NuGet package shared by Pricing + Promotions
3. **Use decimal for Phase 1** — Promotions might not need full Money VO if all amounts are in USD

**Recommendation: Option 1 for Phase 1.** Copy Money.cs + MoneyJsonConverter.cs + tests. The VO is stable (140 tests, proven design). Duplication is acceptable for two BCs. If a third BC needs it, extract to shared kernel (ADR required).

**Counter-argument for Option 3:** Promotions only needs `decimal DiscountValue` for percentages and `Money` for fixed amounts. Could use `decimal` for percentages and delegate Money handling to the discount calculation endpoint (which already needs to work with Pricing BC's Money). **Phase 1 could start with just decimal and adopt Money when FixedAmountOff is fully implemented.**

#### 11g. Backward Compatibility with Shopping BC

Shopping BC already has placeholder events:
- `CouponApplied` — needs to be implemented as a real domain event
- `CouponRemoved` — same
- `PromotionApplied` — same
- `PriceRefreshed` — already exists (from Pricing BC integration)

**These events are currently unused.** They're listed as "Future Events (Phase 2+)" in CONTEXTS.md. Implementing them requires Shopping BC code changes alongside Promotions BC creation. **This is cross-BC work that needs to be scoped in the cycle plan.**

#### 11h. Free Shipping Promotion Complexity

`FreeShipping` discount type interacts with the Orders BC's `ShippingCost` field. Currently, shipping cost is captured in `CheckoutCompleted`. A free shipping promotion needs to:
1. Set `ShippingCost = 0` (or set `ShippingDiscount = ShippingCost`)
2. Record which promotion provided free shipping
3. Handle partial free shipping (e.g., "free standard shipping, upgrade costs extra")

**Phase 1 simplification:** FreeShipping sets shipping cost to 0. No partial shipping discounts. No shipping method restrictions. Keep it simple.

#### 11i. Minimum Order Amount Promotions

"Spend $50, get 10% off." The promotion has a `MinimumOrderAmount` threshold. This threshold must be checked against the cart subtotal BEFORE applying the discount.

**Question:** Is the minimum based on pre-discount subtotal or the running total after other promotions? **With Phase 1's "no stacking" rule, this is moot.** But for Phase 2 stacking, the order of evaluation matters.

#### 11j. Category-Based Scope

Promotions scoped to categories (e.g., "15% off all dog food") require knowing which SKUs belong to which categories. **This data lives in Product Catalog BC.**

**Options:**
1. Promotions BC subscribes to `ProductAdded`/`ProductUpdated` from Catalog and maintains a local SKU-to-category mapping
2. Promotions BC queries Catalog BC at discount calculation time
3. Promotions admin UI resolves categories to SKU lists at creation time (snapshot)

**Recommendation: Option 3 for Phase 1.** Admin creates promotion → selects category → UI resolves to current SKU list → stored as `IncludedSkus` on the Promotion. This is a snapshot, not a live binding. If new products are added to the category, they're NOT automatically included. **This is simpler and avoids cross-BC coupling. Document as a known limitation for Phase 1.**

**Phase 2:** Subscribe to `ProductAdded`/`ProductUpdated` to auto-update category-scoped promotions.

---

## Open Questions for Workshop Discussion

These items need input from Product Owner, UX Engineer, and QA before the event model can be finalized:

1. **Stacking policy:** No stacking (Phase 1)? Highest discount wins? Or "one coupon + one auto-applied"?
2. **Coupon reservation:** Do we soft-reserve coupons when applied to cart? Or accept the race condition with "second-to-commit gets regular price"?
3. **Auto-apply promotions:** When a promotion activates, do we push-update all active carts? Or lazy-evaluate on next page load?
4. **Promotion correction:** Can an Active promotion's discount value be corrected? Or must it be cancelled and recreated?
5. **Category scope:** Snapshot at creation time? Or live binding to Catalog BC?
6. **Free shipping details:** Full free shipping only? Or partial (e.g., "free on orders over $35")?
7. **Minimum order amount:** Pre-discount or post-discount subtotal?
8. **Admin approval workflow:** Direct activate? Or Draft → PendingApproval → Active?
9. **Coupon code format:** Random alphanumeric (SAVE20-A3X9K)? Human-friendly (SUMMER25)? Configurable?
10. **Per-customer limits:** MaxUsesPerCustomer on Promotion level? Coupon level? Both?
11. **Phase 1 scope boundary:** Just coupons? Or coupons + auto-applied promotions? Or coupons only, no auto-apply?

---

## Lessons Learned from Previous BCs (Do Not Repeat)

| Mistake (from past cycles) | Mitigation for Promotions BC |
|---|---|
| Single event return from `[WriteAggregate]` handler (Cycle 18 bug) | Always return `Events` collection (plural), never single event |
| `Dictionary<string, T>` mutable state in Cart/Inventory aggregates | Use `IReadOnlyList<T>` for all collections in Promotion/Coupon aggregates |
| Aggregate in wrong BC requiring migration (Checkout, Cycle 8) | Promotion and Coupon aggregates stay in Promotions BC — discount calculation is a query, not an aggregate |
| Missing `ChangedBy` actor ID in commands (Order saga gap) | Every command carries `Guid ChangedBy` or `Guid ActivatedBy` — FluentValidation rejects `Guid.Empty` |
| Deferring integration tests to manual testing (Cycle 18) | Alba integration tests written before any manual testing |
| Port conflicts in launchSettings.json | Reserve port 5249 before writing any code |
| Forgetting to add projects to `.sln` and `.slnx` files | Add Promotions and Promotions.Api to both solution files immediately |
| `NullReferenceException` in handlers without null aggregate checks | `Before()` method always checks for null aggregate |

---

## Summary: Key Architecture Decisions Needed

| # | Decision | Options | Impact |
|---|----------|---------|--------|
| 1 | Promotion stream ID format | UUID v7 (new) vs UUID v4 (random) | Stream ordering in projections |
| 2 | Coupon stream ID format | UUID v5 from code (Pricing pattern) vs string stream | Marten consistency, lookup patterns |
| 3 | Money VO strategy | Copy from Pricing vs shared kernel vs decimal-only | Cross-BC coupling, implementation effort |
| 4 | Coupon reservation model | Soft reserve in Promotions vs accept race condition | Complexity vs edge-case UX |
| 5 | Stacking rules (Phase 1) | No stacking vs highest wins vs one-of-each | Discount calculation complexity |
| 6 | Floor price enforcement point | At calculation time vs at creation time | Accuracy vs simplicity |
| 7 | Category scope resolution | Snapshot at creation vs live Catalog subscription | Cross-BC coupling, data freshness |
| 8 | Discount calculation location | Promotions BC endpoint vs Orders BC inline | Responsibility boundary |
| 9 | Phase 1 scope | Coupons only vs coupons + auto-apply | Delivery timeline, complexity |
| 10 | ADR for CheckoutCompleted evolution | Add discount fields vs new event type | Backward compatibility |

---

*This brain dump is Stage 1 input. Stages 2–6 below organize, refine, and finalize the event model.*

---
---

# Stage 2: Timeline

> Arrange all events into a coherent chronological flow representing the full Promotions BC lifecycle.

Each swimlane below represents a **temporal phase** of the promotion lifecycle. Events (🟠 orange stickies) are listed in order. Commands (🔵 blue) and queries (🟢 green) are tagged where they originate.

---

## Phase 1: Promotion Creation & Configuration (Admin)

```
Time ─────────────────────────────────────────────────────────────────►

Admin Portal                          Promotions BC
────────────                          ─────────────
🔵 CreatePromotion ──────────────────► 🟠 PromotionCreated (Draft)
                                        │
🔵 ConfigureDiscountRules ───────────► 🟠 DiscountRulesConfigured
                                        │
🔵 ConfigureScope ───────────────────► 🟠 PromotionScopeConfigured
                                        │
🔵 SetEligibilityRules ─────────────► 🟠 EligibilityRulesSet
                                        │
🔵 SetRedemptionLimits ─────────────► 🟠 RedemptionLimitsSet
```

**Aggregate:** Promotion  
**State after:** Draft (fully configured, not yet activatable without at least discount rules)

---

## Phase 2: Coupon Generation (Admin — coupon-required promotions only)

```
Time ─────────────────────────────────────────────────────────────────►

Admin Portal                          Promotions BC              Coupon Aggregates
────────────                          ─────────────              ─────────────────
🔵 GenerateCouponBatch ─────────────► 🟠 CouponBatchGenerated   ─► 🟠 CouponIssued (×N)
                                        (on Promotion stream)       (one per coupon stream)
```

**Fan-out pattern:** `GenerateCouponBatch` appends one `CouponBatchGenerated` event to the Promotion stream. The handler then emits N individual `IssueCoupon` commands via `OutgoingMessages` — each creates a separate Coupon aggregate.

**Invariant check:** If `RequiresCouponCode = true`, at least one `CouponBatchGenerated` must exist before activation.

---

## Phase 3: Promotion Activation (Immediate or Scheduled)

### Path A: Immediate Activation

```
Time ─────────────────────────────────────────────────────────────────►

Admin Portal                          Promotions BC                    Outbound
────────────                          ─────────────                    ────────
🔵 ActivatePromotion ───────────────► 🟠 PromotionActivated           ─► 📤 PromotionActivated
                                        (Draft → Active)                  (integration msg → Shopping, CE BFF)
                                        │
                                        ├─► ⏱️ ScheduleAsync(ExpirePromotion, EndsAt)
```

### Path B: Scheduled Activation (StartsAt is in the future)

```
Time ─────────────────────────────────────────────────────────────────►

Admin Portal                          Promotions BC
────────────                          ─────────────
🔵 SchedulePromotion ───────────────► 🟠 PromotionScheduled (Draft → Scheduled)
                                        │
                                        ├─► ⏱️ ScheduleAsync(ActivatePromotion, StartsAt)

        ⋯ time passes until StartsAt ⋯

Wolverine Scheduler ─────────────────► 🟠 PromotionActivated (Scheduled → Active)
                                        │
                                        ├─► 📤 PromotionActivated (integration msg)
                                        ├─► ⏱️ ScheduleAsync(ExpirePromotion, EndsAt)
```

**Stale-message guard:** Both scheduled messages carry a `ScheduleId`. Handler checks `promotion.PendingScheduleId == message.ScheduleId` before applying. Discards if mismatched (promotion was cancelled and recreated).

---

## Phase 4: Customer Discovers Promotion

```
Time ─────────────────────────────────────────────────────────────────►

Customer / CE BFF                     Promotions BC
─────────────────                     ─────────────
🟢 GetActivePromotions ─────────────► (reads ActivePromotionsView)
                                        │
🟢 GetPromotionDetails(id) ────────► (reads PromotionSummaryView)
                                        │
🟢 GetApplicablePromotions(skus) ──► (reads ActivePromotionsView, filters by scope)
```

**Read path only — no events emitted.** Served by inline Marten projections.

For **auto-applied promotions** (`RequiresCouponCode = false`): Customer sees discounted prices automatically on product pages and in cart (BFF queries Promotions on each render).

---

## Phase 5: Customer Applies Coupon (Coupon-Required Promotions)

```
Time ─────────────────────────────────────────────────────────────────►

Shopping BC                           Promotions BC                    Coupon Aggregate
───────────                           ─────────────                    ────────────────
🔵 ValidateCoupon ──── sync HTTP ───► (validate against CouponLookupView
                                        + Coupon aggregate state)
                                        │
                ◄────── HTTP 200 ──────┤ CouponValidationResult
                                        │ (IsValid, PromotionId,
                                        │  DiscountType, DiscountValue)
                                        │
                                      🟠 CouponReserved ──────────────► (soft-hold on Coupon)
                                        (with ReservationTtl = 15min)
```

**Sync HTTP because:** Customer is waiting for instant "✅ Coupon applied!" / "❌ Invalid coupon" feedback.

**Reservation semantics:**
- `CouponReserved` places a soft hold with a TTL
- If another customer tries to validate the same single-use coupon → "Coupon is currently in use"
- If the reservation expires (customer abandons cart) → `CouponReservationReleased`
- If the order is placed → `CouponRedeemed` (hard commit)

---

## Phase 6: Checkout & Discount Calculation

```
Time ─────────────────────────────────────────────────────────────────►

Shopping/Orders BC                    Promotions BC                    Pricing BC
──────────────────                    ─────────────                    ──────────
🟢 CalculateDiscount ── sync HTTP ──► (evaluate all active promotions
                                        + applied coupons
                                        + stacking rules
                                        + eligibility rules)
                                        │
                                        ├── 🟢 GetFloorPrice(sku) ─── sync HTTP ──► CurrentPriceView
                                        │                     ◄───── FloorPrice ──┤
                                        │
                                        ├── (clamp: discount cannot push below floor)
                                        │
                ◄────── HTTP 200 ──────┤ DiscountBreakdown
                                        │ (TotalDiscount,
                                        │  LineItemDiscounts[],
                                        │  AppliedPromotions[])
```

**Pure function:** No state mutation. No events. Stateless computation of discount given current cart, active promotions, applied coupons, and floor prices.

**Stacking rule (Phase 1):** `AllowsStacking = false` for all promotions. If multiple promotions apply, **highest discount wins**. Price Schedules (Pricing BC) are applied first — promotions overlay on top of the scheduled/base price.

---

## Phase 7: Order Placed with Promotion

```
Time ─────────────────────────────────────────────────────────────────►

Orders BC                                           Promotions BC
─────────                                           ─────────────
🟠 OrderPlaced (with discount fields) ── async ───► (consumed via RabbitMQ)
```

**Orders BC enhancement (ADR required):** `OrderPlaced` integration message gains optional promotion fields:

```csharp
public sealed record OrderPlaced(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<OrderLineItem> LineItems,
    ShippingAddress ShippingAddress,
    string ShippingMethod,
    string PaymentMethodToken,
    decimal TotalAmount,
    IReadOnlyList<AppliedPromotionRef>? AppliedPromotions,  // NEW
    DateTimeOffset PlacedAt);

public sealed record AppliedPromotionRef(
    Guid PromotionId,
    string? CouponCode,
    decimal DiscountAmount);
```

---

## Phase 8: Redemption Recording & Cap Enforcement

```
Time ─────────────────────────────────────────────────────────────────►

Promotions BC (RabbitMQ handler)      Promotion Aggregate         Coupon Aggregate
────────────────────────────────      ────────────────────        ────────────────
OrderPlaced received ───────────────► 🟠 RedemptionRecorded       🟠 CouponRedeemed
                                        (increment count)          (reservation → committed)
                                        │
                                        ├── IF count == cap:
                                        │   🟠 RedemptionCapReached
                                        │   🟠 PromotionExpired
                                        │   📤 PromotionExpired (integration msg)
```

**Concurrency control:** Marten optimistic concurrency on the Promotion event stream. Two simultaneous `RedemptionRecorded` appends → one gets `ConcurrencyException` → Wolverine retries (standard retry policy). If the retry finds cap reached, the redemption is rejected and the order proceeds at full price.

**Sequential queue (belt-and-suspenders):** For flash-sale scenarios with high contention:
```csharp
opts.LocalQueue("promotion-redemption").Sequential();
```

**Double-soft-reservation fallback:** If between coupon validation and order placement another customer redeemed the same coupon, the order succeeds at full price. The handler returns `CouponAlreadyRedeemed` integration message so Orders BC can record "discount not applied."

---

## Phase 9: Promotion Expires or Is Cancelled

### Path A: Natural Expiration (scheduled message)

```
Time ─────────────────────────────────────────────────────────────────►

Wolverine Scheduler                   Promotions BC                    Outbound
───────────────────                   ─────────────                    ────────
⏱️ ExpirePromotion fires ───────────► 🟠 PromotionExpired             📤 PromotionExpired
  (stale-guard checks ScheduleId)      (Active → Expired)               (→ Shopping, CE BFF)
```

### Path B: Admin Cancellation

```
Admin Portal                          Promotions BC                    Outbound
────────────                          ─────────────                    ────────
🔵 CancelPromotion ────────────────► 🟠 PromotionCancelled            📤 PromotionCancelled
                                        (any non-terminal → Cancelled)    (→ Shopping, CE BFF)
```

### Path C: Admin Pause / Resume

```
Admin Portal                          Promotions BC                    Outbound
────────────                          ─────────────                    ────────
🔵 PausePromotion ─────────────────► 🟠 PromotionPaused              📤 PromotionPaused
                                        (Active → Paused)                (→ Shopping, CE BFF)
        ⋯ investigation ⋯
🔵 ResumePromotion ────────────────► 🟠 PromotionResumed             📤 PromotionResumed
                                        (Paused → Active)                (→ Shopping, CE BFF)
```

---

## Phase 10: Coupon Cleanup (Cascading from Promotion Expiry/Cancellation)

```
Time ─────────────────────────────────────────────────────────────────►

Promotions BC                         Coupon Aggregates
─────────────                         ─────────────────
PromotionExpired handler ────────────► query CouponLookupView
                                        (WHERE PromotionId = X AND Status = Active)
                                        │
                                        ├── 🔵 ExpireCoupon(code1) ──► 🟠 CouponExpired
                                        ├── 🔵 ExpireCoupon(code2) ──► 🟠 CouponExpired
                                        ├── ⋯ (fan-out via OutgoingMessages)
                                        └── 🔵 ExpireCoupon(codeN) ──► 🟠 CouponExpired
```

**Fan-out pattern:** Cascading coupon expiry uses Wolverine `OutgoingMessages` to emit individual `ExpireCoupon` commands. Each processes independently, avoiding a massive single transaction for promotions with thousands of coupons.

### Admin-Initiated Coupon Revocation (independent of promotion lifecycle)

```
Admin Portal                          Promotions BC
────────────                          ─────────────
🔵 RevokeCoupon(code) ─────────────► 🟠 CouponRevoked (Active → Revoked)
```

---
---

# Stage 3: Commands, Queries, and Originating Events

> For each event in the timeline, identify the command or query that produces it, who issues it, and the event(s) emitted.

## Commands → Events (Write Side)

### Promotion Aggregate Commands

| # | Command | Issuer | Event(s) Produced | HTTP Endpoint |
|---|---------|--------|-------------------|---------------|
| 1 | `CreatePromotion` | Admin Portal | `PromotionCreated` | `POST /api/promotions` |
| 2 | `ConfigureDiscountRules` | Admin Portal | `DiscountRulesConfigured` | `PUT /api/promotions/{id}/discount-rules` |
| 3 | `ConfigureScope` | Admin Portal | `PromotionScopeConfigured` | `PUT /api/promotions/{id}/scope` |
| 4 | `SetEligibilityRules` | Admin Portal | `EligibilityRulesSet` | `PUT /api/promotions/{id}/eligibility` |
| 5 | `SetRedemptionLimits` | Admin Portal | `RedemptionLimitsSet` | `PUT /api/promotions/{id}/redemption-limits` |
| 6 | `GenerateCouponBatch` | Admin Portal | `CouponBatchGenerated` + N × `CouponIssued` (fan-out) | `POST /api/promotions/{id}/coupon-batches` |
| 7 | `SchedulePromotion` | Admin Portal | `PromotionScheduled` + ⏱️ `ActivatePromotion` scheduled | `POST /api/promotions/{id}/schedule` |
| 8 | `ActivatePromotion` | Admin Portal _or_ Wolverine Scheduler | `PromotionActivated` + 📤 integration + ⏱️ `ExpirePromotion` scheduled | `POST /api/promotions/{id}/activate` |
| 9 | `PausePromotion` | Admin Portal | `PromotionPaused` + 📤 integration | `POST /api/promotions/{id}/pause` |
| 10 | `ResumePromotion` | Admin Portal | `PromotionResumed` + 📤 integration | `POST /api/promotions/{id}/resume` |
| 11 | `CancelPromotion` | Admin Portal | `PromotionCancelled` + 📤 integration + cascading `ExpireCoupon` | `POST /api/promotions/{id}/cancel` |
| 12 | `ExpirePromotion` | Wolverine Scheduler | `PromotionExpired` + 📤 integration + cascading `ExpireCoupon` | _(internal command, no HTTP)_ |
| 13 | `RecordRedemption` | RabbitMQ handler (from `OrderPlaced`) | `RedemptionRecorded` [+ `RedemptionCapReached` + `PromotionExpired`] | _(internal command, no HTTP)_ |

### Coupon Aggregate Commands

| # | Command | Issuer | Event(s) Produced | HTTP Endpoint |
|---|---------|--------|-------------------|---------------|
| 14 | `IssueCoupon` | Fan-out from `GenerateCouponBatch` handler | `CouponIssued` | _(internal command, no HTTP)_ |
| 15 | `ValidateCoupon` | Shopping BC (sync HTTP) | `CouponReserved` _(if valid)_ | `POST /api/promotions/coupons/validate` |
| 16 | `ReleaseCouponReservation` | Cart timeout / coupon removal | `CouponReservationReleased` | _(internal command, no HTTP)_ |
| 17 | `RedeemCoupon` | Promotions BC handler (from `RecordRedemption`) | `CouponRedeemed` | _(internal command, no HTTP)_ |
| 18 | `ExpireCoupon` | Fan-out from promotion expiry handler | `CouponExpired` | _(internal command, no HTTP)_ |
| 19 | `RevokeCoupon` | Admin Portal | `CouponRevoked` | `DELETE /api/promotions/coupons/{code}` |

### Command Records (C# Definitions)

```csharp
// ─── Promotion Aggregate Commands ───────────────────────────────────

namespace Promotions.Promotions;

public sealed record CreatePromotion(
    string Name,
    string Description,
    Guid CreatedBy);

public sealed record ConfigureDiscountRules(
    Guid PromotionId,
    DiscountType DiscountType,
    decimal DiscountValue,
    DiscountApplication DiscountApplication,
    Guid ConfiguredBy);

public sealed record ConfigureScope(
    Guid PromotionId,
    PromotionScope Scope,
    IReadOnlyList<string>? IncludedSkus,
    IReadOnlyList<string>? ExcludedSkus,
    IReadOnlyList<string>? IncludedCategories,
    Guid ConfiguredBy);

public sealed record SetEligibilityRules(
    Guid PromotionId,
    decimal? MinimumOrderAmount,
    bool? NewCustomersOnly,
    Guid ConfiguredBy);

public sealed record SetRedemptionLimits(
    Guid PromotionId,
    int? RedemptionCap,
    int MaxUsesPerCustomer,
    bool AllowsStacking,
    Guid ConfiguredBy);

public sealed record GenerateCouponBatch(
    Guid PromotionId,
    int Count,
    string? Prefix,
    int MaxUsesPerCoupon,
    Guid GeneratedBy);

public sealed record SchedulePromotion(
    Guid PromotionId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    Guid ScheduledBy);

public sealed record ActivatePromotion(
    Guid PromotionId,
    Guid ScheduleId,
    Guid? ActivatedBy);

public sealed record PausePromotion(
    Guid PromotionId,
    string Reason,
    Guid PausedBy);

public sealed record ResumePromotion(
    Guid PromotionId,
    Guid ResumedBy);

public sealed record CancelPromotion(
    Guid PromotionId,
    string Reason,
    Guid CancelledBy);

public sealed record ExpirePromotion(
    Guid PromotionId,
    Guid ScheduleId);

public sealed record RecordRedemption(
    Guid PromotionId,
    Guid OrderId,
    Guid CustomerId,
    string? CouponCode,
    decimal DiscountApplied,
    DateTimeOffset PlacedAt);

// ─── Coupon Aggregate Commands ──────────────────────────────────────

namespace Promotions.Coupons;

public sealed record IssueCoupon(
    string CouponCode,
    Guid PromotionId,
    Guid BatchId,
    int MaxUses,
    int MaxUsesPerCustomer);

public sealed record ValidateCoupon(
    string CouponCode,
    Guid CartId,
    Guid? CustomerId,
    IReadOnlyList<CartItemRef> CartItems);

public sealed record CartItemRef(
    string Sku,
    int Quantity,
    decimal UnitPrice);

public sealed record ReleaseCouponReservation(
    string CouponCode,
    Guid CartId);

public sealed record RedeemCoupon(
    string CouponCode,
    Guid OrderId,
    Guid CustomerId);

public sealed record ExpireCoupon(
    string CouponCode);

public sealed record RevokeCoupon(
    string CouponCode,
    string Reason,
    Guid RevokedBy);
```

## Queries (Read Side)

| # | Query | Issuer | Read Model Used | HTTP Endpoint |
|---|-------|--------|-----------------|---------------|
| Q1 | `GetActivePromotions` | CE BFF / Shopping BC | `ActivePromotionsView` | `GET /api/promotions/active` |
| Q2 | `GetPromotionDetails` | Admin Portal | `PromotionSummaryView` | `GET /api/promotions/{id}` |
| Q3 | `GetApplicablePromotions` | CE BFF / Shopping BC | `ActivePromotionsView` (filtered by SKUs) | `GET /api/promotions/applicable?skus=X,Y,Z` |
| Q4 | `CalculateDiscount` | Shopping BC / Orders BC | `ActivePromotionsView` + `CouponLookupView` + Pricing HTTP | `POST /api/promotions/calculate-discount` |
| Q5 | `GetPromotionCalendar` | Admin Portal | `PromotionCalendarView` | `GET /api/promotions/calendar?from=X&to=Y` |
| Q6 | `GetCouponStatus` | Admin Portal | `CouponLookupView` | `GET /api/promotions/coupons/{code}` |
| Q7 | `GetPromotionRedemptions` | Admin Portal | `PromotionSummaryView` | `GET /api/promotions/{id}/redemptions` |

### Query Records (C# Definitions)

```csharp
namespace Promotions.Promotions;

public sealed record GetActivePromotions;

public sealed record GetPromotionDetails(Guid PromotionId);

public sealed record GetApplicablePromotions(IReadOnlyList<string> Skus);

public sealed record CalculateDiscount(
    Guid? CustomerId,
    IReadOnlyList<CartItemRef> Items,
    IReadOnlyList<string>? CouponCodes,
    decimal ShippingCost);

public sealed record GetPromotionCalendar(
    DateTimeOffset From,
    DateTimeOffset To);

namespace Promotions.Coupons;

public sealed record GetCouponStatus(string CouponCode);
```

---
---

# Stage 4: Views and Projections

> What read models support each command and query? Specify Marten projection type, lifecycle, key, and which events drive each projection.

## Projection Summary

| # | Projection | Key Type | Key | Lifecycle | Purpose |
|---|-----------|----------|-----|-----------|---------|
| P1 | `ActivePromotionsView` | `Guid` | PromotionId | **Inline** | Hot-path: discount calculation, storefront badges |
| P2 | `CouponLookupView` | `string` | CouponCode (uppercase) | **Inline** | Hot-path: coupon validation at checkout |
| P3 | `PromotionSummaryView` | `Guid` | PromotionId | **Async** | Admin dashboard: promotion status + metrics |
| P4 | `CustomerRedemptionView` | `string` | `"{CustomerId}:{PromotionId}"` | **Inline** | Per-customer usage limit enforcement |
| P5 | `PromotionCalendarView` | `Guid` | PromotionId | **Async** | Admin: visual calendar of promotion schedule |

---

## P1: `ActivePromotionsView` — Inline Projection

**Purpose:** Fast lookup of currently active promotions for discount calculation and storefront display. Zero-lag required because discount calculations happen in the customer's critical path.

**Projection type:** `MultiStreamProjection<ActivePromotionsView, Guid>` — Guid-keyed, maps directly from Promotion event streams.

```csharp
namespace Promotions.Promotions;

/// <summary>
/// Read model for currently active promotions.
/// Inline projection — zero lag, same transaction as command.
/// Consumed by: CalculateDiscount endpoint, GetActivePromotions query,
/// GetApplicablePromotions query.
/// </summary>
public sealed record ActivePromotionsView
{
    public Guid Id { get; init; }  // PromotionId
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
    public PromotionStatus Status { get; init; }
    public DiscountType DiscountType { get; init; }
    public decimal DiscountValue { get; init; }
    public DiscountApplication DiscountApplication { get; init; }
    public PromotionScope Scope { get; init; }
    public IReadOnlyList<string> IncludedSkus { get; init; } = [];
    public IReadOnlyList<string> ExcludedSkus { get; init; } = [];
    public IReadOnlyList<string> IncludedCategories { get; init; } = [];
    public bool AllowsStacking { get; init; }
    public bool RequiresCouponCode { get; init; }
    public decimal? MinimumOrderAmount { get; init; }
    public int? RedemptionCap { get; init; }
    public int CurrentRedemptionCount { get; init; }
    public DateTimeOffset StartsAt { get; init; }
    public DateTimeOffset EndsAt { get; init; }
    public DateTimeOffset LastUpdatedAt { get; init; }
}

public sealed class ActivePromotionsViewProjection
    : MultiStreamProjection<ActivePromotionsView, Guid>
{
    public ActivePromotionsViewProjection()
    {
        Identity<PromotionCreated>(x => x.PromotionId);
        Identity<DiscountRulesConfigured>(x => x.PromotionId);
        Identity<PromotionScopeConfigured>(x => x.PromotionId);
        Identity<EligibilityRulesSet>(x => x.PromotionId);
        Identity<RedemptionLimitsSet>(x => x.PromotionId);
        Identity<PromotionScheduled>(x => x.PromotionId);
        Identity<PromotionActivated>(x => x.PromotionId);
        Identity<PromotionPaused>(x => x.PromotionId);
        Identity<PromotionResumed>(x => x.PromotionId);
        Identity<PromotionExpired>(x => x.PromotionId);
        Identity<PromotionCancelled>(x => x.PromotionId);
        Identity<RedemptionRecorded>(x => x.PromotionId);
        Identity<RedemptionCapReached>(x => x.PromotionId);
    }

    public ActivePromotionsView Create(PromotionCreated evt) =>
        new()
        {
            Id = evt.PromotionId,
            Name = evt.Name,
            Description = evt.Description,
            Status = PromotionStatus.Draft,
            LastUpdatedAt = evt.CreatedAt
        };

    public static ActivePromotionsView Apply(
        ActivePromotionsView view, DiscountRulesConfigured evt) =>
        view with
        {
            DiscountType = evt.DiscountType,
            DiscountValue = evt.DiscountValue,
            DiscountApplication = evt.DiscountApplication,
            LastUpdatedAt = evt.ConfiguredAt
        };

    public static ActivePromotionsView Apply(
        ActivePromotionsView view, PromotionScopeConfigured evt) =>
        view with
        {
            Scope = evt.Scope,
            IncludedSkus = evt.IncludedSkus ?? [],
            ExcludedSkus = evt.ExcludedSkus ?? [],
            IncludedCategories = evt.IncludedCategories ?? [],
            LastUpdatedAt = evt.ConfiguredAt
        };

    public static ActivePromotionsView Apply(
        ActivePromotionsView view, EligibilityRulesSet evt) =>
        view with
        {
            MinimumOrderAmount = evt.MinimumOrderAmount,
            LastUpdatedAt = evt.SetAt
        };

    public static ActivePromotionsView Apply(
        ActivePromotionsView view, RedemptionLimitsSet evt) =>
        view with
        {
            RedemptionCap = evt.RedemptionCap,
            AllowsStacking = evt.AllowsStacking,
            LastUpdatedAt = evt.SetAt
        };

    public static ActivePromotionsView Apply(
        ActivePromotionsView view, PromotionScheduled evt) =>
        view with
        {
            Status = PromotionStatus.Scheduled,
            StartsAt = evt.StartsAt,
            EndsAt = evt.EndsAt,
            LastUpdatedAt = evt.ScheduledAt
        };

    public static ActivePromotionsView Apply(
        ActivePromotionsView view, PromotionActivated evt) =>
        view with
        {
            Status = PromotionStatus.Active,
            LastUpdatedAt = evt.ActivatedAt
        };

    public static ActivePromotionsView Apply(
        ActivePromotionsView view, PromotionPaused evt) =>
        view with { Status = PromotionStatus.Paused, LastUpdatedAt = evt.PausedAt };

    public static ActivePromotionsView Apply(
        ActivePromotionsView view, PromotionResumed evt) =>
        view with { Status = PromotionStatus.Active, LastUpdatedAt = evt.ResumedAt };

    public static ActivePromotionsView Apply(
        ActivePromotionsView view, RedemptionRecorded evt) =>
        view with
        {
            CurrentRedemptionCount = evt.NewRedemptionCount,
            LastUpdatedAt = evt.RecordedAt
        };

    public static ActivePromotionsView Apply(
        ActivePromotionsView view, PromotionExpired evt) =>
        view with { Status = PromotionStatus.Expired, LastUpdatedAt = evt.ExpiredAt };

    public static ActivePromotionsView Apply(
        ActivePromotionsView view, PromotionCancelled evt) =>
        view with { Status = PromotionStatus.Cancelled, LastUpdatedAt = evt.CancelledAt };
}
```

**Query patterns:**
- `GetActivePromotions`: `session.Query<ActivePromotionsView>().Where(x => x.Status == Active)`
- `GetApplicablePromotions(skus)`: Filter by `Scope == AllItems` OR `IncludedSkus.Intersect(skus).Any()`
- `CalculateDiscount`: Load all Active views, evaluate eligibility per cart item

---

## P2: `CouponLookupView` — Inline Projection

**Purpose:** Fast coupon code validation (hot path during checkout). String-keyed by normalized coupon code, following the Pricing BC `CurrentPriceView` pattern (MultiStreamProjection from Guid streams to string-keyed documents).

```csharp
namespace Promotions.Coupons;

/// <summary>
/// Read model for fast coupon code validation.
/// Inline projection — zero lag, same transaction as command.
/// Key: CouponCode string (normalized uppercase).
/// Consumed by: ValidateCoupon endpoint, admin GetCouponStatus query.
/// </summary>
public sealed record CouponLookupView
{
    public string Id { get; init; } = null!;  // CouponCode (uppercase)
    public string CouponCode { get; init; } = null!;
    public Guid CouponId { get; init; }       // UUID v5 stream ID
    public Guid PromotionId { get; init; }
    public Guid? BatchId { get; init; }
    public CouponStatus Status { get; init; }
    public int MaxUses { get; init; }
    public int MaxUsesPerCustomer { get; init; }
    public int CurrentUseCount { get; init; }
    public Guid? ReservedByCartId { get; init; }
    public DateTimeOffset? ReservedUntil { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset LastUpdatedAt { get; init; }
}

public sealed class CouponLookupViewProjection
    : MultiStreamProjection<CouponLookupView, string>
{
    public CouponLookupViewProjection()
    {
        // Map Guid event streams → string-keyed documents
        Identity<CouponIssued>(x => x.CouponCode);
        Identity<CouponReserved>(x => x.CouponCode);
        Identity<CouponReservationReleased>(x => x.CouponCode);
        Identity<CouponRedeemed>(x => x.CouponCode);
        Identity<CouponExpired>(x => x.CouponCode);
        Identity<CouponRevoked>(x => x.CouponCode);
    }

    public CouponLookupView Create(CouponIssued evt) =>
        new()
        {
            Id = evt.CouponCode,
            CouponCode = evt.CouponCode,
            CouponId = evt.CouponId,
            PromotionId = evt.PromotionId,
            BatchId = evt.BatchId,
            Status = CouponStatus.Active,
            MaxUses = evt.MaxUses,
            MaxUsesPerCustomer = evt.MaxUsesPerCustomer,
            CurrentUseCount = 0,
            IssuedAt = evt.IssuedAt,
            LastUpdatedAt = evt.IssuedAt
        };

    public static CouponLookupView Apply(CouponLookupView view, CouponReserved evt) =>
        view with
        {
            ReservedByCartId = evt.CartId,
            ReservedUntil = evt.ReservedUntil,
            LastUpdatedAt = evt.ReservedAt
        };

    public static CouponLookupView Apply(CouponLookupView view, CouponReservationReleased evt) =>
        view with
        {
            ReservedByCartId = null,
            ReservedUntil = null,
            LastUpdatedAt = evt.ReleasedAt
        };

    public static CouponLookupView Apply(CouponLookupView view, CouponRedeemed evt) =>
        view with
        {
            CurrentUseCount = evt.NewUseCount,
            Status = evt.NewUseCount >= view.MaxUses ? CouponStatus.Redeemed : view.Status,
            ReservedByCartId = null,
            ReservedUntil = null,
            LastUpdatedAt = evt.RedeemedAt
        };

    public static CouponLookupView Apply(CouponLookupView view, CouponExpired evt) =>
        view with { Status = CouponStatus.Expired, LastUpdatedAt = evt.ExpiredAt };

    public static CouponLookupView Apply(CouponLookupView view, CouponRevoked evt) =>
        view with { Status = CouponStatus.Revoked, LastUpdatedAt = evt.RevokedAt };
}
```

---

## P3: `PromotionSummaryView` — Async Projection

**Purpose:** Admin dashboard — campaign performance metrics. Async is acceptable because admin dashboards tolerate seconds-old data.

```csharp
namespace Promotions.Promotions;

/// <summary>
/// Read model for admin promotion management dashboard.
/// Async projection — eventual consistency (seconds of lag acceptable).
/// Includes aggregated metrics: redemption count, discount totals.
/// </summary>
public sealed record PromotionSummaryView
{
    public Guid Id { get; init; }  // PromotionId
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
    public PromotionStatus Status { get; init; }
    public DiscountType DiscountType { get; init; }
    public decimal DiscountValue { get; init; }
    public DiscountApplication DiscountApplication { get; init; }
    public PromotionScope Scope { get; init; }
    public bool RequiresCouponCode { get; init; }
    public int? RedemptionCap { get; init; }
    public int TotalRedemptions { get; init; }
    public decimal TotalDiscountAmount { get; init; }
    public int CouponBatchCount { get; init; }
    public int TotalCouponsIssued { get; init; }
    public DateTimeOffset StartsAt { get; init; }
    public DateTimeOffset EndsAt { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastUpdatedAt { get; init; }
}
```

---

## P4: `CustomerRedemptionView` — Inline Projection

**Purpose:** Per-customer usage limit enforcement. Needed at validation time to check "has this customer already used this promotion?" Must be inline for accuracy during concurrent redemptions.

```csharp
namespace Promotions.Coupons;

/// <summary>
/// Read model tracking per-customer redemptions against a specific promotion.
/// Inline projection — zero lag, needed for MaxUsesPerCustomer enforcement.
/// Key: "{CustomerId}:{PromotionId}" composite string.
/// </summary>
public sealed record CustomerRedemptionView
{
    public string Id { get; init; } = null!;  // "{CustomerId}:{PromotionId}"
    public Guid CustomerId { get; init; }
    public Guid PromotionId { get; init; }
    public int RedemptionCount { get; init; }
    public IReadOnlyList<RedemptionEntry> Redemptions { get; init; } = [];
    public DateTimeOffset LastRedeemedAt { get; init; }
}

public sealed record RedemptionEntry(
    Guid OrderId,
    string? CouponCode,
    decimal DiscountApplied,
    DateTimeOffset RedeemedAt);
```

**Note:** This projection is fed by `RedemptionRecorded` events from the Promotion aggregate. It uses a `MultiStreamProjection<CustomerRedemptionView, string>` with a composite key derived from the event's `CustomerId` and `PromotionId` fields.

---

## P5: `PromotionCalendarView` — Async Projection

**Purpose:** Admin visual calendar of all promotions with their time ranges.

```csharp
namespace Promotions.Promotions;

/// <summary>
/// Read model for the admin promotion calendar.
/// Async projection — dashboard tolerates lag.
/// </summary>
public sealed record PromotionCalendarView
{
    public Guid Id { get; init; }  // PromotionId
    public string Name { get; init; } = null!;
    public PromotionStatus Status { get; init; }
    public DiscountType DiscountType { get; init; }
    public decimal DiscountValue { get; init; }
    public DateTimeOffset StartsAt { get; init; }
    public DateTimeOffset EndsAt { get; init; }
    public DateTimeOffset LastUpdatedAt { get; init; }
}
```

---

## Projection Registration (Program.cs)

```csharp
builder.Services.AddMarten(opts =>
{
    // ... connection, schema config ...

    opts.Events.StreamIdentity = StreamIdentity.AsGuid;

    // Inline projections (zero lag — customer critical path)
    opts.Projections.Add<ActivePromotionsViewProjection>(ProjectionLifecycle.Inline);
    opts.Projections.Add<CouponLookupViewProjection>(ProjectionLifecycle.Inline);
    opts.Projections.Add<CustomerRedemptionViewProjection>(ProjectionLifecycle.Inline);

    // Aggregate snapshots
    opts.Projections.Snapshot<Promotion>(SnapshotLifecycle.Inline);
    opts.Projections.Snapshot<Coupon>(SnapshotLifecycle.Inline);

    // Async projections (admin dashboards — lag acceptable)
    opts.Projections.Add<PromotionSummaryViewProjection>(ProjectionLifecycle.Async);
    opts.Projections.Add<PromotionCalendarViewProjection>(ProjectionLifecycle.Async);
})
.AddAsyncDaemon(DaemonMode.Solo);
```

---
---

# Stage 5: Aggregates

> Finalize aggregate boundaries with events each owns, invariants each enforces, state shape, and Polecat migration considerations.

## Aggregate 1: `Promotion`

**Stream ID:** UUID v7 (time-ordered, new)  
**Identity:** `StreamIdentity.AsGuid`  
**Pattern:** Marten event-sourced aggregate with `Create()`/`Apply()` (sealed record, immutable)

### State Shape

```csharp
namespace Promotions.Promotions;

/// <summary>
/// Event-sourced aggregate representing a marketing promotion.
/// Owns discount rules, scope, eligibility, redemption tracking, and lifecycle.
/// Write-only model: contains only state and Apply() methods.
/// Business logic resides in handlers using the Decider pattern.
/// </summary>
public sealed record Promotion(
    Guid Id,
    string Name,
    string? Description,
    PromotionStatus Status,
    DiscountType DiscountType,
    decimal DiscountValue,
    DiscountApplication DiscountApplication,
    PromotionScope Scope,
    IReadOnlyList<string> IncludedSkus,
    IReadOnlyList<string> ExcludedSkus,
    IReadOnlyList<string> IncludedCategories,
    bool AllowsStacking,
    bool RequiresCouponCode,
    decimal? MinimumOrderAmount,
    bool? NewCustomersOnly,
    int? RedemptionCap,
    int CurrentRedemptionCount,
    int MaxUsesPerCustomer,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    Guid? PendingActivationScheduleId,
    Guid? PendingExpirationScheduleId,
    int CouponBatchCount,
    Guid CreatedBy,
    DateTimeOffset CreatedAt)
{
    public bool IsTerminal =>
        Status is PromotionStatus.Expired or PromotionStatus.Cancelled;

    public bool IsActivatable =>
        Status is PromotionStatus.Draft or PromotionStatus.Scheduled;

    public bool HasReachedCap =>
        RedemptionCap.HasValue && CurrentRedemptionCount >= RedemptionCap.Value;

    // ─── Factory ────────────────────────────────────────────────────

    public static Promotion Create(PromotionCreated evt) =>
        new(Id: evt.PromotionId,
            Name: evt.Name,
            Description: evt.Description,
            Status: PromotionStatus.Draft,
            DiscountType: default,
            DiscountValue: 0,
            DiscountApplication: default,
            Scope: default,
            IncludedSkus: [],
            ExcludedSkus: [],
            IncludedCategories: [],
            AllowsStacking: false,
            RequiresCouponCode: false,
            MinimumOrderAmount: null,
            NewCustomersOnly: null,
            RedemptionCap: null,
            CurrentRedemptionCount: 0,
            MaxUsesPerCustomer: 1,
            StartsAt: default,
            EndsAt: default,
            PendingActivationScheduleId: null,
            PendingExpirationScheduleId: null,
            CouponBatchCount: 0,
            CreatedBy: evt.CreatedBy,
            CreatedAt: evt.CreatedAt);

    // ─── Apply Methods ──────────────────────────────────────────────

    public Promotion Apply(DiscountRulesConfigured evt) =>
        this with
        {
            DiscountType = evt.DiscountType,
            DiscountValue = evt.DiscountValue,
            DiscountApplication = evt.DiscountApplication
        };

    public Promotion Apply(PromotionScopeConfigured evt) =>
        this with
        {
            Scope = evt.Scope,
            IncludedSkus = evt.IncludedSkus ?? [],
            ExcludedSkus = evt.ExcludedSkus ?? [],
            IncludedCategories = evt.IncludedCategories ?? []
        };

    public Promotion Apply(EligibilityRulesSet evt) =>
        this with
        {
            MinimumOrderAmount = evt.MinimumOrderAmount,
            NewCustomersOnly = evt.NewCustomersOnly
        };

    public Promotion Apply(RedemptionLimitsSet evt) =>
        this with
        {
            RedemptionCap = evt.RedemptionCap,
            MaxUsesPerCustomer = evt.MaxUsesPerCustomer,
            AllowsStacking = evt.AllowsStacking
        };

    public Promotion Apply(CouponBatchGenerated evt) =>
        this with { CouponBatchCount = CouponBatchCount + 1 };

    public Promotion Apply(PromotionScheduled evt) =>
        this with
        {
            Status = PromotionStatus.Scheduled,
            StartsAt = evt.StartsAt,
            EndsAt = evt.EndsAt,
            PendingActivationScheduleId = evt.ActivationScheduleId
        };

    public Promotion Apply(PromotionActivated evt) =>
        this with
        {
            Status = PromotionStatus.Active,
            PendingActivationScheduleId = null,
            PendingExpirationScheduleId = evt.ExpirationScheduleId
        };

    public Promotion Apply(PromotionPaused evt) =>
        this with { Status = PromotionStatus.Paused };

    public Promotion Apply(PromotionResumed evt) =>
        this with { Status = PromotionStatus.Active };

    public Promotion Apply(RedemptionRecorded evt) =>
        this with { CurrentRedemptionCount = evt.NewRedemptionCount };

    public Promotion Apply(RedemptionCapReached evt) =>
        this with { Status = PromotionStatus.Expired };

    public Promotion Apply(PromotionExpired evt) =>
        this with
        {
            Status = PromotionStatus.Expired,
            PendingExpirationScheduleId = null
        };

    public Promotion Apply(PromotionCancelled evt) =>
        this with
        {
            Status = PromotionStatus.Cancelled,
            PendingActivationScheduleId = null,
            PendingExpirationScheduleId = null
        };
}
```

### Owned Events

```csharp
namespace Promotions.Promotions;

public sealed record PromotionCreated(
    Guid PromotionId,
    string Name,
    string? Description,
    Guid CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record DiscountRulesConfigured(
    Guid PromotionId,
    DiscountType DiscountType,
    decimal DiscountValue,
    DiscountApplication DiscountApplication,
    Guid ConfiguredBy,
    DateTimeOffset ConfiguredAt);

public sealed record PromotionScopeConfigured(
    Guid PromotionId,
    PromotionScope Scope,
    IReadOnlyList<string>? IncludedSkus,
    IReadOnlyList<string>? ExcludedSkus,
    IReadOnlyList<string>? IncludedCategories,
    Guid ConfiguredBy,
    DateTimeOffset ConfiguredAt);

public sealed record EligibilityRulesSet(
    Guid PromotionId,
    decimal? MinimumOrderAmount,
    bool? NewCustomersOnly,
    Guid SetBy,
    DateTimeOffset SetAt);

public sealed record RedemptionLimitsSet(
    Guid PromotionId,
    int? RedemptionCap,
    int MaxUsesPerCustomer,
    bool AllowsStacking,
    Guid SetBy,
    DateTimeOffset SetAt);

public sealed record CouponBatchGenerated(
    Guid PromotionId,
    Guid BatchId,
    int Count,
    string? Prefix,
    int MaxUsesPerCoupon,
    Guid GeneratedBy,
    DateTimeOffset GeneratedAt);

public sealed record PromotionScheduled(
    Guid PromotionId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    Guid ActivationScheduleId,
    Guid ScheduledBy,
    DateTimeOffset ScheduledAt);

public sealed record PromotionActivated(
    Guid PromotionId,
    Guid ExpirationScheduleId,
    Guid? ActivatedBy,
    DateTimeOffset ActivatedAt);

public sealed record PromotionPaused(
    Guid PromotionId,
    string Reason,
    Guid PausedBy,
    DateTimeOffset PausedAt);

public sealed record PromotionResumed(
    Guid PromotionId,
    Guid ResumedBy,
    DateTimeOffset ResumedAt);

public sealed record RedemptionRecorded(
    Guid PromotionId,
    Guid OrderId,
    Guid CustomerId,
    string? CouponCode,
    decimal DiscountApplied,
    int NewRedemptionCount,
    DateTimeOffset RecordedAt);

public sealed record RedemptionCapReached(
    Guid PromotionId,
    int FinalRedemptionCount,
    DateTimeOffset ReachedAt);

public sealed record PromotionExpired(
    Guid PromotionId,
    DateTimeOffset ExpiredAt);

public sealed record PromotionCancelled(
    Guid PromotionId,
    string Reason,
    Guid CancelledBy,
    DateTimeOffset CancelledAt);
```

### Enums

```csharp
namespace Promotions.Promotions;

/// <summary>
/// Lifecycle states of a Promotion aggregate.
/// </summary>
public enum PromotionStatus
{
    /// <summary>Promotion is being configured. Can be modified.</summary>
    Draft,
    /// <summary>Promotion has start/end dates set. Waiting for activation time.</summary>
    Scheduled,
    /// <summary>Promotion is live. Customers can use it.</summary>
    Active,
    /// <summary>Promotion temporarily suspended by admin.</summary>
    Paused,
    /// <summary>Promotion reached its end date or redemption cap. Terminal.</summary>
    Expired,
    /// <summary>Promotion permanently ended early by admin. Terminal.</summary>
    Cancelled
}

/// <summary>
/// Phase 1 discount types.
/// </summary>
public enum DiscountType
{
    PercentageOff,
    FixedAmountOff,
    FreeShipping
}

/// <summary>
/// How the discount is applied to the order.
/// </summary>
public enum DiscountApplication
{
    /// <summary>Discount applied once to the entire order.</summary>
    PerOrder,
    /// <summary>Discount applied to each eligible line item individually.</summary>
    PerEligibleItem
}

/// <summary>
/// What items the promotion targets.
/// </summary>
public enum PromotionScope
{
    /// <summary>Promotion applies to all items in the order.</summary>
    AllItems,
    /// <summary>Promotion applies only to specified categories.</summary>
    ByCategory,
    /// <summary>Promotion applies only to specific SKUs.</summary>
    BySpecificSkus
}
```

### Promotion Invariants

| # | Invariant | Enforcement Point | Error Response |
|---|-----------|-------------------|----------------|
| 1 | Status transitions must follow valid paths (no Expired→Active, no Cancelled→anything) | `Before()` in each handler | 400 Bad Request |
| 2 | `StartsAt < EndsAt` | `SchedulePromotionValidator` | 422 Validation Error |
| 3 | PercentageOff: `0 < DiscountValue ≤ 100` | `ConfigureDiscountRulesValidator` | 422 Validation Error |
| 4 | FixedAmountOff: `DiscountValue > 0` | `ConfigureDiscountRulesValidator` | 422 Validation Error |
| 5 | `CurrentRedemptionCount ≤ RedemptionCap` (when cap set) | `RecordRedemptionHandler.Before()` | Marten ConcurrencyException + retry |
| 6 | Cannot modify Active/Expired/Cancelled promotion (only Pause, Cancel, or record redemption) | `Before()` in config handlers | 400 Bad Request |
| 7 | Scope must be non-empty (at least one SKU, category, or AllItems) | `ConfigureScopeValidator` | 422 Validation Error |
| 8 | If `RequiresCouponCode = true`, at least one batch must exist before activation | `ActivatePromotionHandler.Before()` | 400 Bad Request |
| 9 | Every mutating command carries a non-empty actor Guid (`CreatedBy`, `ActivatedBy`, etc.) | FluentValidation on each command | 422 Validation Error |
| 10 | Stale scheduled message guard: `ScheduleId` must match `PendingScheduleId` | `ActivatePromotionHandler.Before()`, `ExpirePromotionHandler.Before()` | No-op (discard) |

### Valid State Transitions

```
Draft       → Scheduled   (via SchedulePromotion)
Draft       → Active      (via ActivatePromotion, immediate)
Draft       → Cancelled   (via CancelPromotion)
Scheduled   → Active      (via ActivatePromotion, scheduled trigger)
Scheduled   → Cancelled   (via CancelPromotion)
Active      → Paused      (via PausePromotion)
Active      → Expired     (via ExpirePromotion or RedemptionCapReached)
Active      → Cancelled   (via CancelPromotion)
Paused      → Active      (via ResumePromotion)
Paused      → Cancelled   (via CancelPromotion)
Paused      → Expired     (via ExpirePromotion — scheduled message fires while paused)
Expired     → (terminal, no transitions)
Cancelled   → (terminal, no transitions)
```

---

## Aggregate 2: `Coupon`

**Stream ID:** UUID v5 derived from `"promotions:{couponCode.ToUpperInvariant()}"` (deterministic, case-insensitive, follows Pricing BC pattern)  
**Identity:** `StreamIdentity.AsGuid`  
**Pattern:** Marten event-sourced aggregate with `Create()`/`Apply()`

### State Shape

```csharp
namespace Promotions.Coupons;

/// <summary>
/// Event-sourced aggregate representing a single redeemable coupon code.
/// Separate from Promotion because:
/// 1. Independent lifecycle (issued → reserved → redeemed OR expired/revoked)
/// 2. Own concurrency boundary (high-traffic validation)
/// 3. Batch generation creates thousands — cannot be part of Promotion stream
/// </summary>
public sealed record Coupon(
    Guid Id,
    string CouponCode,
    Guid PromotionId,
    Guid? BatchId,
    CouponStatus Status,
    int MaxUses,
    int MaxUsesPerCustomer,
    int CurrentUseCount,
    IReadOnlyList<CouponRedemption> Redemptions,
    Guid? ReservedByCartId,
    DateTimeOffset? ReservedUntil,
    DateTimeOffset IssuedAt)
{
    public bool IsTerminal =>
        Status is CouponStatus.Redeemed or CouponStatus.Expired or CouponStatus.Revoked;

    public bool IsAvailable =>
        Status == CouponStatus.Active && CurrentUseCount < MaxUses;

    public bool IsReservedByOther(Guid cartId) =>
        ReservedByCartId.HasValue
        && ReservedByCartId.Value != cartId
        && ReservedUntil.HasValue
        && ReservedUntil.Value > DateTimeOffset.UtcNow;

    public bool HasCustomerExceededLimit(Guid customerId) =>
        Redemptions.Count(r => r.CustomerId == customerId) >= MaxUsesPerCustomer;

    /// <summary>
    /// Deterministic stream ID from coupon code.
    /// Follows Pricing BC pattern (UUID v5 from natural key).
    /// </summary>
    public static Guid StreamIdFromCode(string couponCode) =>
        GuidV5.Create("promotions", couponCode.Trim().ToUpperInvariant());

    // ─── Factory ────────────────────────────────────────────────────

    public static Coupon Create(CouponIssued evt) =>
        new(Id: evt.CouponId,
            CouponCode: evt.CouponCode,
            PromotionId: evt.PromotionId,
            BatchId: evt.BatchId,
            Status: CouponStatus.Active,
            MaxUses: evt.MaxUses,
            MaxUsesPerCustomer: evt.MaxUsesPerCustomer,
            CurrentUseCount: 0,
            Redemptions: [],
            ReservedByCartId: null,
            ReservedUntil: null,
            IssuedAt: evt.IssuedAt);

    // ─── Apply Methods ──────────────────────────────────────────────

    public Coupon Apply(CouponReserved evt) =>
        this with
        {
            ReservedByCartId = evt.CartId,
            ReservedUntil = evt.ReservedUntil
        };

    public Coupon Apply(CouponReservationReleased evt) =>
        this with
        {
            ReservedByCartId = null,
            ReservedUntil = null
        };

    public Coupon Apply(CouponRedeemed evt) =>
        this with
        {
            CurrentUseCount = evt.NewUseCount,
            Status = evt.NewUseCount >= MaxUses ? CouponStatus.Redeemed : Status,
            Redemptions = [..Redemptions, new CouponRedemption(
                evt.CustomerId, evt.OrderId, evt.RedeemedAt)],
            ReservedByCartId = null,
            ReservedUntil = null
        };

    public Coupon Apply(CouponExpired evt) =>
        this with { Status = CouponStatus.Expired };

    public Coupon Apply(CouponRevoked evt) =>
        this with { Status = CouponStatus.Revoked };
}
```

### Embedded Value Objects

```csharp
namespace Promotions.Coupons;

public sealed record CouponRedemption(
    Guid CustomerId,
    Guid OrderId,
    DateTimeOffset RedeemedAt);
```

### UUID v5 Generator

```csharp
namespace Promotions;

/// <summary>
/// UUID v5 generator (name-based, SHA-1) for deterministic stream IDs.
/// Same pattern as Pricing BC's deterministic SKU-based IDs.
/// </summary>
public static class GuidV5
{
    private static readonly Guid PromotionsNamespace =
        new("a8e1d7c2-4f3b-5e6a-9d0c-1b2e3f4a5b6c");

    public static Guid Create(string prefix, string name)
    {
        var combined = $"{prefix}:{name}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(combined);
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var hash = sha1.ComputeHash(
            [..PromotionsNamespace.ToByteArray(), ..bytes]);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // Version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // Variant RFC 4122
        return new Guid(hash[..16]);
    }
}
```

### Owned Events

```csharp
namespace Promotions.Coupons;

public sealed record CouponIssued(
    Guid CouponId,
    string CouponCode,
    Guid PromotionId,
    Guid? BatchId,
    int MaxUses,
    int MaxUsesPerCustomer,
    DateTimeOffset IssuedAt);

public sealed record CouponReserved(
    string CouponCode,
    Guid CartId,
    Guid? CustomerId,
    DateTimeOffset ReservedAt,
    DateTimeOffset ReservedUntil);

public sealed record CouponReservationReleased(
    string CouponCode,
    Guid CartId,
    DateTimeOffset ReleasedAt);

public sealed record CouponRedeemed(
    string CouponCode,
    Guid OrderId,
    Guid CustomerId,
    int NewUseCount,
    DateTimeOffset RedeemedAt);

public sealed record CouponExpired(
    string CouponCode,
    Guid PromotionId,
    DateTimeOffset ExpiredAt);

public sealed record CouponRevoked(
    string CouponCode,
    string Reason,
    Guid RevokedBy,
    DateTimeOffset RevokedAt);
```

### Coupon Status Enum

```csharp
namespace Promotions.Coupons;

/// <summary>
/// Lifecycle states of a Coupon aggregate.
/// </summary>
public enum CouponStatus
{
    /// <summary>Coupon is available for use.</summary>
    Active,
    /// <summary>Coupon has reached its max uses. Terminal.</summary>
    Redeemed,
    /// <summary>Parent promotion expired. Terminal.</summary>
    Expired,
    /// <summary>Admin revoked the coupon. Terminal.</summary>
    Revoked
}
```

### Coupon Invariants

| # | Invariant | Enforcement Point | Error Response |
|---|-----------|-------------------|----------------|
| 1 | Cannot redeem beyond `MaxUses` (`CurrentUseCount < MaxUses`) | `RedeemCouponHandler.Before()` | Coupon fully redeemed — order proceeds at full price |
| 2 | Cannot redeem after parent Promotion expired/cancelled | `ValidateCouponHandler` checks `ActivePromotionsView` | HTTP 200 with `IsValid = false` |
| 3 | Revoked/Expired/Redeemed coupons are terminal | `Before()` in all mutation handlers | 400 Bad Request |
| 4 | Code uniqueness enforced by UUID v5 stream ID | Marten stream identity | `ConcurrencyException` if stream exists |
| 5 | Per-customer limit: `Redemptions.Count(c => c.CustomerId == X) < MaxUsesPerCustomer` | `ValidateCouponHandler` and `RedeemCouponHandler` | Validation error / order at full price |
| 6 | Reservation: cannot reserve if already reserved by another cart (unless expired) | `ValidateCouponHandler.Before()` | HTTP 200 with `IsValid = false, Reason = "Coupon is currently in use"` |

---

## Non-Aggregate: Discount Calculation Engine (Stateless)

**Not an aggregate.** Pure function with no state, no events, no stream.

```csharp
namespace Promotions.Discounts;

/// <summary>
/// Stateless discount calculation engine.
/// Pure function: (cart items, active promotions, coupons, floor prices) → discount breakdown.
/// Called at cart display time and checkout finalization.
/// No events emitted. No state stored.
/// </summary>
public static class DiscountCalculator
{
    public static DiscountBreakdown Calculate(
        IReadOnlyList<CartItemRef> items,
        IReadOnlyList<ActivePromotionsView> activePromotions,
        IReadOnlyList<CouponLookupView> appliedCoupons,
        IReadOnlyDictionary<string, decimal?> floorPrices,
        decimal shippingCost)
    {
        // Phase 1: No stacking — highest discount wins per item
        // 1. Evaluate auto-applied promotions (RequiresCouponCode = false)
        // 2. Evaluate coupon-linked promotions
        // 3. For each item, select best discount
        // 4. Clamp: ensure (UnitPrice - discount) >= FloorPrice
        // 5. Handle FreeShipping separately (overlay on shipping cost)
        // 6. Return breakdown

        // Implementation deferred to cycle plan
        throw new NotImplementedException();
    }
}

/// <summary>
/// Response from discount calculation. Immutable snapshot of computed discounts.
/// </summary>
public sealed record DiscountBreakdown(
    decimal TotalDiscount,
    decimal DiscountedShippingCost,
    IReadOnlyList<LineItemDiscount> LineItemDiscounts,
    IReadOnlyList<AppliedPromotionSummary> AppliedPromotions);

public sealed record LineItemDiscount(
    string Sku,
    decimal OriginalUnitPrice,
    decimal DiscountAmount,
    decimal EffectiveUnitPrice,
    Guid PromotionId,
    string PromotionName);

public sealed record AppliedPromotionSummary(
    Guid PromotionId,
    string Name,
    DiscountType DiscountType,
    decimal DiscountValue,
    string? CouponCode,
    decimal TotalDiscountApplied);
```

---

## Polecat Migration Considerations

| Concern | Current (Marten) Design | Polecat Migration Path |
|---------|-------------------------|------------------------|
| **Aggregates** | Sealed records with `Create()`/`Apply()` via JasperFx.Events interfaces | ✅ Direct port — JasperFx.Events is shared abstraction |
| **Events** | Plain sealed records, no Marten-specific attributes | ✅ Direct port — no infrastructure coupling |
| **Inline projections** | `MultiStreamProjection<T, TId>` base class | ✅ Shared JasperFx.Events base class |
| **Async projections** | Marten async daemon (`DaemonMode.Solo`) | ⚠️ Polecat has own async projection mechanism — requires config change |
| **Document store** | Marten JSONB documents for read models | ⚠️ Polecat uses SQL Server — EF Core read models instead |
| **Schema isolation** | `opts.DatabaseSchemaName = "promotions"` | ⚠️ SQL Server schema equivalent needed |
| **UUID v5 stream ID** | `GuidV5.Create()` in domain code | ✅ Infrastructure-agnostic, pure C# |
| **`DiscountCalculator`** | Pure function, no infrastructure dependency | ✅ Zero migration effort |

**Key design rule:** No PostgreSQL-specific JSONB queries in handler code. Use Marten's LINQ API (which JasperFx.Events also supports). All business logic in pure functions. Infrastructure at the edges only.

---
---

# Stage 6: Domain Events vs. Integration Messages

> Distinguish internal domain events from cross-BC integration messages. For each integration message, identify owning BC, direction, and integration strategy.

## Domain Events (Internal to Promotions BC)

These events are **private** to the Promotions event store. They are NOT published to other BCs. They drive aggregate state transitions and Marten projections.

### Promotion Stream Events

| Event | Purpose | Drives Projections |
|-------|---------|-------------------|
| `PromotionCreated` | Aggregate creation (Draft) | P1, P3, P5 |
| `DiscountRulesConfigured` | Discount type/value/application set | P1, P3, P5 |
| `PromotionScopeConfigured` | Target SKUs/categories set | P1 |
| `EligibilityRulesSet` | Min order amount, customer rules | P1 |
| `RedemptionLimitsSet` | Cap, per-customer limit, stacking | P1, P3 |
| `CouponBatchGenerated` | Batch metadata recorded | P3 |
| `PromotionScheduled` | Start/end dates + scheduled message ID | P1, P3, P5 |
| `PromotionActivated` | Transition to Active | P1, P3, P5 |
| `PromotionPaused` | Transition to Paused | P1, P3, P5 |
| `PromotionResumed` | Transition to Active (from Paused) | P1, P3, P5 |
| `RedemptionRecorded` | Increment count + track order | P1, P3, P4 |
| `RedemptionCapReached` | Cap hit, triggers expiry | P1, P3 |
| `PromotionExpired` | Terminal state | P1, P3, P5 |
| `PromotionCancelled` | Terminal state | P1, P3, P5 |

### Coupon Stream Events

| Event | Purpose | Drives Projections |
|-------|---------|-------------------|
| `CouponIssued` | Coupon created, ready for use | P2 |
| `CouponReserved` | Soft-hold during checkout | P2 |
| `CouponReservationReleased` | Hold released (timeout/removal) | P2 |
| `CouponRedeemed` | Order confirmed, coupon committed | P2, P4 |
| `CouponExpired` | Parent promotion expired | P2 |
| `CouponRevoked` | Admin revocation | P2 |

---

## Integration Messages (Cross-BC, in `Messages.Contracts`)

### Messages Published BY Promotions BC

| # | Message | Target BC(s) | Strategy | Queue | Trigger |
|---|---------|-------------|----------|-------|---------|
| I1 | `PromotionActivated` | Shopping, CE BFF | **Async RabbitMQ** | `promotion-lifecycle-events` | Domain event `PromotionActivated` |
| I2 | `PromotionExpired` | Shopping, CE BFF | **Async RabbitMQ** | `promotion-lifecycle-events` | Domain event `PromotionExpired` |
| I3 | `PromotionCancelled` | Shopping, CE BFF | **Async RabbitMQ** | `promotion-lifecycle-events` | Domain event `PromotionCancelled` |
| I4 | `PromotionPaused` | Shopping, CE BFF | **Async RabbitMQ** | `promotion-lifecycle-events` | Domain event `PromotionPaused` |
| I5 | `PromotionResumed` | Shopping, CE BFF | **Async RabbitMQ** | `promotion-lifecycle-events` | Domain event `PromotionResumed` |

### Messages Consumed BY Promotions BC

| # | Message | Source BC | Strategy | Queue | Handler |
|---|---------|----------|----------|-------|---------|
| I6 | `OrderPlaced` (with `AppliedPromotions`) | Orders | **Async RabbitMQ** | `promotions-order-placed` | `OrderPlacedHandler` → `RecordRedemption` + `RedeemCoupon` |

### Synchronous HTTP Contracts (Request/Response, NOT integration messages)

| # | Endpoint | Caller | Callee | Purpose |
|---|----------|--------|--------|---------|
| H1 | `POST /api/promotions/coupons/validate` | Shopping BC | Promotions BC | Coupon validation (customer waiting) |
| H2 | `POST /api/promotions/calculate-discount` | Shopping BC / Orders BC | Promotions BC | Discount calculation for cart/checkout |
| H3 | `GET /api/pricing/{sku}` | Promotions BC | Pricing BC | Floor price lookup (clamp discounts) |
| H4 | `GET /api/pricing/bulk?skus=X,Y,Z` | Promotions BC | Pricing BC | Bulk floor price lookup |

---

### Integration Message Records (in `src/Shared/Messages.Contracts/Promotions/`)

```csharp
namespace Messages.Contracts.Promotions;

/// <summary>
/// Integration message published by Promotions BC when a promotion goes live.
/// Consumed by Shopping BC (update cart-level badges, evaluate auto-applied promotions)
/// and Customer Experience BFF (display sale badges, toast notifications).
/// </summary>
public sealed record PromotionActivated(
    Guid PromotionId,
    string Name,
    string DiscountType,
    decimal DiscountValue,
    string DiscountApplication,
    string Scope,
    IReadOnlyList<string> IncludedSkus,
    IReadOnlyList<string> IncludedCategories,
    bool RequiresCouponCode,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateTimeOffset ActivatedAt);

/// <summary>
/// Integration message published by Promotions BC when a promotion reaches its end date
/// or redemption cap. Consumed by Shopping BC (remove auto-applied discounts from active
/// carts) and Customer Experience BFF (remove sale badges).
/// </summary>
public sealed record PromotionExpired(
    Guid PromotionId,
    string Name,
    string Reason,
    DateTimeOffset ExpiredAt);

/// <summary>
/// Integration message published by Promotions BC when an admin cancels a promotion.
/// Same consumers as PromotionExpired. Distinct event for audit trail.
/// </summary>
public sealed record PromotionCancelled(
    Guid PromotionId,
    string Name,
    string Reason,
    Guid CancelledBy,
    DateTimeOffset CancelledAt);

/// <summary>
/// Integration message published by Promotions BC when an admin pauses a promotion.
/// Shopping BC should stop applying this promotion to new carts.
/// Existing cart applications remain until cart expires or checkout completes.
/// </summary>
public sealed record PromotionPaused(
    Guid PromotionId,
    string Name,
    DateTimeOffset PausedAt);

/// <summary>
/// Integration message published by Promotions BC when an admin resumes a paused promotion.
/// Shopping BC can resume applying this promotion.
/// </summary>
public sealed record PromotionResumed(
    Guid PromotionId,
    string Name,
    DateTimeOffset ResumedAt);
```

### HTTP Contract Records (Sync Request/Response)

```csharp
namespace Promotions.Coupons;

/// <summary>
/// Response from coupon validation endpoint.
/// NOT an integration message — this is an HTTP response body.
/// </summary>
public sealed record CouponValidationResult(
    string CouponCode,
    bool IsValid,
    string? InvalidReason,
    Guid? PromotionId,
    string? PromotionName,
    string? DiscountType,
    decimal? DiscountValue,
    string? DiscountApplication,
    IReadOnlyList<string>? EligibleSkus);
```

---

## Integration Flow Diagram

```
                     ┌──────────────┐
                     │  Admin Portal│
                     │     (BFF)    │
                     └──────┬───────┘
                            │ HTTP commands
                            ▼
┌──────────┐  sync HTTP  ┌──────────────┐  async RabbitMQ  ┌──────────┐
│ Shopping  │◄───────────►│  Promotions  │────────────────►│  CE BFF  │
│    BC     │  validate   │     BC       │  lifecycle       │          │
│           │  calculate  │              │  events          │          │
└──────────┘              └──────┬───────┘                  └──────────┘
                                │ sync HTTP
                                ▼
                         ┌──────────────┐
                         │  Pricing BC  │
                         │ (floor price)│
                         └──────────────┘
                                ▲
     ┌──────────┐  async       │
     │ Orders   │──────────────┘
     │   BC     │ OrderPlaced
     └──────────┘ (RabbitMQ → Promotions)
```

---

## Critical Design Decisions Summary

| # | Decision | Chosen Option | Rationale |
|---|----------|---------------|-----------|
| 1 | Promotion stream ID | UUID v7 | Time-ordered, no natural key, follows new-aggregate convention |
| 2 | Coupon stream ID | UUID v5 from code | Deterministic lookup by code, case-insensitive, follows Pricing BC pattern |
| 3 | Coupon validation | Sync HTTP | Customer waiting for instant feedback — async unacceptable |
| 4 | Redemption recording | Async RabbitMQ | Order already placed, no customer waiting |
| 5 | Coupon reservation | Soft-hold with TTL on Coupon aggregate | Prevents double-validation race; fallback: order at full price |
| 6 | Floor price enforcement | At calculation time (sync HTTP to Pricing) | Runtime clamp, not design-time constraint |
| 7 | Stacking (Phase 1) | No stacking — highest discount wins | Simplicity; stacking engine deferred to Phase 2 |
| 8 | Category scope | Snapshot at creation time (resolved to SKU list) | Avoids cross-BC coupling; known limitation for Phase 1 |
| 9 | Promotion auto-activation | Wolverine `ScheduleAsync` with stale-message guard | Proven pattern (Returns BC `ExpireReturn`, Pricing BC scheduled price) |
| 10 | Coupon cascade expiry | Fan-out via `OutgoingMessages` | Avoids massive single transaction for promotions with thousands of coupons |
| 11 | Polecat | Design infrastructure-agnostic; ADR before adoption | Aggregates use JasperFx.Events interfaces; no PostgreSQL-specific queries in handlers |
| 12 | Money VO | Copy from Pricing BC (Phase 1) | BC isolation; extract to shared kernel if third BC needs it |
| 13 | Port allocation | 5249 | Next sequential port per CLAUDE.md convention |

---

## Appendix: Mapping Domain Events → Integration Messages

Not every domain event becomes an integration message. Most events are private to the Promotions event store. Only **lifecycle transitions that other BCs need to react to** cross the boundary.

| Domain Event | Published as Integration Message? | Why / Why Not |
|---|---|---|
| `PromotionCreated` | ❌ No | Draft promotions are internal; no external BC cares about drafts |
| `DiscountRulesConfigured` | ❌ No | Configuration detail; only matters at calculation time |
| `PromotionScopeConfigured` | ❌ No | Configuration detail |
| `EligibilityRulesSet` | ❌ No | Configuration detail |
| `RedemptionLimitsSet` | ❌ No | Configuration detail |
| `CouponBatchGenerated` | ❌ No | Internal batch tracking |
| `PromotionScheduled` | ❌ No | Not yet active; Shopping BC doesn't need to know until activation |
| `PromotionActivated` | ✅ **Yes** | Shopping BC updates badges, CE BFF shows notifications |
| `PromotionPaused` | ✅ **Yes** | Shopping BC stops applying this promotion |
| `PromotionResumed` | ✅ **Yes** | Shopping BC resumes applying |
| `RedemptionRecorded` | ❌ No | Internal bookkeeping |
| `RedemptionCapReached` | ❌ No | Triggers `PromotionExpired` which IS published |
| `PromotionExpired` | ✅ **Yes** | Shopping BC removes discounts, CE BFF removes badges |
| `PromotionCancelled` | ✅ **Yes** | Same as Expired but with admin attribution |
| `CouponIssued` | ❌ No | Internal coupon lifecycle |
| `CouponReserved` | ❌ No | Internal reservation tracking |
| `CouponReservationReleased` | ❌ No | Internal |
| `CouponRedeemed` | ❌ No | Internal; Orders BC already knows about the discount |
| `CouponExpired` | ❌ No | Cascading from promotion expiry; Shopping already got `PromotionExpired` |
| `CouponRevoked` | ❌ No | Internal admin action; no external BC reaction needed |

---

*Stages 2–6 complete. This event model is ready for implementation planning and cycle scoping.*

---

# Stage 7: Sagas and Workflows

## 7.1 Saga Candidate Analysis

Three workflow candidates were identified during Stages 2–6. Each is evaluated below for whether a full Wolverine `Saga` is warranted or a simpler pattern suffices.

### Candidate 1: Promotion Scheduling (Activate at StartsAt, Expire at EndsAt)

**Verdict: ❌ No Saga — Use Wolverine Scheduled Messages with Stale-Message Guards**

**Why not a saga?**
- Only two scheduled points in time (start, end) — no intermediate states or compensating actions
- No coordination across multiple aggregates or BCs
- The Promotion aggregate itself tracks `Status` and `ActivationScheduleId` / `ExpirationScheduleId`
- Stale-message guard pattern (compare `ScheduleId` on arrival) is proven in Returns BC (`ExpireReturn`) and Pricing BC (`ExpirePriceRule`)

**Pattern:**
```
SchedulePromotion command
  → Promotion.Apply(PromotionScheduled) stores ActivationScheduleId + ExpirationScheduleId
  → Handler returns:
      ScheduleAsync(new ActivatePromotion(PromotionId, ActivationScheduleId), StartsAt)

ActivatePromotion arrives at StartsAt
  → [WriteAggregate] handler loads Promotion
  → Guard: if (promotion.ActivationScheduleId != command.ScheduleId) return [];  // stale
  → Promotion.Apply(PromotionActivated) stores ExpirationScheduleId
  → Handler returns:
      Events([new PromotionActivated(...)]),
      OutgoingMessages([
        new ScheduleMessage(new ExpirePromotion(PromotionId, ExpirationScheduleId), EndsAt),
        new Contracts.PromotionActivated(...)  // integration message → RabbitMQ
      ])

ExpirePromotion arrives at EndsAt
  → [WriteAggregate] handler loads Promotion
  → Guard: if (promotion.ExpirationScheduleId != command.ScheduleId) return [];  // stale
  → Promotion.Apply(PromotionExpired)
  → Handler returns:
      Events([new PromotionExpired(...)]),
      OutgoingMessages([
        new Contracts.PromotionExpired(...),    // integration message → RabbitMQ
        ...coupons.Select(c => new ExpireCoupon(c.Code))  // fan-out
      ])
```

**Risk:** If the Promotions service is down at the scheduled time, Wolverine's durable inbox will deliver the message when it recovers. The stale-message guard ensures idempotency if the promotion was already cancelled/paused.

---

### Candidate 2: Coupon Batch Generation

**Verdict: ❌ No Saga — Use Handler Fan-Out via `OutgoingMessages`**

**Why not a saga?**
- All `IssueCoupon` commands target independent Coupon aggregates — no coordination needed
- No compensating actions (if one coupon fails to issue, the others are still valid)
- No external BC interaction
- The `CouponBatchGenerated` event on the Promotion aggregate records the batch metadata (count, prefix, batchId)
- Individual `IssueCoupon` commands are idempotent (UUID v5 from code ensures deterministic IDs)

**Pattern:**
```
GenerateCouponBatch command
  → [WriteAggregate] handler loads Promotion
  → Generates coupon codes: "{Prefix}-{random}" × Count
  → Returns:
      Events([new CouponBatchGenerated(PromotionId, BatchId, Count, Prefix, ...)]),
      OutgoingMessages(codes.Select(code =>
        new IssueCoupon(code, PromotionId, BatchId, MaxUsesPerCoupon, MaxUsesPerCustomer)
      ))
```

**Scalability consideration:** For very large batches (10,000+ coupons), the `OutgoingMessages` collection could be large. Wolverine's durable outbox handles this — messages are persisted to the outbox table and dispatched asynchronously. If this proves problematic at scale, we can chunk into batches of 500 using a simple recursive pattern:

```
GenerateCouponBatchChunk(Guid PromotionId, Guid BatchId, int RemainingCount, int ChunkSize, string Prefix, ...)
  → Issues min(RemainingCount, ChunkSize) coupons
  → If RemainingCount > ChunkSize, enqueues another GenerateCouponBatchChunk with RemainingCount - ChunkSize
```

This is still not a saga — just recursive message dispatch. Defer chunking to Phase 2 if batch sizes stay under 1,000.

---

### Candidate 3: Coupon Reservation Lifecycle (Reserve → Redeem or Release)

**Verdict: ❌ No Saga — Use Scheduled Message for TTL Expiry**

**Why not a saga?**
- Only two outcomes: redeem or release — no multi-step coordination
- The Coupon aggregate tracks reservation state directly (`ReservedBy`, `ReservedUntil`)
- TTL-based auto-release uses the same scheduled message pattern as promotion activation

**Pattern:**
```
ValidateCoupon (sync HTTP from Shopping BC)
  → [WriteAggregate] handler loads Coupon
  → If valid: Apply(CouponReserved) with ReservedUntil = now + 15 minutes
  → Returns:
      Events([new CouponReserved(...)]),
      OutgoingMessages([
        new ScheduleMessage(new ReleaseCouponReservation(CouponCode, CartId), ReservedUntil)
      ]),
      HTTP response: CouponValidationResult { IsValid = true, ... }

ReleaseCouponReservation arrives at TTL
  → [WriteAggregate] handler loads Coupon
  → Guard: if coupon.Status != Reserved || coupon.ReservedByCartId != CartId → skip (already redeemed/released)
  → Apply(CouponReservationReleased)

RedeemCoupon (from OrderPlacedHandler)
  → [WriteAggregate] handler loads Coupon
  → Guard: if coupon.Status == Redeemed → skip (idempotent)
  → Apply(CouponRedeemed) — this implicitly cancels the pending release (guard on ReleaseCouponReservation will skip)
```

---

### Candidate 4: Redemption Recording (Cross-Aggregate Coordination)

**Verdict: ❌ No Saga — Use Handler Cascading Commands**

When `OrderPlaced` arrives from Orders BC, the handler must update both the Promotion aggregate (increment redemption count) and the Coupon aggregate (mark redeemed). These are independent aggregates.

**Why not a saga?**
- Two independent aggregate updates with no rollback requirement
- If one fails, Wolverine retry policy handles it
- No ordering dependency — `RecordRedemption` and `RedeemCoupon` can execute in parallel

**Pattern:**
```csharp
// OrderPlacedHandler.cs — cascading handler
public static OutgoingMessages Handle(Contracts.OrderPlaced message)
{
    var messages = new OutgoingMessages();

    foreach (var appliedPromo in message.AppliedPromotions)
    {
        messages.Add(new RecordRedemption(
            appliedPromo.PromotionId,
            message.OrderId,
            message.CustomerId,
            appliedPromo.CouponCode,
            appliedPromo.DiscountApplied,
            message.PlacedAt));

        if (appliedPromo.CouponCode is not null)
        {
            messages.Add(new RedeemCoupon(
                appliedPromo.CouponCode,
                message.OrderId,
                message.CustomerId));
        }
    }

    return messages;
}
```

---

## 7.2 Saga Decision Summary

| Candidate | Saga? | Pattern Used | Rationale |
|-----------|-------|-------------|-----------|
| Promotion Scheduling | ❌ No | Scheduled messages + stale-message guard | Two time points, no coordination, proven pattern |
| Coupon Batch Generation | ❌ No | Handler fan-out via `OutgoingMessages` | Independent aggregates, no compensation needed |
| Coupon Reservation Lifecycle | ❌ No | Scheduled message for TTL expiry | Two outcomes, aggregate tracks state directly |
| Redemption Recording | ❌ No | Handler cascading commands | Independent updates, no ordering dependency |

**Key Insight:** The Promotions BC does not require any Wolverine Sagas in Phase 1. All workflows decompose into simpler patterns: scheduled messages, handler fan-out, and cascading commands. This is a sign of well-bounded aggregate design — each aggregate owns its own state transitions without cross-aggregate coordination.

**Future Consideration:** If Phase 2 introduces stacking rules that require multi-promotion coordination (e.g., "apply best 2 of 5 eligible promotions"), a `DiscountResolutionSaga` may emerge. Defer until stacking requirements are concrete.

---

# Stage 8: Subscribers and Reactors

## 8.1 Who Subscribes TO Promotions BC Events?

These are integration messages published by the Promotions BC that other BCs consume.

### 8.1.1 Shopping BC → Subscribes to Promotion Lifecycle Events

| Integration Message | Trigger | Shopping BC Reaction | Transport |
|---|---|---|---|
| `PromotionActivated` | Promotion reaches `StartsAt` | Update local `ActivePromotionCache` projection; flag affected cart items for re-pricing; show promotional badges on applicable product pages | RabbitMQ (async) |
| `PromotionPaused` | Admin pauses promotion | Remove promotion from active cache; stop applying discount to new carts; existing carts keep discount until next recalculation | RabbitMQ (async) |
| `PromotionResumed` | Admin resumes promotion | Restore promotion to active cache; resume applying discount | RabbitMQ (async) |
| `PromotionExpired` | Promotion reaches `EndsAt` or cap reached | Remove promotion from active cache; recalculate any active carts that had this promotion applied | RabbitMQ (async) |
| `PromotionCancelled` | Admin cancels promotion | Same as Expired — remove and recalculate | RabbitMQ (async) |

**Shopping BC Handler Pattern:**
```
PromotionActivatedHandler → upsert ActivePromotionCache document
PromotionExpiredHandler → delete from ActivePromotionCache, enqueue RecalculateAffectedCarts
```

**Note:** Shopping BC maintains its own local read model of active promotions. It does NOT query Promotions BC for every cart operation. This is the standard event-driven denormalization pattern — Shopping owns a local copy of promotion data it needs.

**⚠️ Undefined Integration:** Shopping BC's `CouponApplied` event (listed in CONTEXTS.md as a future Phase 2 event) implies Shopping BC applies coupon codes to carts. The flow is:
1. Customer enters coupon code in cart UI
2. Shopping BC calls `POST /api/promotions/coupons/validate` (sync HTTP)
3. Promotions BC validates and reserves the coupon
4. Shopping BC records `CouponApplied` locally
5. At checkout, the coupon reservation is included in `CheckoutInitiated`

This sync HTTP call is already defined in Stage 3. No additional integration message needed.

---

### 8.1.2 Customer Experience BFF → Subscribes to Promotion Lifecycle Events

| Integration Message | CE BFF Reaction | Transport |
|---|---|---|
| `PromotionActivated` | Push notification to SignalR hub → storefront clients show promotional banner/badge; update `ActivePromotionsCompositionView` | RabbitMQ → SignalR |
| `PromotionPaused` | Remove promotional banner from storefront | RabbitMQ → SignalR |
| `PromotionResumed` | Restore promotional banner | RabbitMQ → SignalR |
| `PromotionExpired` | Remove promotional banner; show "Sale ended" toast if customer was viewing promoted product | RabbitMQ → SignalR |
| `PromotionCancelled` | Same as Expired | RabbitMQ → SignalR |

**CE BFF Handler Pattern:**
```csharp
// In Storefront/Notifications/PromotionNotificationHandler.cs
public static async Task Handle(
    Contracts.PromotionActivated message,
    IEventBroadcaster broadcaster)
{
    await broadcaster.BroadcastToAll(new PromotionStartedNotification(
        message.PromotionId, message.Name, message.DiscountType,
        message.DiscountValue, message.StartsAt, message.EndsAt));
}
```

**CE BFF also composes promotion data for product pages** via sync HTTP:
- `GET /api/promotions/applicable?skus=X,Y,Z` — called when rendering product detail pages to show "20% OFF" badges

---

### 8.1.3 Correspondence BC → Subscribes to Promotion Lifecycle Events (Future)

| Integration Message | Correspondence BC Reaction | Transport | Phase |
|---|---|---|---|
| `PromotionActivated` | Send "New Sale!" marketing email to opted-in customers | RabbitMQ (async) | Phase 2+ |
| `PromotionExpired` | Send "Last chance!" reminder email (if configured) | RabbitMQ (async) | Phase 2+ |

**⚠️ Undefined Integration:** CONTEXTS.md does not list Promotions → Correspondence integration. This should be added to CONTEXTS.md when Promotions BC is implemented. Phase 1 does NOT require this — marketing emails are a Phase 2+ concern.

---

### 8.1.4 Admin Portal BFF → Subscribes to Promotion Lifecycle Events (Future)

| Integration Message | Admin Portal Reaction | Transport | Phase |
|---|---|---|---|
| `PromotionActivated` | Update promotion dashboard; show "now live" status | RabbitMQ → SignalR | Phase 2+ |
| `PromotionExpired` | Update dashboard; show redemption summary | RabbitMQ → SignalR | Phase 2+ |
| `PromotionCancelled` | Update dashboard; alert ops team | RabbitMQ → SignalR | Phase 2+ |

**⚠️ Undefined Integration:** Admin Portal is a future BC (🟢 Low Priority in CONTEXTS.md). No implementation needed for Phase 1 — admin operations go directly to Promotions API endpoints.

---

### 8.1.5 Vendor Portal BFF → Subscribes to Promotion Events (Future Consideration)

**⚠️ Not in CONTEXTS.md.** Vendors may want visibility into promotions affecting their products (e.g., "Your product X is included in a 20% off sale"). This is a Phase 3+ concern. Flag for backlog.

---

## 8.2 What Does Promotions BC Subscribe TO from Other BCs?

### 8.2.1 Orders BC → Promotions BC

| Integration Message | Trigger in Orders BC | Promotions BC Reaction | Transport |
|---|---|---|---|
| `OrderPlaced` (with `AppliedPromotions` payload) | Order successfully placed with applied discounts | `OrderPlacedHandler` dispatches `RecordRedemption` + `RedeemCoupon` commands to update redemption counts and mark coupons redeemed | RabbitMQ (async) |

**Message Contract (inbound):**
```csharp
// In Messages.Contracts (shared)
public sealed record OrderWithPromotionPlaced(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<AppliedPromotionRef> AppliedPromotions,
    DateTimeOffset PlacedAt);

public sealed record AppliedPromotionRef(
    Guid PromotionId,
    string? CouponCode,
    decimal DiscountApplied);
```

**Cross-reference with CONTEXTS.md:** ✅ Orders BC publishes `OrderPlaced` → multiple BCs. The `AppliedPromotions` payload extension needs to be added to the existing `OrderPlaced` contract when Promotions BC is implemented.

---

### 8.2.2 Pricing BC → Promotions BC (Sync HTTP)

| Query | Trigger in Promotions BC | Purpose | Transport |
|---|---|---|---|
| `GET /api/pricing/{sku}` | `CalculateDiscount` handler | Fetch floor price (MAP) to clamp discount | Sync HTTP |
| `GET /api/pricing/bulk?skus=X,Y,Z` | `CalculateDiscount` handler (multiple items) | Batch floor price lookup | Sync HTTP |

**Cross-reference with CONTEXTS.md:** ✅ Pricing BC exposes price lookup endpoints. The `MAPViolationDetected` event published by Pricing BC is NOT consumed by Promotions BC — Promotions proactively queries floor prices at calculation time rather than reacting to violations.

**⚠️ Resilience pattern:** If Pricing BC is unavailable, the `CalculateDiscount` handler should:
1. Apply the discount without floor price clamping (optimistic)
2. Log a warning for ops review
3. OR: fail the calculation and return an error to the caller

**Decision:** Fail closed — return error if floor price cannot be verified. Rationale: applying a discount below MAP is a legal/contractual risk. Better to lose a sale than violate MAP agreements. This aligns with Pricing BC's `MAPViolationDetected` invariant.

---

### 8.2.3 Product Catalog BC → Promotions BC (NOT subscribed — by design)

**Why Promotions does NOT subscribe to Product Catalog events:**
- Promotion scope is resolved to SKU lists at creation time (Decision #8 from Stage 6)
- Category-to-SKU resolution happens once, not continuously
- If a new product is added to a category after a promotion is created, it is NOT automatically included
- This is a conscious trade-off: simplicity over dynamic scope, accepted limitation for Phase 1

**Future Phase 2 consideration:** If dynamic scope is needed, Promotions BC could subscribe to `ProductAdded` / `ProductCategorized` events and maintain a local `CategoryMembershipView` projection. This would require re-evaluating Decision #8.

---

### 8.2.4 Shopping BC → Promotions BC (Sync HTTP)

| Endpoint | Trigger in Shopping BC | Purpose | Transport |
|---|---|---|---|
| `POST /api/promotions/coupons/validate` | Customer enters coupon code in cart | Validate coupon, reserve if valid | Sync HTTP (request/response) |
| `POST /api/promotions/calculate-discount` | Cart recalculation (item added/removed, checkout) | Calculate applicable discounts for cart contents | Sync HTTP (request/response) |
| `GET /api/promotions/applicable?skus=X,Y,Z` | Product page rendering | Show promotional badges on eligible products | Sync HTTP (request/response) |

**Cross-reference with CONTEXTS.md:** ✅ Shopping BC makes sync calls to Promotions for real-time pricing. These are already defined in Stage 3.

---

## 8.3 Subscriber and Reactor Summary

### Outbound (Promotions → Other BCs)

| # | Message | Consumer BCs | Transport | Phase |
|---|---------|-------------|-----------|-------|
| R1 | `PromotionActivated` | Shopping, CE BFF | RabbitMQ | Phase 1 |
| R2 | `PromotionPaused` | Shopping, CE BFF | RabbitMQ | Phase 1 |
| R3 | `PromotionResumed` | Shopping, CE BFF | RabbitMQ | Phase 1 |
| R4 | `PromotionExpired` | Shopping, CE BFF | RabbitMQ | Phase 1 |
| R5 | `PromotionCancelled` | Shopping, CE BFF | RabbitMQ | Phase 1 |
| R6 | `PromotionActivated` | Correspondence | RabbitMQ | Phase 2+ |
| R7 | `PromotionActivated` | Admin Portal BFF | RabbitMQ | Phase 2+ |

### Inbound (Other BCs → Promotions)

| # | Message | Source BC | Transport | Handler | Phase |
|---|---------|----------|-----------|---------|-------|
| R8 | `OrderWithPromotionPlaced` | Orders | RabbitMQ | `OrderPlacedHandler` | Phase 1 |

### Sync HTTP (Promotions serves, other BCs call)

| # | Endpoint | Caller BC | Purpose | Phase |
|---|----------|-----------|---------|-------|
| R9 | `POST /api/promotions/coupons/validate` | Shopping | Coupon validation + reservation | Phase 1 |
| R10 | `POST /api/promotions/calculate-discount` | Shopping / Orders | Discount calculation | Phase 1 |
| R11 | `GET /api/promotions/applicable?skus=X,Y,Z` | CE BFF | Product page badges | Phase 1 |
| R12 | `GET /api/promotions/active` | CE BFF / Admin Portal | List all active promotions | Phase 1 |

### Sync HTTP (Promotions calls other BCs)

| # | Endpoint | Target BC | Purpose | Phase |
|---|----------|-----------|---------|-------|
| R13 | `GET /api/pricing/{sku}` | Pricing | Floor price (MAP) lookup | Phase 1 |
| R14 | `GET /api/pricing/bulk?skus=X,Y,Z` | Pricing | Batch floor price lookup | Phase 1 |

---

## 8.4 Undefined Integrations Flagged for CONTEXTS.md Update

| # | Integration | Status | Action Required |
|---|-------------|--------|----------------|
| F1 | Promotions → Correspondence (marketing emails) | ❌ Not in CONTEXTS.md | Add to Phase 2 integration table |
| F2 | Promotions → Admin Portal BFF (dashboard updates) | ❌ Not in CONTEXTS.md | Add when Admin Portal BC is scoped |
| F3 | Promotions → Vendor Portal BFF (vendor visibility) | ❌ Not in CONTEXTS.md | Backlog item — Phase 3+ |
| F4 | `OrderPlaced` contract extension with `AppliedPromotions` | ⚠️ Needs contract update | Extend existing `OrderPlaced` contract in Messages.Contracts |
| F5 | Shopping BC `CouponApplied` domain event | ⚠️ Listed as future in CONTEXTS.md | Confirm this is Shopping-internal, not an integration event |

---

# Stage 9: Naming Critique

## 9.1 Aggregate Names

| Name | Verdict | Notes |
|------|---------|-------|
| `Promotion` | ✅ **Keep** | Clear, ubiquitous language. Represents the full lifecycle of a promotional offer. Alternative "Discount" is too narrow (misses scheduling, eligibility, redemption tracking). |
| `Coupon` | ✅ **Keep** | Clear, universally understood. Represents a single redeemable code. Alternative "CouponCode" is redundant (a coupon IS a code). Alternative "Voucher" is regionally ambiguous. |

---

## 9.2 Domain Events

### Promotion Aggregate Events (14)

| # | Event Name | Verdict | Notes |
|---|-----------|---------|-------|
| 1 | `PromotionCreated` | ✅ Keep | Standard `{Aggregate}Created` pattern |
| 2 | `DiscountRulesConfigured` | ✅ Keep | "Configured" implies setup/draft phase — correct for multi-step creation |
| 3 | `PromotionScopeConfigured` | ✅ Keep | Consistent with "Configured" verb for setup events |
| 4 | `EligibilityRulesSet` | ⚠️ **Minor inconsistency** | Uses "Set" while others use "Configured". However, "Set" reads more naturally for rules. **Keep** — the distinction is meaningful: "Configured" = complex object, "Set" = simple properties. |
| 5 | `RedemptionLimitsSet` | ✅ Keep | Consistent with `EligibilityRulesSet` — "Set" for simple properties |
| 6 | `CouponBatchGenerated` | ✅ Keep | "Generated" is accurate — coupons are created in bulk |
| 7 | `PromotionScheduled` | ✅ Keep | Clear past-tense, captures the scheduling action |
| 8 | `PromotionActivated` | ✅ Keep | Clear lifecycle transition |
| 9 | `PromotionPaused` | ✅ Keep | Clear admin action |
| 10 | `PromotionResumed` | ✅ Keep | Pairs naturally with Paused |
| 11 | `RedemptionRecorded` | ✅ Keep | "Recorded" correctly implies bookkeeping (not the act of redeeming) |
| 12 | `RedemptionCapReached` | ✅ Keep | Descriptive — tells you exactly what happened. Past tense via "Reached". |
| 13 | `PromotionExpired` | ✅ Keep | Clear lifecycle transition |
| 14 | `PromotionCancelled` | ✅ Keep | Clear admin action. British spelling is fine — consistent within the BC. |

### Coupon Aggregate Events (6)

| # | Event Name | Verdict | Notes |
|---|-----------|---------|-------|
| 1 | `CouponIssued` | ✅ Keep | "Issued" correctly implies creation from an authority (Promotion) |
| 2 | `CouponReserved` | ✅ Keep | Domain-accurate — coupon is held temporarily during checkout |
| 3 | `CouponReservationReleased` | ⚠️ **Verbose but clear** | Alternative: `CouponReleased` — shorter but loses "reservation" context. **Keep as-is** — explicitness wins for event names that appear in event stores. |
| 4 | `CouponRedeemed` | ✅ Keep | Standard e-commerce term |
| 5 | `CouponExpired` | ✅ Keep | Clear lifecycle end |
| 6 | `CouponRevoked` | ✅ Keep | "Revoked" correctly implies admin action (vs. "Expired" which is time-based) |

**Summary:** All 20 domain event names pass review. No renames required.

---

## 9.3 Commands

### Promotion Commands (13)

| # | Command Name | Verdict | Notes |
|---|-------------|---------|-------|
| 1 | `CreatePromotion` | ✅ Keep | Standard `Create{Aggregate}` pattern |
| 2 | `ConfigureDiscountRules` | ✅ Keep | Imperative mood, clear intent |
| 3 | `ConfigureScope` | ⚠️ **Rename** → `ConfigurePromotionScope` | Ambiguous without aggregate prefix in message routing. Every other command includes context. |
| 4 | `SetEligibilityRules` | ✅ Keep | Consistent with event name |
| 5 | `SetRedemptionLimits` | ✅ Keep | Consistent with event name |
| 6 | `GenerateCouponBatch` | ✅ Keep | Clear, imperative |
| 7 | `SchedulePromotion` | ✅ Keep | Imperative mood, matches event |
| 8 | `ActivatePromotion` | ✅ Keep | Clear lifecycle command |
| 9 | `PausePromotion` | ✅ Keep | Clear admin action |
| 10 | `ResumePromotion` | ✅ Keep | Pairs with Pause |
| 11 | `CancelPromotion` | ✅ Keep | Clear admin action |
| 12 | `ExpirePromotion` | ✅ Keep | Internal scheduled command, not user-facing |
| 13 | `RecordRedemption` | ✅ Keep | "Record" correctly implies bookkeeping |

### Coupon Commands (6)

| # | Command Name | Verdict | Notes |
|---|-------------|---------|-------|
| 1 | `IssueCoupon` | ✅ Keep | Imperative, matches event |
| 2 | `ValidateCoupon` | ⚠️ **Consider** | This command both validates AND reserves. Name only captures half the behavior. However, from the caller's perspective (Shopping BC), they're "validating" — the reservation is an implementation detail. **Keep** — caller intent wins. |
| 3 | `ReleaseCouponReservation` | ✅ Keep | Explicit, matches event |
| 4 | `RedeemCoupon` | ✅ Keep | Standard e-commerce term |
| 5 | `ExpireCoupon` | ✅ Keep | Internal cascading command |
| 6 | `RevokeCoupon` | ✅ Keep | Admin action, matches event |

**Action item:** Rename `ConfigureScope` → `ConfigurePromotionScope` for consistency and routing clarity.

---

## 9.4 Queries

| # | Query Name | Verdict | Notes |
|---|-----------|---------|-------|
| 1 | `GetActivePromotions` | ✅ Keep | Clear, describes what's returned |
| 2 | `GetPromotionDetails` | ✅ Keep | Standard detail query |
| 3 | `GetApplicablePromotions` | ✅ Keep | "Applicable" correctly implies filtering by SKU eligibility |
| 4 | `CalculateDiscount` | ⚠️ **Consider** | This is both a query (returns data) AND has side effects (reserves coupons via handler). Strictly speaking it's a command. However, it's invoked via `POST` and the caller treats it as a calculation. **Keep** — pragmatic naming that matches the ubiquitous language. Document the side-effect clearly. |
| 5 | `GetPromotionCalendar` | ✅ Keep | Clear, descriptive |
| 6 | `GetCouponStatus` | ✅ Keep | Clear, returns coupon state |
| 7 | `GetPromotionRedemptions` | ✅ Keep | Clear, returns redemption history |

---

## 9.5 Projections (Read Models)

| # | Projection Name | Verdict | Notes |
|---|----------------|---------|-------|
| 1 | `ActivePromotionsView` | ✅ Keep | `{Adjective}{Aggregate}View` — clear that it's filtered to active promotions |
| 2 | `CouponLookupView` | ✅ Keep | "Lookup" correctly implies keyed access by coupon code |
| 3 | `CustomerRedemptionView` | ✅ Keep | Clear — tracks per-customer, per-promotion redemption data |
| 4 | `PromotionSummaryView` | ✅ Keep | "Summary" correctly implies aggregated metrics (redemption counts, totals) |
| 5 | `PromotionCalendarView` | ✅ Keep | Clear — temporal view for scheduling UI |

**Naming convention:** All projections use `{Context}View` suffix — consistent with Pricing BC patterns.

---

## 9.6 Integration Messages

| # | Message Name | Verdict | Notes |
|---|-------------|---------|-------|
| 1 | `PromotionActivated` (integration) | ⚠️ **Namespace disambiguation** | Same name as domain event. Must live in `Messages.Contracts` namespace. Code should reference as `Contracts.PromotionActivated`. This is the established CritterSupply pattern (e.g., Orders BC `OrderPlaced` domain event vs `Contracts.OrderPlaced`). **Keep** — namespace handles disambiguation. |
| 2 | `PromotionExpired` (integration) | ✅ Keep | Same namespace pattern as above |
| 3 | `PromotionCancelled` (integration) | ✅ Keep | Same namespace pattern |
| 4 | `PromotionPaused` (integration) | ✅ Keep | Same namespace pattern |
| 5 | `PromotionResumed` (integration) | ✅ Keep | Same namespace pattern |
| 6 | `OrderWithPromotionPlaced` (inbound) | ⚠️ **Consider** | This extends the existing `OrderPlaced` contract. Two options: (a) Extend `OrderPlaced` with optional `AppliedPromotions` list, or (b) Separate message `OrderWithPromotionPlaced`. Option (a) is cleaner — avoids message proliferation. **Recommend (a)** — extend `OrderPlaced` contract. |

---

## 9.7 Value Objects and Supporting Types

| Name | Verdict | Notes |
|------|---------|-------|
| `DiscountType` (enum) | ✅ Keep | `Percentage`, `FixedAmount`, `FreeShipping`, `BuyXGetY` |
| `DiscountApplication` (enum) | ✅ Keep | `PerItem`, `PerOrder` |
| `PromotionScope` (enum) | ✅ Keep | `StoreWide`, `ByCategory`, `BySku` |
| `PromotionStatus` (enum) | ✅ Keep | `Draft`, `Scheduled`, `Active`, `Paused`, `Expired`, `Cancelled` |
| `CouponStatus` (enum) | ✅ Keep | `Active`, `Reserved`, `Redeemed`, `Expired`, `Revoked` |
| `CartItemRef` (record) | ✅ Keep | Lightweight DTO for cross-BC communication |
| `Money` (value object) | ✅ Keep | Copied from Pricing BC — standard financial VO |
| `DiscountCalculator` | ✅ Keep | Pure function, stateless — name clearly describes purpose |
| `CouponValidationResult` | ✅ Keep | HTTP response DTO — clear intent |

---

## 9.8 Naming Critique Summary

| Category | Total | ✅ Keep | ⚠️ Action |
|----------|-------|---------|-----------|
| Aggregates | 2 | 2 | 0 |
| Domain Events | 20 | 20 | 0 (minor notes, no renames) |
| Commands | 19 | 18 | 1 rename: `ConfigureScope` → `ConfigurePromotionScope` |
| Queries | 7 | 7 | 0 (document `CalculateDiscount` side-effect) |
| Projections | 5 | 5 | 0 |
| Integration Messages | 6 | 5 | 1 recommendation: extend `OrderPlaced` instead of separate message |
| Value Objects | 9 | 9 | 0 |

**Overall:** Naming is strong. The ubiquitous language is consistent, events use past tense, commands use imperative mood, and projections use the `{Context}View` convention. Only one rename recommended.

---

# Stage 10: Risk Assessment and Implementation Phasing

## 10.1 Technical Risks

### Risk 1: Integration Complexity with Shopping BC (HIGH)

**Description:** Shopping BC must maintain a local cache of active promotions AND make sync HTTP calls to Promotions BC for coupon validation and discount calculation. This creates two integration vectors — async events for cache updates and sync HTTP for real-time operations.

**Concerns:**
- Shopping BC's cart recalculation must handle Promotions BC being unavailable (sync HTTP dependency)
- Stale promotion cache could apply expired promotions (eventual consistency lag)
- Coupon reservation TTL (15 min) must align with Shopping BC's checkout flow timing

**Mitigations:**
1. Circuit breaker on sync HTTP calls — degrade gracefully (show cart without discount, prompt retry)
2. Aggressive inline projection for `ActivePromotionsView` ensures near-zero read lag within Promotions BC
3. Shopping BC's local `ActivePromotionCache` is eventually consistent but always behind the "truth" in Promotions BC — sync HTTP call at checkout is the final authority
4. Coupon reservation TTL is configurable per deployment; default 15 min with monitoring

**Risk Level:** HIGH — this is the most complex integration in the system. Needs thorough integration testing with Testcontainers.

---

### Risk 2: OrderPlaced Contract Extension (MEDIUM)

**Description:** The existing `OrderPlaced` integration message (published by Orders BC) must be extended with an `AppliedPromotions` payload. This is a breaking change to an existing contract consumed by multiple BCs (Inventory, Payments, Fulfillment).

**Concerns:**
- Existing consumers must tolerate the new optional field
- If `AppliedPromotions` is null/empty, existing behavior must be unchanged
- Schema versioning strategy for the contract

**Mitigations:**
1. Add `AppliedPromotions` as `IReadOnlyList<AppliedPromotionRef>?` (nullable, backward-compatible)
2. Existing consumers ignore fields they don't need (standard JSON deserialization behavior)
3. Deploy Promotions BC AFTER contract extension is deployed and verified in Orders BC
4. Integration test: verify `OrderPlaced` without promotions still works for all existing consumers

**Risk Level:** MEDIUM — well-understood pattern, but requires coordinated deployment.

---

### Risk 3: Polecat / JasperFx.Events Migration (MEDIUM)

**Description:** The Promotions BC is designed infrastructure-agnostic per Decision #11, but the potential migration from Marten to Polecat (JasperFx.Events) during or after implementation could require rework.

**Concerns:**
- Aggregate interface compatibility (`IAggregate<T>` vs potential new interfaces)
- Projection registration patterns may change
- Testing infrastructure (TestContainers configuration) may differ

**Mitigations:**
1. Aggregates use `JasperFx.Events` interfaces where available (already decided in Stage 6)
2. No PostgreSQL-specific queries in handlers — all data access through Marten abstractions
3. ADR required before any Polecat adoption — captures migration plan and risk assessment
4. Promotions BC can be the "pilot" for Polecat if timing aligns, since it's greenfield

**Risk Level:** MEDIUM — mitigated by infrastructure-agnostic design, but timeline uncertainty remains.

---

### Risk 4: Concurrency on Redemption Recording (MEDIUM)

**Description:** High-traffic promotions (e.g., flash sales) could see many simultaneous `RecordRedemption` commands hitting the same Promotion aggregate, causing optimistic concurrency conflicts.

**Concerns:**
- Marten's optimistic concurrency throws `ConcurrencyException` on version conflict
- Wolverine retry policy helps, but under extreme load, retries compound the problem
- Redemption cap enforcement must be exact (not approximate)

**Mitigations:**
1. Wolverine retry policy: `.RetryOnce()` → `.RetryWithCooldown(100ms, 250ms)` → `.MoveToErrorQueue()`
2. `RedemptionCapReached` check happens inside the aggregate's `Apply` method — the cap is enforced at the event level, not the handler level
3. For extreme scenarios (>100 concurrent redemptions/second), consider moving redemption count to a Marten `FetchForWriting` with exclusive lock — but defer until proven necessary
4. Monitor `ConcurrencyException` rate in OpenTelemetry — alert if > 5% of redemption commands fail after retries

**Risk Level:** MEDIUM — Wolverine retry policy handles normal load; extreme load needs monitoring.

---

### Risk 5: Floor Price (MAP) Enforcement Availability (LOW)

**Description:** `CalculateDiscount` makes a sync HTTP call to Pricing BC for floor prices. If Pricing BC is unavailable, Promotions BC cannot verify MAP compliance.

**Concerns:**
- Discount below MAP is a contractual/legal risk
- Pricing BC unavailability blocks all discount calculations

**Mitigations:**
1. Decision: fail closed (return error if Pricing BC unavailable) — see Stage 8.2.2
2. Short-lived cache (5-minute TTL) for floor prices in Promotions BC — reduces call volume and provides brief resilience
3. Health check endpoint includes Pricing BC connectivity — ops team alerted immediately
4. Pricing BC is expected to have 99.9%+ uptime (it's a read-heavy service with inline projections)

**Risk Level:** LOW — fail-closed strategy eliminates legal risk; cache provides brief resilience.

---

### Risk 6: Event Versioning (LOW for Phase 1)

**Description:** Domain events are immutable once written to the event store. If event schemas need to change, versioning strategy is needed.

**Concerns:**
- Adding fields to existing events (backward-compatible)
- Removing or renaming fields (breaking change)
- Projection replay must handle all versions

**Mitigations:**
1. Phase 1: no versioning needed — greenfield BC, no existing events
2. Use nullable fields for future additions (standard Marten approach)
3. Establish event versioning ADR before Phase 2 if schema changes are needed
4. Marten supports event upcasting — transform old events to new schema during deserialization

**Risk Level:** LOW — not a concern for Phase 1 greenfield implementation.

---

## 10.2 Risk Summary Matrix

| # | Risk | Likelihood | Impact | Level | Phase |
|---|------|-----------|--------|-------|-------|
| R1 | Shopping BC integration complexity | High | High | **HIGH** | Phase 1 |
| R2 | OrderPlaced contract extension | Medium | Medium | **MEDIUM** | Phase 1 |
| R3 | Polecat migration | Low | High | **MEDIUM** | Phase 2+ |
| R4 | Redemption concurrency | Medium | Medium | **MEDIUM** | Phase 1 |
| R5 | Pricing BC availability | Low | Medium | **LOW** | Phase 1 |
| R6 | Event versioning | Low | Low | **LOW** | Phase 2+ |

---

## 10.3 Implementation Phasing

### Phase 1 — Cycle 29: Core Promotions Engine (4-5 weeks)

**Objective:** Stand up the Promotions BC with coupon validation, discount calculation, and basic promotion lifecycle — the minimum viable feature set that unblocks Shopping BC cart discounts.

**Deliverables:**

| # | Deliverable | Aggregates/Components | Priority |
|---|------------|----------------------|----------|
| 1 | **Project scaffolding** | Solution structure, `Program.cs`, Marten/Wolverine config, port 5249, Docker Compose, test project | P0 |
| 2 | **Promotion aggregate** (core lifecycle) | `CreatePromotion`, `ConfigureDiscountRules`, `ConfigurePromotionScope`, `SetEligibilityRules`, `SetRedemptionLimits` | P0 |
| 3 | **Promotion scheduling** | `SchedulePromotion`, `ActivatePromotion`, `ExpirePromotion` with scheduled messages | P0 |
| 4 | **Coupon aggregate** (full lifecycle) | `IssueCoupon`, `ValidateCoupon`, `RedeemCoupon`, `ExpireCoupon`, `CouponReserved`, `CouponReservationReleased` | P0 |
| 5 | **Coupon batch generation** | `GenerateCouponBatch` → fan-out `IssueCoupon` | P0 |
| 6 | **DiscountCalculator** (pure function) | Percentage, fixed amount, free shipping — no stacking | P0 |
| 7 | **Inline projections** | `ActivePromotionsView`, `CouponLookupView`, `CustomerRedemptionView` | P0 |
| 8 | **Sync HTTP endpoints** | `POST /validate`, `POST /calculate-discount`, `GET /active`, `GET /applicable` | P0 |
| 9 | **Floor price integration** | HTTP client to Pricing BC for MAP lookup | P1 |
| 10 | **Integration messages (outbound)** | `PromotionActivated`, `PromotionExpired`, `PromotionCancelled` → RabbitMQ | P1 |
| 11 | **Integration messages (inbound)** | `OrderWithPromotionPlaced` → `RecordRedemption` + `RedeemCoupon` | P1 |
| 12 | **Admin commands** | `PausePromotion`, `ResumePromotion`, `CancelPromotion`, `RevokeCoupon` | P1 |
| 13 | **Async projections** | `PromotionSummaryView`, `PromotionCalendarView` | P2 |
| 14 | **Query endpoints** | `GET /calendar`, `GET /{id}`, `GET /{id}/redemptions`, `GET /coupons/{code}` | P2 |
| 15 | **Integration tests** | Alba + Testcontainers for all HTTP endpoints and message handlers | P0 |

**Phase 1 exit criteria:**
- ✅ Shopping BC can validate coupons via sync HTTP
- ✅ Shopping BC can calculate discounts via sync HTTP
- ✅ Orders BC's `OrderPlaced` triggers redemption recording
- ✅ Promotion lifecycle events flow to RabbitMQ
- ✅ All 3 inline projections operational
- ✅ Integration tests pass with Testcontainers

**Estimated effort:** 4-5 weeks (aligns with Cycle 29 scope)

---

### Phase 2 — Cycle 30: Shopping BC Integration + Stacking Engine (3-4 weeks)

**Objective:** Wire up Shopping BC to consume promotion lifecycle events (local cache), implement BOGO discount type, and introduce basic stacking rules.

**Deliverables:**

| # | Deliverable | Notes |
|---|------------|-------|
| 1 | **Shopping BC: `ActivePromotionCache`** | Local read model updated from `PromotionActivated`/`Expired` events |
| 2 | **Shopping BC: `CouponApplied` event** | Cart captures applied coupon codes with reservation |
| 3 | **Shopping BC: Cart recalculation** | Recalculate discounts when promotions activate/expire |
| 4 | **BOGO discount type** | `BuyXGetY` in `DiscountCalculator` — most complex discount logic |
| 5 | **Basic stacking rules** | "Highest discount wins" → configurable stacking (mutual exclusion groups) |
| 6 | **CE BFF: Promotion notifications** | SignalR push for promotion banners, product badges |
| 7 | **CE BFF: Product page badges** | Show "20% OFF" on eligible product pages via `GET /applicable` |
| 8 | **OrderPlaced contract extension** | Add `AppliedPromotions` to existing `OrderPlaced` message |
| 9 | **End-to-end integration tests** | Full flow: create promo → activate → add to cart → checkout → redeem |

**Phase 2 exit criteria:**
- ✅ Customer sees promotional badges on product pages
- ✅ Customer can apply coupon codes in cart
- ✅ Discount persists through checkout to order placement
- ✅ Redemption recorded and cap enforced
- ✅ BOGO promotions work end-to-end

---

### Phase 3 — Cycle 31+: Advanced Features + Observability (2-3 weeks)

**Objective:** Polish, advanced discount scenarios, and operational tooling.

**Deliverables:**

| # | Deliverable | Notes |
|---|------------|-------|
| 1 | **Advanced stacking engine** | Configurable stacking rules, mutual exclusion groups, priority ordering |
| 2 | **Dynamic scope (optional)** | Subscribe to Product Catalog events for auto-updating category-based scopes |
| 3 | **Correspondence integration** | Marketing emails on promotion activation |
| 4 | **Admin Portal promotion management** | CRUD UI for promotions (when Admin Portal BC is scoped) |
| 5 | **Promotion analytics dashboard** | Redemption rates, revenue impact, coupon usage metrics via `PromotionSummaryView` |
| 6 | **Floor price cache** | Short-lived cache for Pricing BC floor prices (reduce sync HTTP calls) |
| 7 | **Load testing** | Verify redemption concurrency under realistic flash sale load |
| 8 | **Event versioning ADR** | Establish versioning strategy before any schema changes |
| 9 | **Polecat evaluation** | If Polecat is ready, evaluate migration for Promotions BC as pilot |

---

## 10.4 Dependency Graph

```
Phase 1 Prerequisites (must exist before Promotions BC):
  ├── Pricing BC (Cycles 22-23) ✅ — floor price API
  ├── Product Catalog BC (Cycles 21-22) ✅ — SKU data for scope configuration
  ├── Orders BC (Cycles 24-27) ✅ — OrderPlaced message source
  └── Shopping BC (Cycles 16-18) ✅ — primary consumer of Promotions API

Phase 1 (Cycle 29): Promotions BC Core
  └── All prerequisites met ✅

Phase 2 (Cycle 30): Shopping + CE BFF Integration
  ├── Depends on: Phase 1 complete
  ├── Depends on: Shopping BC handler additions (same cycle)
  └── Depends on: CE BFF handler additions (same cycle)

Phase 3 (Cycle 31+): Advanced Features
  ├── Depends on: Phase 2 complete
  ├── Optional: Admin Portal BC (not yet scoped)
  └── Optional: Correspondence BC Phase 2
```

---

## 10.5 Phase 1 Cycle 29 Task Breakdown

Recommended vertical slice ordering for Phase 1:

| Week | Slice | Description |
|------|-------|-------------|
| **Week 1** | Scaffolding + Promotion CRUD | Project structure, aggregate, 5 configuration commands, `ActivePromotionsView` inline projection, basic HTTP endpoints, first integration tests |
| **Week 2** | Coupon Lifecycle | Coupon aggregate, `IssueCoupon`, `ValidateCoupon`, `CouponLookupView`, coupon HTTP endpoints, batch generation fan-out |
| **Week 3** | Scheduling + Discount Calculation | Scheduled messages (activate/expire), `DiscountCalculator` pure function, `POST /calculate-discount`, floor price HTTP client to Pricing BC |
| **Week 4** | Integration Messages + Redemption | `PromotionActivated`/`Expired` → RabbitMQ, `OrderWithPromotionPlaced` handler, `RecordRedemption`, `RedeemCoupon`, `CustomerRedemptionView`, admin commands |
| **Week 5** | Polish + Async Projections + Tests | `PromotionSummaryView`, `PromotionCalendarView`, remaining query endpoints, comprehensive integration test suite, documentation updates to CONTEXTS.md |

---

## 10.6 Success Metrics

| Metric | Target | Measurement |
|--------|--------|-------------|
| Coupon validation latency (p99) | < 50ms | OpenTelemetry trace on `POST /validate` |
| Discount calculation latency (p99) | < 100ms | OpenTelemetry trace on `POST /calculate-discount` (includes Pricing BC call) |
| Redemption recording success rate | > 99.5% | `ConcurrencyException` rate in error queue |
| Promotion lifecycle event delivery | < 2s from activation to Shopping BC cache update | RabbitMQ consumer lag metric |
| Integration test coverage | All HTTP endpoints + all message handlers | Alba test count |
| Zero MAP violations | 0 discounts below floor price | `MAPViolationDetected` event count = 0 |

---

*Stages 7–10 complete. The Promotions BC event model is fully specified and ready for Cycle 29 implementation.*

