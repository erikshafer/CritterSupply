# Inventory BC Remaster — S2: P1 Failure Modes and Physical Operations

**Milestone:** M42.2 — Inventory BC Remaster: S2
**Session type:** Implementation
**Scope:** P1 slices 13–24 + S1 deferred item (RestockFromReturn integration test)
**Slice table:** `docs/planning/inventory-remaster-slices.md` (slices 13–24)
**ADR:** `docs/decisions/0060-inventory-bc-remaster-rationale.md`

---

## Context

S1 delivered all 12 P0 slices. The foundation is in place:
- `InventoryStreamId.Compute()` — UUID v5 stream IDs
- `StockAvailabilityView` — inline multi-stream projection live
- `StockReservationRequested` handler — Fulfillment-initiated reservation flow active
- `OrderPlacedHandler` — dual-publish bridge preserved

This session delivers P1: the failure modes and physical operation tracking slices.

**The critical prerequisite before any P1 slice:** the `ProductInventory` aggregate must
gain a `PickedAllocations` bucket and `HasPendingBackorders` flag, and `TotalOnHand` must
be updated to include picked quantity. All physical tracking slices depend on this.

**Baseline from S1 (verify at session start):**
- Inventory unit tests: 83
- Inventory integration tests: 54
- Orders integration: 55 / Orders unit: 144

---

## Required Reading — Do First

1. `docs/planning/inventory-remaster-slices.md` — P1 slices 13–24.

2. `docs/features/inventory/failure-modes.feature` — primary acceptance criteria for
   this entire session. Read it completely.

3. `src/Inventory/Inventory/Management/ProductInventory.cs` — current state after S1.
   Note what is missing: no `PickedAllocations`, no `HasPendingBackorders`,
   `TotalOnHand = Available + Reserved + Committed` (missing Picked).

4. `docs/planning/milestones/inventory-remaster-s1-retrospective.md` — deferred items
   from S1 (RestockFromReturn integration test, concurrent reservation note).

5. `docs/skills/critterstack-testing-patterns.md` — ISystemClock / FrozenSystemClock
   pattern (needed for Slice 16 reservation expiry testing).

6. `docs/skills/wolverine-message-handlers.md` — Anti-Pattern #13 (inline cascade
   limitation). Slices 13–14 subscribe to Fulfillment integration events and append
   to the stream — Wolverine cannot cascade from those appended events.

---

## Mandatory Bookend

```bash
dotnet build
dotnet test tests/Inventory/Inventory.Api.IntegrationTests
dotnet test tests/Inventory/Inventory.UnitTests
dotnet test tests/Orders/Orders.Api.IntegrationTests
dotnet test tests/Orders/Orders.UnitTests
```

Record baseline counts before any changes. All suites must stay green throughout.

---

## Track A — Aggregate Enhancements (prerequisite for all P1 slices)

Before implementing any P1 slice, extend `ProductInventory`:

**Add new state buckets to the record:**

```csharp
public sealed record ProductInventory(
    Guid Id,
    string Sku,
    string WarehouseId,
    int AvailableQuantity,
    Dictionary<Guid, int> Reservations,
    Dictionary<Guid, int> CommittedAllocations,
    Dictionary<Guid, Guid> ReservationOrderIds,
    Dictionary<Guid, int> PickedAllocations,     // NEW — items physically off shelf, still in building
    bool HasPendingBackorders,                    // NEW — set when BackorderRegistered
    DateTimeOffset InitializedAt)
{
    public int ReservedQuantity => Reservations.Values.Sum();
    public int CommittedQuantity => CommittedAllocations.Values.Sum();
    public int PickedQuantity => PickedAllocations.Values.Sum();              // NEW
    public int TotalOnHand =>
        AvailableQuantity + ReservedQuantity + CommittedQuantity + PickedQuantity; // UPDATED
```

Update `ProductInventory.Create()` to initialise `PickedAllocations = new Dictionary<Guid, int>()` and `HasPendingBackorders = false`.

**Add Apply methods for all new domain events (stubs are fine — handlers add the logic):**

