# Marten DCB Tagging Mechanics — Source-Confirmed Findings

**Date:** 2026-04-06
**Marten Version:** 8.28.0
**JasperFx.Events Version:** 1.25.0
**Wolverine Version:** 5.27.0

---

## Overview

This document records findings from investigating Marten's Dynamic Consistency Boundary (DCB) tag-based API, confirmed via reflection analysis and integration testing against Marten 8.28.0 / .NET 10.

---

## 1. Tag Type Registration

**API:** `opts.Events.RegisterTagType<TTag>(tableSuffix)` → `ITagTypeRegistration`

- Creates a database table `mt_event_tag_{tableSuffix}` to store tag-event mappings
- `ITagTypeRegistration.ForAggregate<T>()` associates the tag type with an aggregate type
- Tag type MUST have exactly one public instance property (validated by `ValueTypeInfo`)
- `Guid` CANNOT be a tag type in .NET 10: `Guid.Variant` and `Guid.Version` are public instance properties, causing validation to fail
- Solution: wrapper records like `sealed record CouponStreamId(Guid Value)`

**Example:**
```csharp
opts.Events.RegisterTagType<CouponStreamId>("coupon").ForAggregate<Coupon>();
```

---

## 2. Event Tagging at Write Time

**API:** `session.Events.BuildEvent(data)` → `IEvent`, then `IEvent.AddTag<TTag>(tag)`

- `BuildEvent(object)` wraps a raw event in an `IEvent` wrapper
- `AddTag<TTag>(TTag tag)` is an instance method on `IEvent` (from `Event<T>`)
- `WithTag<TTag>(IEvent, TTag)` is an extension method on `JasperFx.Events.Event` class (returns `IEvent`, chainable)
- Both `AddTag` and `WithTag` add entries to `IEvent.Tags` (`IReadOnlyList<EventTag>`)

### Critical: Append vs StartStream for Tagged Events

| Method | Preserves Tags? | Notes |
|--------|----------------|-------|
| `session.Events.Append(streamId, wrappedIEvent)` | ✅ Yes | Recognizes pre-wrapped `IEvent` objects |
| `session.Events.StartStream<T>(id, wrappedIEvent)` | ❌ No | Re-wraps the object, losing tags |
| `boundary.AppendOne(wrappedIEvent)` | ✅ Yes | The DCB-specific append path |

**Workaround for stream creation:** Use `Append` directly instead of `StartStream`. Marten creates the stream implicitly.

```csharp
// Works — tags preserved, stream created implicitly
var wrapped = session.Events.BuildEvent(evt);
wrapped.AddTag(new CouponStreamId(streamId));
session.Events.Append(streamId, wrapped);

// Does NOT work — tags lost due to re-wrapping
session.Events.StartStream<T>(streamId, wrapped);
```

---

## 3. EventTagQuery Construction

**API:** `EventTagQuery.For<TTag>(tagValue)` → `EventTagQuery`

- `For<TTag>(tagValue)` is the static factory method — creates a new query with the first tag value
- `.Or<TTag>(tagValue)` adds additional tag values (different type or same type)
- **CRITICAL:** Each `.For()` / `.Or()` MUST be followed by `.AndEventsOfType<T1, T2, ...>()` to create a condition
- Without `.AndEventsOfType<>()`, `Conditions` is empty and queries throw `ArgumentException`
- `AndEventsOfType<>()` has overloads for 1-6 generic type parameters
- Multiple `.AndEventsOfType<>()` calls after the same tag value are cumulative

**Example:**
```csharp
EventTagQuery
    .For(new CouponStreamId(couponStreamId))
    .AndEventsOfType<CouponIssued, CouponRedeemed, CouponRevoked, CouponExpired>()
    .Or(new PromotionStreamId(promotionId))
    .AndEventsOfType<PromotionCreated, PromotionActivated, PromotionPaused, PromotionResumed, PromotionCancelled, PromotionExpired>()
    .AndEventsOfType<PromotionRedemptionRecorded>();
```

---

## 4. IEventBoundary<T>

**API:** `Marten.Events.Dcb.IEventBoundary<T>`

- `Aggregate` property: the projected state (type `T`)
- `LastSeenSequence` property: the sequence number used for concurrency
- `AppendOne(object event)`: appends a single event (must have tags set)
- `AppendMany(object[] events)`: appends multiple events
- At `SaveChangesAsync`, `AssertDcbConsistency` checks if any new tagged events matching the query were appended since `LastSeenSequence`
- If new events detected → throws `DcbConcurrencyException`

### Boundary Model in Wolverine

- `[BoundaryModel]` attribute on handler parameter triggers Wolverine's DCB workflow
- The `Load()` method must return `EventTagQuery` for the DCB path
- `[BoundaryModel]` on `Handle()` only — adding it to both `Before()` and `Handle()` causes CS0128 (duplicate variable in codegen)
- `Before()` receives the projected state as a plain parameter (no attribute needed)
- Boundary model type needs `public Guid Id { get; set; }` (Marten treats it as a document)

---

## 5. DcbConcurrencyException

**Type:** `Marten.Events.Dcb.DcbConcurrencyException`
**Inheritance:** `MartenException` → `Exception`

- NOT a subclass of `JasperFx.ConcurrencyException`
- Requires separate retry policy in Wolverine: `opts.OnException<DcbConcurrencyException>()`
- Thrown when `AssertDcbConsistency` detects new matching tagged events since the boundary was loaded

---

## 6. Query and Aggregation Methods

Available on `IEventStoreOperations`:

| Method | Purpose |
|--------|---------|
| `EventsExistAsync(EventTagQuery)` | Returns `bool` — do matching tagged events exist? |
| `QueryByTagsAsync(EventTagQuery)` | Returns `IReadOnlyList<IEvent>` — all matching events |
| `AggregateByTagsAsync<T>(EventTagQuery)` | Returns `T` — projects matching events into aggregate |
| `FetchForWritingByTags<T>(EventTagQuery)` | Returns `IEventBoundary<T>` — for write with concurrency |

---

## 7. Testing Considerations

- TestContainers creates a fresh database → tag tables start empty → no backfill needed
- All write handlers must tag events for tests to work (tag tables are populated by test setup)
- `CleanAllDataAsync()` in test fixtures cleans tag tables alongside event streams
- `CouponRedemptionState` needs `Id` property to avoid `InvalidDocumentException` during cleanup
