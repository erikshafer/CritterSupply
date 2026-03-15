# M30.0: Promotions BC Redemption — Retrospective

**Date Started:** 2026-03-15
**Date Completed:** 2026-03-15
**Status:** ✅ Complete — Redemption Workflow Delivered
**Branch:** `claude/m31-0-establish-current-state`

---

## What Was Delivered

### Core Redemption Commands
- ✅ `RedeemCoupon` command + handler — records coupon redemptions with optimistic concurrency
- ✅ `RevokeCoupon` command + handler — admin revocation with reason tracking
- ✅ `RecordPromotionRedemption` command + handler — tracks usage against promotion limits
- ✅ `ExpireCoupon` scheduled message — auto-expires coupons when promotion ends

### Batch Generation
- ✅ `GenerateCouponBatch` command + handler — fan-out pattern for PREFIX-XXXX formatted codes
- ✅ Draft promotion support — batch generation enabled during draft phase (before activation)

### Discount Calculation
- ✅ `CalculateDiscount` query endpoint — stub CartView integration
- ✅ Percentage discount + fixed amount discount support
- ✅ Per-line-item discount calculation with banker's rounding

### Integration & Choreography
- ✅ `RecordPromotionRedemptionHandler` — choreography integration listening to Orders.OrderPlaced event
- ✅ Shopping BC stub integration (CartView, CartLineItem)

### Testing
- ✅ 29 integration tests across all workflows (100% passing)
- ✅ Test categories: promotion lifecycle, coupon validation, redemption, batch generation, discount calculation

### Documentation
- ✅ CONTEXTS.md updated with M30.0 status
- ✅ CURRENT-CYCLE.md updated with milestone completion
- ✅ Retrospective document (this file)

---

## Key Technical Decisions

### D1: Manual Event Appending Pattern ⭐ **Critical Discovery**

**Problem:** Initial implementation returned tuples `(Aggregate, Event)` from handlers, but events weren't being persisted.

**Root Cause:** When manually loading aggregates via `session.Events.AggregateStreamAsync<T>()` (instead of using `[WriteAggregate]`), returning event tuples doesn't tell Wolverine to persist the events.

**Solution:** Use `session.Events.Append(streamId, event)` to manually append events to streams.

**Pattern:**
```csharp
public static async Task Handle(
    Command cmd,
    IDocumentSession session,
    CancellationToken ct)
{
    // 1. Manually load aggregate
    var streamId = Coupon.StreamId(cmd.CouponCode);
    var coupon = await session.Events.AggregateStreamAsync<Coupon>(streamId, token: ct);

    // 2. Validate
    if (coupon is null) throw new InvalidOperationException("...");

    // 3. Create event
    var evt = new CouponRedeemed(/* ... */);

    // 4. Manually append to stream (Wolverine will persist via transactional outbox)
    session.Events.Append(streamId, evt);
}
```

**Reference Examples:**
- Returns BC: `RequestReturnHandler.cs`, `ExpireReturnHandler.cs`
- Inventory BC: Multiple handlers
- Correspondence BC: Message handlers

**Why This Differs from `[WriteAggregate]` Pattern:**
- `[WriteAggregate]` + tuple return → Wolverine automatically persists
- Manual loading + tuple return → Events NOT persisted
- Manual loading + `session.Events.Append()` → Events persisted correctly

---

### D2: Draft Promotion Coupon Issuance

**Problem:** `GenerateCouponBatchHandler` allowed batch generation for Draft promotions, but `IssueCouponHandler` (called via fan-out) rejected Draft promotions.

**Root Cause:** Design inconsistency between orchestrator and worker handlers.

**Decision:** Allow both Draft and Active promotions to issue coupons.

**Rationale:** Enables batch generation during draft phase before public activation. Operations workflow:
1. Create promotion (Draft status)
2. Generate 1000 coupons via batch command (codes: SAVE20-0001 through SAVE20-1000)
3. Review and activate promotion when ready

**Modified Handler:**
```csharp
// M30.0: Allow coupon issuance for both Draft and Active promotions
// This enables batch generation during draft phase before activation
if (promotion.Status != PromotionStatus.Draft && promotion.Status != PromotionStatus.Active)
{
    throw new InvalidOperationException(
        $"Cannot issue coupon for promotion in {promotion.Status} status. " +
        $"Promotion must be Draft or Active.");
}
```

---

### D3: Banker's Rounding in Discount Calculations

**Problem:** Test expected total discount of `28.66m` but actual was `28.64m`.

**Root Cause:** `Math.Round(6.825, 2)` uses banker's rounding (round to even), not round-away-from-zero.

