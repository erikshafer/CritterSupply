# M40.0 Session 1B Retrospective — Real DCB API

**Date:** 2026-04-06
**Session:** S1B (follows S1 implementation)
**Goal:** Replace manual `LoadAsync` multi-stream approach with Marten's native `EventTagQuery` + `[BoundaryModel]` + `IEventBoundary<T>` API

---

## Summary

S1B successfully replaced S1's manual DCB workaround with Marten 8.28.0's real tag-based DCB API. All 6 write handlers now tag events at write time, populating `mt_event_tag_coupon` and `mt_event_tag_promotion` tables. The `RedeemCouponHandler` uses the full `EventTagQuery` + `[BoundaryModel]` + `IEventBoundary<CouponRedemptionState>` pattern.

**Final state:** 31/31 integration tests passing (30 original + 1 new DCB concurrency test). Build: 0 errors, 19 warnings (same as baseline).

---

## Key Findings

### 1. `session.Events.StartStream<T>(id, wrappedEvent)` does NOT preserve tags

When passing a pre-wrapped `IEvent` (from `BuildEvent()` + `AddTag()`) to `StartStream`, the tags are lost. Marten re-wraps the object internally, creating a new `IEvent` wrapper without the original tags.

**Workaround:** Use `session.Events.Append(streamId, wrappedEvent)` instead. `Append` correctly recognizes pre-wrapped `IEvent` objects and preserves their tags. The stream is created implicitly.

```csharp
// This works — tags are preserved
var wrapped = session.Events.BuildEvent(evt);
wrapped.AddTag(new CouponStreamId(streamId));
session.Events.Append(streamId, wrapped);

// This does NOT work — tags are lost
session.Events.StartStream<Coupon>(streamId, wrapped);
```

### 2. `EventTagQuery` requires `.AndEventsOfType<>()` to create conditions

Calling `EventTagQuery.For(tag)` or `.Or(tag)` alone does NOT create a query condition. Each tag value MUST be followed by `.AndEventsOfType<...>()` to specify which event types to query. Without it, `Conditions` is empty and `EventsExistAsync` throws `ArgumentException`.

```csharp
// This throws: "EventTagQuery must have at least one condition"
EventTagQuery.For(new CouponStreamId(id));

// This works — creates a condition matching CouponIssued events tagged with CouponStreamId
EventTagQuery.For(new CouponStreamId(id))
    .AndEventsOfType<CouponIssued, CouponRedeemed, CouponRevoked, CouponExpired>();
```

### 3. `DcbConcurrencyException` is NOT a subclass of `ConcurrencyException`

- `DcbConcurrencyException` extends `Marten.Exceptions.MartenException` → `System.Exception`
- `ConcurrencyException` is `JasperFx.ConcurrencyException` → `System.Exception`

They are siblings, not parent-child. A separate `opts.OnException<DcbConcurrencyException>()` retry policy is required in `Program.cs`.

### 4. `CouponRedemptionState` needs `Id` property

Marten registers DCB boundary models as document types. Without an `Id` property, `DeleteAllDocumentsAsync()` throws `InvalidDocumentException`. Adding `public Guid Id { get; set; }` resolves this.

### 5. `[BoundaryModel]` on `Handle()` only — confirmed

Adding `[BoundaryModel]` to both `Before()` and `Handle()` causes CS0128 (duplicate local variable) in Wolverine's generated code. `Before()` receives the projected state as a plain parameter; `Handle()` uses `[BoundaryModel] IEventBoundary<CouponRedemptionState>`.

### 6. `Guid` cannot be a tag type in .NET 10 — confirmed

`Guid` has `Variant` and `Version` as public instance properties in .NET 10. Marten's `ValueTypeInfo` validation requires exactly one public instance property. The solution is wrapper records: `CouponStreamId(Guid Value)`, `PromotionStreamId(Guid Value)`.

### 7. DCB concurrency test succeeded

The new `RedeemCoupon_ConcurrentRedemption_SecondIsRejectedByDcb` test proves that the real DCB API detects concurrent redemptions. After the first `CouponRedeemed` is appended, the second attempt is rejected by `Before()` because the boundary state (projected from tagged events) shows `CouponStatus.Redeemed`. This is the proof that S1B accomplishes what S1's manual `LoadAsync` approach also achieved — but now via Marten's native API with cross-stream optimistic concurrency via `AssertDcbConsistency`.

