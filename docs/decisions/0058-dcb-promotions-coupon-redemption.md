# ADR 0058 — Dynamic Consistency Boundary: Promotions Coupon Redemption

**Status:** Accepted
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

Implement the **Dynamic Consistency Boundary (DCB)** pattern for coupon redemption:

1. **Single decision point:** `RedeemCouponHandler` loads both the Coupon and Promotion aggregates into a single `CouponRedemptionState` boundary state, validating all invariants (coupon exists, coupon is Issued, promotion exists, promotion is Active, usage cap not exceeded) in one `Before()` method.

2. **Choreography for downstream effects:** After `CouponRedeemed` is emitted, `RecordPromotionRedemptionHandler` reacts to it via choreography (event handler, not command handler) to update the Promotion's `CurrentRedemptionCount`. The promotion update is a consequence of the committed fact, not a parallel orchestrated step.

3. **`PromotionId` on `RedeemCoupon` command:** The command now includes `PromotionId` so the handler can load the Promotion aggregate alongside the Coupon aggregate.

### Implementation approach

The handler uses Wolverine's compound lifecycle (`LoadAsync` / `Before` / `Handle`) with multi-stream aggregation:

- **`LoadAsync`**: Loads both the `Coupon` and `Promotion` aggregates via `IQuerySession.Events.AggregateStreamAsync()` and projects them into `CouponRedemptionState`
- **`Before`**: Validates all invariants against the boundary state, returning `ProblemDetails` on failure
- **`Handle`**: Appends `CouponRedeemed` to the Coupon stream and cascades it via `OutgoingMessages` for choreography

### Why not Marten's tag-based DCB API (`EventTagQuery` / `[BoundaryModel]`)

Marten 8.28.0's `EventTagQuery` and `IEventBoundary<T>` API requires:
1. Strong-typed tag IDs (e.g., `CouponStreamTag(Guid Value)`) registered via `RegisterTagType<T>()`
2. Events tagged at write time — either via `IEventBoundary.AppendOne()` (which auto-infers tags from event properties) or explicit `WithTag()` on `IEvent`
3. Tag tables (`mt_event_tag_*`) populated at event insertion time

CritterSupply uses raw `Guid` stream IDs throughout. Existing events (`CouponIssued`, `PromotionCreated`, `PromotionActivated`, etc.) are appended via `session.Events.Append()` or Wolverine's `[WriteAggregate]` pattern — neither of which populates tag tables. Adopting the tag-based API would require:
- Modifying all upstream handlers to tag events with strong-typed IDs
- Or running a backfill migration to populate tag tables for existing events
- Or adding strong-typed tag properties to all event records (breaking event schema compatibility)

The manual multi-stream approach achieves the same architectural goal (one decision spanning two streams) without requiring changes to upstream event patterns. It can be evolved to the tag-based API in a future milestone if CritterSupply standardizes on strong-typed IDs across all BCs.

## Consequences

### Positive

- **Single atomic decision:** All redemption invariants (coupon validity + promotion status + usage cap) are checked in one handler invocation, eliminating the race window between separate handler calls
- **Simplified caller contract:** `OrderPlacedHandler` needs to fan out only one command (`RedeemCoupon` with `PromotionId`), not two separate commands
- **Self-contained choreography:** `CouponRedeemed` contains `PromotionId`, so `RecordPromotionRedemptionHandler` can react without needing additional context
- **Backward compatible:** `RecordPromotionRedemption` command and its legacy handler are retained for backward compatibility

### Negative

- **No cross-stream optimistic concurrency:** Unlike the tag-based DCB API which provides `AssertDcbConsistency` across the tag query boundary, the manual approach relies on single-stream optimistic concurrency on the Coupon stream only. Concurrent modifications to the Promotion (e.g., cancellation) between the `LoadAsync` and `SaveChangesAsync` are not detected
- **Eventually consistent promotion count:** The Promotion's `CurrentRedemptionCount` is updated via choreography (async), not inline. Between the `CouponRedeemed` commit and the `PromotionRedemptionRecorded` append, the count may be stale — this creates a small window where an additional redemption could slip past the cap check

### Pattern note

This is CritterSupply's first DCB implementation and serves as the reference example for cross-aggregate consistency boundaries. The manual multi-stream aggregation pattern is a pragmatic starting point; evolving to Marten's tag-based DCB API is a candidate for a future milestone. Next ADR: 0059.

## Alternatives Considered

1. **Tag-based DCB API (`EventTagQuery` + `[BoundaryModel]`):** Maximum correctness but required pervasive changes to upstream handlers and event schemas. Deferred to future milestone.

2. **Keep two separate handlers:** The existing pattern with `RedeemCoupon` + `RecordPromotionRedemption` fan-out. Rejected because the race window between the two handler invocations creates a real consistency gap.

3. **Saga-based orchestration:** An `OrderRedemptionSaga` coordinating both steps with compensation. Rejected as over-engineering for a two-step process that should be a single decision.