```csharp
// Slice 13 — committed → picked
public ProductInventory Apply(StockPicked @event) { ... }

// Slice 14 — picked removed, TotalOnHand decrements
public ProductInventory Apply(StockShipped @event) { ... }

// Slice 15 — no state change on aggregate (alert only)
public ProductInventory Apply(StockDiscrepancyFound @event) => this;

// Slice 16
public ProductInventory Apply(ReservationExpired @event) { ... } // same logic as ReservationReleased

// Slice 18
public ProductInventory Apply(BackorderRegistered @event) =>
    this with { HasPendingBackorders = true };

// Slice 19
public ProductInventory Apply(BackorderCleared @event) =>
    this with { HasPendingBackorders = false };

// Slices 20-21
public ProductInventory Apply(CycleCountInitiated @event) => this;
public ProductInventory Apply(CycleCountCompleted @event) => this;

// Slices 23-24
public ProductInventory Apply(DamageRecorded @event) => this;   // InventoryAdjusted carries the qty change
public ProductInventory Apply(StockWrittenOff @event) => this;  // InventoryAdjusted carries the qty change
```

Create the corresponding domain event records alongside the Apply methods. Each event
needs `Sku` and `WarehouseId` fields (same pattern as S1 events) for the
`StockAvailabilityView` projection. Update the projection's `Identity<T>` registrations
in `StockAvailabilityViewProjection` to include `StockShipped` (deferred from S1).

---

## Track B — Physical Pick and Ship Tracking (Slices 13–15)

### Slice 13: `ItemPicked` → `StockPicked`

