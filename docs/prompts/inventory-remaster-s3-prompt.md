# Inventory BC Remaster — S3: P2 Transfers, Replenishment, and Quarantine

**Milestone:** M42.3 — Inventory BC Remaster: S3
**Session type:** Implementation
**Scope:** S2 deferred items + P2 slices 25–35
**Slice table:** `docs/planning/inventory-remaster-slices.md` (slices 25–35)
**ADR:** `docs/decisions/0060-inventory-bc-remaster-rationale.md`

---

## Context

S1 and S2 are complete. The P0 foundation and all 12 P1 failure-mode slices are
delivered. Test baselines entering this session:
- Inventory unit: 100
- Inventory integration: 83
- Orders integration: 55 / Orders unit: 144 (cross-BC gate — must stay green)

This session has three categories of work:

1. **S2 deferred items** — clear these first before starting any P2 slice work
2. **`InventoryTransfer` aggregate** — brand new aggregate coordinating inter-warehouse
   transfers across two `ProductInventory` streams
3. **Projections and quarantine** — read models and the quarantine lifecycle

The `InventoryTransfer` aggregate is the most architecturally significant new piece in
this session. It coordinates two `ProductInventory` streams using `bus.InvokeAsync()`
inline — Wolverine cannot cascade from `session.Events.Append()` (Anti-Pattern #13).

---

## Required Reading — Do First

1. `docs/decisions/0060-inventory-bc-remaster-rationale.md` — Section 5 (`InventoryTransfer`
   lifecycle and multi-aggregate coordination). The inline `bus.InvokeAsync()` pattern is
   documented here.

2. `docs/planning/inventory-remaster-slices.md` — P2 slices 25–35. Read the Aggregate
   Reference table — `InventoryTransfer` uses `Guid.CreateVersion7()`, not UUID v5.

3. `docs/planning/milestones/inventory-remaster-s2-retrospective.md` — deferred items
   section. Three items must be resolved before P2 work begins.

4. `docs/skills/wolverine-message-handlers.md` — Anti-Pattern #13 (inline cascade for
   `session.Events.Append()` workflows). The transfer handlers are the canonical S3 example.

5. `docs/skills/bc-remaster.md` — Inline Policy Invocation Pattern section. Confirms the
   pattern for multi-aggregate coordination.

6. `src/Inventory/Inventory/Management/ProductInventory.cs` — verify S2 state: confirm
   `PickedAllocations`, `HasPendingBackorders`, and the updated `TotalOnHand` are present.

---

## Mandatory Bookend

```bash
dotnet build
dotnet test tests/Inventory/Inventory.Api.IntegrationTests
dotnet test tests/Inventory/Inventory.UnitTests
dotnet test tests/Orders/Orders.Api.IntegrationTests
dotnet test tests/Orders/Orders.UnitTests
```

Record baseline counts before any changes.

---

## Track A — S2 Deferred Items (resolve before P2 work)

### A1: Gap #13 — ConcurrencyException Policy

The S2 retrospective documented that `.Discard()` on `ConcurrencyException` silently drops
the second reservation in concurrent conflicts. Change the policy in
`src/Inventory/Inventory.Api/Program.cs`:

```csharp
// BEFORE (S2 state):
opts.OnException<ConcurrencyException>()
    .RetryOnce()
    .Then.RetryWithCooldown(100.Milliseconds(), 250.Milliseconds())
    .Then.Discard();

// AFTER:
opts.OnException<ConcurrencyException>()
    .RetryOnce()
    .Then.RetryWithCooldown(100.Milliseconds(), 250.Milliseconds())
    .Then.MoveToErrorQueue();
```

This ensures failed reservations are visible in the dead letter queue rather than silently
dropped. The `MoveToErrorQueue()` approach preserves the message for manual reprocessing or
alerting without losing the `ReservationFailed` signal.

Update the concurrent reservation integration test to verify the second order's message
moves to the error queue rather than being discarded silently.

### A2: AlertFeedView Integration Event for StockDiscrepancyFound

The `ItemPickedHandler` has a `// TODO S3` comment for publishing an alert when a
`StockDiscrepancyFound` event is appended. Implement this now:

Create `Messages.Contracts.Inventory.StockDiscrepancyAlert` integration contract. Publish
it from `ItemPickedHandler.Handle()` alongside the `StockDiscrepancyFound` domain event
append. The Backoffice BC subscribes to this for the operations manager alert feed.

### A3: HTTP Endpoint Wiring

