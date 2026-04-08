# Skills Refresh — Post-M41.0 Fulfillment Remaster
## Four Skill File Updates

## Context

The Fulfillment BC Remaster (M41.0, S1–S5) introduced several new patterns and surfaced
important limitations that are not yet documented in the following skill files:

- `docs/skills/wolverine-message-handlers.md`
- `docs/skills/wolverine-sagas.md`
- `docs/skills/integration-messaging.md`
- `docs/skills/critterstack-testing-patterns.md`

This session applies targeted updates to all four. There are no `src/` or `tests/` changes —
this is documentation only.

---

## Required Reading

1. `docs/planning/milestones/fulfillment-remaster-s3-retrospective.md` — HazmatPolicy
   inline invocation deviation; ICarrierLabelService + ISystemClock test infrastructure
2. `docs/planning/milestones/fulfillment-remaster-s4-retrospective.md` — Orders saga
   migration; new OrderStatus values; CanBeCancelled guard update; idempotency guard
   correction for Reshipping
3. `docs/planning/milestones/fulfillment-remaster-s5-retrospective.md` — Dead handler
   cleanup pattern; new integration contracts; Correspondence enrichment handlers
4. `docs/skills/bc-remaster.md` v0.3 — The Dual-Publish Migration Strategy and Inline
   Policy Invocation Pattern are already documented here; the handler and integration
   skills need matching entries

---

## Mandatory Bookend

```bash
dotnet build
# Expected: 0 errors, 17 warnings (current post-M41.0 baseline)
```

No test runs required — this session modifies only documentation files under `docs/skills/`.

---

## Track A — `docs/skills/wolverine-message-handlers.md`

Two changes: a clarifying note appended to Anti-Pattern #12, and a new Anti-Pattern #13.

### A1: Update Table of Contents

In the Table of Contents, find the entry for item 12:
```
12. [DCB Handler Patterns](#dcb-handler-patterns--m400-addition) ⭐ *M40.0 Addition*
```

Add item 13 immediately after it:
```
13. [Anti-Pattern #13: Inline Cascade for `session.Events.Append()` Workflows](#13--inline-cascade-for-sessioneventsappend-workflows--m410-addition) ⭐ *M41.0 Addition*
```

### A2: Append clarifying note to Anti-Pattern #12

Find the end of the Anti-Pattern #12 section. The section ends with:
```
**Evidence:** INV-3 fix in M33.0 Session 1 — `AdjustInventoryEndpoint` mixed both patterns,
causing silent event publishing failures. The `LowStockDetected` → `AlertFeedView` chain broke
because manual-path events were not reaching the outbox.

**Reference:** [M33.0 Milestone Closure Retrospective — Cross-Cutting Learning 1](../../docs/planning/milestones/m33-0-milestone-closure-retrospective.md)
```

