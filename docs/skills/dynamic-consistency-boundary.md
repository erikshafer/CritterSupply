# Dynamic Consistency Boundary (DCB)

## What Is DCB?

The **Dynamic Consistency Boundary** pattern is a technique for enforcing consistency in event-driven systems without relying on rigid, per-aggregate transactional boundaries. It was introduced by Sara Pellegrini in her blog post *"Killing the Aggregate"* and is documented at [dcb.events](https://dcb.events/).

In standard event sourcing, a consistency boundary maps 1:1 to an aggregate and its stream. DCB relaxes that constraint by allowing a single consistency boundary to span **multiple event streams**, selected dynamically at command-handling time via **event tags**.

The core idea: instead of pulling two aggregates into a saga to enforce a cross-entity invariant, you query for all events tagged with the relevant identifiers, project them into a single decision model, and write your new event(s) atomically — with optimistic concurrency checked against that same tag query.

💡 One way to think about it is having multi-stream consistency without sagas.

## When to Reach for DCB

DCB is appropriate when:

- A command must enforce invariants that naturally span two or more entities (e.g., a student subscribing to a course requires checking both the student's history *and* the course's capacity)
- A saga feels like accidental complexity — you're coordinating state that is fundamentally a single decision
- You want one event to represent one fact, not two compensating events representing the same business outcome

Do **not** reach for DCB when a single aggregate stream is sufficient. It adds more moving parts and should earn its place.

## Quick Decision Guide

Use the simplest option that matches the business problem:

- **Single aggregate stream, single invariant boundary** → use a normal event-sourced aggregate handler
- **Multiple event streams inside one bounded context, but still one immediate decision** → consider DCB
- **Long-running workflow, cross-BC coordination, retries, external side effects, or delayed completion** → use a saga/process manager

## How Wolverine Implements DCB

Wolverine's DCB support (available with Marten, and coming to Polecat/SQL Server) builds on top of the existing aggregate handler workflow. The key additions are:

### `EventTagQuery`

A fluent API that specifies which tagged events to load from Marten before your handler runs. You define this in a `Load()` or `Before()` method on your handler class.

```csharp
public static EventTagQuery Load(SubscribeStudentToCourse command)
    => EventTagQuery
        .For(command.CourseId)
        .AndEventsOfType<CourseCreated, CourseCapacityChanged, StudentSubscribedToCourse, StudentUnsubscribedFromCourse>()
        .Or(command.StudentId)
        .AndEventsOfType<StudentEnrolledInFaculty, StudentSubscribedToCourse, StudentUnsubscribedFromCourse>();
```

Marten loads all events matching **any** of the tag criteria and projects them into your aggregate state using the standard `Apply()` methods.

### `[BoundaryModel]` Attribute

Marks a handler parameter as the projected state built from the DCB tag query. The parameter type is a plain C# class with `Apply()` methods — not tied to a single stream.

```csharp
public static StudentSubscribedToCourse Handle(
    SubscribeStudentToCourse command,
    [BoundaryModel] SubscriptionState state)
{
    if (state.StudentId == null)
        throw new InvalidOperationException("Student never enrolled in faculty");

    if (state.CoursesStudentSubscribed >= MaxCoursesPerStudent)
        throw new InvalidOperationException("Student subscribed to too many courses");

    if (state.CourseId == null)
        throw new InvalidOperationException("Course does not exist");

    if (state.StudentsSubscribedToCourse >= state.CourseCapacity)
        throw new InvalidOperationException("Course is fully booked");

    if (state.AlreadySubscribed)
        throw new InvalidOperationException("Student already subscribed to this course");

    return new StudentSubscribedToCourse(FacultyId.Default, command.StudentId, command.CourseId);
}
```

### `IEventBoundary<T>`

For cases where you need direct control over event appending (rather than returning events as a value), accept `IEventBoundary<T>` as a parameter:

```csharp
public static void Handle(
    SubscribeStudentToCourse command,
    [BoundaryModel] IEventBoundary<SubscriptionState> boundary)
{
    var state = boundary.Aggregate;
    // validation...
    boundary.AppendOne(new StudentSubscribedToCourse(...));
}
```

### Return Value Patterns

The DCB workflow supports the same return patterns as the standard aggregate handler workflow:

- A single event object — appended directly
- `IEnumerable<object>` or `Events` — multiple events appended
- `IAsyncEnumerable<object>` — async event enumeration
- `OutgoingMessages` — cascading messages (not appended as events)
- `ISideEffect` — standard side effect handling

### Concurrency

Wolverine/Marten enforces optimistic concurrency using the same tag query that was used to load events. If any matching event was appended between your load and your save, Marten throws a `ConcurrencyException`. No saga coordination, no compensating events.

## The Boundary State Aggregate

The state type projected for a DCB handler is a plain class with `Apply()` methods, just like any Marten aggregate. The difference is that it can project events from **multiple logical streams** because Marten is loading by tag, not by stream ID.

```csharp
public class SubscriptionState
{
    public CourseId? CourseId { get; private set; }
    public int CourseCapacity { get; private set; }
    public int StudentsSubscribedToCourse { get; private set; }

    public StudentId? StudentId { get; private set; }
    public int CoursesStudentSubscribed { get; private set; }
    public bool AlreadySubscribed { get; private set; }

    public void Apply(CourseCreated e) { CourseId = e.CourseId; CourseCapacity = e.Capacity; }
    public void Apply(CourseCapacityChanged e) { CourseCapacity = e.Capacity; }
    public void Apply(StudentEnrolledInFaculty e) { StudentId = e.StudentId; }

    public void Apply(StudentSubscribedToCourse e)
    {
        if (e.CourseId == CourseId) StudentsSubscribedToCourse++;
        if (e.StudentId == StudentId) CoursesStudentSubscribed++;
        if (e.StudentId == StudentId && e.CourseId == CourseId) AlreadySubscribed = true;
    }
    // ...
}
```

## Unit Testing

Because DCB handlers receive plain state objects, unit testing remains straightforward — no infrastructure required:

```csharp
var state = new SubscriptionState();
// Apply events manually to set up the scenario
state.Apply(new CourseCreated(FacultyId.Default, courseId, "Math 101", capacity: 2));
state.Apply(new StudentEnrolledInFaculty(FacultyId.Default, studentId, "Jane", "Doe"));

var result = SubscriptionHandler.Handle(new SubscribeStudentToCourse(studentId, courseId), state);
result.ShouldBeOfType<StudentSubscribedToCourse>();
```

That said, unit tests are only part of the story. A real DCB implementation should also have integration tests that verify:

- event tag selection loads the intended decision boundary
- concurrent matching writes trigger optimistic concurrency failures
- duplicate or conflicting commands behave correctly under retry/race conditions

## CritterSupply Usage

### When DCB Is Appropriate

Use the simplest option that matches the business problem:

- **Single aggregate stream, single invariant boundary** → use a normal event-sourced aggregate handler
- **Multiple event streams inside one bounded context, one immediate decision** → consider DCB
- **Long-running workflow, cross-BC coordination, retries, external side effects, or delayed completion** → use a saga/process manager

DCB is for **one atomic decision inside one BC** when the invariant spans two or more streams. It is not a replacement for CritterSupply's long-running orchestration sagas (e.g., Orders coordinating Payments, Inventory, and Fulfillment).

### CritterSupply's Canonical Example

**Promotions BC — coupon redemption spanning Coupon + Promotion streams.**

**The problem:** Before M40.0, redeeming a coupon required two sequential commands — `RedeemCoupon` (updates the Coupon aggregate) and `RecordPromotionRedemption` (updates the Promotion aggregate). Between those two commands, a race window existed: the coupon could be marked redeemed while the promotion's usage cap was already exceeded, or another concurrent redemption could slip through before the promotion's count was incremented.

**What DCB enforces in one decision:** The `RedeemCouponHandler` loads a `CouponRedemptionState` boundary model that spans both the Coupon stream and the Promotion stream via `EventTagQuery`. In a single atomic operation, it validates:
- Coupon exists and is in `Issued` status
- Promotion exists and is in `Active` status
- Promotion usage cap has not been reached

If all invariants pass, `CouponRedeemed` is appended via `IEventBoundary<CouponRedemptionState>`, which enforces cross-stream optimistic concurrency through `AssertDcbConsistency`. If any matching tagged event was appended since the boundary was loaded, Marten throws `DcbConcurrencyException`.

**The choreography consequence:** After `CouponRedeemed` is appended, `RecordPromotionRedemptionHandler` reacts to it as a choreography consumer — loading the Promotion aggregate via `FetchForWriting` and appending `PromotionRedemptionRecorded` with proper tags. This keeps the promotion's redemption count eventually consistent without requiring a saga.

**Where to find the code:**
- `src/Promotions/Promotions/Coupon/RedeemCouponHandler.cs` — complete DCB handler
- `src/Promotions/Promotions/Coupon/CouponRedemptionState.cs` — boundary state with `Apply()` methods
- `src/Promotions/Promotions/CouponStreamId.cs` — tag ID definition pattern
- `src/Promotions/Promotions/PromotionStreamId.cs` — tag ID definition pattern
- `src/Promotions/Promotions/Promotion/RecordPromotionRedemptionHandler.cs` — choreography consequence
- `src/Promotions/Promotions.Api/Program.cs` — tag type registration + retry policies

### Implementation Checklist

Follow these steps when introducing DCB to a new BC. Drawn from the actual implementation in M40.0 S1B.

1. **Define strong-typed tag ID records** — one per stream type, `sealed record` with a single `Guid Value` property. Must NOT use raw `Guid` — .NET 10 added `Variant` and `Version` as public instance properties, breaking Marten's `ValueTypeInfo` validation which requires exactly one public instance property.

   ```csharp
   public sealed record CouponStreamId(Guid Value);
   public sealed record PromotionStreamId(Guid Value);
   ```

2. **Register tag types in `Program.cs`** — one `RegisterTagType<T>()` call per tag ID record, with a table suffix and aggregate binding.

   ```csharp
   opts.Events.RegisterTagType<CouponStreamId>("coupon").ForAggregate<Coupon>();
   opts.Events.RegisterTagType<PromotionStreamId>("promotion").ForAggregate<Promotion>();
   ```

3. **Add `DcbConcurrencyException` retry policy** — `DcbConcurrencyException` extends `MartenException`, NOT `JasperFx.ConcurrencyException`. They are siblings, not parent-child. A separate `opts.OnException<DcbConcurrencyException>()` entry is required alongside `ConcurrencyException`.

   ```csharp
   opts.OnException<ConcurrencyException>()
       .RetryWithCooldown(100.Milliseconds(), 250.Milliseconds());
   opts.OnException<DcbConcurrencyException>()
       .RetryWithCooldown(100.Milliseconds(), 250.Milliseconds());
   ```

4. **Update every write handler that appends to DCB-managed streams** to tag events explicitly. `[WriteAggregate]`, `IStartStream`, and raw `session.Events.Append(streamId, rawObject)` do NOT populate tag tables.

   ```csharp
   var wrapped = session.Events.BuildEvent(evt);
   wrapped.AddTag(new CouponStreamId(streamId));
   session.Events.Append(streamId, wrapped);
   ```

5. **Define the boundary state class** with `Apply()` methods for all event types from both streams. Include `public Guid Id { get; set; }` — Marten registers boundary models as documents; without `Id`, `DeleteAllDocumentsAsync()` throws during test cleanup.

6. **Write the DCB handler** with three methods:
   - `Load()` returning `EventTagQuery` — the fluent query spanning both streams
   - `Before()` with the boundary state as a plain parameter (no `[BoundaryModel]` attribute)
   - `Handle()` with `[BoundaryModel] IEventBoundary<TState>` — use `boundary.AppendOne()` for the atomic append with cross-stream concurrency

### Gotchas and Non-Obvious Behavior

**`StartStream` does not preserve tags.** When passing a pre-tagged `IEvent` to `StartStream`, Marten re-wraps the object and drops the tags. Use `Append` instead — it correctly preserves pre-wrapped `IEvent` objects. Streams are created implicitly on first append.

**`AndEventsOfType` is required, not optional.** Calling `.For(tagValue)` or `.Or(tagValue)` alone creates no query condition. Each tag arm must be followed by `.AndEventsOfType<T1, T2, ...>()`. Without it, `FetchForWritingByTags` throws `ArgumentException` at runtime.

**`[BoundaryModel]` on `Handle()` only.** Adding it to `Before()` as well causes Wolverine codegen error CS0128 (duplicate local variable `eventBoundaryOfCouponRedemptionState` in generated code). `Before()` receives the projected state as a plain parameter automatically.

**`DcbConcurrencyException` vs `ConcurrencyException` are separate types.** `DcbConcurrencyException` inherits from `Marten.Exceptions.MartenException`. `ConcurrencyException` is `JasperFx.ConcurrencyException`. Catching one does not catch the other. Add both to the retry policy in `Program.cs`.

**Tag tables are strictly opt-in at write time.** `[WriteAggregate]`, `IStartStream`, and raw `session.Events.Append(streamId, rawObject)` do NOT populate `mt_event_tag_*` tables. Every handler appending to a DCB-managed stream must use `BuildEvent()` + `AddTag()` + `Append()` (or `boundary.AppendOne()` in the DCB handler itself).

**Boundary models need a `Guid Id` property.** Marten treats DCB boundary models as documents. Without an `Id` property, `DeleteAllDocumentsAsync()` throws `InvalidDocumentException` during test cleanup, causing cascading test failures.

### Future DCB Candidates

- **Pricing** — approve a vendor price suggestion against current price rules (one decision spanning `VendorPriceSuggestion` + `ProductPrice` streams)
- **Inventory** — reserve all order lines or reserve none (one decision spanning multiple inventory streams)
- **Product Catalog** — family/variant membership rules after the event-sourcing migration

### Non-Candidates / Guardrails

- **Orders overall** should stay saga-driven; it coordinates long-running cross-BC work and is not a single synchronous decision boundary
- **Single-stream aggregate decisions** should remain normal event-sourced handlers — DCB should earn its complexity
- **BFF/query-only contexts** like Customer Experience are not natural DCB targets because they do not own the underlying invariants

### Reference Files

- `src/Promotions/Promotions/CouponStreamId.cs` — tag ID definition pattern
- `src/Promotions/Promotions/PromotionStreamId.cs` — tag ID definition pattern
- `src/Promotions/Promotions/Coupon/CouponRedemptionState.cs` — boundary state
- `src/Promotions/Promotions/Coupon/RedeemCouponHandler.cs` — complete DCB handler
- `src/Promotions/Promotions.Api/Program.cs` — tag type registration + retry policies
- `docs/research/marten-dcb-tagging-mechanics.md` — Marten source analysis

## Reference

- [dcb.events](https://dcb.events/) — pattern specification and examples
- [Wolverine Docs: Aggregate Handlers and Event Sourcing — DCB section](https://wolverinefx.io/guide/durability/marten/event-sourcing.html#dynamic-consistency-boundary-dcb)
- [Sara Pellegrini — "Killing the Aggregate"](https://sara.event-thinking.io/2023/04/kill-aggregate-chapter-1-I-will-tell-you-a-story.html) (pattern origin)
