# M30.0 Promotions BC Redemption Workflow: Implementation Status

**Date:** 2026-03-15
**Status:** 🟡 In Progress — Phase 1A Complete
**Branch:** `claude/m30-0-redemption-workflow-integration`

---

## ✅ Completed: Phase 1A - Coupon Redemption Commands

### What Was Delivered

1. **RedeemCoupon Handler**
   - Command: `RedeemCoupon(CouponCode, OrderId, CustomerId, RedeemedAt)`
   - Handler uses `[WriteAggregate]` with optimistic concurrency
   - Enforces single-use constraint (must be Issued status)
   - Updated `CouponRedeemed` event with full fields
   - Updated `Coupon` aggregate Apply method + CustomerID property

2. **RevokeCoupon Handler**
   - Command: `RevokeCoupon(CouponCode, Reason)`
   - Admin action for fraud prevention/corrections
   - Can revoke Issued or Redeemed coupons
   - Cannot revoke already-revoked or expired coupons
   - Updated `CouponRevoked` event with full fields

3. **Build Status**
   - ✅ Promotions domain project builds successfully
   - ✅ All validators use FluentValidation
   - ✅ Follows CritterSupply patterns (IStartStream from M29.1, optimistic concurrency from Pricing BC)

---

## 📋 Remaining Work for M30.0

### Priority 1: Core Redemption Workflow (Essential)

#### 1.1 Promotion Redemption Tracking
- [ ] Create `PromotionRedemptionRecorded` event
- [ ] Add `CurrentRedemptionCount` to Promotion aggregate
- [ ] Update Promotion Apply method for redemption tracking
- [ ] Implement redemption cap enforcement (UsageLimit check)

#### 1.2 OrderPlaced Integration Handler
- [ ] Create `OrderPlacedHandler` in Promotions BC
- [ ] Subscribe to `OrderPlaced` integration message from Orders BC
- [ ] Handler fans out to:
  - `RedeemCoupon` command (if coupon applied)
  - Record redemption on Promotion aggregate
- [ ] Use `OutgoingMessages` pattern for command fan-out

#### 1.3 Calculate Discount Endpoint
- [ ] Create `CalculateDiscountRequest` model (CartItems, CouponCodes)
- [ ] Create `CalculateDiscountResponse` model (LineItemDiscounts, TotalDiscount)
- [ ] Implement `CalculateDiscountQuery` HTTP GET endpoint
- [ ] Pure function discount calculator logic
- [ ] Phase 1: Stub floor price check (return full discount)

### Priority 2: Shopping BC Integration (Critical Path)

#### 2.1 Shopping BC Changes
- [ ] Create `ApplyCouponToCart` command in Shopping BC
- [ ] Create `RemoveCouponFromCart` command in Shopping BC
- [ ] Update `ShoppingCart` aggregate with `AppliedCoupons` collection
- [ ] Add cart events: `CouponAppliedToCart`, `CouponRemovedFromCart`
- [ ] Update `CartView` to include discount information

#### 2.2 Shopping → Promotions HTTP Client
- [ ] Create `IPromotionsClient` interface in Shopping domain
- [ ] Implement `PromotionsClient` in Shopping.Api
- [ ] Wire up HttpClient in Shopping.Api Program.cs
- [ ] Call `ValidateCoupon` before applying to cart
- [ ] Call `CalculateDiscount` for cart display

### Priority 3: Pricing BC Integration (Floor Price Enforcement)

#### 3.1 Pricing Client
- [ ] Create `IPricingClient` interface in Promotions domain
- [ ] Create `CurrentPriceResponse` model (Price, FloorPrice)
- [ ] Implement `PricingClient` HTTP client in Promotions.Api
- [ ] Wire up HttpClient in Promotions.Api Program.cs

#### 3.2 Floor Price Enforcement
- [ ] Update `CalculateDiscount` to query Pricing BC for floor prices
- [ ] Implement clamping logic (never go below floor)
- [ ] Return clamped discount + original discount (for transparency)

### Priority 4: Batch Coupon Generation

#### 4.1 GenerateCouponBatch Command
- [ ] Create `GenerateCouponBatch(PromotionId, Prefix, Count, MaxUses)`
- [ ] Create validator
- [ ] Implement handler using fan-out pattern
- [ ] Return `OutgoingMessages` with N `IssueCoupon` commands
- [ ] Add `CouponBatchGenerated` event to Promotion aggregate

#### 4.2 Coupon Code Generation
- [ ] Implement deterministic code generator (e.g., `HOLIDAY2026-A3X9K`)
- [ ] Ensure uniqueness via UUID v5 collision detection
- [ ] Support configurable prefix + random suffix

### Priority 5: RabbitMQ Integration Messages

#### 5.1 PromotionActivated Message
- [ ] Create integration message in Messages.Contracts
- [ ] Publish when `ActivatePromotionHandler` succeeds
- [ ] Shopping BC subscribes → updates active promotions cache

#### 5.2 PromotionExpired Message
- [ ] Create integration message in Messages.Contracts
- [ ] Publish when promotion expires (scheduled or manual)
- [ ] Shopping BC subscribes → removes from active cache