**Explanation:**
- `Math.Round(6.825, 2)` → `6.82` (rounds to even), not `6.83`
- This is .NET's default rounding mode (`MidpointRounding.ToEven`)
- Affects discount calculations when percentage results in exact midpoint values

**Test Fix:**
```csharp
// Calculate expected discount: 15% of each item's unit price * quantity
// SKU-001: 15% of 29.99 = 4.4985, rounded to 4.50 * 3 = 13.50
// SKU-002: 15% of 10.01 = 1.5015, rounded to 1.50 * 1 = 1.50
// SKU-003: 15% of 45.50 = 6.825, banker's rounding to 6.82 * 2 = 13.64
// Total discount: 28.64 (not 28.66 due to banker's rounding)
response.TotalDiscount.ShouldBe(28.64m);
```

**Key Lesson:** When testing financial calculations, always account for banker's rounding in .NET.

---

### D4: Fan-Out Timing in Integration Tests

**Problem:** Batch generation tests failed with "coupon should not be null" even though commands were sent.

**Root Cause:** Initial 300ms delay was insufficient for:
1. Wolverine processing N `IssueCoupon` commands
2. Each command creating a Coupon aggregate
3. CouponLookupView projection updating

**Solution:** Increased delay from 300ms → 1000ms in both batch generation tests.

**Pattern:**
```csharp
// Wait for fan-out IssueCoupon commands to be processed asynchronously
// GenerateCouponBatch creates N IssueCoupon commands via OutgoingMessages
// Each IssueCoupon handler creates a coupon aggregate + updates CouponLookupView projection
await Task.Delay(1000); // Increased from 300ms to handle async projection updates

// Now verify all coupons were created
for (int i = 1; i <= batchSize; i++)
{
    var code = $"{prefix.ToUpperInvariant()}-{i:D4}";
    var coupon = await session.LoadAsync<CouponLookupView>(code);
    coupon.ShouldNotBeNull();
}
```

**Key Lesson:** Fan-out patterns require sufficient test delays for async processing + projection updates.

---

## Pattern Discoveries for Future Use

### Pattern 1: Manual Aggregate Loading → Manual Event Appending

**When to use:**
- Handler needs to load aggregate by deterministic ID (UUID v5 from code)
- Need full control over concurrency handling
- Cannot use `[WriteAggregate]` attribute

**Example:**
```csharp
public static async Task Handle(
    RedeemCoupon cmd,
    IDocumentSession session,
    CancellationToken ct)
{
    var streamId = Coupon.StreamId(cmd.CouponCode); // UUID v5
    var coupon = await session.Events.AggregateStreamAsync<Coupon>(streamId, token: ct);

    // Validation...

    var evt = new CouponRedeemed(cmd.CouponCode, cmd.OrderId, cmd.CustomerId, now);
    session.Events.Append(streamId, evt); // Manual append
}
```

### Pattern 2: Fan-Out via OutgoingMessages

**When to use:**
- Need to create N child entities from single parent command
- Each child requires separate aggregate stream

**Example:**
```csharp
public static async Task<OutgoingMessages> Handle(
    GenerateCouponBatch cmd,
    IDocumentSession session,
    CancellationToken ct)
{
    var promotion = await session.Events.AggregateStreamAsync<Promotion>(cmd.PromotionId, token: ct);
    var outgoing = new OutgoingMessages();

    // Generate N IssueCoupon commands (fan-out)
    for (int i = 1; i <= cmd.Count; i++)
    {
        var couponCode = $"{cmd.Prefix.ToUpperInvariant()}-{i:D4}";
        outgoing.Add(new IssueCoupon(couponCode, promotion.Id));
    }

    // Record batch generation on parent aggregate
    var batchEvent = new CouponBatchGenerated(promotion.Id, batchId, cmd.Prefix, cmd.Count, timestamp);
    session.Events.Append(cmd.PromotionId, batchEvent);

    return outgoing;
}
```

### Pattern 3: Choreography Integration via RabbitMQ

**When to use:**
- BC needs to react to integration events from other BCs
- Loose coupling preferred over tight orchestration

**Example:**
```csharp
public static class RecordPromotionRedemptionHandler
{
    public static async Task Handle(
        Messages.Contracts.Orders.OrderPlaced evt,
        IDocumentSession session,
        CancellationToken ct)
    {
        // React to Orders BC event
        var promotion = await session.Events.AggregateStreamAsync<Promotion>(
            evt.PromotionId, token: ct);

        var redemptionEvent = new PromotionRedemptionRecorded(
            promotion.Id, evt.OrderId, evt.CustomerId, DateTimeOffset.UtcNow);

        session.Events.Append(evt.PromotionId, redemptionEvent);
    }
}
```