Inventory subscribes to `ItemPicked` from Fulfillment. The integration contract needs
`WarehouseId` and `OrderId` — check if these are on the existing contract; if not, add
them (Gap #11 from the event modeling retrospective — coordinate with Fulfillment).

Create `src/Inventory/Inventory/Management/ItemPickedHandler.cs`:

```csharp
public static class ItemPickedHandler
{
    public static async Task<ProductInventory?> Load(
        Messages.Contracts.Fulfillment.ItemPicked message,
        IDocumentSession session, CancellationToken ct)
    {
        var id = InventoryStreamId.Compute(message.Sku, message.WarehouseId);
        return await session.LoadAsync<ProductInventory>(id, ct);
    }

    public static OutgoingMessages Handle(
        Messages.Contracts.Fulfillment.ItemPicked message,
        ProductInventory? inventory,
        IDocumentSession session)
    {
        if (inventory is null) return new OutgoingMessages(); // no-op: unknown SKU

        // Find the committed allocation for this order
        var reservationId = inventory.ReservationOrderIds
            .FirstOrDefault(x => x.Value == message.OrderId).Key;

        if (reservationId == Guid.Empty || !inventory.CommittedAllocations.ContainsKey(reservationId))
        {
            // Stale message — reservation already released or committed to wrong warehouse
            // Log warning, no state change
            return new OutgoingMessages();
        }

        var committedQty = inventory.CommittedAllocations[reservationId];
        var outgoing = new OutgoingMessages();

        session.Events.Append(inventory.Id,
            new StockPicked(message.Sku, message.WarehouseId,
                reservationId, message.Quantity, DateTimeOffset.UtcNow));

        // Short pick detection (Slice 15) — inline, cannot cascade from Append()
        if (message.Quantity < committedQty)
        {
            session.Events.Append(inventory.Id,
                new StockDiscrepancyFound(message.Sku, message.WarehouseId,
                    committedQty, message.Quantity,
                    DiscrepancyType.ShortPick,
                    "Short pick detected during order fulfillment",
                    DateTimeOffset.UtcNow));
            // TODO S3: publish to AlertFeedView integration event
        }
        else if (message.Quantity == 0)
        {
            session.Events.Append(inventory.Id,
                new StockDiscrepancyFound(message.Sku, message.WarehouseId,
                    committedQty, 0, DiscrepancyType.ZeroPick,
                    "Complete bin miss — zero items found", DateTimeOffset.UtcNow));
        }

        return outgoing;
    }
}
```

Wire `inventory-fulfillment-events` queue subscription for `ItemPicked` in `Program.cs`.

### Slice 14: `ShipmentHandedToCarrier` → `StockShipped`

Inventory already wires `ShipmentHandedToCarrier` via the existing
`inventory-fulfillment-events` queue (added in S1 stubs if present; verify). Create
`src/Inventory/Inventory/Management/ShipmentHandedToCarrierHandler.cs`.

The handler correlates by `OrderId` to find the `PickedAllocation`. If the item is in
Picked state: append `StockShipped`, decrement TotalOnHand. If it arrives before
`ItemPicked` (out-of-order delivery — see feature file scenario): append both `StockPicked`
and `StockShipped` atomically (combined pick-and-ship path).

Also check if this arrival clears a pending backorder — call `BackorderPolicy.CheckAndPublish()`
inline (same inline pattern as `LowStockPolicy` from S1).

### Slice 15: Short Pick Detection

Short pick is appended inline within `ItemPickedHandler.Handle()` (see Track B above).
`StockDiscrepancyFound` is a domain event appended via `session.Events.Append()` — it
cannot be expressed as a cascade from the handler return value per Anti-Pattern #13.

Create `DiscrepancyType` enum: `ShortPick`, `ZeroPick`, `CycleCount`.

**Tests:**
- `ItemPicked` with Quantity = committed: `StockPicked` appended, no discrepancy
- `ItemPicked` with Quantity < committed: `StockPicked` + `StockDiscrepancyFound` appended
- `ItemPicked` with Quantity = 0: `StockDiscrepancyFound` (ZeroPick), no `StockPicked`
- `ItemPicked` for released reservation: no-op

---

## Track C — Reservation Expiry and Concurrent Conflict (Slices 16–17)

### Slice 16: `ReservationExpired` — Scheduled Timeout

Use the `ISystemClock` / `FrozenSystemClock` pattern (see `critterstack-testing-patterns.md`).
`ProductInventory` does not currently inject a clock — the expiry is driven by a
Wolverine scheduled message, not by the aggregate reading the clock.

When a reservation is created (in `StockReservationRequestedHandler.Handle()`), schedule
an expiry message:

```csharp
// At end of Handle() in StockReservationRequestedHandler:
outgoing.Add(new ExpireReservation(command.ReservationId, command.InventoryId)
    .ScheduleAt(DateTimeOffset.UtcNow.AddMinutes(30)));
```

Create `src/Inventory/Inventory/Management/ExpireReservation.cs` (command + handler):

```csharp
public sealed record ExpireReservation(Guid ReservationId, Guid InventoryId);

public static class ExpireReservationHandler
{
    public static async Task<ProductInventory?> Load(...) { ... }

    public static OutgoingMessages Handle(
        ExpireReservation command,
        ProductInventory? inventory,
        IDocumentSession session)
    {
        if (inventory is null) return new OutgoingMessages();

        // Idempotency: if already committed or released, no-op
        if (!inventory.Reservations.ContainsKey(command.ReservationId))
            return new OutgoingMessages();

        var quantity = inventory.Reservations[command.ReservationId];
        var orderId = inventory.ReservationOrderIds[command.ReservationId];

        session.Events.Append(inventory.Id,
            new ReservationExpired(command.ReservationId, quantity,
                "Reservation expired after timeout", DateTimeOffset.UtcNow));

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Inventory.ReservationReleased(
            orderId, inventory.Id, command.ReservationId,
            inventory.Sku, inventory.WarehouseId, quantity,
            "Expired", DateTimeOffset.UtcNow));

        return outgoing;
    }
}
```

**Tests using `FrozenSystemClock`:** Add `ISystemClock` + `FrozenSystemClock` to the
Inventory test fixture (same pattern as Fulfillment — see `critterstack-testing-patterns.md`).
Test that:
- Expiry fires after timeout: `ReservationExpired` appended, `ReservationReleased` published
- Expiry fires after commit: no-op (idempotent)
- Expiry fires after release: no-op (idempotent)

### Slice 17: Concurrent Reservation Conflict

No new handler code — this is the existing Marten optimistic concurrency + Wolverine
`ConcurrencyException` retry policy. The S1 retrospective noted that `.Discard()` may
silently drop the second reservation. In this session:

1. Write the integration test for concurrent reservations (was deferred from S1)
2. Observe actual behavior — does the second order get `ReservationFailed` or is it silently dropped?
3. Document findings in the session retrospective
4. If `.Discard()` swallows the failure, change the policy to `.MoveToDeadLetterQueue()`
   so failures are visible — this is Gap #13 from the event modeling retrospective

**Do not leave this slice without a written test and a documented decision.**

---

## Track D — Backorder Tracking (Slices 18–19)

### Slice 18: `BackorderCreated` → `BackorderRegistered`

`BackorderCreated` requires SKU data (Gap #10 from the event modeling retrospective).
Check the current contract shape in `Messages.Contracts/Fulfillment/BackorderCreated.cs`.
If `Items: IReadOnlyList<BackorderedItem>` is not present, add it now — this is the
coordinated enrichment noted in ADR 0060.

Create `src/Inventory/Inventory/Management/BackorderCreatedHandler.cs`. For each item
in the `BackorderCreated.Items` list, load the corresponding `ProductInventory` stream
and append `BackorderRegistered`:

```csharp
public sealed record BackorderRegistered(
    string Sku, string WarehouseId,
    Guid OrderId, Guid ShipmentId,
    int Quantity, DateTimeOffset RegisteredAt);
```

Set `HasPendingBackorders = true` via the Apply method. Update `BackorderImpactView`
projection (create if it doesn't exist — see slice table).

Wire `inventory-fulfillment-events` queue subscription for `BackorderCreated`.

### Slice 19: Stock Arrival Clears Backorder

When stock arrives for a SKU that has pending backorders (`HasPendingBackorders = true`),
publish `BackorderStockAvailable` to Fulfillment.

Create `src/Inventory/Inventory/Management/BackorderPolicy.cs`:

```csharp
public static class BackorderPolicy
{
    public static OutgoingMessages? CheckAndPublish(
        ProductInventory inventory, int newAvailableQuantity)
    {
        if (!inventory.HasPendingBackorders) return null;
        if (newAvailableQuantity <= 0) return null;

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Inventory.BackorderStockAvailable(
            inventory.Sku, inventory.WarehouseId, newAvailableQuantity,
            DateTimeOffset.UtcNow));
        return outgoing;
    }
}
```

Call `BackorderPolicy.CheckAndPublish()` inline from:
- `ReceiveStockHandler.Handle()` (after appending `StockReceived`)
- `ShipmentHandedToCarrierHandler` does NOT trigger backorder clearing (that decrements stock)

Also append `BackorderCleared` domain event on the stream when backorders are cleared.

`BackorderStockAvailable` integration contract needs to be created in
`Messages.Contracts/Inventory/BackorderStockAvailable.cs` if it doesn't exist.

**Tests:**
- Stock arrival with `HasPendingBackorders = false`: no `BackorderStockAvailable` published
- Stock arrival with `HasPendingBackorders = true`: `BackorderCleared` + `BackorderStockAvailable`
- Order cancellation clears backorder registration (via `ReservationReleaseRequested`)

---

## Track E — Cycle Counts, Damage, and Write-Off (Slices 20–24)

### Slices 20–22: Cycle Count Workflow

Create `src/Inventory/Inventory/Management/CycleCount.cs` with command + handler.

`InitiateCycleCount` (HTTP, `POST /api/inventory/{sku}/{warehouseId}/cycle-count`):
appends `CycleCountInitiated`. No state change on the aggregate.

`CompleteCycleCount` (HTTP, `POST /api/inventory/{sku}/{warehouseId}/cycle-count/complete`):

```csharp
public sealed record CompleteCycleCount(
    Guid InventoryId,
    int PhysicalCount,
    string CountedBy);
```

In `Handle()`:
- Compute expected available: `physicalCount - inventory.ReservedQuantity - inventory.CommittedQuantity - inventory.PickedQuantity`
- If expected available == `inventory.AvailableQuantity`: append `CycleCountCompleted` only
- If different: append `CycleCountCompleted` + `StockDiscrepancyFound` (DiscrepancyType.CycleCount)
  + `InventoryAdjusted` (delta to correct available quantity)

The adjustment may push `AvailableQuantity` negative if reserved + committed > physical.
In that case, reject with `ProblemDetails` — operations manager must investigate before
adjusting (the feature file's "requires operations manager approval" scenario).

**Tests:** Happy path (no discrepancy), shortage discrepancy, surplus discrepancy,
negative-would-result rejection.

### Slice 23: Damage Recorded

Create `src/Inventory/Inventory/Management/RecordDamage.cs`.
`POST /api/inventory/{inventoryId}/damage`:

```csharp
public sealed record RecordDamage(
    Guid InventoryId,
    int Quantity,
    string DamageReason,
    string RecordedBy);
```

Handler appends both `DamageRecorded` and `InventoryAdjusted` (negative quantity). Delegate
to `LowStockPolicy.CheckAndPublish()` inline if threshold crossed.

### Slice 24: Stock Write-Off

Create `src/Inventory/Inventory/Management/WriteOffStock.cs`.
`POST /api/inventory/{inventoryId}/write-off`:

Similar to `RecordDamage` but appends `StockWrittenOff` + `InventoryAdjusted`. Requires
`OperationsManager` policy (more destructive than damage recording).

---

## S1 Deferred — RestockFromReturn Integration Test

`RestockFromReturnHandler` was implemented in S1 but its integration test was deferred
because it requires Returns BC fixtures. In this session, add the test using a direct
message invocation pattern (same as `StockReservationRequested` tests — no cross-BC
fixture needed):

```csharp
[Fact]
public async Task RestockFromReturn_WhenReturnPasses_AppendsStockRestocked()
{
    // Seed inventory, then invoke the Returns integration event directly
    await _fixture.ExecuteAndWaitAsync(
        new Messages.Contracts.Returns.ReturnItemRestocked(
            returnId: Guid.NewGuid(),
            sku: "DOG-FOOD-40LB",
            warehouseId: "NJ-FC",
            quantity: 2));

    // Assert StockRestocked appended, AvailableQuantity increased
}
```

---

## Definition of Done

- [ ] `ProductInventory` has `PickedAllocations`, `HasPendingBackorders`, `PickedQuantity`
- [ ] `TotalOnHand = Available + Reserved + Committed + Picked`
- [ ] `StockAvailabilityViewProjection` includes `StockShipped` identity registration
- [ ] `ItemPickedHandler`: `StockPicked` appended; short pick appends `StockDiscrepancyFound` inline
- [ ] `ShipmentHandedToCarrierHandler`: `StockShipped` appended; combined pick-and-ship path works
- [ ] `ExpireReservationHandler`: idempotent; `ReservationExpired` appended; `ReservationReleased` published
- [ ] Concurrent reservation test written; Gap #13 `.Discard()` behavior documented or fixed
- [ ] `BackorderCreatedHandler`: `BackorderRegistered` appended; `HasPendingBackorders = true`
- [ ] `BackorderPolicy`: `BackorderStockAvailable` published when stock arrives for backordered SKU
- [ ] `CycleCountHandler`: no-discrepancy + discrepancy + surplus paths all tested
- [ ] `RecordDamageHandler` and `WriteOffStockHandler` implemented
- [ ] `RestockFromReturn` integration test added (S1 deferred)
- [ ] Build: 0 errors, ≤4 warnings (S1 baseline)
- [ ] All pre-existing Inventory tests green
- [ ] Orders integration (55) and unit (144) tests green

---

## Session Bookend

```bash
dotnet build
dotnet test tests/Inventory/Inventory.Api.IntegrationTests
dotnet test tests/Inventory/Inventory.UnitTests
dotnet test tests/Orders/Orders.Api.IntegrationTests
dotnet test tests/Orders/Orders.UnitTests
```

Record final test counts in the session retrospective.

---

## Session Retrospective

Create `docs/planning/milestones/inventory-remaster-s2-retrospective.md` at session end.
Required sections: What Was Delivered (slice table + test delta), Deferred Items,
Build/Test Status, and Gap #13 resolution (`.Discard()` policy decision).

---

## Commit Convention

```
inventory-remaster: ProductInventory — PickedAllocations bucket, HasPendingBackorders, TotalOnHand updated
inventory-remaster: ItemPickedHandler — StockPicked + inline short pick detection
inventory-remaster: ShipmentHandedToCarrierHandler — StockShipped, TotalOnHand decrement
inventory-remaster: ExpireReservationHandler — scheduled reservation expiry (ISystemClock)
inventory-remaster: BackorderCreatedHandler — BackorderRegistered, HasPendingBackorders
inventory-remaster: BackorderPolicy — BackorderStockAvailable on stock arrival
inventory-remaster: CycleCountHandler — initiate, complete, discrepancy detection
inventory-remaster: RecordDamageHandler + WriteOffStockHandler
inventory-remaster: RestockFromReturn integration test (S1 deferred)
```

---

## Role Notes

**@PSA — Principal Software Architect**
The aggregate extension (Track A) is the highest-risk change — it adds new constructor
parameters to the record. Every `new ProductInventory(...)` call in tests and seed helpers
must be updated. Verify `Create()` initialises `PickedAllocations` as an empty dictionary
and `HasPendingBackorders` as false. The `TotalOnHand` change will affect any existing
test that asserts on that value — update assertions, not business logic.

The `BackorderPolicy.CheckAndPublish()` inline call follows the same pattern as
`LowStockPolicy` from S1. It must be called from `ReceiveStockHandler.Handle()` after
the `StockReceived` event is appended — Wolverine cannot cascade from `session.Events.Append()`.

**@QAE — QA Engineer**
Slice 17 is not optional. Write the concurrent reservation integration test before closing
the session. The test should fire two simultaneous `StockReservationRequested` messages at
inventory with exactly enough stock for one. Observe whether the second order receives
`ReservationFailed` or is silently discarded. If silently discarded, change the Wolverine
error policy from `.Discard()` to `.MoveToDeadLetterQueue()` on the `ConcurrencyException`
handler in `Program.cs` and document the change. This is Gap #13 from the event modeling
retrospective and must be resolved in this session.
