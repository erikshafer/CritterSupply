# M30.0 Promotions BC Redemption Workflow: Implementation Status

**Date:** 2026-03-15
**Status:** 🟡 In Progress — Core Redemption Foundation Complete
**Branch:** `claude/m30-0-redemption-workflow-integration`

---

## 📌 Terminology Clarification

**Important:** This document tracks **M30.0** (single milestone) implementation progress.

- **M30.0** = Promotions BC Redemption Workflow (this milestone)
  - Scope: Core redemption commands, discount calculation stub, OrderPlaced handler skeleton
  - Outcome: Foundation for coupon redemption (awaits Shopping BC integration in M30.1+)

- **M30.1+** = Future milestones (deferred work)
  - M30.1: Shopping BC integration (ApplyCouponToCart, full OrderPlaced flow)
  - M30.2: Pricing BC integration (floor price enforcement)
  - M30.3+: Advanced features (batch generation UI, stacking rules, etc.)

**Notes within M30.0:**
- "Stub implementation" = Minimal working code to unblock future milestones
- "Skeleton handler" = Handler exists but doesn't process data yet (Shopping BC integration needed)
- References to "M30.1+ work" = Explicitly deferred to future milestones

See [ADR 0032: Milestone-Based Planning Schema](../decisions/0032-milestone-based-planning-schema.md) for details.

---

## ✅ Completed: Part 1 - Coupon Redemption Commands

### What Was Delivered (Commit 1)

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

3. **PromotionRedemptionRecorded Event + Aggregate Updates**
   - Event: `PromotionRedemptionRecorded(PromotionId, OrderId, CustomerId, CouponCode, RedeemedAt)`
   - Added `CurrentRedemptionCount` to Promotion aggregate
   - Added Apply method to increment count on each redemption

4. **Batch Coupon Generation**
   - Command: `GenerateCouponBatch(PromotionId, Prefix, Count)`
   - Handler uses fan-out pattern (returns tuple: event + OutgoingMessages)
   - Generates sequential codes: PREFIX-0001, PREFIX-0002, etc.
   - Max 10,000 coupons per batch (validator constraint)
   - Can only generate for Draft or Active promotions

---

## ✅ Completed: Part 2 - Discount Calculation &amp; OrderPlaced Handler

### What Was Delivered (Commits 2-3)

1. **CalculateDiscount Endpoint (HTTP POST /api/promotions/discounts/calculate)**
   - Request: `CalculateDiscountRequest(CartItems, CouponCodes)`
   - Response: `CalculateDiscountResponse(LineItemDiscounts, TotalDiscount, OriginalTotal, DiscountedTotal)`
   - Pure function percentage discount calculator
   - **M30.0 stub:** Allows full discount (floor price enforcement deferred to M30.2)
   - Returns zero discount for invalid/expired coupons
   - Single coupon constraint enforced by validator

2. **OrderPlacedHandler Infrastructure**
   - Handler: `OrderPlacedHandler` subscribes to `OrderPlaced` integration message
   - **M30.0 skeleton:** No coupon data in OrderPlaced yet (Shopping BC integration needed)
   - **M30.1+ work:** Will fan out to `RedeemCoupon` + `RecordPromotionRedemption` when Shopping BC adds coupon support

3. **RecordPromotionRedemption Handler**
   - Command: `RecordPromotionRedemption(PromotionId, OrderId, CustomerId, CouponCode, RedeemedAt)`
   - Handler uses `[WriteAggregate]` with optimistic concurrency
   - Enforces usage limit check (cannot exceed UsageLimit)
   - Enforces Active status check (cannot record on Draft/Paused/Expired)
   - Marten optimistic concurrency prevents redemption cap race condition

4. **Build Status**
   - ✅ Promotions domain project builds successfully (0 warnings, 0 errors)
   - ✅ Promotions.Api project builds successfully
   - ✅ All validators use FluentValidation
   - ✅ Follows CritterSupply patterns (IStartStream, optimistic concurrency, fan-out)

---

## 📋 Remaining Work for M30.0

### Priority 1: Core Redemption Workflow (Essential) ✅ COMPLETE

#### 1.1 Promotion Redemption Tracking ✅
- [x] Create `PromotionRedemptionRecorded` event
- [x] Add `CurrentRedemptionCount` to Promotion aggregate
- [x] Update Promotion Apply method for redemption tracking
- [x] Implement redemption cap enforcement (UsageLimit check)

#### 1.2 OrderPlaced Integration Handler ✅
- [x] Create `OrderPlacedHandler` in Promotions BC
- [x] Subscribe to `OrderPlaced` integration message from Orders BC
- [x] Handler fans out to:
  - `RedeemCoupon` command (if coupon applied)
  - Record redemption on Promotion aggregate
- [x] Use `OutgoingMessages` pattern for command fan-out
- **Note:** M30.0 skeleton only (Shopping BC integration needed for coupon data — deferred to M30.1+)