The S2 retrospective noted that CycleCount, RecordDamage, and WriteOffStock handlers exist
but lack `[WolverinePost]` attributes. Add the HTTP endpoints:

- `POST /api/inventory/{inventoryId}/cycle-count` — `InitiateCycleCount`
- `POST /api/inventory/{inventoryId}/cycle-count/complete` — `CompleteCycleCount`
- `POST /api/inventory/{inventoryId}/damage` — `RecordDamage`
- `POST /api/inventory/{inventoryId}/write-off` — `WriteOffStock`

Apply appropriate `[Authorize]` policies (`WarehouseClerk` for cycle count and damage;
`OperationsManager` for write-off).

---

## Track B — InventoryTransfer Aggregate (prerequisite for Slices 25–29)

Create `src/Inventory/Inventory/Transfers/` directory for all transfer-related files.

### InventoryTransfer.cs

```csharp
public sealed record InventoryTransfer(
    Guid Id,
    string Sku,
    string FromWarehouseId,
    string ToWarehouseId,
    int Quantity,
    TransferStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? ReceivedAt,
    int? ReceivedQuantity)
{
    public static InventoryTransfer Create(TransferRequested @event) =>
        new(
            @event.TransferId,
            @event.Sku,
            @event.FromWarehouseId,
            @event.ToWarehouseId,
            @event.Quantity,
            TransferStatus.Requested,
            @event.RequestedAt,
            null, null, null);

    public InventoryTransfer Apply(TransferShipped @event) =>
        this with { Status = TransferStatus.Shipped, ShippedAt = @event.ShippedAt };

    public InventoryTransfer Apply(TransferReceived @event) =>
        this with
        {
            Status = TransferStatus.Received,
            ReceivedAt = @event.ReceivedAt,
            ReceivedQuantity = @event.ReceivedQuantity
        };

    public InventoryTransfer Apply(TransferCancelled @event) =>
        this with { Status = TransferStatus.Cancelled };

    public InventoryTransfer Apply(TransferShortReceived @event) =>
        this with
        {
            Status = TransferStatus.Received,
            ReceivedAt = @event.ReceivedAt,
            ReceivedQuantity = @event.ReceivedQuantity
        };
}

public enum TransferStatus { Requested, Shipped, Received, Cancelled }
```

**Stream ID:** `Guid.CreateVersion7()` — generated at request time, passed through all
subsequent commands. Not UUID v5 (no natural key to derive from).

Register `InventoryTransfer` snapshot in `Program.cs`:
```csharp
opts.Projections.Snapshot<InventoryTransfer>(SnapshotLifecycle.Inline);
```

### Domain Events

Create in `src/Inventory/Inventory/Transfers/`:
- `TransferRequested.cs` — Sku, FromWarehouseId, ToWarehouseId, Quantity, RequestedAt
- `TransferShipped.cs` — TransferId, ShippedAt
- `TransferReceived.cs` — TransferId, ReceivedQuantity, ReceivedAt
- `TransferCancelled.cs` — TransferId, CancelledAt, Reason
- `TransferShortReceived.cs` — TransferId, ExpectedQuantity, ReceivedQuantity, ReceivedAt
- `StockTransferredOut.cs` — Sku, WarehouseId, TransferId, Quantity (domain event on source ProductInventory)
- `TransferStockReceived.cs` — Sku, WarehouseId, TransferId, Quantity (domain event on destination ProductInventory)

Add `Apply()` methods on `ProductInventory` for `StockTransferredOut` and
`TransferStockReceived`:

```csharp
// StockTransferredOut: reduces AvailableQuantity at source
public ProductInventory Apply(StockTransferredOut @event) =>
    this with { AvailableQuantity = AvailableQuantity - @event.Quantity };

// TransferStockReceived: increases AvailableQuantity at destination
public ProductInventory Apply(TransferStockReceived @event) =>
    this with { AvailableQuantity = AvailableQuantity + @event.Quantity };
```

Also add these to `StockAvailabilityViewProjection` Identity registrations.

---

## Track C — Transfer Lifecycle Handlers (Slices 25–29)

### Slice 25: RequestTransfer (HTTP POST)

Create `src/Inventory/Inventory/Transfers/RequestTransfer.cs`:

```csharp
public sealed record RequestTransfer(
    string Sku,
    string FromWarehouseId,
    string ToWarehouseId,
    int Quantity);
```

`RequestTransferHandler` creates the `InventoryTransfer` stream and debits the source
`ProductInventory` **inline**:

```csharp
public static class RequestTransferHandler
{
    // No Load() — IStartStream creates the InventoryTransfer aggregate

    public static async Task<IStartStream> Handle(
        RequestTransfer command,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken ct)
    {
        var transferId = Guid.CreateVersion7();
        var sourceInventoryId = InventoryStreamId.Compute(command.Sku, command.FromWarehouseId);

        // Verify source has sufficient stock before creating the transfer
        var sourceInventory = await session.LoadAsync<ProductInventory>(sourceInventoryId, ct);
        if (sourceInventory is null || sourceInventory.AvailableQuantity < command.Quantity)
            throw new InvalidOperationException(
                $"Insufficient stock at {command.FromWarehouseId} for SKU {command.Sku}");

        // Debit source inventory inline — cannot cascade from StartStream
        await bus.InvokeAsync(new DebitSourceForTransfer(
            sourceInventoryId, command.Sku, command.FromWarehouseId,
            transferId, command.Quantity));

        return MartenOps.StartStream<InventoryTransfer>(
            transferId,
            new TransferRequested(
                transferId, command.Sku,
                command.FromWarehouseId, command.ToWarehouseId,
                command.Quantity, DateTimeOffset.UtcNow));
    }
}
```

Create `DebitSourceForTransfer` command + handler — loads source `ProductInventory`,
appends `StockTransferredOut`. This is the inner command invoked inline.

**HTTP:** `POST /api/inventory/transfers` — `WarehouseClerk` policy.

### Slice 26: ShipTransfer (HTTP POST)

```csharp
public sealed record ShipTransfer(Guid TransferId);
```

`ShipTransferHandler` loads `InventoryTransfer`, validates status is `Requested`, appends
`TransferShipped`. No `ProductInventory` mutation at this stage — stock is in transit and
not available at either location (already debited from source at request time).

**HTTP:** `POST /api/inventory/transfers/{transferId}/ship` — `WarehouseClerk` policy.

### Slice 27: ReceiveTransfer (HTTP POST)

```csharp
public sealed record ReceiveTransfer(Guid TransferId, int ReceivedQuantity);
```

`ReceiveTransferHandler` loads `InventoryTransfer`, validates status is `Shipped`, then:

1. If `ReceivedQuantity == transfer.Quantity`: happy path — appends `TransferReceived`,
   credits destination **inline**:
```csharp
   await bus.InvokeAsync(new CreditDestinationForTransfer(
       destinationInventoryId, transfer.Sku, transfer.ToWarehouseId,
       transferId, receivedQuantity));
   session.Events.Append(transferId, new TransferReceived(...));
```

2. If `ReceivedQuantity < transfer.Quantity`: short receipt — appends
   `TransferShortReceived` + `StockDiscrepancyFound`, credits destination with actual
   received quantity only. See Slice 29.

Create `CreditDestinationForTransfer` command + handler — loads destination
`ProductInventory`, appends `TransferStockReceived`. Also check `BackorderPolicy`
inline (new stock arriving at destination may clear backorders).

**HTTP:** `POST /api/inventory/transfers/{transferId}/receive` — `WarehouseClerk` policy.

### Slice 28: CancelTransfer (HTTP POST, pre-ship only)

```csharp
public sealed record CancelTransfer(Guid TransferId, string Reason);
```

`CancelTransferHandler` loads `InventoryTransfer`, validates status is `Requested` (cannot
cancel after shipping), appends `TransferCancelled`, then restores source stock **inline**:

```csharp
await bus.InvokeAsync(new RestoreSourceAfterCancelledTransfer(
    sourceInventoryId, transfer.Sku, transfer.FromWarehouseId,
    transferId, transfer.Quantity));
session.Events.Append(transferId, new TransferCancelled(...));
```

Create `RestoreSourceAfterCancelledTransfer` command + handler — appends an
`InventoryAdjusted` event (positive) to the source stream. Reuse `LowStockPolicy` check.

**HTTP:** `DELETE /api/inventory/transfers/{transferId}` — `WarehouseClerk` policy.

### Slice 29: Short Transfer Receipt

Handled inside `ReceiveTransferHandler` (see Slice 27 branch 2):

```csharp
session.Events.Append(transferId,
    new TransferShortReceived(
        transferId, transfer.Quantity, command.ReceivedQuantity,
        DateTimeOffset.UtcNow));

session.Events.Append(
    InventoryStreamId.Compute(transfer.Sku, transfer.ToWarehouseId),
    new StockDiscrepancyFound(
        transfer.Sku, transfer.ToWarehouseId,
        transfer.Quantity, command.ReceivedQuantity,
        DiscrepancyType.TransferShortReceipt,
        $"Transfer {transferId} short: expected {transfer.Quantity}, received {command.ReceivedQuantity}",
        DateTimeOffset.UtcNow));
```