---

## Lessons Learned

### What Went Well

1. **Existing BC Reference Examples**
   - Returns BC handlers provided clear examples of manual event appending pattern
   - Saved hours of debugging by finding correct pattern quickly

2. **Test-Driven Discovery**
   - Integration tests caught handler pattern issues immediately
   - Banker's rounding issue found via test failure (not production bug)

3. **Clear Scope Boundaries**
   - Stub Shopping BC integration kept scope manageable
   - Real Shopping integration deferred to M30.1+ without blocking progress

4. **Comprehensive Test Coverage**
   - 29 integration tests across all workflows
   - Edge cases covered (double redemption, expired coupons, usage limits)

### What Could Be Improved

1. **Read Skill Files First**
   - Could have read `docs/skills/wolverine-message-handlers.md` before implementing
   - Pattern discovery was reactive instead of proactive

2. **Test Timing Estimates**
   - Initial 300ms delay was too optimistic
   - Should have started with 1000ms based on multi-step async workflows

3. **Banker's Rounding Documentation**
   - Could have documented this pattern in M29.1 (first discount calculation implementation)
   - Rediscovered same issue in M30.0 tests

### Blockers Encountered (and Resolved)

**Blocker 1: Events Not Persisting**
- **Resolution Time:** ~30 minutes
- **Resolution:** Found Returns BC examples showing `session.Events.Append()` pattern

**Blocker 2: Batch Generation Test Failures**
- **Resolution Time:** ~15 minutes
- **Resolution:** Increased delay from 300ms → 1000ms

**Blocker 3: Discount Rounding Test Failure**
- **Resolution Time:** ~10 minutes
- **Resolution:** Updated test expectation to account for banker's rounding

---

## Deferred to M30.1+

### Shopping BC Real Integration
- Replace stub `CartView` with real Shopping BC HTTP client
- Implement `ApplyCouponToCart` handler in Shopping BC
- Implement `RemoveCouponFromCart` handler in Shopping BC
- Add coupon display in cart UI

### Pricing BC Floor Price Integration
- Query CurrentPriceView to enforce MAP floor prices
- Prevent discounts below minimum advertised price
- Add floor price violation alerts

### Advanced Features
- Multi-coupon stacking rules
- Tiered discount logic (spend $X, save Y%)
- Product category restrictions
- Customer segment targeting

---

## Metrics

- **Code:** ~2000 lines (handlers, projections, tests, integration contracts)
- **Tests:** 29 integration tests (100% passing)
- **Build Time:** ~22 seconds (Promotions.IntegrationTests only)
- **Test Runtime:** ~22 seconds (full suite with TestContainers startup)
- **Dependencies:** Marten 8.22.2, Wolverine 5.17.0, Alba, TestContainers

---

## Key Files Modified/Created

### Domain Handlers
- `src/Promotions/Promotions/Coupon/RedeemCouponHandler.cs`
- `src/Promotions/Promotions/Coupon/RevokeCouponHandler.cs`
- `src/Promotions/Promotions/Coupon/IssueCouponHandler.cs` (modified)
- `src/Promotions/Promotions/Promotion/RecordPromotionRedemptionHandler.cs`
- `src/Promotions/Promotions/Promotion/GenerateCouponBatchHandler.cs`

### Queries
- `src/Promotions/Promotions.Api/Queries/CalculateDiscount.cs`

### Integration Contracts
- `src/Shared/Messages.Contracts/Shopping/CartView.cs`
- `src/Shared/Messages.Contracts/Shopping/CartLineItem.cs`
- `src/Shared/Messages.Contracts/Promotions/LineItemDiscount.cs`

### Tests
- `tests/Promotions/Promotions.IntegrationTests/CouponRedemptionTests.cs` (21 tests)
- `tests/Promotions/Promotions.IntegrationTests/DiscountCalculationTests.cs` (8 tests)
- `tests/Promotions/Promotions.IntegrationTests/PromotionLifecycleTests.cs` (modified, removed obsolete test)

### Documentation
- `CONTEXTS.md` (updated Promotions BC entry)
- `docs/planning/CURRENT-CYCLE.md` (marked M30.0 complete)
- `docs/planning/milestones/m30-0-retrospective.md` (this file)

---

## Next Milestone

**M31.0: Correspondence BC Extended**
- Additional integration events (Shipment, Returns, Payments)
- SMS channel implementation (Twilio)
- Template system for message formatting

---

*M30.0 completed successfully on 2026-03-15. This document serves as the final retrospective record.*