Append the following note directly after that reference line (still within Anti-Pattern #12):

```markdown
**Important distinction — cross-stream inline cascade (acceptable, see Anti-Pattern #13):**

Anti-Pattern #12 targets the case where `InvokeAsync()` and `session.Events.Append()` both
operate on the **same aggregate/stream** in the same request — two competing persistence
strategies for the same thing.

A related but distinct pattern is acceptable: using `bus.InvokeAsync()` to cascade to a
**different** downstream aggregate/stream, from within a handler that uses
`session.Events.Append()` for its own stream. This is the "inline policy invocation" pattern
documented in Anti-Pattern #13.

**The rule:** don't mix strategies for the same stream. Cascading to a *different* stream
inline is fine and is sometimes the only option when Wolverine's automatic cascading doesn't
apply.
```

### A3: Add Anti-Pattern #13

Insert the following new section **between** the end of Anti-Pattern #12 and the start of
`## File Organization and Naming`:

```markdown
### 13. ❌ Expecting Wolverine to Cascade Events Appended via `session.Events.Append()` ⭐ *M41.0 Addition*

**Problem:** When a handler uses the `Load()/Before()/Handle()` compound pattern and appends
events via `session.Events.Append(streamId, evt)` or `session.Events.StartStream<T>(...)`,
Wolverine **cannot cascade** those events to downstream handlers. Wolverine cascades from
messages *returned* from `Handle()` — not from events written directly to a Marten session
stream.

**Where this bites you:** Any handler that writes events to the stream and also needs a
downstream policy handler or follow-on command to react to those events. The downstream
handler simply never fires — no error, no warning.

**❌ WRONG — expecting cascade from session-written event:**

```csharp
public static class FulfillmentRequestedHandler
{
    public static OutgoingMessages Handle(
        RequestFulfillment cmd,
        IDocumentSession session)
    {
        // WorkOrderCreated is appended to the stream...
        session.Events.StartStream<WorkOrder>(workOrderId, new WorkOrderCreated(...));

        // ...but Wolverine CANNOT trigger HazmatPolicyHandler here.
        // WorkOrderCreated is not in the return value — it's in the session.
        // HazmatPolicyHandler.Handle(WorkOrderCreated) will NEVER be invoked.

        return outgoing;
    }
}
```

**Why it doesn't work:**
- Wolverine's cascade mechanism inspects the *return value* of `Handle()` for outgoing messages
- Events appended via `session.Events.Append()` or `session.Events.StartStream()` go directly
  to Marten's unit-of-work — they are invisible to Wolverine's cascade pipeline
- This is a fundamental architectural constraint, not a configuration issue

**✅ CORRECT — call the policy inline:**

```csharp
public static class FulfillmentRequestedHandler
{
    public static async Task<OutgoingMessages> Handle(
        RequestFulfillment cmd,
        IDocumentSession session,
        IMessageBus bus)   // Inject IMessageBus for inline dispatch
    {
        session.Events.StartStream<WorkOrder>(workOrderId, new WorkOrderCreated(...));

        // Policy logic invoked inline — don't wait for Wolverine to cascade
        await HazmatPolicy.CheckAndApply(workOrderId, cmd.LineItems, session, bus);

        return outgoing;
    }
}
```

**Rules for the inline cascade pattern:**
- Use `bus.InvokeAsync()` (not `bus.PublishAsync()`) — synchronous within the handler's context
- `bus.ScheduleAsync()` remains valid for delayed/scheduled delivery
- Call the inline policy in **every handler** that creates the relevant stream, not just one.
  In the Fulfillment Remaster, three handlers all call `HazmatPolicy.CheckAndApply()`:
  `FulfillmentRequestedHandler`, `CreateReshipmentHandler`, and
  `SplitOrderIntoShipmentsHandler` — because all three create WorkOrder streams
- This is NOT a violation of Anti-Pattern #12, which targets mixing strategies for the
  **same** stream. Here the inline `InvokeAsync()` targets a **different** downstream aggregate

**CritterSupply reference examples:**
- `src/Fulfillment/Fulfillment/WorkOrders/HazmatPolicy.cs` — inline static method pattern (S3)
- `src/Fulfillment/Fulfillment/Shipments/GenerateShippingLabel.cs` — invoked inline from
  `PackingCompletedHandler` when the label cascade couldn't be expressed as a return value (S2)

**Reference:** [Fulfillment Remaster S3 Retrospective — Deviation #1](../../planning/milestones/fulfillment-remaster-s3-retrospective.md)
```

---

## Track B — `docs/skills/wolverine-sagas.md`

Four changes: handler table, CanBeCancelled example, new OrderStatus/properties section,
and additions to the DOs and DO NOTs tables.

### B1: Replace the Handler Method Summary table

In the Quick Reference section, find the entire Handler Method Summary table. Replace it
with the updated table that reflects the Orders saga's state after M41.0 S4:

**Find this table (the three Fulfillment rows are the key targets):**
```
| `Handle(ShipmentDispatched)` | Fulfillment BC | `void` | `Shipped` status |
| `Handle(ShipmentDelivered)` | Fulfillment BC | `OutgoingMessages` | `Delivered` + schedules return window |
| `Handle(ShipmentDeliveryFailed)` | Fulfillment BC | `void` | Delivery failure tracking |
```

**Replace the entire table with:**

```markdown
| Handler | Message Source | Returns | Key Behavior |
|---------|--------------|---------|-------------|
| `Handle(CancelOrder)` | Orders API | `OutgoingMessages` | Compensation + conditional `MarkCompleted()` |
| `Handle(PaymentCaptured)` | Payments BC | `OutgoingMessages` | Tracks `PaymentId`; commits if inventory ready |
| `Handle(PaymentFailed)` | Payments BC | `OutgoingMessages` | Releases reservations |
| `Handle(PaymentAuthorized)` | Payments BC | `void` | Sets `PendingPayment` status |
| `Handle(RefundCompleted)` | Payments BC | `void` | Closes `Cancelled` / `OutOfStock` sagas |
| `Handle(RefundFailed)` | Payments BC | `void` | Logs; no status change |
| `Handle(ReservationConfirmed)` | Inventory BC | `OutgoingMessages` | Multi-SKU tracking; race-condition fix |
| `Handle(ReservationFailed)` | Inventory BC | `OutgoingMessages` | `OutOfStock` compensation |
| `Handle(ReservationCommitted)` | Inventory BC | `OutgoingMessages` | Fulfillment request when all committed |
| `Handle(ReservationReleased)` | Inventory BC | `void` | Compensation acknowledgement |
| `Handle(ShipmentHandedToCarrier)` | Fulfillment BC | `void` | `Shipped` status *(replaces ShipmentDispatched — M41.0 S4)* |
| `Handle(TrackingNumberAssigned)` | Fulfillment BC | `void` | Stores tracking number; no status change |
| `Handle(ShipmentDelivered)` | Fulfillment BC | `OutgoingMessages` | `Delivered` + schedules return window |
| `Handle(ReturnToSenderInitiated)` | Fulfillment BC | `void` | `DeliveryFailed` status *(replaces ShipmentDeliveryFailed — M41.0 S4)* |
| `Handle(ReshipmentCreated)` | Fulfillment BC | `void` | `Reshipping` status; stores `ActiveReshipmentShipmentId` |
| `Handle(BackorderCreated)` | Fulfillment BC | `void` | `Backordered` status |
| `Handle(FulfillmentCancelled)` | Fulfillment BC | `OutgoingMessages` | Refund + release inventory + `Cancelled` |
| `Handle(OrderSplitIntoShipments)` | Fulfillment BC | `void` | Stores `ShipmentCount`; no status change |
| `Handle(ReturnWindowExpired)` | Scheduled | `void` | Closes if no active returns |
| `Handle(ReturnRequested)` | Returns BC | `void` | Adds to `ActiveReturnIds` |
| `Handle(ReturnCompleted)` | Returns BC | `OutgoingMessages` | Refund request + conditional close |
| `Handle(ReturnDenied)` | Returns BC | `void` | Removes from active + conditional close |
| `Handle(ReturnRejected)` | Returns BC | `void` | Removes from active + conditional close |
| `Handle(ReturnExpired)` | Returns BC | `void` | Removes from active + conditional close |
```

### B2: Update the CanBeCancelled() code example

In the `## Shared Guard: CanBeCancelled()` section, find the guard definition:

```csharp
public static bool CanBeCancelled(OrderStatus status) =>
    status is not (OrderStatus.Shipped or OrderStatus.Delivered
        or OrderStatus.Closed or OrderStatus.Cancelled
        or OrderStatus.OutOfStock or OrderStatus.PaymentFailed);
```

Replace it with:

```csharp
// M41.0 S4: Shipped removed from exclusion list.
// FulfillmentCancelled can arrive pre-handoff (order is Shipped but carrier hasn't
// taken possession yet). DeliveryFailed, Reshipping, and Backordered are also
// cancellable — they are non-terminal fulfillment states, not post-delivery.
public static bool CanBeCancelled(OrderStatus status) =>
    status is not (OrderStatus.Delivered or OrderStatus.Closed
        or OrderStatus.Cancelled or OrderStatus.OutOfStock
        or OrderStatus.PaymentFailed);
```

### B3: Add new section after CanBeCancelled, before DOs and DO NOTs

Insert the following new section between `## Shared Guard: CanBeCancelled()` and
`## DOs and DO NOTs`:

```markdown
## New OrderStatus Values and Saga Properties (M41.0 S4)

⭐ *M41.0 S4 Addition*

The Fulfillment Remaster introduced new lifecycle states and saga properties when the Orders
saga was migrated to the new Fulfillment contract surface.

### New OrderStatus Values

```csharp
/// <summary>All delivery attempts exhausted; carrier returning to sender. May transition to Reshipping.</summary>
DeliveryFailed,

/// <summary>A reshipment is in progress. Will return to Shipped when replacement is handed to carrier.</summary>
Reshipping,

/// <summary>Fulfillment waiting on stock replenishment. Reshipment or cancellation may follow.</summary>
Backordered,
```

### New Saga Properties

```csharp
/// <summary>Tracking number assigned by Fulfillment BC after label generation.</summary>
public string? TrackingNumber { get; set; }

/// <summary>Number of shipments this order has been split into (multi-FC routing).</summary>
public int ShipmentCount { get; set; } = 1;

/// <summary>Shipment ID of the active reshipment, if any.</summary>
public Guid? ActiveReshipmentShipmentId { get; set; }
```

### Pattern for Adding Nullable OrderDecision Fields

When new messages introduce new saga state, follow the consistent nullable field pattern:

**1. Add the field to `OrderDecision`:**
```csharp
public sealed record OrderDecision
{
    // ... existing fields ...
    public string? TrackingNumber { get; init; }
    public Guid? ActiveReshipmentShipmentId { get; init; }
    public int? ShipmentCount { get; init; }
}
```

**2. Return the new state from the Decider pure function:**
```csharp
public static OrderDecision HandleTrackingNumberAssigned(
    Order current,
    FulfillmentMessages.TrackingNumberAssigned message)
{
    return new OrderDecision { TrackingNumber = message.TrackingNumber };
}
```

**3. Apply in the saga handler with a null check:**
```csharp
public void Handle(FulfillmentMessages.TrackingNumberAssigned message)
{
    var decision = OrderDecider.HandleTrackingNumberAssigned(this, message);
    if (decision.TrackingNumber != null) TrackingNumber = decision.TrackingNumber;
}
```

The null check pattern (`if (decision.Field != null) Field = decision.Field`) preserves the
existing value when the Decider signals no change. This pattern is consistent throughout
the entire Order saga.

---

## ⚠️ Non-Terminal Mid-Lifecycle States and Idempotency Guards

⭐ *M41.0 S4 Critical Discovery*

When a saga can cycle through states (e.g., `DeliveryFailed` → `Reshipping` → `Shipped` →
`Delivered` for a reshipment scenario), be extremely careful about which statuses appear in
idempotency guards for the events that drive those transitions.

**The Reshipping trap:** `ShipmentHandedToCarrier` transitions the Order saga to `Shipped`.
A naive idempotency guard might exclude `Reshipping` from this handler:

```csharp
// ❌ WRONG — this permanently traps the saga in Reshipping
public static OrderDecision HandleShipmentHandedToCarrier(
    Order current,
    FulfillmentMessages.ShipmentHandedToCarrier message)
{
    if (current.Status is OrderStatus.Shipped or OrderStatus.Delivered
        or OrderStatus.Closed or OrderStatus.Reshipping) // ← DO NOT include Reshipping
        return new OrderDecision();

    return new OrderDecision { Status = OrderStatus.Shipped };
}
```

**Why this is wrong:** The reshipment lifecycle requires:
1. `ReturnToSenderInitiated` → `DeliveryFailed`
2. `ReshipmentCreated` → `Reshipping`
3. `ShipmentHandedToCarrier` → **must transition to `Shipped`**

If `Reshipping` is in the guard for step 3, the saga can never escape that state. The
replacement shipment's carrier handoff event is silently ignored.

**Rule:** Only include truly terminal states or states where the event is genuinely a
duplicate in idempotency guards. `Reshipping` is a non-terminal mid-lifecycle state that
**must** be able to receive `ShipmentHandedToCarrier` to make forward progress.

**Verify with the full lifecycle test:**

```csharp
[Fact]
public async Task Full_Lifecycle_With_Reshipment()
{
    // Place → inventory → payment → FulfillmentRequested
    // → ShipmentHandedToCarrier (Shipped)
    // → ReturnToSenderInitiated (DeliveryFailed)
    // → ReshipmentCreated (Reshipping)
    // → ShipmentHandedToCarrier again (Shipped) ← the critical transition
    // → ShipmentDelivered (Delivered)
}
```

This test only passes if `Reshipping` is NOT in the `ShipmentHandedToCarrier` idempotency guard.
```

### B4: Update the DOs and DO NOTs tables

At the end of the ✅ DOs table, add a new row:

```
| 16 | **DO** be careful about non-terminal mid-lifecycle states in idempotency guards | States like `Reshipping` must be able to receive the event that transitions them forward (e.g., `ShipmentHandedToCarrier`); including them in guards permanently traps the saga |
```

At the end of the ❌ DO NOTs table, add a new row:

```
| 11 | **DO NOT** include non-terminal mid-lifecycle states (like `Reshipping`) in idempotency guards for the event that advances them | The saga will be permanently stuck and the full lifecycle reshipment test will fail |
```

---

## Track C — `docs/skills/integration-messaging.md`

Two changes: update the "Adding a New Integration" checklist, and add Lesson 16.

### C1: Update the "Adding a New Integration" checklist

Find the `### Checklist` section under `## Adding a New Integration`. The current list has
8 items. Replace steps 7 and 8 with the following (expanding 2 items into 3):

**Find:**
```markdown
7. **Write cross-BC smoke test** to verify RabbitMQ pipeline end-to-end
8. **Update `CONTEXTS.md`** only if new BC integration is introduced (not for additional messages in existing integration)
```

**Replace with:**
```markdown
7. **Write cross-BC smoke test** to verify RabbitMQ pipeline end-to-end
8. **When retiring a contract:** Run `grep -r "RetiredEventName" src/` for every message
   type being removed. Classify every result as:
   - **Active** — still has a publisher; not being retired
   - **Dead-needs-migration** — should consume the replacement event; update the handler
   - **Dead-no-publisher** — no publisher emits this anymore; delete or add `[Obsolete]`
   Never close a milestone with unresolved dead handlers — they compile silently but serve
   no purpose and mislead future sessions.
9. **Update `CONTEXTS.md`** when:
   - A **new BC-to-BC integration direction** is introduced (e.g., first-ever message from
     Pricing to Inventory)
   - **Contract names change** — retiring `ShipmentDispatched` in favour of
     `ShipmentHandedToCarrier` updates the Fulfillment entry even though the direction
     (Fulfillment → Orders) hasn't changed
   - **Legacy contracts are retired** — remove them from the relevant BC entry
   
   Do NOT update for adding new messages to an existing integration direction with no
   naming changes.
```

### C2: Add Lesson 16

Find `### Lesson 15: Message Enrichment Tradeoffs Must Be Documented` and append the
following new lesson section immediately after it (before `---` and `## Appendix`):

```markdown
### Lesson 16: Dual-Publish Migration Strategy for Contract Retirement (M41.0)

**Use Case:** Retiring or renaming an integration contract when multiple consumers depend on it.

**Problem:** A hard cutover (removing the old event and publishing only the new one in the
same session) requires all consumers to migrate simultaneously. For a remaster series spanning
multiple sessions, this is impractical and high-risk — especially when one of the consumers
is the Orders saga, which coordinates payments, inventory, and fulfillment.

**Pattern — Dual-Publish during the migration period:**

The publisher temporarily emits both the old and new event. A migration comment marks the
temporary dual-publish clearly:

```csharp
// MIGRATION: Dual-publish for backward compatibility with Orders saga.
// Remove after Orders saga gains ShipmentHandedToCarrier handler (M41.0 S4).
outgoing.Add(new LegacyMessages.ShipmentDispatched(shipmentId, orderId, carrier, trackingNumber, at));
// New contract:
outgoing.Add(new ShipmentHandedToCarrier(shipmentId, orderId, carrier, trackingNumber, at));
```

This keeps all existing consumers working without modification. The S1 implementation session
activates the dual-publish. A later coordinated migration session retires it.

**The coordinated migration session (S4 in the Fulfillment Remaster):**
1. Add new handlers in all consumers — **add first**
2. Verify all consumer test suites pass with the new handlers
3. Remove legacy handlers from consumers — **remove second**
4. Remove the dual-publish from the publisher
5. Run the full cross-BC test suite as the final gate

**Strict sequencing: add → verify → remove. Never remove before adding.**

**What the migration comment must contain:**
- What it maintains compatibility with: `// Orders saga`
- When it can be removed: `// Remove after Orders saga gains ShipmentHandedToCarrier handler`
- The milestone/session where retirement happens: `// M41.0 S4`

**After the dual-publish is removed — dead consumer verification:**

Run `grep -r "ShipmentDispatched" src/` (and equivalent for every retired contract name).
For each result, determine whether the consumer is still active or is now dead. Dead handlers
(no publisher) must be migrated to the replacement event or deleted before the milestone closes.
See the "Adding a New Integration" checklist Step 8 for the full classification process.

**CritterSupply example (M41.0):**
- `ShipmentDispatched` → `ShipmentHandedToCarrier`: S1 dual-publish, S4 retirement
- `ShipmentDeliveryFailed` → `ReturnToSenderInitiated`: S1 dual-publish, S4 retirement
- Consumer BCs affected: Orders, Correspondence, Customer Experience, Backoffice
- Dead consumers found post-S4: 8 handlers/projections (Customer Experience + Backoffice)
- Cleaned up in: M41.0 S5 (milestone closure session)

**Reference:** [Fulfillment Remaster S1 Retrospective](../../planning/milestones/fulfillment-remaster-s1-retrospective.md) · [S4 Retrospective](../../planning/milestones/fulfillment-remaster-s4-retrospective.md)

Also update the document version footer at the very end of the file:

**Find:**
```
**Document Version:** 1.0
**Last Updated:** 2026-03-15
```

**Replace with:**
```
**Document Version:** 1.1
**Last Updated:** 2026-04-07
```
```

---

## Track D — `docs/skills/critterstack-testing-patterns.md`

Two new sections added before `## Key Principles`.

### D1: Update Table of Contents

Find the TOC entry:
```
15. [Key Principles](#key-principles)
```

Replace it with:
```
15. [Testing Time-Dependent Handlers — ISystemClock Pattern](#testing-time-dependent-handlers--isystemclock-pattern) ⭐ *M41.0 Addition*
16. [Testing Failure Paths — Injectable Failure Stub Pattern](#testing-failure-paths--injectable-failure-stub-pattern) ⭐ *M41.0 Addition*
17. [Key Principles](#key-principles)
```

### D2: Insert new section 15 — ISystemClock Pattern

Insert the following section **immediately before** `## Key Principles`:

```markdown
## Testing Time-Dependent Handlers — ISystemClock Pattern

⭐ *M41.0 S3 Addition*

Handlers that check elapsed time (scheduled jobs, SLA monitoring, lost-in-transit detection,
return window expiry) cannot use `DateTimeOffset.UtcNow` directly in production code — tests
would either need to wait real time or inject race conditions. The solution is an injectable
clock abstraction.

### The Infrastructure

**`ISystemClock` interface (production code, in the BC's domain project):**

```csharp
public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Production implementation — wraps DateTimeOffset.UtcNow.</summary>
public class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
```

**`FrozenSystemClock` (test infrastructure, in the integration test project):**

```csharp
/// <summary>Test implementation — settable clock for time-based scenario control.</summary>
public class FrozenSystemClock : ISystemClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}
```

**In `Program.cs` (production):**
```csharp
builder.Services.AddSingleton<ISystemClock, SystemClock>();
```

**In `TestFixture.cs` (tests):**
```csharp
// Expose the clock as a public singleton so test classes can advance it
public FrozenSystemClock Clock { get; private set; } = new FrozenSystemClock();