Add `TransferShortReceipt` to the `DiscrepancyType` enum.

---

## Track D — Replenishment Trigger and Projections (Slices 30–32)

### Slice 30: ReplenishmentTriggered (Inline Policy)

When stock arrives and `AvailableQuantity` rises above the replenishment trigger threshold
(currently `LowStockPolicy.DefaultThreshold * 2` as a simple heuristic), and there are no
pending backorders (backorder path already handled by `BackorderPolicy`), publish
`ReplenishmentTriggered`.

Extend `LowStockPolicy.cs` with a `CheckReplenishment()` method. Call it inline from
`ReceiveStockHandler.Handle()` and `CreditDestinationForTransfer` handler.

Create `Messages.Contracts.Inventory.ReplenishmentTriggered` integration contract.

This is a P2 slice — keep it simple. The full replenishment workflow (purchase orders,
vendor integration) is out of scope for this remaster.

### Slice 31: NetworkInventorySummaryView (Async Projection)

Unlike `StockAvailabilityView` (inline, routing-critical), the network summary is for
the operations dashboard — eventual consistency is acceptable.

Create `src/Inventory/Inventory/Management/NetworkInventorySummaryView.cs`:

```csharp
public sealed class NetworkInventorySummaryView
{
    public string Sku { get; set; } = string.Empty;
    public int TotalAvailableNetwork { get; set; }
    public int TotalReservedNetwork { get; set; }
    public int TotalCommittedNetwork { get; set; }
    public int TotalOnHandNetwork { get; set; }
    public int WarehouseCount { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
}
```

Implement as a `MultiStreamProjection<NetworkInventorySummaryView, string>` keyed by
SKU — same grouping as `StockAvailabilityView` but summing all quantity buckets.

Register with `ProjectionLifecycle.Async` in `Program.cs`.

**HTTP:** `GET /api/inventory/network/{sku}` — `WarehouseClerk` policy.

### Slice 32: BackorderImpactView (Async Projection)

Create `src/Inventory/Inventory/Management/BackorderImpactView.cs`:

```csharp
public sealed class BackorderImpactView
{
    public string Sku { get; set; } = string.Empty;
    public int PendingBackorderCount { get; set; }
    public int TotalBackorderedQuantity { get; set; }
    public List<BackorderEntry> Backorders { get; set; } = new();
    public DateTimeOffset LastUpdated { get; set; }
}

public sealed record BackorderEntry(
    Guid OrderId, Guid ShipmentId, int Quantity, DateTimeOffset RegisteredAt);
```

Implement as a `MultiStreamProjection<BackorderImpactView, string>` keyed by SKU,
consuming `BackorderRegistered` and `BackorderCleared` events.

Register with `ProjectionLifecycle.Async`.

**HTTP:** `GET /api/inventory/backorders/{sku}` — `WarehouseClerk` policy.

---

## Track E — Quarantine Lifecycle (Slices 33–35)

Create `src/Inventory/Inventory/Management/QuarantineStock.cs` (command + handler),
`ReleaseQuarantine.cs`, and `DisposeQuarantine.cs`.

The quarantine lifecycle follows the feature file scenarios exactly:

**Slice 33 — `POST /api/inventory/{inventoryId}/quarantine`:**
- Appends `StockQuarantined` domain event
- Appends `InventoryAdjusted` (negative — stock removed from available pool)
- `LowStockPolicy` inline check

**Slice 34 — `POST /api/inventory/{inventoryId}/quarantine/release`:**
- Validates quarantine is active (requires tracking quarantined quantity — add
  `QuarantinedQuantity` field to `ProductInventory` or use a separate quarantine dictionary)
- Appends `QuarantineReleased`
- Appends `InventoryAdjusted` (positive — stock returned to available pool)
- `BackorderPolicy` inline check (new available stock may clear backorders)

**Slice 35 — `POST /api/inventory/{inventoryId}/quarantine/dispose`:**
- Validates quarantine is active
- Appends `QuarantineDisposed`
- Appends `StockWrittenOff` (permanent removal — no InventoryAdjusted needed since
  stock was already removed during quarantine)
- Requires `OperationsManager` policy

Add `Apply()` methods to `ProductInventory` for `StockQuarantined`, `QuarantineReleased`,
`QuarantineDisposed`. The simplest tracking approach: add `int QuarantinedQuantity` field
(or `Dictionary<Guid, int> QuarantineAllocations` if per-incident tracking is needed).
Use the dictionary approach for audit trail completeness.