---

## What Changed (vs S1)

| Component | S1 (Manual) | S1B (Real DCB API) |
|-----------|-------------|-------------------|
| Tag types | None | `CouponStreamId`, `PromotionStreamId` |
| Tag registration | None | `RegisterTagType<T>("suffix").ForAggregate<T>()` |
| Write handlers | Raw `Append`/`StartStream`/`[WriteAggregate]` | `BuildEvent()` + `AddTag()` + `Append()` |
| `RedeemCouponHandler.Load()` | `LoadAsync` → `AggregateStreamAsync` × 2 | `EventTagQuery.For().AndEventsOfType().Or()` |
| `RedeemCouponHandler.Handle()` | `IDocumentSession.Events.Append()` | `[BoundaryModel] IEventBoundary<T>` + `boundary.AppendOne()` |
| `CouponRedemptionState` | `ProjectFromCoupon()`/`ProjectFromPromotion()` helpers | Standard `Apply()` methods |
| `RecordPromotionRedemptionHandler` | `[WriteAggregate]` returning `Events` | `FetchForWriting` + tagged `Append` |
| `LegacyRecordPromotionRedemptionHandler` | Present (backward compat) | Deleted |
| Cross-stream concurrency | Single-stream only (Coupon) | `AssertDcbConsistency` across tag boundary |
| Retry policy | `ConcurrencyException` only | `ConcurrencyException` + `DcbConcurrencyException` |

---

## Files Changed

### New (2 files)
- `src/Promotions/Promotions/CouponStreamId.cs`
- `src/Promotions/Promotions/PromotionStreamId.cs`

### Modified (9 files)
- `src/Promotions/Promotions.Api/Program.cs` — tag type registration + DcbConcurrencyException retry
- `src/Promotions/Promotions/Coupon/IssueCouponHandler.cs` — tagged append
- `src/Promotions/Promotions/Coupon/RevokeCouponHandler.cs` — tagged append
- `src/Promotions/Promotions/Coupon/RedeemCouponHandler.cs` — full DCB API rewrite
- `src/Promotions/Promotions/Coupon/CouponRedemptionState.cs` — Apply() methods
- `src/Promotions/Promotions/Promotion/CreatePromotionHandler.cs` — tagged append
- `src/Promotions/Promotions/Promotion/ActivatePromotionHandler.cs` — FetchForWriting + tagged append
- `src/Promotions/Promotions/Promotion/GenerateCouponBatchHandler.cs` — tagged append
- `src/Promotions/Promotions/Promotion/RecordPromotionRedemptionHandler.cs` — FetchForWriting + tagged append, LegacyHandler deleted

### Tests (1 file)
- `tests/Promotions/Promotions.IntegrationTests/CouponRedemptionTests.cs` — legacy test replaced + DCB concurrency test added

### Docs (3 files)
- `docs/decisions/0058-dcb-promotions-coupon-redemption.md` — updated to real implementation
- `docs/research/marten-dcb-tagging-mechanics.md` — research committed
- `docs/planning/CURRENT-CYCLE.md` — S1B complete

---

## Build & Test State

| Metric | S1 (Before) | S1B (After) |
|--------|-------------|-------------|
| Build errors | 0 | 0 |
| Build warnings | 19 | 19 |
| Integration tests | 30/30 | 31/31 |
| New tests | — | DCB concurrency test |

---

## Lessons Learned

1. **Read the Marten source, not just docs:** The XML doc for `IEventBoundary.AppendOne()` says "event MUST have tags set via WithTag()" but the actual method name is `AddTag()`. The `WithTag()` is an extension method on `JasperFx.Events.Event`.

2. **`AndEventsOfType` is not optional:** Without it, `EventTagQuery` has zero conditions and throws at runtime. This is not documented clearly.

3. **`StartStream` ≠ `Append` for tagged events:** `StartStream` re-wraps objects, losing tags. `Append` preserves pre-wrapped `IEvent` objects. This cost significant debugging time.

4. **Boundary models need `Id`:** Marten treats them as documents. Without an `Id` property, all tests fail during `CleanAllDataAsync()`.

5. **The real DCB API works:** Once all the quirks are addressed, the pattern is clean and powerful. The `EventTagQuery` + `[BoundaryModel]` + `IEventBoundary<T>` + `boundary.AppendOne()` chain provides exactly the cross-stream consistency that S1's manual approach couldn't.
