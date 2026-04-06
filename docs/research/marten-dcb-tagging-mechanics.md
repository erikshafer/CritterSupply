# Marten DCB Tagging Mechanics — Source-Confirmed Research

**Date:** 2026-04-06
**Context:** M40.0 S1 landed a working multi-stream consistency boundary for coupon redemption
but did not use Marten's native `EventTagQuery` / `[BoundaryModel]` / `IEventBoundary<T>` API.
This document records the investigation into why, and what is required to use the genuine
DCB API, based on reading the Marten source code at `C:\Code\marten`.

**Marten version investigated:** 8.28.0
**JasperFx.Events version:** co-shipped with Marten 8.28.0

---

## The Core Question

M40.0 S1's `RedeemCouponHandler` used manual `LoadAsync` (two separate
`session.Events.AggregateStreamAsync()` calls) rather than `EventTagQuery` +
`FetchForWritingByTags<T>()`. ADR 0058 documented why: tag tables were empty for events
appended via normal patterns. This research confirms that finding and documents the
complete path to using the real API.

---

## How Tag Tables Get Populated — Definitive Answer

Tag tables (`mt_event_tag_{suffix}`) are populated **only** when an `IEvent` wrapper has
non-empty `Tags` at write time. The critical code is in
`src/Marten/Events/Operations/EventTagOperations.cs`:

```csharp
public static void QueueTagOperations(EventGraph eventGraph, DocumentSessionBase session, StreamAction stream)
{
    if (eventGraph.TagTypes.Count == 0) return;

    foreach (var @event in stream.Events)
    {
        var tags = @event.Tags;
        if (tags == null || tags.Count == 0) continue;  // ← no tags = no insert

        foreach (var tag in tags)
        {
            var registration = eventGraph.FindTagType(tag.TagType);
            if (registration == null) continue;
            session.QueueOperation(new InsertEventTagOperation(...));
        }
    }
}
```

If `@event.Tags` is null or empty, **nothing is inserted** into the tag tables regardless
of what tag types are registered.

---

## What Sets Tags on Events

### ✅ Path 1: `BuildEvent()` + `.WithTag()` + `session.Events.Append()`

The canonical tagging pattern, used in all of Marten's own DCB tests:

```csharp
var wrapped = session.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
wrapped.WithTag(studentId, courseId);           // ← tags set here
session.Events.Append(streamId, wrapped);       // ← tags travel with the IEvent
await session.SaveChangesAsync();               // ← tag table rows inserted
```

This pattern works in both Rich and Quick append modes (confirmed in
`src/EventSourcingTests/Dcb/dcb_quick_append_tests.cs`).

### ✅ Path 2: `IEventBoundary<T>.AppendOne()` with typed ID properties on the event

When appending via a DCB boundary, Marten can infer tags from the event's typed ID
properties if those types are registered tag types:

```csharp
// StudentGraded has StudentId and CourseId typed ID properties
boundary.AppendOne(new StudentGraded(studentId, courseId, 95));
// Marten infers tags from the StudentId and CourseId properties automatically
```

Source: `EventBoundary.RouteEventByTags()` in `src/Marten/Events/Dcb/EventBoundary.cs`
calls `EventTagInference.InferTags(wrapped.Data, _events.TagTypes)`.

### ✅ Path 3: `IEventBoundary<T>.AppendOne()` with a pre-tagged wrapped event

```csharp
var graded = session.Events.BuildEvent(new StudentGraded(studentId, courseId, 88));
graded.WithTag(studentId, courseId);    // ← explicit tags take precedence over inference
boundary.AppendOne(graded);
```

### ❌ Path 4: `session.Events.Append(streamId, rawObject)` — NO TAGS

`EventGraph.Actions.cs` → `Append()` calls `BuildEvent(o)` which creates an IEvent
wrapper with **zero tags**. No tag inference happens.

### ❌ Path 5: `[WriteAggregate]` compound handlers — NO TAGS

