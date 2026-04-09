# Inventory BC Remaster — S1: P0 Foundation

**Milestone:** M42.1 — Inventory BC Remaster: S1
**Session type:** Implementation
**Scope:** P0 slices 1–12
**ADR:** `docs/decisions/0060-inventory-bc-remaster-rationale.md`
**Slice table:** `docs/planning/inventory-remaster-slices.md`

---

## Context

This session implements the P0 foundation for the remastered Inventory BC. The event modeling
session (M42.0, ADR 0060) produced the complete domain model. This session makes it real.

**The two critical P0 deliverables that unlock everything else:**
1. `StockAvailabilityView` — the inline multi-stream projection that Fulfillment's routing
   engine queries to make warehouse selection decisions
2. `StockReservationRequested` handler — the new Fulfillment → Inventory integration event
   that replaces the hardcoded WH-01 routing

The `OrderPlacedHandler` is **kept alive** as a dual-publish bridge during this session.
Its retirement happens in the Phase 2 coordinated migration (S4). Do not remove it.

---

## Required Reading — Do First

1. `docs/decisions/0060-inventory-bc-remaster-rationale.md` — **read completely.** All
   architectural decisions are here. Pay special attention to: routing integration flow
   (Section 1), `StockAvailabilityView` shape and lifecycle (Section 2), UUID v5 migration
   (Section 7).

2. `docs/planning/inventory-remaster-slices.md` — P0 slices 1–12. The Aggregate Reference
   table at the top defines the two aggregates and their stream ID patterns.

3. `docs/features/inventory/routing-integration.feature` — acceptance criteria for Slices 3–5
   and 12.

4. `docs/features/inventory/reservation-lifecycle.feature` — acceptance criteria for
   Slices 6–7.

5. `docs/features/inventory/stock-lifecycle.feature` — acceptance criteria for Slices 1–2
   and 8–11.

6. `src/Inventory/Inventory/Management/ProductInventory.cs` — current aggregate. Note
   `CombinedGuid()` with MD5: this is replaced in Slice 1.

7. `src/Inventory/Inventory/Management/OrderPlacedHandler.cs` — the dual-publish bridge.
   Understand what it does so you preserve it correctly while adding the new flow alongside it.

8. `src/Listings/Listings/Listings/ListingStreamId.cs` — the UUID v5 reference implementation
   to follow for `InventoryStreamId.cs`.

---

## Mandatory Bookend

```bash
dotnet build
dotnet test tests/Inventory/Inventory.Api.IntegrationTests
dotnet test tests/Inventory/Inventory.UnitTests
dotnet test tests/Orders/Orders.Api.IntegrationTests
dotnet test tests/Orders/Orders.UnitTests
```

Record baseline test counts at session start. All pre-existing tests must remain green
throughout. The Orders suites (55 integration + 144 unit) are the critical cross-BC gate.

---

## Track A — UUID v5 Foundation (Slices 1–2)

### Slice 1: UUID v5 Stream IDs

Create `src/Inventory/Inventory/Management/InventoryStreamId.cs`:

```csharp
public static class InventoryStreamId
{
    // Matches the UUID v5 convention from ADR 0016 + ADR 0060
    // Input: "inventory:{sku}:{warehouseId}"
    // Uses GuidUtility.Create(GuidUtility.UrlNamespace, input) — same pattern as ListingStreamId
    public static Guid Compute(string sku, string warehouseId) =>
        GuidUtility.Create(GuidUtility.UrlNamespace, $"inventory:{sku}:{warehouseId}");
}
```

Replace all usages of `ProductInventory.CombinedGuid()` with `InventoryStreamId.Compute()`.
Update `InitializeInventoryHandler`, `ReserveStockHandler`, `GetStockLevel`, `GetLowStock`,
and anywhere else `CombinedGuid` is called.

Mark `CombinedGuid()` as `[Obsolete]` pointing to `InventoryStreamId.Compute()` — do not
delete it yet. Update `ProductInventory.Create()` to use `InventoryStreamId.Compute()`.

