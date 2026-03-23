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

After reviewing the DCB specification/examples and the current CritterSupply codebase, the strongest repository-specific guidance is:

- DCB is a good fit for **one immediate business decision inside one bounded context** when the invariant spans multiple event streams
- DCB is **not** a replacement for CritterSupply's long-running orchestration sagas (for example, the Orders saga coordinating Payments, Inventory, and Fulfillment)
- The best first DCB candidate is **Promotions**, where one redemption decision currently spans both `Coupon` and `Promotion`

### Why DCB Is a Thing Here

In CritterSupply terms, DCB matters when the traditional event-sourced approach would otherwise force you to:

- split one business fact across two aggregate streams
- coordinate those writes with a saga or multi-step handler flow
- emit compensating events to repair an artificial partial success

That is valuable for cases like **"can this redemption happen now?"** or **"can this approval still be applied right now?"** It is not the right tool for workflows that are naturally long-running, cross-BC, or side-effect heavy.

### Recommended DCB Candidates

#### 1. Promotions — coupon redemption + promotion cap boundary

**Best first candidate.**

Why it fits:

- `Coupon` and `Promotion` are separate event-sourced concepts
- a successful redemption is logically **one fact**, but today's design splits validation between both models
- the DCB boundary can enforce coupon validity, promotion activity, redemption caps, and future per-customer limits in one decision

Relevant repository areas:

- `src/Promotions/Promotions/Coupon/`
- `src/Promotions/Promotions/Promotion/`
- `src/Promotions/Promotions/OrderIntegration/`
- `docs/planning/promotions-event-modeling.md`

Why DCB is better than the likely alternative:

- avoids a mini-saga or dual-write flow for redemption bookkeeping
- reduces "coupon was valid, then later lost the race" behavior under concurrency
- maps closely to the DCB creators' `opt-in-token` and `course-subscriptions` examples

#### 2. Pricing — approve a vendor price suggestion against current price rules

**Strong second candidate.**

Why it fits:

- the business decision spans a pending `VendorPriceSuggestion` and the authoritative `ProductPrice` stream
- operators care about one immediate truth: **can this suggestion still be approved right now?**
- DCB can prevent split outcomes like "suggestion approved but price change rejected"

Relevant repository areas:

- `docs/planning/pricing-event-modeling.md`
- `docs/features/pricing/vendor-price-suggestions.feature`
- `src/Pricing/Pricing/Products/`
- `src/Shared/Messages.Contracts/Pricing/VendorPriceSuggestionSubmitted.cs`

#### 3. Inventory — reserve all order lines or reserve none

**Viable, but only after durability/idempotency hardening.**

Why it fits:

- one order-level reservation decision may span multiple inventory streams
- DCB could model **reserve-all-or-fail** without partial reservations and compensating releases

Why it is not the first move:

- the current inventory pain is still more about message durability, idempotency, and reservation lifecycle hardening than DCB specifically
- the Orders saga remains the correct pattern for the broader payment/inventory/fulfillment workflow

Relevant repository areas:

- `src/Inventory/Inventory/Management/`
- `src/Orders/Orders/Placement/`
- `docs/workflows/inventory-workflows.md`
- `docs/decisions/0029-order-saga-design-decisions.md`

#### 4. Product Catalog — family/variant membership after the event-sourcing migration

**Possible future candidate, but not a priority.**

Why it fits:

- once Product Catalog is event-sourced, some family/variant membership rules may span a family boundary plus one or more variant/product streams

Why it is lower priority:

- Product Catalog is still a Marten document store today
- the immediate value is getting the event-sourced migration and variant model in place first
- some family/variant workflows may still be adequately handled without DCB

Relevant repository areas:

- `src/Product Catalog/ProductCatalog/Products/Product.cs`
- `docs/planning/catalog-listings-marketplaces-evolution-plan.md`
- `docs/planning/catalog-variant-model.md`

### Non-Candidates / Guardrails

- **Orders overall** should stay saga-driven; it coordinates long-running cross-BC work and is not a single synchronous decision boundary
- **single-stream aggregate decisions** should remain normal event-sourced handlers — DCB should earn its complexity
- **BFF/query-only contexts** like Customer Experience are not natural DCB targets because they do not own the underlying invariants

## Reference

- [dcb.events](https://dcb.events/) — pattern specification and examples
- [Wolverine Docs: Aggregate Handlers and Event Sourcing — DCB section](https://wolverinefx.io/guide/durability/marten/event-sourcing.html#dynamic-consistency-boundary-dcb)
- [Sara Pellegrini — "Killing the Aggregate"](https://sara.event-thinking.io/2023/04/kill-aggregate-chapter-1-I-will-tell-you-a-story.html) (pattern origin)
