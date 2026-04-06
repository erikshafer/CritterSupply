# ADR 0058 — Dynamic Consistency Boundary: Promotions Coupon Redemption

**Status:** Accepted (Updated S1B — Real DCB API)
**Date:** 2026-04-06
**Milestone:** M40.0 — Dynamic Consistency Boundary: Promotions BC

---

## Context

Coupon redemption in the Promotions BC was split across two independent handlers:

1. **`RedeemCouponHandler`** — Validated coupon status, appended `CouponRedeemed` to the Coupon stream
2. **`RecordPromotionRedemptionHandler`** — Validated promotion status + usage cap, appended `PromotionRedemptionRecorded` to the Promotion stream

Both handlers were invoked independently via `OrderPlacedHandler`, which would fan out two separate commands (`RedeemCoupon` + `RecordPromotionRedemption`). This created a race window: between the two handler invocations, a concurrent redemption could exhaust the promotion's usage cap while the coupon was being marked as redeemed — allowing `CouponRedeemed` to commit while the cap check hadn't happened yet.

The two-command pattern also required callers to coordinate two messages for what is logically a single atomic decision: "Is this redemption valid right now?"

## Decision

Implement the **Dynamic Consistency Boundary (DCB)** pattern for coupon redemption using Marten's native `EventTagQuery` + `[BoundaryModel]` + `IEventBoundary<T>` API:

1. **Single decision point:** `RedeemCouponHandler` uses `EventTagQuery` to load all tagged events from both the Coupon and Promotion streams, projecting them into `CouponRedemptionState` via standard `Apply()` methods. All invariants (coupon exists, coupon is Issued, promotion exists, promotion is Active, usage cap not exceeded) are checked in one `Before()` method.

2. **Cross-stream optimistic concurrency:** `IEventBoundary<CouponRedemptionState>` provides `AssertDcbConsistency` at `SaveChangesAsync` time. If any matching tagged event was appended since the boundary was loaded, `DcbConcurrencyException` is thrown — preventing concurrent double-redemption across the boundary, not just per-aggregate.

3. **Choreography for downstream effects:** After `CouponRedeemed` is emitted, `RecordPromotionRedemptionHandler` reacts to it via choreography (event handler) to update the Promotion's `CurrentRedemptionCount`. The promotion update is a consequence of the committed fact, not a parallel orchestrated step.

4. **`PromotionId` on `RedeemCoupon` command:** The command includes `PromotionId` so the handler can construct the `EventTagQuery` spanning both streams.

### Implementation approach

The handler uses Wolverine's compound lifecycle (`Load` / `Before` / `Handle`) with Marten's DCB API:

- **`Load`**: Returns `EventTagQuery` spanning both Coupon and Promotion streams. Marten loads all matching tagged events and projects `CouponRedemptionState` via `Apply()` methods.
- **`Before`**: Validates all invariants against the boundary state, returning `ProblemDetails` on failure. Note: `[BoundaryModel]` is NOT used on `Before()` — adding it to both `Before()` and `Handle()` causes CS0128 in Wolverine's codegen.
- **`Handle`**: Receives `[BoundaryModel] IEventBoundary<CouponRedemptionState>`. Appends `CouponRedeemed` via `boundary.AppendOne()` with explicit tagging. Cascades the event via `OutgoingMessages` for choreography.

### Tag type registration

Strong-typed tag IDs are required because `Guid` has 2 public instance properties in .NET 10 (`Variant`, `Version`), causing `ValueTypeInfo` validation to fail:

```csharp
public sealed record CouponStreamId(Guid Value);
public sealed record PromotionStreamId(Guid Value);
```

Registered in `Program.cs`:
```csharp
opts.Events.RegisterTagType<CouponStreamId>("coupon").ForAggregate<Coupon>();
opts.Events.RegisterTagType<PromotionStreamId>("promotion").ForAggregate<Promotion>();
```

### Event tagging

All handlers that append to Coupon or Promotion streams use `BuildEvent()` + `AddTag()`:

