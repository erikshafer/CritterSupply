# Fulfillment BC Remaster — Implementation Session 4 (S4)
## Orders Saga Coordinated Migration + Legacy Contract Retirement

## Context

S1 implemented all P0 slices with a dual-publish strategy: when `ShipmentHandedToCarrier`
fires in Fulfillment, it also publishes the legacy `ShipmentDispatched` message so the
Orders saga keeps working unchanged. That compatibility shim has been running since S1.

S4 retires it. This session completes the Fulfillment remaster by wiring the Orders saga
to the new Fulfillment contract surface and removing the legacy messages that are no longer
needed.

**This is the highest-risk session of the entire remaster.** The Orders saga coordinates
Payments, Inventory, Fulfillment, and Returns. Any regression here breaks order lifecycle
correctness. The sequencing is strict: add first, verify, remove second. Never flip that order.

The Correspondence BC also has a handler that will break when the dual-publish is removed.
That fix is part of this session's scope.

---

## Scope

**In scope:**
- Orders saga: new `Handle()` methods for Fulfillment's new integration events
- Orders saga: new `OrderDecider` pure functions for each new event
- Orders saga: new `OrderStatus` enum values
- Orders saga: remove legacy `Handle(ShipmentDispatched)` and `Handle(ShipmentDeliveryFailed)`
- Orders saga: remove legacy `OrderDecider` methods for retired events
- Fulfillment: remove dual-publish (stop emitting legacy `ShipmentDispatched` + `ShipmentDeliveryFailed`)
- Correspondence BC: replace `ShipmentDeliveryFailedHandler` with `ReturnToSenderInitiatedHandler`
- CONTEXTS.md: update Fulfillment and Orders entries to reflect retired contracts
- Orders integration tests: update existing tests, add new tests for new events

**Not in scope:**
- P3 international slices (deferred throughout the remaster)
- Inventory BC Remaster (next remaster series session)
- Correspondence BC handlers for `DeliveryAttemptFailed`, `BackorderCreated`,
  `ShipmentLostInTransit` (customer notification events — handled by a follow-on session)
- `MultiShipmentView` production identity resolution (flagged debt from S3)
- `CarrierPerformanceView` carrier resolution (flagged debt from S3)

---

## Required Reading — Do First

1. **`src/Orders/Orders/Placement/Order.cs`** — read completely. The saga handler pattern is
   consistent throughout: `Handle(MessageType message)` calls `OrderDecider.Handle*(...)`.
   All new handlers follow this exact pattern with no deviation.

2. **`src/Orders/Orders/Placement/OrderDecider.cs`** — read completely. Every new handler
   gets a corresponding pure function here. The `OrderDecision` record carries all state
   changes and outgoing messages. Pure functions only — no `DateTimeOffset.UtcNow` calls
   inside Decider methods; receive `timestamp` as a parameter.

3. **`src/Orders/Orders/Placement/OrderStatus.cs`** — current enum. Three new values needed
   in S4; see below.

4. **`docs/planning/milestones/fulfillment-remaster-s1-retrospective.md`** — the dual-publish
   migration section. Understand what was published where.

5. **`docs/planning/milestones/fulfillment-remaster-s3-retrospective.md`** — the S3 deviations
   carry forward. Particularly: `HazmatPolicy` is called inline (not as a Wolverine cascade).

6. **`src/Correspondence/Correspondence/`** — find `ShipmentDeliveryFailedHandler`. This is
   what needs to be replaced with `ReturnToSenderInitiatedHandler` in Part 4.

7. **`src/Fulfillment/Fulfillment/Shipments/`** — find where the dual-publish happens. Look
   for the comment:
   ```csharp
   // MIGRATION: Dual-publish for backward compatibility with Orders saga.
   // Remove after Orders saga gains ShipmentHandedToCarrier handler.
   ```
   And:
   ```csharp
   // Note: Do NOT publish ShipmentDeliveryFailed...
   ```
   These are the exact lines to remove in Part 3.

8. **`docs/skills/wolverine-message-handlers.md`** — `OutgoingMessages` pattern, Decider
   pattern reference.

---

## Mandatory Bookend — Run Before Starting