// During host initialization — swap the production clock for the frozen one:
Host = await AlbaHost.For<Program>(builder =>
{
    builder.ConfigureServices(services =>
    {
        services.RemoveAll<ISystemClock>();
        services.AddSingleton<ISystemClock>(Clock); // Share the fixture's instance
        // ... other test services ...
    });
});
```

### Usage in Tests

**Reset the clock at the start of each test class** — the `FrozenSystemClock` is a singleton
shared across all classes in the xUnit collection. If one class advances the clock, the next
inherits that state unless it resets.

```csharp
public class TimeBasedMonitoringTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public TimeBasedMonitoringTests(TestFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.CleanAllDocumentsAsync();
        _fixture.Clock.UtcNow = DateTimeOffset.UtcNow; // Reset to real "now" each time
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CheckForLostShipment_After_8_Days_Detects_Lost()
    {
        var shipmentId = await SeedInTransitShipment();

        // Advance the clock past the 7-day lost threshold, then invoke the scheduled check
        _fixture.Clock.UtcNow = DateTimeOffset.UtcNow.AddDays(8);
        await _fixture.ExecuteAndWaitAsync(new CheckForLostShipment(shipmentId));

        using var session = _fixture.GetDocumentSession();
        var shipment = await session.Events.AggregateStreamAsync<Shipment>(shipmentId);
        shipment!.Status.ShouldBe(ShipmentStatus.LostInTransit);
    }

    [Fact]
    public async Task CheckForLostShipment_At_5_Days_Does_Not_Detect_Lost()
    {
        var shipmentId = await SeedInTransitShipment();

        _fixture.Clock.UtcNow = DateTimeOffset.UtcNow.AddDays(5); // Below threshold
        await _fixture.ExecuteAndWaitAsync(new CheckForLostShipment(shipmentId));

        using var session = _fixture.GetDocumentSession();
        var shipment = await session.Events.AggregateStreamAsync<Shipment>(shipmentId);
        shipment!.Status.ShouldBe(ShipmentStatus.InTransit); // Unchanged
    }
}
```

### Handler Pattern Using ISystemClock

```csharp
public static class CheckForLostShipmentHandler
{
    public static async Task<OutgoingMessages?> Handle(
        CheckForLostShipment message,
        IDocumentSession session,
        ISystemClock clock)  // Injected — never call DateTimeOffset.UtcNow directly
    {
        var shipment = await session.Events.AggregateStreamAsync<Shipment>(message.ShipmentId);
        if (shipment is null || shipment.Status != ShipmentStatus.InTransit)
            return null;

        var daysSinceLastScan = (clock.UtcNow - shipment.LastCarrierScanAt).TotalDays;
        if (daysSinceLastScan < 7) return null;

        var outgoing = new OutgoingMessages();
        session.Events.Append(shipment.Id, new ShipmentLostInTransit(shipment.Id, clock.UtcNow));
        outgoing.Add(new Messages.Contracts.Fulfillment.ShipmentLostInTransit(...));
        return outgoing;
    }
}
```

**CritterSupply reference:**
- `src/Fulfillment/Fulfillment/ISystemClock.cs` — interface + `SystemClock` production wrapper
- `tests/Fulfillment/Fulfillment.Api.IntegrationTests/FrozenSystemClock.cs` — test clock
- `tests/Fulfillment/Fulfillment.Api.IntegrationTests/Shipments/TimeBasedMonitoringTests.cs`

**Reference:** [Fulfillment Remaster S3 Retrospective — Part 1B](../../planning/milestones/fulfillment-remaster-s3-retrospective.md)

---

## Testing Failure Paths — Injectable Failure Stub Pattern

⭐ *M41.0 S3 Addition*

When a handler wraps an external service (label generation, payment gateway, carrier API),
the default test fixture uses a stub that always succeeds. Testing failure paths requires
injecting a failing implementation without affecting the happy-path test classes.

### The Pattern

**Step 1: Extract the interface (production code):**

```csharp
public interface ICarrierLabelService
{
    Task<LabelResult> GenerateLabelAsync(ShipmentDetails details, CancellationToken ct);
}