```csharp
var wrapped = session.Events.BuildEvent(evt);
wrapped.AddTag(new CouponStreamId(streamId));
session.Events.Append(streamId, wrapped);
```

This populates `mt_event_tag_coupon` and `mt_event_tag_promotion` tables, enabling `EventTagQuery` to find events.

### EventTagQuery construction

Each `.For()` / `.Or()` clause MUST be followed by `.AndEventsOfType<>()` to create a valid condition:

```csharp
EventTagQuery
    .For(new CouponStreamId(Coupon.StreamId(cmd.CouponCode)))
    .AndEventsOfType<CouponIssued, CouponRedeemed, CouponRevoked, CouponExpired>()
    .Or(new PromotionStreamId(cmd.PromotionId))
    .AndEventsOfType<PromotionCreated, PromotionActivated, ...>()
```

### DcbConcurrencyException handling

`DcbConcurrencyException` extends `MartenException`, NOT `ConcurrencyException` (which is `JasperFx.ConcurrencyException`). A separate retry policy is required:

```csharp
opts.OnException<DcbConcurrencyException>()
    .RetryOnce()
    .Then.RetryWithCooldown(100.Milliseconds(), 250.Milliseconds())
    .Then.Discard();
```

### Evolution from S1 to S1B

S1 implemented a manual multi-stream approach using `LoadAsync` to load both aggregates. This achieved the same business outcome (single decision spanning two streams) but:
- Lacked cross-stream optimistic concurrency (only single-stream via Coupon aggregate)
- Did not demonstrate the real Marten DCB API
- Required `ProjectFromCoupon()`/`ProjectFromPromotion()` helper methods instead of standard `Apply()`

S1B replaces the manual approach entirely with Marten's native DCB API.

## Consequences

### Positive

- **Cross-stream optimistic concurrency:** `IEventBoundary<T>` provides `AssertDcbConsistency` across the tag query boundary — if any matching tagged event is appended between load and commit, `DcbConcurrencyException` is thrown
- **Single atomic decision:** All redemption invariants checked in one handler invocation
- **Standard Marten patterns:** Uses `Apply()` methods on `CouponRedemptionState` (same as any Marten projection), not custom projection helpers
- **Reference architecture value:** Demonstrates the complete tag-based DCB API (`RegisterTagType` → `BuildEvent` + `AddTag` → `EventTagQuery.For().AndEventsOfType()` → `[BoundaryModel] IEventBoundary<T>` → `boundary.AppendOne()`)

### Negative

- **All handlers must tag events:** Every handler appending to Coupon or Promotion streams must use `BuildEvent()` + `AddTag()` instead of raw `session.Events.Append()`. This is additional ceremony but ensures tag tables are populated.
- **StartStream doesn't preserve tags:** `session.Events.StartStream<T>(id, wrappedEvent)` does NOT preserve tags on pre-wrapped `IEvent` objects. The workaround is to use `session.Events.Append(id, wrappedEvent)` instead, which handles `IEvent` correctly.
- **Eventually consistent promotion count:** The Promotion's `CurrentRedemptionCount` is updated via choreography (async). Between `CouponRedeemed` commit and `PromotionRedemptionRecorded` append, the count may be stale. However, each subsequent `RedeemCoupon` loads fresh boundary state.

### Pattern note

This is CritterSupply's first production DCB implementation using Marten's native API and serves as the reference example for cross-aggregate consistency boundaries. Next ADR: 0059.

## Alternatives Considered

1. **Manual multi-stream aggregation (S1 approach):** Pragmatic starting point but lacked cross-stream concurrency detection. Superseded by S1B.

2. **Keep two separate handlers:** The existing pattern with `RedeemCoupon` + `RecordPromotionRedemption` fan-out. Rejected because the race window creates a real consistency gap.

3. **Saga-based orchestration:** An `OrderRedemptionSaga` coordinating both steps with compensation. Rejected as over-engineering for a two-step process that should be a single decision.