### Priority 6: Projections & Queries

#### 6.1 ActivePromotionsView
- [ ] MultiStreamProjection listening to PromotionActivated/Expired
- [ ] Queryable view for customer-facing promotion discovery
- [ ] Include: Name, Description, DiscountType, DiscountValue, EndDate

#### 6.2 CustomerRedemptionView (Optional - Phase 2)
- [ ] Tracks which promotions a customer has used
- [ ] Enforces per-customer redemption limits

### Priority 7: Infrastructure & Configuration

#### 7.1 Docker Compose
- [ ] Add Promotions.Api service to docker-compose.yml
- [ ] Create `promotions` profile
- [ ] Map port 5250
- [ ] Set Postgres connection string (promotions schema)
- [ ] Set RabbitMQ connection

#### 7.2 Aspire AppHost
- [ ] Add Promotions.Api to AppHost.cs
- [ ] Add ProjectReference in CritterSupply.AppHost.csproj
- [ ] Wire to Postgres + RabbitMQ resources

### Priority 8: Testing & Documentation

#### 8.1 Integration Tests
- [ ] Test RedeemCoupon happy path
- [ ] Test RedeemCoupon double-redemption (optimistic concurrency)
- [ ] Test RevokeCoupon
- [ ] Test OrderPlacedHandler → RedeemCoupon flow
- [ ] Test CalculateDiscount with floor price clamping
- [ ] Test GenerateCouponBatch fan-out

#### 8.2 Documentation
- [ ] Update CONTEXTS.md with M30.0 integration contracts
- [ ] Update CURRENT-CYCLE.md to reflect M30.0 progress
- [ ] Create M30.0 retrospective document
- [ ] Update port allocation table (already done for 5250)

---

## 🚧 Deferred to M30.1+ (Not in Scope)

The following items from the event modeling document are explicitly deferred:

- **Scheduled Messages** — `ExpirePromotion` scheduled trigger (manual expiration only for M30.0)
- **Coupon Reservation** — Soft-hold when applied to cart (accept race condition for M30.0)
- **Advanced Stacking Rules** — Phase 1: No stacking (one promotion per order)
- **Category Scope** — Snapshot at creation time (no live Catalog BC subscription)
- **Money Value Object** — Using `decimal` for M30.0 (percentage discounts only)
- **Multi-Use Coupons** — Single-use only for M30.0

---

## 🎯 Recommended Implementation Order

Based on critical path and dependencies:

1. **Week 1: Core Redemption** (Priority 1)
   - PromotionRedemptionRecorded event
   - OrderPlacedHandler
   - CalculateDiscount endpoint (stub floor price)

2. **Week 2: Shopping Integration** (Priority 2)
   - ApplyCouponToCart / RemoveCouponFromCart
   - Shopping → Promotions HTTP client
   - CartView discount display

3. **Week 3: Pricing Integration + Batch Generation** (Priority 3 + 4)
   - IPricingClient + floor price enforcement
   - GenerateCouponBatch handler

4. **Week 4: Integration Messages + Projections** (Priority 5 + 6)
   - PromotionActivated/Expired RabbitMQ
   - ActivePromotionsView projection

5. **Week 5: Infrastructure + Testing** (Priority 7 + 8)
   - Docker Compose + Aspire
   - Integration test suite
   - Documentation updates

---

## 📝 Key Decisions Made

| Decision | Rationale | Source |
|----------|-----------|--------|
| UUID v5 for Coupon stream ID | Deterministic lookup without DB query | promotions-event-modeling.md:72-84 |
| Decimal for discount values | Simpler for percentage-only discounts | M29.1 retrospective, promotions-event-modeling.md:51 |
| No coupon reservation (Phase 1) | Accept race condition to reduce complexity | promotions-event-modeling.md:158-161 |
| Optimistic concurrency for redemption cap | Standard Marten pattern (no sequential queue) | All BCs use this pattern |
| Fan-out pattern for batch generation | One command → N IssueCoupon messages | promotions-event-modeling.md:1103-1109 |

---

## 📚 Key References

- **Event Model:** `docs/planning/promotions-event-modeling.md` (3687 lines, all 10 stages complete)
- **M29.1 Retrospective:** `docs/planning/cycles/cycle-29-phase-2-retrospective-notes.md`
- **Skill Files:**
  - `docs/skills/wolverine-message-handlers.md` (Compound handlers, return patterns)
  - `docs/skills/marten-event-sourcing.md` (Snapshot projections, optimistic concurrency)
  - `docs/skills/wolverine-sagas.md` (Not needed — redemption uses aggregates + handlers)
- **Pricing BC Reference:** `src/Pricing/Pricing/` (UUID v5 pattern, Money VO, floor price API)
- **Shopping BC Reference:** `src/Shopping/Shopping/` (Cart aggregate, coupon application pattern)

---

**Next Session Checklist:**
1. Read this status document first
2. Implement Priority 1 items (PromotionRedemptionRecorded + OrderPlacedHandler)
3. Build and test after each logical unit
4. Commit frequently with `(M30.0)` prefix
5. Update this status document as you progress