```bash
dotnet build
# Expected: 0 errors, 19 warnings

dotnet test tests/Orders/Orders.Api.IntegrationTests
# Expected: 48 passing — the number that must not decrease until all parts complete

dotnet test tests/Fulfillment/Fulfillment.Api.IntegrationTests
# Expected: 78 passing

dotnet test tests/Correspondence/Correspondence.Api.IntegrationTests
# Record the current count
```

---

## Sequencing — Follow This Exactly

```
STEP 1: Add new Orders saga handlers (Part 1)
        └─ verify: 48 Orders tests still pass
STEP 2: Add new Orders integration tests using new events
        └─ verify: > 48 Orders tests passing
STEP 3: Remove legacy Orders saga handlers (Part 2)
        └─ verify: all Orders tests still pass
STEP 4: Remove legacy dual-publish from Fulfillment (Part 3)
        └─ verify: all Orders tests still pass, 78 Fulfillment tests still pass
STEP 5: Update Correspondence BC (Part 4)
        └─ verify: Correspondence tests still pass
STEP 6: Documentation and CONTEXTS.md (Part 5)
```

If any step fails, stop and fix before proceeding. Do not remove legacy code before the
replacement is verified.

---

## New OrderStatus Values

Add to `src/Orders/Orders/Placement/OrderStatus.cs`:

```csharp
/// <summary>
/// All delivery attempts exhausted; carrier is returning package to sender.
/// Order may transition to Reshipping if a reshipment is created.
/// </summary>
DeliveryFailed,

/// <summary>
/// A reshipment has been created; new fulfillment is in progress.
/// Order will return to Shipped when the replacement shipment is handed to carrier.
/// </summary>
Reshipping,

/// <summary>
/// Fulfillment is waiting for stock replenishment. A reshipment or
/// cancellation with refund may follow.
/// </summary>
Backordered,
```

---

## New Saga Properties

Add to `src/Orders/Orders/Placement/Order.cs` (alongside existing properties):

```csharp
/// <summary>
/// Tracking number assigned by Fulfillment BC once a shipping label is generated.
/// </summary>
public string? TrackingNumber { get; set; }

/// <summary>
/// Number of shipments this order has been split into (multi-FC split orders).
/// Populated when OrderSplitIntoShipments is received.
/// </summary>
public int ShipmentCount { get; set; } = 1;

/// <summary>
/// The shipment ID of the active reshipment (if any).
/// Set when ReshipmentCreated is received.
/// </summary>
public Guid? ActiveReshipmentShipmentId { get; set; }
```

---

## Part 1 — New Orders Saga Handlers

Add each handler to `Order.cs` and the corresponding pure function to `OrderDecider.cs`.
Follow the existing pattern exactly.

### 1. ShipmentHandedToCarrier → Shipped

Replaces `Handle(ShipmentDispatched)`. Semantically identical — physical custody transferred
to carrier means the order is shipped.

**Decider:**
```csharp
public static OrderDecision HandleShipmentHandedToCarrier(
    Order current,
    FulfillmentMessages.ShipmentHandedToCarrier message)
{
    // Idempotency: already Shipped or beyond
    if (current.Status is OrderStatus.Shipped or OrderStatus.Delivered
        or OrderStatus.Closed or OrderStatus.Reshipping)
        return new OrderDecision();

    return new OrderDecision { Status = OrderStatus.Shipped };
}
```

**Saga handler:** `void Handle(FulfillmentMessages.ShipmentHandedToCarrier message)`

### 2. TrackingNumberAssigned → Store tracking, no status change

The tracking number is informational. Store it on the saga for customer-facing queries.

**Decider:**
```csharp
public static OrderDecision HandleTrackingNumberAssigned(
    Order current,
    FulfillmentMessages.TrackingNumberAssigned message)
{
    return new OrderDecision { TrackingNumber = message.TrackingNumber };
}
```

Add `TrackingNumber` to `OrderDecision` record.

**Saga handler:** `void Handle(FulfillmentMessages.TrackingNumberAssigned message)`

The saga handler applies: `if (decision.TrackingNumber != null) TrackingNumber = decision.TrackingNumber;`

### 3. ReturnToSenderInitiated → DeliveryFailed

All delivery attempts exhausted. The carrier is returning the package. Order transitions to
`DeliveryFailed`. The saga stays open — a reshipment may follow.