Wolverine's `[WriteAggregate]` uses `FetchForWriting()` internally then calls
`session.Events.Append()` with the events returned from `Handle()`. Same problem as
Path 4.

### ❌ Path 6: `MartenOps.StartStream<T>()` / `IStartStream` — NO TAGS

`EventGraph.Actions.cs` → `StartStream()` also calls `BuildEvent(o)` internally with no
tag handling.

---

## What `.ForAggregate<TAgg>()` Actually Does

**It does NOT auto-tag events at write time.**

`RegisterTagType<T>().ForAggregate<TAgg>()` sets the `AggregateType` on the
`TagTypeRegistration`. This is used in two places only:

1. **`EventBoundary.RouteEventByTags()`** — determines which stream to route a
   boundary-appended event to.

2. **`autoDiscoverTagTypesFromProjections()`** — auto-registers tag types from
   `SingleStreamProjection<T, TId>` projections where `TId` is a non-primitive 1-property
   record. `PrimitiveIdentityTypes` includes `Guid`, so CritterSupply's aggregates (which
   use raw `Guid` stream IDs) are skipped by auto-discovery.

This was confirmed by reading `EventGraph.cs` and the auto-discovery test
`src/EventSourcingTests/Dcb/auto_discover_tag_types_from_projections.cs`, which still
requires manual `BuildEvent` + `.WithTag()` even with auto-discovered tag types.

---

## Why `Guid` Cannot Be a Tag Type

`ValueTypeInfo.ForType(typeof(Guid))` requires exactly **one public instance property**.
In .NET 10, `Guid` gained `Variant` and `Version` as public instance properties — failing
the requirement. This is a .NET 10 behavioral change, not a Marten issue.

The solution is a single-property wrapper record:

```csharp
public sealed record CouponStreamId(Guid Value);    // ✅ exactly 1 public property
public sealed record PromotionStreamId(Guid Value); // ✅ exactly 1 public property
```

---

## The Complete Working DCB Pattern

### Registration (`Program.cs`)

```csharp
opts.Events.RegisterTagType<CouponStreamId>("coupon")
    .ForAggregate<Coupon>();
opts.Events.RegisterTagType<PromotionStreamId>("promotion")
    .ForAggregate<Promotion>();
```

### Write handlers — explicit tagging required for every append

```csharp
// Stream creation (replaces MartenOps.StartStream / IStartStream return):
var evt = new CouponIssued(couponCode, promotionId, DateTimeOffset.UtcNow);
var wrapped = session.Events.BuildEvent(evt);
wrapped.WithTag(new CouponStreamId(couponStreamId));
session.Events.StartStream<Coupon>(couponStreamId, wrapped);

// Regular append (replaces [WriteAggregate] returning Events):
var evt = new PromotionActivated(promotionId, DateTimeOffset.UtcNow);
var wrapped = session.Events.BuildEvent(evt);
wrapped.WithTag(new PromotionStreamId(promotionId));
session.Events.Append(promotionId, wrapped);
```

### DCB handler shape — the real API

```csharp
public static class RedeemCouponHandler
{
    // Load() returning EventTagQuery triggers the DCB workflow
    public static EventTagQuery Load(RedeemCoupon cmd)
        => new EventTagQuery()
            .Or<CouponStreamId>(new CouponStreamId(Coupon.StreamId(cmd.CouponCode)))
            .Or<PromotionStreamId>(new PromotionStreamId(cmd.PromotionId));

    // Before() receives the projected boundary state (do NOT also use [BoundaryModel] here —
    // that causes CS0128 codegen error)
    public static ProblemDetails Before(RedeemCoupon cmd, CouponRedemptionState? state)
    { /* validate invariants */ }

    // Handle() uses IEventBoundary<T> for cross-stream optimistic concurrency
    public static OutgoingMessages Handle(
        RedeemCoupon cmd,
        [BoundaryModel] IEventBoundary<CouponRedemptionState> boundary,
        IDocumentSession session)
    {
        var couponStreamId = new CouponStreamId(Coupon.StreamId(cmd.CouponCode));
        var wrapped = session.Events.BuildEvent(new CouponRedeemed(/* ... */));
        wrapped.WithTag(couponStreamId);
        boundary.AppendOne(wrapped);
        // ...
    }
}
```

