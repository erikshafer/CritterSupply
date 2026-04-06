# M40.0 Session 1 Retrospective — DCB Promotions Coupon Redemption

**Session:** S1 — Implementation
**Date:** 2026-04-06
**Duration:** ~2 hours
**Milestone:** M40.0 — Dynamic Consistency Boundary: Promotions BC

---

## What Was Delivered

### Code Changes

1. **`RedeemCoupon` command** — Added `PromotionId` parameter (required for loading both streams).
2. **`RedeemCouponValidator`** — Added `PromotionId` NotEmpty rule.
3. **`CouponRedemptionState`** — New boundary state class projecting from both `Coupon` and `Promotion` aggregates via `ProjectFromCoupon()`/`ProjectFromPromotion()` helper methods.
4. **`RedeemCouponHandler`** — Full replacement: `LoadAsync` loads both aggregates, `Before()` validates all invariants (coupon exists, coupon status, promotion exists, promotion status, usage cap), `Handle()` appends `CouponRedeemed` and cascades via `OutgoingMessages`.
5. **`RecordPromotionRedemptionHandler`** — Converted to choreography: reacts to `CouponRedeemed` event (not `RecordPromotionRedemption` command). Legacy handler retained as `LegacyRecordPromotionRedemptionHandler`.
6. **`RecordPromotionRedemption` command** — Added deprecation comment (not deleted per guard rails).
7. **`OrderPlacedHandler`** — Updated Phase 2 comment: single `RedeemCoupon` fan-out, no `RecordPromotionRedemption` needed.

### Documentation

- **ADR 0058** — Documents the DCB decision, why tag-based API wasn't used, consequences, and alternatives.
- **Session 1 retrospective** (this file)
- **CURRENT-CYCLE.md** — Updated with M40.0 as active milestone.

### Tests

- **30 tests passing** (29 baseline + 3 new DCB tests - 2 removed/replaced)
- Updated: `RedeemCoupon_WithValidIssuedCoupon_RedeemsSuccessfully`, `RedeemCoupon_WhenAlreadyRedeemed_Fails`, `RevokeCoupon_ForRedeemedCoupon_RevokesSuccessfully`
- Replaced: `RecordPromotionRedemption_ExceedingUsageLimit_Fails` → `RedeemCoupon_WhenPromotionCapExceeded_Fails`, `RecordPromotionRedemption_ForDraftPromotion_Fails` → `RedeemCoupon_WhenPromotionIsNotActive_Fails`
- Added: `RedeemCoupon_CausesPromotionRedemptionRecorded` (choreography end-to-end)

### Build State

- **Build:** 0 errors, 19 warnings (unchanged from baseline)
- **Tests:** 30/30 passing

---

## Wolverine DCB API — Version 5.27.0 Findings

### API Surface Explored

- `EventTagQuery.For<TTag>(tagValue).AndEventsOfType<T1, T2>().Or<TTag2>(tagValue2)`
- `[BoundaryModel]` attribute on handler parameters
- `IEventBoundary<T>` for appending events with automatic tag routing
- `RegisterTagType<T>().ForAggregate<TAgg>()` for tag table registration
- `ValueTypeInfo.ForType(typeof(T))` — requires exactly 1 public instance property (not `Guid` which has 2: `Variant`, `Version`)

### What Worked

- `RegisterTagType<T>()` accepts `sealed record CouponStreamTag(Guid Value)` (1 property, single ctor — satisfies `ValueTypeInfo`)
- `[BoundaryModel]` on `Handle()` parameters triggers `FetchForWritingByTags` in codegen
- `Load()` returning `EventTagQuery` is correctly recognized as the DCB load pattern

### What Did Not Work

1. **Dual `[BoundaryModel]`** — Using `[BoundaryModel]` on both `Before()` and `Handle()` caused `CS0128: A local variable named 'eventBoundaryOfCouponRedemptionState' is already defined in this scope` — the codegen creates the variable twice. Must use `[BoundaryModel]` on only one method.

2. **Tag tables are empty for non-tagged events** — The critical discovery: `EventTagQuery` only finds events stored in `mt_event_tag_*` tables. These tables are ONLY populated when events are appended via `IEventBoundary.AppendOne()` (which infers tags from event properties) or with explicit `WithTag()`. Regular `session.Events.Append()` and Wolverine's `[WriteAggregate]` pattern do NOT populate tag tables. This means ALL upstream handlers would need modification to tag events at write time.