**Tests:** Update existing `InitializeInventory` and `ReservationFlow` tests to use the new
UUID v5 IDs. Test seed helpers that hardcode inventory IDs will need updating.

### Slice 2: `StockAvailabilityView` Projection

Create `src/Inventory/Inventory/Management/StockAvailabilityView.cs`:

```csharp
public sealed record WarehouseAvailability(string WarehouseId, int AvailableQuantity);

public sealed class StockAvailabilityView
{
    public string Sku { get; set; } = string.Empty;
    public List<WarehouseAvailability> Warehouses { get; set; } = new();
    public int TotalAvailable => Warehouses.Sum(w => w.AvailableQuantity);
}
```

Create `src/Inventory/Inventory/Management/StockAvailabilityViewProjection.cs` as a
`MultiStreamProjection<StockAvailabilityView, string>` keyed by SKU. The projection
groups all `ProductInventory` stream events by their `Sku` field:

```csharp
public class StockAvailabilityViewProjection : MultiStreamProjection<StockAvailabilityView, string>
{
    public StockAvailabilityViewProjection()
    {
        Identity<InventoryInitialized>(e => e.Sku);
        Identity<StockReserved>(e => e.Sku);
        Identity<ReservationReleased>(e => e.Sku);
        Identity<ReservationCommitted>(e => e.Sku);
        Identity<StockReceived>(e => e.Sku);
        Identity<StockRestocked>(e => e.Sku);
        Identity<InventoryAdjusted>(e => e.Sku);
        // StockShipped added in S2 when physical tracking is implemented
    }

    public void Apply(StockAvailabilityView view, InventoryInitialized e)
    {
        view.Sku = e.Sku;
        SetWarehouse(view, e.WarehouseId, e.InitialQuantity);
    }

    // Apply methods for each event type, updating the WarehouseAvailability entry
    // for the event's WarehouseId...
}
```

**Important — event payload enrichment required:** Several existing domain events need
`Sku` and `WarehouseId` added so the projection can route them to the correct view.
Add these fields to: `StockReserved`, `ReservationCommitted`, `ReservationReleased`,
`StockReceived`, `StockRestocked`, `InventoryAdjusted`. Update all handlers that produce
these events to populate the new fields from the loaded `ProductInventory` aggregate.

Register in `Program.cs` with `ProjectionLifecycle.Inline` — the routing engine is on the
critical checkout path; stale data leads to double-booking.

**Tests:** Verify:
- Initializing inventory at NJ-FC and OH-FC for the same SKU creates one view with two entries
- After reservation at NJ-FC, subsequent availability query immediately shows reduced quantity

---

## Track B — Routing Integration (Slices 3–5)

### Slice 3: `GET /api/inventory/availability/{sku}` Endpoint

Create `src/Inventory/Inventory.Api/StockQueries/GetStockAvailability.cs`:

```csharp
public sealed record StockAvailabilityResponse(
    string Sku,
    IReadOnlyList<WarehouseAvailabilityItem> Warehouses,
    int TotalAvailable);

public sealed record WarehouseAvailabilityItem(string WarehouseId, int AvailableQuantity);

public sealed class GetStockAvailability
{
    [WolverineGet("/api/inventory/availability/{sku}")]
    [Authorize(Policy = "WarehouseClerk")]
    public static async Task<Ok<StockAvailabilityResponse>> Handle(
        string sku,
        IDocumentSession session,
        CancellationToken ct)
    {
        var view = await session.LoadAsync<StockAvailabilityView>(sku, ct);

        if (view is null)
            return TypedResults.Ok(new StockAvailabilityResponse(sku, [], 0));

        var warehouses = view.Warehouses
            .Select(w => new WarehouseAvailabilityItem(w.WarehouseId, w.AvailableQuantity))
            .ToList();

        return TypedResults.Ok(new StockAvailabilityResponse(sku, warehouses, view.TotalAvailable));
    }
}
```

Include all warehouses in the response, even zero-availability ones — the routing engine
needs the complete picture to make informed decisions.

