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

## CritterSupply Usage

> 🚧 **Coming soon.** A concrete CritterSupply example will be added here once a good domain fit is identified. Likely candidates involve cross-entity invariants in the **Promotions**, **Inventory**, or **Orders** bounded contexts — for instance, applying a promotion requires checking both eligibility rules and current redemption counts across multiple streams.

## Reference

- [dcb.events](https://dcb.events/) — pattern specification and examples
- [Wolverine Docs: Aggregate Handlers and Event Sourcing — DCB section](https://wolverinefx.io/guide/durability/marten/event-sourcing.html#dynamic-consistency-boundary-dcb)
- [Sara Pellegrini — "Killing the Aggregate"](https://sara.event-thinking.io/2023/04/kill-aggregate-chapter-1-I-will-tell-you-a-story.html) (pattern origin)