#### 1.3 Calculate Discount Endpoint ✅
- [x] Create `CalculateDiscountRequest` model (CartItems, CouponCodes)
- [x] Create `CalculateDiscountResponse` model (LineItemDiscounts, TotalDiscount)
- [x] Implement `CalculateDiscount` HTTP POST endpoint
- [x] Pure function discount calculator logic
- [x] Stub floor price check (return full discount — real enforcement deferred to M30.2)

### Priority 2: Shopping BC Integration ⚠️ DEFERRED TO M30.1+

**Note:** This work is **not** part of M30.0. It is explicitly deferred to milestone M30.1 (or later).

#### 2.1 Shopping BC Changes (M30.1)
- [ ] Create `ApplyCouponToCart` command in Shopping BC
- [ ] Create `RemoveCouponFromCart` command in Shopping BC
- [ ] Update `ShoppingCart` aggregate with `AppliedCoupons` collection
- [ ] Add cart events: `CouponAppliedToCart`, `CouponRemovedFromCart`
- [ ] Update `CartView` to include discount information

#### 2.2 Shopping → Promotions HTTP Client (M30.1)
- [ ] Create `IPromotionsClient` interface in Shopping domain
- [ ] Implement `PromotionsClient` in Shopping.Api
- [ ] Wire up HttpClient in Shopping.Api Program.cs
- [ ] Call `ValidateCoupon` before applying to cart
- [ ] Call `CalculateDiscount` for cart display

### Priority 3: Pricing BC Integration ⚠️ DEFERRED TO M30.2+

**Note:** This work is **not** part of M30.0. It is explicitly deferred to milestone M30.2 (or later).

#### 3.1 Pricing Client (M30.2)
- [ ] Create `IPricingClient` interface in Promotions domain
- [ ] Create `CurrentPriceResponse` model (Price, FloorPrice)
- [ ] Implement `PricingClient` HTTP client in Promotions.Api
- [ ] Wire up HttpClient in Promotions.Api Program.cs

#### 3.2 Floor Price Enforcement (M30.2)
- [ ] Update `CalculateDiscount` to query Pricing BC for floor prices
- [ ] Implement clamping logic (never go below floor)
- [ ] Return clamped discount + original discount (for transparency)

### Priority 4: Batch Coupon Generation ⚠️ PARTIALLY DEFERRED

**Note:** Core command/handler exist in M30.0 (Part 1). UI and advanced features deferred to M30.3+.

#### 4.1 GenerateCouponBatch Command ✅ COMPLETE (M30.0)
- [x] Create `GenerateCouponBatch(PromotionId, Prefix, Count)` command
- [x] Create validator (max 10,000 coupons per batch)
- [x] Implement handler using fan-out pattern
- [x] Return `OutgoingMessages` with N `IssueCoupon` commands
- [x] Add `CouponBatchGenerated` event to Promotion aggregate
- [x] Sequential code generation: PREFIX-0001, PREFIX-0002, etc.

#### 4.2 Coupon Code Generation (M30.3+ - UI & Advanced Features)
- [ ] Admin UI for batch generation
- [ ] Random suffix support (e.g., `HOLIDAY2026-A3X9K`)
- [ ] Batch generation progress tracking

### Priority 5: RabbitMQ Integration Messages ⚠️ DEFERRED TO M30.4+

**Note:** This work is **not** part of M30.0. Integration messages deferred to future milestones.

#### 5.1 PromotionActivated Message (M30.4)
- [ ] Create integration message in Messages.Contracts
- [ ] Publish when `ActivatePromotionHandler` succeeds
- [ ] Shopping BC subscribes → updates active promotions cache

#### 5.2 PromotionExpired Message (M30.4)
- [ ] Create integration message in Messages.Contracts
- [ ] Publish when promotion expires (scheduled or manual)
- [ ] Shopping BC subscribes → removes from active cache

### Priority 6: Projections & Queries ⚠️ DEFERRED TO M30.5+

**Note:** This work is **not** part of M30.0. Projections deferred to future milestones.

#### 6.1 ActivePromotionsView (M30.5)
- [ ] MultiStreamProjection listening to PromotionActivated/Expired
- [ ] Queryable view for customer-facing promotion discovery
- [ ] Include: Name, Description, DiscountType, DiscountValue, EndDate

#### 6.2 CustomerRedemptionView (M30.6 - Optional)
- [ ] Tracks which promotions a customer has used
- [ ] Enforces per-customer redemption limits

### Priority 7: Infrastructure & Configuration ⚠️ DEFERRED TO M30.7+

**Note:** This work is **not** part of M30.0. Infrastructure setup deferred to future milestones.

#### 7.1 Docker Compose (M30.7)
- [ ] Add Promotions.Api service to docker-compose.yml
- [ ] Create `promotions` profile
- [ ] Map port 5250
- [ ] Set Postgres connection string (promotions schema)
- [ ] Set RabbitMQ connection