**Tests:** Unknown SKU returns empty list with TotalAvailable = 0. Multi-warehouse SKU
returns all warehouses. Availability reflects reservations immediately (inline projection).

### Slice 4: `StockReservationRequested` Handler

This is the new Fulfillment → Inventory integration message that carries the routing engine's
warehouse decision. Create the contract in `Messages.Contracts/Fulfillment/` if it doesn't
already exist:

```csharp
public sealed record StockReservationRequested(
    Guid OrderId,
    string Sku,
    string WarehouseId,   // From Fulfillment's routing decision — no more WH-01
    Guid ReservationId,
    int Quantity);
```

Create `src/Inventory/Inventory/Management/StockReservationRequestedHandler.cs`. The handler
follows the same `Load()/Before()/Handle()` pattern as `ReserveStockHandler` — the business
logic is identical, but the trigger is an integration message rather than an HTTP command.

Wire the RabbitMQ subscription in `Program.cs`:
```csharp
opts.ListenToRabbitMqQueue("inventory-fulfillment-events")
    .SubscribeToMessage<StockReservationRequested>();
```

### Slice 5: Reservation Failure Path

The `Before()` guard in `StockReservationRequestedHandler` handles insufficient stock —
return `ProblemDetails` (409), which causes Wolverine to route to the error path and publish
`ReservationFailed` to Orders BC.

**Tests:**
- Happy path: `StockReservationRequested` → `StockReserved` → `ReservationConfirmed` published
- Failure path: insufficient stock → `ReservationFailed` published, no domain event appended

---

## Track C — Stock Lifecycle Modernization (Slices 6–11)

### Slices 6–7: Reservation Commit and Release

`CommitReservationHandler` and `ReleaseReservationHandler` are functionally correct. The
change here is that `ReservationCommitted` and `ReservationReleased` domain events need
`Sku` and `WarehouseId` added (to drive `StockAvailabilityView` updates). Populate these
from the loaded `ProductInventory` aggregate in the `Handle()` methods.

### Slice 8: `ReceiveStock` / `StockReceived` Enrichment

Update `StockReceived.cs` to carry structured payload instead of freeform `Source`:

```csharp
public sealed record StockReceived(
    string Sku,
    string WarehouseId,
    string SupplierId,          // Replaces freeform Source string
    string? PurchaseOrderId,
    int Quantity,
    DateTimeOffset ReceivedAt);
```

Update `ReceiveStock` command and `ReceiveStockHandler` to include the new fields.
`ProductInventory.Apply(StockReceived)` behavior is unchanged — just richer event.

Publish `StockReplenished` integration event from `Handle()` (check if this contract exists;
create it if not).

### Slice 9: `RestockFromReturn` Handler

Create `src/Inventory/Inventory/Management/RestockFromReturnHandler.cs`. This handler
subscribes to the Returns BC integration event that fires when an inspection passes.

Update `StockRestocked.cs`:

```csharp
public sealed record StockRestocked(
    string Sku,
    string WarehouseId,
    Guid ReturnId,
    int Quantity,
    DateTimeOffset RestockedAt);
```

Wire the Returns queue subscription in `Program.cs` if not already present.

### Slice 10: `AdjustInventory` / `InventoryAdjusted` Enrichment

Update `InventoryAdjusted.cs` to carry `Sku` and `WarehouseId`. Populate from the loaded
aggregate in `AdjustInventoryHandler.Handle()`. Behavior otherwise unchanged.

### Slice 11: `LowStockThresholdBreached` Inline Policy

Create `src/Inventory/Inventory/Management/LowStockPolicy.cs`:

```csharp
public static class LowStockPolicy
{
    public const int DefaultThreshold = 10;

    public static bool CrossedThresholdDownward(int previousQty, int newQty) =>
        previousQty >= DefaultThreshold && newQty < DefaultThreshold;
}
```

In `AdjustInventoryHandler.Handle()`, after appending `InventoryAdjusted`:
1. Compute the new available quantity
2. If `LowStockPolicy.CrossedThresholdDownward(previous, new)`:
   - Append `LowStockThresholdBreached` domain event to the stream
   - Add `LowStockDetected` integration event to `OutgoingMessages`