---

## Definition of Done

**S2 Deferred:**
- [ ] `ConcurrencyException` policy changed to `.MoveToErrorQueue()`
- [ ] `StockDiscrepancyAlert` integration contract + publishing from `ItemPickedHandler`
- [ ] HTTP endpoints added for CycleCount, RecordDamage, WriteOffStock

**InventoryTransfer Aggregate:**
- [ ] `InventoryTransfer` aggregate with all Apply methods
- [ ] All 7 domain events created (`TransferRequested`, `TransferShipped`,
  `TransferReceived`, `TransferCancelled`, `TransferShortReceived`,
  `StockTransferredOut`, `TransferStockReceived`)
- [ ] `ProductInventory.Apply(StockTransferredOut)` and `Apply(TransferStockReceived)` added
- [ ] `InventoryTransfer` snapshot registered `Inline`

**Transfer Handlers (Slices 25–29):**
- [ ] `RequestTransferHandler` — creates transfer, debits source via `bus.InvokeAsync()`
- [ ] `ShipTransferHandler` — validates Requested status, appends TransferShipped
- [ ] `ReceiveTransferHandler` — credits destination via `bus.InvokeAsync()`, short receipt path
- [ ] `CancelTransferHandler` — validates pre-ship, restores source via `bus.InvokeAsync()`
- [ ] Short receipt appends `TransferShortReceived` + `StockDiscrepancyFound`

**Projections and Replenishment (Slices 30–32):**
- [ ] `ReplenishmentTriggered` published inline from stock receipt handlers
- [ ] `NetworkInventorySummaryView` async projection + HTTP query
- [ ] `BackorderImpactView` async projection + HTTP query

**Quarantine (Slices 33–35):**
- [ ] `QuarantineStock` — appends StockQuarantined + InventoryAdjusted
- [ ] `ReleaseQuarantine` — validates active, restores stock
- [ ] `DisposeQuarantine` — validates active, permanent write-off

**Cross-cutting:**
- [ ] Build: 0 errors, ≤4 warnings
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

Create `docs/planning/milestones/inventory-remaster-s3-retrospective.md` at session end.
Required sections: What Was Delivered, Deferred Items, Build/Test Status, and any notes on
the inline `bus.InvokeAsync()` coordination pattern discovered during transfer implementation.

---

## Commit Convention

```
inventory-remaster: ConcurrencyException → MoveToErrorQueue (Gap #13 resolution)
inventory-remaster: StockDiscrepancyAlert contract + HTTP endpoint wiring (S2 deferred)
inventory-remaster: InventoryTransfer aggregate + domain events
inventory-remaster: RequestTransferHandler + DebitSourceForTransfer (inline coordination)
inventory-remaster: ShipTransfer + ReceiveTransfer + CancelTransfer handlers
inventory-remaster: short transfer receipt + TransferShortReceipt discrepancy type
inventory-remaster: ReplenishmentTriggered inline policy
inventory-remaster: NetworkInventorySummaryView + BackorderImpactView async projections
inventory-remaster: QuarantineStock + ReleaseQuarantine + DisposeQuarantine
```

## Role Notes

**@PSA — Principal Software Architect**
The `InventoryTransfer` aggregate coordinates two `ProductInventory` streams. The inline
`bus.InvokeAsync()` pattern (Anti-Pattern #13 / bc-remaster.md Inline Policy Invocation)
is the only correct approach — Wolverine cannot cascade from `session.Events.Append()` or
`MartenOps.StartStream()`. Every handler that modifies a `ProductInventory` stream from
within a transfer handler must go through `bus.InvokeAsync()`.

The inner commands (`DebitSourceForTransfer`, `CreditDestinationForTransfer`,
`RestoreSourceAfterCancelledTransfer`) should be simple single-purpose commands with their
own `Load()/Before()/Handle()` handlers. They must NOT be HTTP-exposed — they are internal
coordination commands only.

**@QAE — QA Engineer**
The transfer lifecycle requires testing the full multi-step path: Request → Ship → Receive.
Verify that:
- `TotalOnHand` at source decrements immediately on `RequestTransfer`
- Stock is in limbo (not available at source, not yet at destination) between Ship and Receive
- `TotalOnHand` at destination increments on `ReceiveTransfer`
- Cancel before ship restores source stock (compensation path)
- Short receipt credits destination with actual received quantity only

Also verify the `BackorderPolicy` fires correctly when `ReceiveTransfer` brings stock to
a destination warehouse that has pending backorders.