**Decider:**
```csharp
public static OrderDecision HandleReturnToSenderInitiated(
    Order current,
    FulfillmentMessages.ReturnToSenderInitiated message)
{
    // Idempotency: already in DeliveryFailed or beyond
    if (current.Status is OrderStatus.DeliveryFailed or OrderStatus.Reshipping
        or OrderStatus.Closed or OrderStatus.Cancelled)
        return new OrderDecision();

    return new OrderDecision { Status = OrderStatus.DeliveryFailed };
}
```

**Saga handler:** `void Handle(FulfillmentMessages.ReturnToSenderInitiated message)`

### 4. ReshipmentCreated → Reshipping

A replacement shipment has been created. The order is being reshipped. The saga stays open
and will receive `ShipmentHandedToCarrier` again when the new shipment is dispatched.

**Decider:**
```csharp
public static OrderDecision HandleReshipmentCreated(
    Order current,
    FulfillmentMessages.ReshipmentCreated message)
{
    return new OrderDecision
    {
        Status = OrderStatus.Reshipping,
        ActiveReshipmentShipmentId = message.NewShipmentId
    };
}
```

Add `ActiveReshipmentShipmentId` to `OrderDecision` record.

**Saga handler:** `void Handle(FulfillmentMessages.ReshipmentCreated message)`

The saga handler applies the shipment ID:
```csharp
if (decision.ActiveReshipmentShipmentId.HasValue)
    ActiveReshipmentShipmentId = decision.ActiveReshipmentShipmentId;
```

### 5. BackorderCreated → Backordered

Stock is unavailable at all FCs. Fulfillment is paused pending replenishment. The order
waits in `Backordered` status until Fulfillment either reshipping (stock restored) or
cancels.

**Decider:**
```csharp
public static OrderDecision HandleBackorderCreated(
    Order current,
    FulfillmentMessages.BackorderCreated message)
{
    return new OrderDecision { Status = OrderStatus.Backordered };
}
```

**Saga handler:** `void Handle(FulfillmentMessages.BackorderCreated message)`

### 6. FulfillmentCancelled → Cancelled with compensation

The fulfillment center cancelled the order (no stock, FC closed, etc.). Trigger refund if
payment was captured. Release inventory reservations.

**Decider:**
```csharp
public static OrderDecision HandleFulfillmentCancelled(
    Order current,
    FulfillmentMessages.FulfillmentCancelled message,
    DateTimeOffset timestamp)
{
    // Guard: only cancel if not already in a terminal state
    if (!OrderDecider.CanBeCancelled(current.Status))
        return new OrderDecision();

    var messages = new List<object>();

    if (current.IsPaymentCaptured)
    {
        messages.Add(new Messages.Contracts.Payments.RefundRequested(
            current.Id,
            current.TotalAmount,
            $"Fulfillment cancelled: {message.Reason}",
            timestamp));
    }

    foreach (var reservationId in current.ReservationIds.Keys)
    {
        messages.Add(new IntegrationMessages.ReservationReleaseRequested(
            current.Id,
            reservationId,
            $"Fulfillment cancelled: {message.Reason}",
            timestamp));
    }

    messages.Add(new IntegrationMessages.OrderCancelled(
        current.Id,
        current.CustomerId,
        $"Fulfillment cancelled: {message.Reason}",
        timestamp));

    return new OrderDecision
    {
        Status = OrderStatus.Cancelled,
        Messages = messages
    };
}
```

**Saga handler:** `OutgoingMessages Handle(FulfillmentMessages.FulfillmentCancelled message)`

Note: `CanBeCancelled` already excludes terminal states. Update it if needed to include
`DeliveryFailed` and `Reshipping` as cancellable states (they are — fulfilment failed).