Change `AdjustInventoryHandler.Handle()` return type from `void` to `OutgoingMessages` to
support publishing the integration event.

**Tests:**
- Adjustment from 12 → 7: `LowStockThresholdBreached` appended, `LowStockDetected` published
- Adjustment from 8 → 6: already below threshold, neither event fires
- Receive stock after low-stock: verify the `LowStockAlertView` (if it exists) is updated

---

## Track D — Migration Bridge (Slice 12)

### Slice 12: `OrderPlacedHandler` Dual-Publish Bridge

The existing `OrderPlacedHandler` must remain active and unmodified. Add a migration comment:

```csharp
/// <summary>
/// MIGRATION BRIDGE: Dual-publish compatibility handler for Phase 1.
/// The WH-01 hardcode is intentionally preserved during migration Phase 1.
/// Remove after Orders saga sends FulfillmentRequested before reservation (M42.x S4).
/// See ADR 0060, Section 1 for the complete routing integration migration plan.
/// </summary>
```

**Do not modify the handler logic.** The new `StockReservationRequested` handler runs
alongside this, not as a replacement. Both serve different flows during the migration period.

Verify existing `OrderPlacedFlowTests` pass without any changes.

---

## Definition of Done

- [ ] `InventoryStreamId.Compute()` used everywhere; `CombinedGuid()` marked `[Obsolete]`
- [ ] `StockAvailabilityView` projection registered as `ProjectionLifecycle.Inline`
- [ ] `GET /api/inventory/availability/{sku}` returns per-warehouse breakdown
- [ ] `StockReservationRequested` handler live; happy path + failure path both tested
- [ ] Domain events `StockReserved`, `ReservationCommitted`, `ReservationReleased`,
      `StockReceived`, `StockRestocked`, `InventoryAdjusted` all carry `Sku` + `WarehouseId`
- [ ] `LowStockThresholdBreached` appended + `LowStockDetected` published when threshold crossed
- [ ] `OrderPlacedHandler` preserved with migration comment; `OrderPlacedFlowTests` green
- [ ] Build: 0 errors, ≤17 warnings
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

Create `docs/planning/milestones/inventory-remaster-s1-retrospective.md` at session end.
Required sections: What Was Delivered (slice table with test delta), Deferred Items,
Build/Test Status, and any technical deviations worth noting for future sessions.

---

## Commit Convention

```
inventory-remaster: InventoryStreamId.Compute() — UUID v5 replaces MD5 CombinedGuid
inventory-remaster: StockAvailabilityView — inline MultiStreamProjection keyed by SKU
inventory-remaster: GET /api/inventory/availability/{sku} — routing query endpoint
inventory-remaster: StockReservationRequested handler — Fulfillment-initiated reservation
inventory-remaster: domain events enriched with Sku + WarehouseId for projection
inventory-remaster: LowStockThresholdBreached + LowStockPolicy inline check
inventory-remaster: OrderPlacedHandler migration comment (dual-publish bridge, Phase 1)
```

---

## Role Notes

**@PSA — Principal Software Architect**
The `StockAvailabilityView` `MultiStreamProjection` is the most complex new piece. The
`Identity<T>` registrations must cover every event that affects `AvailableQuantity`. Verify
the projection is registered as `Inline` (not `Async`) in `Program.cs` — the routing engine
is on the critical checkout path. Confirm with an integration test that a reservation is
reflected in a subsequent availability query within the same transaction.

**@QAE — QA Engineer**
Verify the concurrent reservation scenario from `reservation-lifecycle.feature`: two
simultaneous `StockReservationRequested` messages targeting the same inventory stream. The
Marten optimistic concurrency + Wolverine `ConcurrencyException` retry policy (`RetryOnce`
then cooldown then `Discard`) must produce exactly one `ReservationConfirmed` and one
`ReservationFailed`. The current `.Discard()` policy noted in Gap #13 of the event modeling
retrospective may silently drop failures — document the behavior observed but do not change
the policy in this session.