/// <summary>Stub — always succeeds in Development and CI by default.</summary>
public class StubCarrierLabelService : ICarrierLabelService
{
    public Task<LabelResult> GenerateLabelAsync(ShipmentDetails details, CancellationToken ct)
        => Task.FromResult(new LabelResult(
            TrackingNumber: $"STUB-{Guid.NewGuid():N}",
            LabelUrl: "https://stub.example.com/label.pdf",
            Success: true));
}
```

**Step 2: Register the stub in `Program.cs`:**
```csharp
builder.Services.AddSingleton<ICarrierLabelService, StubCarrierLabelService>();
```

**Step 3: Create the always-failing stub (test infrastructure only):**

```csharp
/// <summary>
/// Always-failing stub. Used only in <see cref="LabelFailureTestFixture"/>.
/// Never register this in the main TestFixture.
/// </summary>
public class AlwaysFailingCarrierLabelService : ICarrierLabelService
{
    public Task<LabelResult> GenerateLabelAsync(ShipmentDetails details, CancellationToken ct)
        => Task.FromResult(new LabelResult(
            TrackingNumber: null,
            LabelUrl: null,
            Success: false,
            ErrorMessage: "Carrier API unavailable (test stub)"));
}
```

**Step 4: Create a dedicated test fixture with its own xUnit collection:**

```csharp
// IMPORTANT: Separate collection — shares nothing with the main test fixture
[CollectionDefinition(Name)]
public class LabelFailureTestCollection : ICollectionFixture<LabelFailureTestFixture>
{
    public const string Name = "Label Failure Tests";
}