#### 7.2 Aspire AppHost (M30.7)
- [ ] Add Promotions.Api to AppHost.cs
- [ ] Add ProjectReference in CritterSupply.AppHost.csproj
- [ ] Wire to Postgres + RabbitMQ resources

### Priority 8: Testing & Documentation (M30.0 In Progress)

#### 8.1 Integration Tests (M30.0)
- [ ] Test RedeemCoupon happy path
- [ ] Test RedeemCoupon double-redemption (optimistic concurrency)
- [ ] Test RevokeCoupon
- [ ] Test RecordPromotionRedemption usage limit enforcement
- [ ] Test CalculateDiscount stub (no floor price yet — M30.2)
- [ ] Test GenerateCouponBatch fan-out
- **Note:** Full OrderPlacedHandler → RedeemCoupon flow deferred to M30.1 (needs Shopping BC)

#### 8.2 Documentation (M30.0)
- [ ] Update CONTEXTS.md with M30.0 integration contracts
- [ ] Update CURRENT-CYCLE.md to reflect M30.0 progress
- [ ] Create M30.0 retrospective document
- [x] Clarify terminology (remove "Phase" references)

---

## 🚧 Deferred to M30.1+ (Out of Scope for M30.0)

The following items from the event modeling document are **explicitly deferred to future milestones**:

- **Shopping BC Integration (M30.1)** — ApplyCouponToCart, RemoveCouponFromCart, full OrderPlaced flow
- **Pricing BC Integration (M30.2)** — Real floor price enforcement via HTTP client
- **Batch Generation UI (M30.3)** — Admin interface for coupon batch creation
- **RabbitMQ Integration Messages (M30.4)** — PromotionActivated/Expired publication
- **ActivePromotionsView Projection (M30.5)** — Queryable view for customer-facing discovery
- **Scheduled Messages (M30.6+)** — `ExpirePromotion` scheduled trigger (manual expiration only in M30.0)
- **Coupon Reservation (M30.6+)** — Soft-hold when applied to cart (accept race condition in M30.0)
- **Advanced Stacking Rules (M30.7+)** — No stacking in M30.0 (one promotion per order)
- **Category Scope (M30.8+)** — Snapshot at creation time (no live Catalog BC subscription in M30.0)
- **Money Value Object (M30.8+)** — Using `decimal` in M30.0 (percentage discounts only)
- **Multi-Use Coupons (M30.9+)** — Single-use only in M30.0

---

## 🎯 M30.0 Scope Summary

**What's IN M30.0:**
- ✅ Core redemption commands (RedeemCoupon, RevokeCoupon, RecordPromotionRedemption)
- ✅ Discount calculation stub (no floor price enforcement yet)
- ✅ OrderPlacedHandler skeleton (awaits Shopping BC coupon data)
- ✅ Batch coupon generation (GenerateCouponBatch with sequential codes)
- 🔄 Integration tests for redemption workflow
- 🔄 Documentation updates (CONTEXTS.md, CURRENT-CYCLE.md, retrospective)

**What's DEFERRED (M30.1+):**
- Shopping BC integration (M30.1) — ApplyCouponToCart, full OrderPlaced flow
- Pricing BC integration (M30.2) — Real floor price enforcement
- All remaining priorities (M30.3–M30.9+) — See "Deferred" section above

**Why this scope?**
M30.0 delivers the **foundation** for coupon redemption. Full end-to-end flow requires Shopping BC changes (M30.1) and Pricing BC integration (M30.2). This milestone establishes contracts and patterns for future milestones to build upon.

---

## 📝 Key Decisions Made

| Decision | Rationale | Source |
|----------|-----------|--------|
| UUID v5 for Coupon stream ID | Deterministic lookup without DB query | promotions-event-modeling.md:72-84 |
| Decimal for discount values | Simpler for percentage-only discounts | M29.1 retrospective, promotions-event-modeling.md:51 |
| No coupon reservation (M30.0) | Accept race condition to reduce complexity | promotions-event-modeling.md:158-161 |
| Optimistic concurrency for redemption cap | Standard Marten pattern (no sequential queue) | All BCs use this pattern |
| Fan-out pattern for batch generation | One command → N IssueCoupon messages | promotions-event-modeling.md:1103-1109 |
| Stub implementations in M30.0 | Unblock future milestones (M30.1+ deliver full features) | ADR 0032 milestone schema |

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

**Next Steps for M30.0 Completion:**
1. Write integration tests for redemption workflow (Priority 8.1)
2. Update CONTEXTS.md with M30.0 integration contracts (Priority 8.2)
3. Update CURRENT-CYCLE.md to show M30.0 progress (Priority 8.2)
4. Create M30.0 retrospective document (Priority 8.2)
5. Mark M30.0 complete, begin planning M30.1 (Shopping BC integration)
