# Skills Refresh — Post-M41.0 Fulfillment Remaster (1 of 2)
## Wolverine Handler and Saga Skills

## Context

The Fulfillment BC Remaster (M41.0, S1–S5) introduced patterns not yet documented in:

- `docs/skills/wolverine-message-handlers.md` — missing Anti-Pattern #13
- `docs/skills/wolverine-sagas.md` — handler table stale; missing new OrderStatus values and
  a critical idempotency guard finding

This is a documentation-only session. No `src/` or `tests/` files are touched.

**Prompt 2 of 2** covers `integration-messaging.md` and `critterstack-testing-patterns.md`.

---

## Required Reading

1. `docs/planning/milestones/fulfillment-remaster-s3-retrospective.md` — HazmatPolicy
   inline invocation deviation (Deviation #1)
2. `docs/planning/milestones/fulfillment-remaster-s4-retrospective.md` — Orders saga
   migration; new OrderStatus values; CanBeCancelled guard update; idempotency guard
   correction for Reshipping

---

## Mandatory Bookend

```bash
dotnet build
# Expected: 0 errors, 17 warnings (unchanged — no source files modified)
```

---

## Track A — `docs/skills/wolverine-message-handlers.md`

Two changes: a clarifying note appended to Anti-Pattern #12, and a new Anti-Pattern #13.

### A1: Update Table of Contents

Find the TOC entry for item 12:
```
12. [DCB Handler Patterns](#dcb-handler-patterns--m400-addition) ⭐ *M40.0 Addition*
```

Add item 13 immediately after it:
```
13. [Anti-Pattern #13: Inline Cascade for `session.Events.Append()` Workflows](#13--inline-cascade-for-sessioneventsappend-workflows--m410-addition) ⭐ *M41.0 Addition*
```

### A2: Append clarifying note to Anti-Pattern #12

The Anti-Pattern #12 section currently ends with:

```
**Reference:** [M33.0 Milestone Closure Retrospective — Cross-Cutting Learning 1](../../docs/planning/milestones/m33-0-milestone-closure-retrospective.md)
```

Append the following note directly after that reference line (still within the Anti-Pattern
#12 section, before the next `###` heading):

```markdown
**Important distinction — cross-stream inline cascade (acceptable, see Anti-Pattern #13):**

Anti-Pattern #12 targets the case where `InvokeAsync()` and `session.Events.Append()` both
operate on the **same aggregate/stream** in the same request — two competing persistence
strategies for the same thing.

A related but distinct pattern is acceptable: using `bus.InvokeAsync()` to cascade to a
**different** downstream aggregate/stream from within a handler that uses
`session.Events.Append()` for its own stream. This is the "inline policy invocation" pattern
documented in Anti-Pattern #13.

**The rule:** don't mix strategies for the same stream. Cascading to a *different* stream
inline is fine and is sometimes the only option when Wolverine's automatic cascading doesn't
apply.
```

### A3: Add Anti-Pattern #13

Insert the following new section immediately **before** `## File Organization and Naming`
(which is the last content section before the References):

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
  `PackingCompletedHandler` when label cascade couldn't be expressed as a return value (S2)

**Reference:** [Fulfillment Remaster S3 Retrospective — Deviation #1](../../planning/milestones/fulfillment-remaster-s3-retrospective.md)
```

---

## Track B — `docs/skills/wolverine-sagas.md`

Four changes: handler table, `CanBeCancelled` example, new section on OrderStatus values and
the Reshipping idempotency trap, and additions to DOs/DO NOTs.

### B1: Replace the Handler Method Summary table

In the Quick Reference section, find the complete Handler Method Summary table. It currently
contains these three Fulfillment rows:

```
| `Handle(ShipmentDispatched)` | Fulfillment BC | `void` | `Shipped` status |
| `Handle(ShipmentDelivered)` | Fulfillment BC | `OutgoingMessages` | `Delivered` + schedules return window |
| `Handle(ShipmentDeliveryFailed)` | Fulfillment BC | `void` | Delivery failure tracking |
```

Replace the **entire** table (all rows, header included) with:

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

In the `## Shared Guard: CanBeCancelled()` section, find:

```csharp
public static bool CanBeCancelled(OrderStatus status) =>
    status is not (OrderStatus.Shipped or OrderStatus.Delivered
        or OrderStatus.Closed or OrderStatus.Cancelled
        or OrderStatus.OutOfStock or OrderStatus.PaymentFailed);
```

Replace with:

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

### B3: Add new section before "DOs and DO NOTs"

Insert the following new section immediately between `## Shared Guard: CanBeCancelled()`
and `## DOs and DO NOTs`:

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

The null check (`if (decision.Field != null) Field = decision.Field`) preserves the existing
value when the Decider signals no change. This pattern is consistent throughout the entire
Order saga.

---

## ⚠️ Non-Terminal Mid-Lifecycle States and Idempotency Guards

⭐ *M41.0 S4 Critical Discovery*

When a saga can cycle through states (e.g., `DeliveryFailed` → `Reshipping` → `Shipped` →
`Delivered` for a reshipment scenario), be careful about which statuses appear in idempotency
guards for the events that drive those transitions.

**The Reshipping trap:** `ShipmentHandedToCarrier` transitions the Order saga to `Shipped`.
A naive idempotency guard might exclude `Reshipping`:

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

If `Reshipping` is in the guard for step 3, the saga can never escape that state.

**Rule:** Only include truly terminal states (or states where the event is genuinely a
duplicate) in idempotency guards. `Reshipping` is a non-terminal mid-lifecycle state that
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

This test only passes if `Reshipping` is NOT in the `ShipmentHandedToCarrier` guard.
```

### B4: Update DOs and DO NOTs tables

At the end of the **✅ DOs** table, add a new row:

```
| 16 | **DO** be careful about non-terminal mid-lifecycle states in idempotency guards | States like `Reshipping` must be able to receive the event that advances them (e.g., `ShipmentHandedToCarrier`); including them in guards permanently traps the saga |
```

At the end of the **❌ DO NOTs** table, add a new row:

```
| 11 | **DO NOT** include non-terminal mid-lifecycle states (like `Reshipping`) in idempotency guards for the event that advances them | The saga will be permanently stuck; the full reshipment lifecycle test will fail |
```

---

## Session Bookend

```bash
dotnet build
# Required: 0 errors, 17 warnings (unchanged)
```

## Commit

```
skills: wolverine-message-handlers — Anti-Pattern #13 (inline cascade for session.Events.Append) + Anti-Pattern #12 clarification
skills: wolverine-sagas — handler table updated (M41.0 S4); CanBeCancelled guard; new OrderStatus values; Reshipping idempotency trap
```

## Role

**@PSA — Principal Software Architect**
These are precision edits to large files. Read the current file content before making any
change to confirm the exact insertion point. The handler table replacement in Track B is
the most error-prone change — verify the before and after row counts (19 rows → 24 rows).

**@DOE — Documentation Engineer**
Verify all cross-reference links use relative paths. Confirm code examples are
consistent with types and namespaces in `src/Orders/Orders/Placement/` and
`src/Fulfillment/Fulfillment/`.