public class LabelFailureTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("fulfillment_failure_test_db")       // Different name from main fixture
        .WithName($"fulfillment-failure-postgres-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.ConfigureMarten(opts => opts.Connection(connectionString));
                services.DisableAllExternalWolverineTransports();

                // Swap the default stub for the always-failing version
                services.RemoveAll<ICarrierLabelService>();
                services.AddSingleton<ICarrierLabelService, AlwaysFailingCarrierLabelService>();
            });
        });
    }

    // DisposeAsync follows the standard TestFixture pattern
}
```

**Step 5: Write failure-path tests bound to the dedicated collection:**

```csharp
[Collection(LabelFailureTestCollection.Name)] // NOT the main collection
public class LabelGenerationFailureTests : IAsyncLifetime
{
    private readonly LabelFailureTestFixture _fixture;

    public LabelGenerationFailureTests(LabelFailureTestFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GenerateShippingLabel_WhenCarrierFails_AppendsLabelGenerationFailed()
    {
        var shipmentId = await SeedShipmentReadyForLabel();

        await _fixture.ExecuteAndWaitAsync(new GenerateShippingLabel(shipmentId));

        using var session = _fixture.GetDocumentSession();
        var shipment = await session.Events.AggregateStreamAsync<Shipment>(shipmentId);
        shipment!.Status.ShouldBe(ShipmentStatus.LabelGenerationFailed);
    }
}
```

### Why a Separate xUnit Collection?

xUnit collections share a single fixture instance. Injecting `AlwaysFailingCarrierLabelService`
into the main fixture would break every label-related test in every class in that collection —
not just the failure-path tests.

A separate `[CollectionDefinition]` creates a fully isolated fixture with its own Postgres
container and its own DI configuration. The cost is a second container startup, which is
acceptable for a small targeted set of failure-path tests.

**Decision guide:**

| Situation | Approach |
|---|---|
| Main stub "never fails"; need failure-path tests | Separate fixture + separate xUnit collection |
| Stub has conditional failure mode (e.g., toggle flag) | `RemoveAll + AddSingleton` within the main fixture per test class |
| Clock / time advancement needed | `FrozenSystemClock` singleton on main fixture — reset in `InitializeAsync()` |

**CritterSupply reference:**
- `src/Fulfillment/Fulfillment/Shipments/ICarrierLabelService.cs`
- `tests/Fulfillment/Fulfillment.Api.IntegrationTests/AlwaysFailingCarrierLabelService.cs`
- `tests/Fulfillment/Fulfillment.Api.IntegrationTests/LabelFailureTestFixture.cs`
- `tests/Fulfillment/Fulfillment.Api.IntegrationTests/Shipments/LabelGenerationFailureTests.cs`

**Reference:** [Fulfillment Remaster S3 Retrospective — Part 1A](../../planning/milestones/fulfillment-remaster-s3-retrospective.md)

---
```

---

## Session Bookend — Verification

```bash
dotnet build
# Required: 0 errors, 17 warnings (unchanged — no source files touched)
```

No test execution needed. Confirm all four files have been saved and are valid Markdown.

---

## Commit Convention

```
skills: wolverine-message-handlers — Anti-Pattern #13 (inline cascade for session.Events.Append)
skills: wolverine-sagas — handler table updated (M41.0 S4 contracts); CanBeCancelled; new OrderStatus values + Reshipping trap
skills: integration-messaging — Lesson 16 (dual-publish migration); dead consumer grep checklist; CONTEXTS.md guidance clarified
skills: critterstack-testing-patterns — ISystemClock/FrozenSystemClock pattern; injectable failure stub + separate collection pattern
```

---

## Role

**@PSA — Principal Software Architect**
These are precision edits to large files. Read the current file content before making any
change to find the exact insertion point. Do not regenerate sections that are not being
changed. The handler table replacement in Track B is the most error-prone — verify the
before and after row counts.

**@DOE — Documentation Engineer**
Verify that code examples compile conceptually — method signatures, type names, and namespace
references should be consistent with what exists in `src/Fulfillment/`. Check that all
cross-reference links use relative paths matching the `docs/` directory structure.