3. **`IEventBoundary.AppendOne(object)` requires tag inference** — The method wraps the raw object into `IEvent` via `BuildEvent()` and then calls `RouteEventByTags()`, which either finds explicit tags on the `IEvent` or tries to infer them from the event's properties matching registered tag types. Since `CouponRedeemed` has `Guid` properties (not `CouponStreamTag`/`PromotionTag`), inference fails.

4. **`Guid` cannot be a tag type** — `Guid` has 2 public instance properties (`Variant`, `Version` in .NET 10), so `ValueTypeInfo.ForType(typeof(Guid))` fails (requires exactly 1).

### Decision: Manual Multi-Stream Aggregation

Given these constraints, the implementation uses manual multi-stream aggregation via `LoadAsync`:
- Loads both `Coupon` and `Promotion` aggregates via `IQuerySession.Events.AggregateStreamAsync()`
- Projects into `CouponRedemptionState` via helper methods
- Standard `Before()`/`Handle()` compound pattern
- `session.Events.Append()` for event storage (existing concurrency pattern)
- `OutgoingMessages` for cascading `CouponRedeemed` to the choreography handler

This achieves the DCB pattern's goal (single decision spanning multiple streams) without requiring changes to upstream handlers or event schemas. It trades cross-stream optimistic concurrency (which the tag-based API provides) for pragmatic compatibility.

### Recommendation for Future

If CritterSupply adopts strong-typed IDs (e.g., `CouponId(Guid Value)`, `PromotionId(Guid Value)`), the events could carry these types as properties, enabling:
- `RegisterTagType<CouponId>().ForAggregate<Coupon>()`
- Automatic tag inference from event properties
- Full `EventTagQuery` + `[BoundaryModel]` + `IEventBoundary<T>` API usage

This would be a separate milestone requiring event schema evolution and upstream handler changes.

---

## How EventTagQuery Was Constructed

The planned fluent chain:
```csharp
EventTagQuery
    .For(CouponStreamTag.FromCode(cmd.CouponCode))
    .AndEventsOfType<CouponIssued, CouponRedeemed, CouponRevoked, CouponExpired>()
    .Or(new PromotionTag(cmd.PromotionId))
    .AndEventsOfType<PromotionCreated, PromotionActivated, PromotionPaused, PromotionResumed>()
    .AndEventsOfType<PromotionCancelled, PromotionExpired, PromotionRedemptionRecorded>()
```

This was validated against the decompiled API — the fluent chain correctly creates conditions with tag values and event type filters. However, it was not used in the final implementation because tag tables would be empty (see above).

---

## RecordPromotionRedemptionHandler Choreography Wiring

`[WriteAggregate]` successfully resolves `Promotion` from `CouponRedeemed.PromotionId` by the `{AggregateName}Id` convention. The generated code calls `FetchForWriting<Promotion>(couponRedeemed.PromotionId)`. This works because `CouponRedeemed` has a `PromotionId` property matching the `Promotion` aggregate name.

The choreography fires via `OutgoingMessages`: `RedeemCouponHandler.Handle()` adds the `CouponRedeemed` event to `OutgoingMessages`, and Wolverine routes it to `RecordPromotionRedemptionHandler.Handle(CouponRedeemed, [WriteAggregate] Promotion)` through the durable local queue.

---

## Surprises

1. **`Guid.Variant` and `Guid.Version`** — .NET 10 added these public properties to `Guid`. Earlier .NET versions had 0 public instance properties on `Guid`. This is why `ValueTypeInfo.ForType(typeof(Guid))` fails — it's a .NET 10 change, not a Marten issue.

2. **Tag tables are strictly opt-in** — This is by design: DCB is a premium pattern that requires intentional schema evolution. You can't retroactively query by tags without populating the tag tables first.

3. **`DoNotAssertOnExceptionsDetected()` was already configured** — The test fixture already had this in `ExecuteAndWaitAsync`, so no additional configuration was needed for the choreography tests.

---

## Final State

- **Build:** 0 errors, 19 warnings
- **Tests:** 30/30 passing
- **New files:** `CouponRedemptionState.cs`, ADR 0058, this retrospective
- **Modified files:** `RedeemCoupon.cs`, `RedeemCouponHandler.cs`, `RedeemCouponValidator.cs`, `RecordPromotionRedemptionHandler.cs`, `RecordPromotionRedemption.cs`, `OrderPlacedHandler.cs`, `Program.cs`, `CouponRedemptionTests.cs`, `DiscountCalculationTests.cs`, `CURRENT-CYCLE.md`
- **Deleted files:** `CouponStreamTag.cs`, `PromotionTag.cs` (unused after switching from tag-based API)