### Boundary state — standard `Apply()` methods, not projection helpers

```csharp
public sealed class CouponRedemptionState
{
    public bool CouponExists { get; private set; }
    public CouponStatus CouponStatus { get; private set; }
    public PromotionStatus PromotionStatus { get; private set; }
    public int? UsageLimit { get; private set; }
    public int CurrentRedemptionCount { get; private set; }

    // Marten calls these as it projects events from the tag query results
    public void Apply(CouponIssued e) { CouponExists = true; CouponStatus = CouponStatus.Issued; }
    public void Apply(CouponRedeemed e) { CouponStatus = CouponStatus.Redeemed; }
    public void Apply(CouponRevoked e) { CouponStatus = CouponStatus.Revoked; }
    public void Apply(CouponExpired e) { CouponStatus = CouponStatus.Expired; }
    public void Apply(PromotionCreated e) { UsageLimit = e.UsageLimit; }
    public void Apply(PromotionActivated e) { PromotionStatus = PromotionStatus.Active; }
    public void Apply(PromotionPaused e) { PromotionStatus = PromotionStatus.Paused; }
    public void Apply(PromotionResumed e) { PromotionStatus = PromotionStatus.Active; }
    public void Apply(PromotionCancelled e) { PromotionStatus = PromotionStatus.Cancelled; }
    public void Apply(PromotionExpired e) { PromotionStatus = PromotionStatus.Expired; }
    public void Apply(PromotionRedemptionRecorded e) { CurrentRedemptionCount++; }
}
```

---

## Cross-Stream Optimistic Concurrency

The real DCB API provides genuine cross-stream optimistic concurrency. `FetchForWritingByTags<T>()`
automatically queues `AssertDcbConsistency` which runs at `SaveChangesAsync()` — checking
whether any matching tagged event was appended after the boundary was loaded. If so, it
throws `DcbConcurrencyException`.

This is the key advantage over the manual `LoadAsync` approach used in M40.0 S1: the
manual approach only provides single-stream optimistic concurrency on the Coupon stream.
With the real DCB API, concurrent modifications to either the Coupon OR the Promotion
stream trigger a concurrency failure.

The existing Wolverine retry policy in `Program.cs` (configured for `ConcurrencyException`)
handles `DcbConcurrencyException` automatically.

---

## Important Note on `[BoundaryModel]` in Wolverine

From M40.0 S1 investigation: using `[BoundaryModel]` on **both** `Before()` and `Handle()`
causes codegen error `CS0128` (duplicate local variable). Use `[BoundaryModel]` on
`Handle()` only. The boundary state flows from `Load()` through Wolverine's pipeline
automatically — `Before()` receives the projected state as a plain parameter without the
attribute.

---

## Relevant Marten Source Files

| File | Key Finding |
|------|-------------|
| `src/Marten/Events/Operations/EventTagOperations.cs` | Tags only inserted when `@event.Tags` non-empty |
| `src/Marten/Events/EventGraph.Actions.cs` | `Append()` and `StartStream()` call `BuildEvent()` with NO tag handling |
| `src/Marten/Events/Dcb/EventBoundary.cs` | Tag inference only via `IEventBoundary.AppendOne()` |
| `src/Marten/Events/EventGraph.cs` | `autoDiscoverTagTypesFromProjections()` skips `Guid` |
| `src/Marten/Events/Dcb/AssertDcbConsistency.cs` | Cross-stream concurrency check |
| `src/EventSourcingTests/Dcb/dcb_tag_query_and_consistency_tests.cs` | All tagging patterns — authoritative reference |
| `src/EventSourcingTests/Dcb/auto_discover_tag_types_from_projections.cs` | `.ForAggregate<>()` does not auto-tag |
| `src/DcbLoadTest/Program.cs` | Official load test: `BuildEvent` + `.WithTag()` + `Append` |