Update `CanBeCancelled` to read:
```csharp
public static bool CanBeCancelled(OrderStatus status) =>
    status is not (OrderStatus.Delivered or OrderStatus.Closed
        or OrderStatus.Cancelled or OrderStatus.OutOfStock
        or OrderStatus.PaymentFailed);
```
(Remove `Shipped` from the exclusion list — technically it's cancellable before handoff,
but in practice `FulfillmentCancelled` won't fire post-handoff. Note this in a comment.)

### 7. OrderSplitIntoShipments → Store split count, no status change

Informational — the order is fulfilling from multiple FCs. Store the shipment count for
customer-facing display.

**Decider:**
```csharp
public static OrderDecision HandleOrderSplitIntoShipments(
    Order current,
    FulfillmentMessages.OrderSplitIntoShipments message)
{
    return new OrderDecision { ShipmentCount = message.ShipmentCount };
}
```

Add `ShipmentCount` to `OrderDecision` record.

**Saga handler:** `void Handle(FulfillmentMessages.OrderSplitIntoShipments message)`

### Verification Gate — Part 1

```bash
dotnet test tests/Orders/Orders.Api.IntegrationTests
# Required: 48 still passing (new handlers added but nothing removed yet)
```

---

## Part 2 — Remove Legacy Orders Saga Handlers

Only proceed after Part 1 verification passes.

**Remove from `Order.cs`:**
- `Handle(FulfillmentMessages.ShipmentDispatched message)` — entire method
- `Handle(FulfillmentMessages.ShipmentDeliveryFailed message)` — entire method

**Remove from `OrderDecider.cs`:**
- `HandleShipmentDispatched(...)` — entire method
- `HandleShipmentDeliveryFailed(...)` — entire method

**Remove from `Messages.Contracts/Fulfillment/` (if only referenced by removed handlers):**
- `ShipmentDispatched.cs` — if no other consumer references it
- `ShipmentDeliveryFailed.cs` — if no other consumer references it

Check first: `grep -r "ShipmentDispatched" src/` and `grep -r "ShipmentDeliveryFailed" src/`
to confirm no other consumers exist before deleting the contract types.

### Verification Gate — Part 2

```bash
dotnet test tests/Orders/Orders.Api.IntegrationTests
# Required: all previously passing tests still pass
```

Note: Some tests may have been testing `Handle(ShipmentDispatched)` behavior. Update those
tests to use `Handle(ShipmentHandedToCarrier)` instead — the behavior is identical.

---

## Part 3 — Remove Dual-Publish from Fulfillment

Only proceed after Part 2 verification passes.

Find the dual-publish in `ConfirmCarrierPickupHandler` (or wherever the S1 migration
comment is). Remove:

1. The legacy `ShipmentDispatched` publish:
   ```csharp
   // MIGRATION: Dual-publish for backward compatibility with Orders saga.
   // Remove after Orders saga gains ShipmentHandedToCarrier handler.
   outgoing.Add(new LegacyMessages.ShipmentDispatched(...));
   ```
   Delete this block entirely.

2. Any legacy `ShipmentDeliveryFailed` publish that was dual-publishing (if it was part
   of the migration strategy). Find it and remove.

3. Remove the `using` alias for `LegacyMessages` (or whatever alias was used) from the
   handler file if it now has no references.

### Verification Gate — Part 3

```bash
dotnet build
# Required: 0 errors, 19 warnings

dotnet test tests/Orders/Orders.Api.IntegrationTests
# Required: all tests still pass

dotnet test tests/Fulfillment/Fulfillment.Api.IntegrationTests
# Required: 78 still passing
```

---

## Part 4 — Correspondence BC Update

The Correspondence BC currently has a `ShipmentDeliveryFailedHandler` that was subscribed
to the `ShipmentDeliveryFailed` integration message. After Part 3, that message is no
longer published. Replace it.

**Add:** `ReturnToSenderInitiatedHandler`
```csharp
// Handles Fulfillment.ReturnToSenderInitiated
// Sends customer email: "Your package is being returned — we'll be in touch"
public static class ReturnToSenderInitiatedHandler
{
    public static async Task Handle(
        Messages.Contracts.Fulfillment.ReturnToSenderInitiated message,
        IEmailProvider emailProvider,
        CancellationToken ct)
    {
        await emailProvider.SendAsync(new EmailMessage(
            // recipient lookup via OrderId would be ideal — stub for now
            To: "customer@example.com",
            Subject: "Your package is being returned to us",
            Body: $"After multiple delivery attempts, the carrier is returning your package. " +
                  $"We'll contact you shortly about reshipping or issuing a refund. " +
                  $"Order reference: {message.OrderId}"),
            ct);
    }
}
```

**Remove or mark obsolete:** `ShipmentDeliveryFailedHandler` — this handler will never
receive messages again. Either delete it or add a `[Obsolete]` attribute with a note
explaining that `ShipmentDeliveryFailed` is no longer published as of M41.0 S4.

The `[Obsolete]` approach is safer if there are in-flight shipments from the pre-remaster
model that might still produce the old message in a real deployment. For CritterSupply
(a reference architecture with no actual production traffic), deletion is fine.

**Wire the new handler:** Ensure `ReturnToSenderInitiated` is subscribed on the
Correspondence BC's RabbitMQ queue from Fulfillment. Check `Program.cs` for the
Correspondence BC to see how Fulfillment queue subscriptions are configured, and add
the new message type to the subscription.

### Verification Gate — Part 4

```bash
dotnet test tests/Correspondence/Correspondence.Api.IntegrationTests
# Required: all previously passing tests still pass
```

---

## New Orders Integration Tests

After Part 1, write tests for each new handler before moving to Part 2.

Suggested test additions in `tests/Orders/Orders.Api.IntegrationTests/`:

**`FulfillmentMigrationTests.cs`** (new file):

1. **`ShipmentHandedToCarrier_Transitions_To_Shipped`**
    - Given: Order in `Fulfilling` status
    - When: `ShipmentHandedToCarrier` received
    - Then: Order status is `Shipped`

2. **`TrackingNumberAssigned_Stores_Tracking_Number`**
    - Given: Order in `Shipped` status
    - When: `TrackingNumberAssigned { TrackingNumber: "1Z999..." }` received
    - Then: `Order.TrackingNumber == "1Z999..."`, status unchanged

3. **`ReturnToSenderInitiated_Transitions_To_DeliveryFailed`**
    - Given: Order in `Shipped` status
    - When: `ReturnToSenderInitiated` received
    - Then: Order status is `DeliveryFailed`

4. **`ReshipmentCreated_Transitions_To_Reshipping`**
    - Given: Order in `DeliveryFailed` status
    - When: `ReshipmentCreated { NewShipmentId: <guid> }` received
    - Then: Order status is `Reshipping`, `ActiveReshipmentShipmentId` is set

5. **`BackorderCreated_Transitions_To_Backordered`**
    - Given: Order in `Fulfilling` status
    - When: `BackorderCreated` received
    - Then: Order status is `Backordered`

6. **`FulfillmentCancelled_Triggers_Refund_And_Cancels`**
    - Given: Order in `Fulfilling` status, payment captured
    - When: `FulfillmentCancelled` received
    - Then: Order status is `Cancelled`, `RefundRequested` message published

7. **`Idempotency_ShipmentHandedToCarrier_Already_Shipped`**
    - Given: Order in `Shipped` status
    - When: `ShipmentHandedToCarrier` received again (duplicate)
    - Then: Status unchanged, no duplicate messages

8. **`Full_Lifecycle_With_Reshipment`**
    - Place order → payment → inventory → fulfillment request → ShipmentHandedToCarrier
      (`Shipped`) → ReturnToSenderInitiated (`DeliveryFailed`) → ReshipmentCreated
      (`Reshipping`) → ShipmentHandedToCarrier again (`Shipped`) → ShipmentDelivered
      (`Delivered`)

---

## CURRENT-CYCLE.md Update

Apply these changes to `docs/planning/CURRENT-CYCLE.md`:

**Quick Status table:**
```
| **Current Milestone** | M41.0 — Fulfillment BC Remaster: S4 (Orders saga migration + legacy contract retirement) |
| **Status** | 🟢 **IN PROGRESS** |
| **Recent Completion** | M41.0 S3 — Fulfillment BC Remaster P2 complete: 10 slices, 78 integration tests, ISystemClock + ICarrierLabelService (2026-04-07) |
```

**Active Milestone section** — replace current S3 entry with:
```
### M41.0: Fulfillment BC Remaster — S4 (Orders Saga Migration)

**Status:** 🟢 **In Progress**
**ADR:** [0059 — Fulfillment BC Remaster Rationale](../decisions/0059-fulfillment-bc-remaster-rationale.md)

**Goal:** Retire the dual-publish migration strategy from S1. Wire the Orders saga to the
new Fulfillment contract surface (ShipmentHandedToCarrier, TrackingNumberAssigned,
ReturnToSenderInitiated, ReshipmentCreated, BackorderCreated, FulfillmentCancelled,
OrderSplitIntoShipments). Remove legacy ShipmentDispatched and ShipmentDeliveryFailed
handlers and contracts. Update Correspondence BC.

**Key Deliverables — S4:**
- 7 new Orders saga handlers + Decider methods
- 3 new OrderStatus values: Backordered, DeliveryFailed, Reshipping
- Legacy handler removal (ShipmentDispatched, ShipmentDeliveryFailed)
- Dual-publish removal from Fulfillment
- Correspondence BC: ShipmentDeliveryFailedHandler → ReturnToSenderInitiatedHandler
- CONTEXTS.md update for Fulfillment and Orders
- 8+ new Orders integration tests

**S1 Retrospective:** [S1](./milestones/fulfillment-remaster-s1-retrospective.md)
**S2 Retrospective:** [S2](./milestones/fulfillment-remaster-s2-retrospective.md)
**S3 Retrospective:** [S3](./milestones/fulfillment-remaster-s3-retrospective.md)
**S4 Retrospective:** TBD
```

---

## CONTEXTS.md Updates

**Fulfillment entry** — update the "Communicates with" table to reflect retired contracts:

| Communicates with | Direction | Notes |
|---|---|---|
| Orders | ↔ bidirectional | Receives `FulfillmentRequested`; publishes `ShipmentHandedToCarrier`, `TrackingNumberAssigned`, `ShipmentDelivered`, `ReturnToSenderInitiated`, `ReshipmentCreated`, `BackorderCreated`, `FulfillmentCancelled`, `OrderSplitIntoShipments` |

Remove `ShipmentDispatched` and `ShipmentDeliveryFailed` from this entry.

**Orders entry** — update to reflect new Fulfillment integration events it handles.

---

## Session Bookend — Before Closing

```bash
dotnet build
# Required: 0 errors, 19 warnings

dotnet test tests/Orders/Orders.Api.IntegrationTests
# Required: > 48 (record final count)

dotnet test tests/Fulfillment/Fulfillment.Api.IntegrationTests
# Required: 78 (unchanged)

dotnet test tests/Correspondence/Correspondence.Api.IntegrationTests
# Required: all passing (record count)
```

Commit the session retrospective at
`docs/planning/milestones/fulfillment-remaster-s4-retrospective.md`.

The retrospective must document:
- Final test counts for all four suites
- Whether any legacy messages from S1 were found outside the dual-publish location
- The `CanBeCancelled` guard update (if any change was made)
- The `ShipmentDeliveryFailed` contract type fate (deleted vs. obsoleted)
- Known deferred items (Correspondence handlers for `DeliveryAttemptFailed`,
  `BackorderCreated`, `ShipmentLostInTransit`)
- Assessment: is the Fulfillment Remaster milestone complete, or does an S5 follow?

---

## Commit Convention

```
fulfillment-remaster-s4: new OrderStatus values — Backordered, DeliveryFailed, Reshipping
fulfillment-remaster-s4: new Order saga properties — TrackingNumber, ShipmentCount, ActiveReshipmentShipmentId
fulfillment-remaster-s4: OrderDecider — 7 new pure functions for Fulfillment events
fulfillment-remaster-s4: Order saga — 7 new Handle() methods for Fulfillment events
fulfillment-remaster-s4: Orders integration tests — FulfillmentMigrationTests
fulfillment-remaster-s4: remove legacy Handle(ShipmentDispatched) + Handle(ShipmentDeliveryFailed)
fulfillment-remaster-s4: Fulfillment — remove dual-publish (ShipmentDispatched + ShipmentDeliveryFailed)
fulfillment-remaster-s4: Correspondence — ReturnToSenderInitiatedHandler replaces ShipmentDeliveryFailedHandler
fulfillment-remaster-s4: CONTEXTS.md — Fulfillment and Orders entries updated
fulfillment-remaster-s4: CURRENT-CYCLE.md update
fulfillment-remaster-s4: retrospective
```

---

## Role

**@PSA — Principal Software Architect**
Follow the sequencing exactly. The Decider pattern is consistent — no shortcuts. Every new
event type gets a pure function in `OrderDecider.cs` and a `Handle()` method in `Order.cs`.
The dual-publish removal in Part 3 is irreversible once committed — make sure Parts 1 and 2
are fully verified before touching Fulfillment.

**@QAE — QA Engineer**
The `FulfillmentMigrationTests.cs` file is the most important new test artifact in S4.
The idempotency tests and the full lifecycle test with reshipment are particularly valuable.
Watch for any existing Orders test that references `ShipmentDispatched` by name — those
tests must be updated to use `ShipmentHandedToCarrier` in Part 2. Run the full Orders test
suite after every part, not just at the end.
