# Cycle 26: Returns BC Phase 2 — Implementation Plan

**Status:** 📋 **PLANNED**  
**Date:** 2026-03-13  
**BC:** Returns  
**Port:** 5245  
**Depends On:** Cycle 25 (Returns BC Phase 1) ✅ COMPLETE  

---

## Objective

Phase 2 closes the critical integration gaps identified in the Phase 1 retrospective. After this cycle, the Returns BC publishes contracts rich enough for Inventory BC to restock autonomously and for Customer Experience BC to show real-time return status via SSE. Mixed inspection results are supported, the `GetReturnsForOrder` query works, and a CS agent runbook exists.

### What Phase 2 Does NOT Include

- Exchange workflow (UC-11)
- Fraud detection / risk scoring (UC-09)
- RBAC for CS agent endpoints
- Carrier integration / label generation (`LabelGenerated`, `InTransit` states)
- Store credit for rejected returns
- Order History UI ("Request Return" button)

---

## Deliverables Overview

| # | Deliverable | Priority | Spec Ref |
|---|-------------|----------|----------|
| D1 | `ReturnCompleted` contract expansion (per-item disposition) | P0 | FR-06, CONTEXTS.md |
| D2 | Verify `DeliveredAt` persistence in Orders saga | P0 | FR-01, TR-01 |
| D3 | `ReturnRejected` integration event | P1 | Integration Events table |
| D4 | `ReturnApproved` integration event | P1 | Integration Events table |
| D5 | `ReturnExpired` integration event | P1 | Integration Events table, FR-07 |
| D6 | Mixed inspection results | P1 | FR-05, UC-05 |
| D7 | `GetReturnsForOrder` query (Marten inline projection) | P1 | FR-09, NFR-05 |
| D8 | RabbitMQ routing updates | P1 | CONTEXTS.md |
| D9 | Fulfillment → Returns queue wiring | P1 | TR-01 |
| D10 | CS agent runbook | P1 | OR-01, OR-04 |
| D11 | Tests for all new behavior | — | Testing Strategy |

---

## D1: `ReturnCompleted` Contract Expansion (P0)

### Why

The current `ReturnCompleted` integration event carries only `FinalRefundAmount`. Per the spec (FR-06) and CONTEXTS.md:

> `ReturnCompleted` — inspection passed; **carries full item disposition** (SKU, qty, IsRestockable, warehouse, condition) for Orders BC (refund) and Inventory BC (restocking)

Without per-item disposition data, Inventory BC cannot determine which items to restock, at which warehouse, or in what condition. This is a blocking gap for Inventory integration.

### Changes

#### 1a. New `ReturnedItem` record in Messages.Contracts

**File:** `src/Shared/Messages.Contracts/Returns/ReturnedItem.cs` *(NEW)*

```csharp
namespace Messages.Contracts.Returns;

/// <summary>
/// Per-item disposition data in a completed return.
/// Inventory BC uses this to determine restocking; Orders BC uses it to verify refund line items.
/// </summary>
public sealed record ReturnedItem(
    string Sku,
    int Quantity,
    bool IsRestockable,
    string? WarehouseId,
    string? RestockCondition);
```

**Why separate file:** Follows the existing Messages.Contracts pattern (one record per file). `RestockCondition` uses string (not enum) because it's an integration boundary — the consuming BCs interpret the value; Returns BC doesn't enforce a fixed set.

#### 1b. Expand `ReturnCompleted` contract

**File:** `src/Shared/Messages.Contracts/Returns/ReturnCompleted.cs` *(MODIFY)*

```csharp
namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when a return is fully processed.
/// Orders saga listens to this to trigger a refund and close the saga.
/// Inventory BC listens to restock eligible items.
/// </summary>
public sealed record ReturnCompleted(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    decimal FinalRefundAmount,
    IReadOnlyList<ReturnedItem> Items,
    DateTimeOffset CompletedAt);
```

**Changes from Phase 1:**
- Added `CustomerId` (needed by downstream BCs for customer context)
- Added `IReadOnlyList<ReturnedItem> Items` (the critical per-item disposition data)

#### 1c. Update `SubmitInspectionHandler` to populate expanded contract

**File:** `src/Returns/Returns/Returns/ReturnCommandHandlers.cs` *(MODIFY — `SubmitInspectionHandler.Handle`)*

The handler currently publishes `ReturnCompleted` without items. Update the passed-inspection branch:

```csharp
// In the !hasFailures branch of SubmitInspectionHandler.Handle:
var returnedItems = command.Results.Select(r => new Messages.Contracts.Returns.ReturnedItem(
    Sku: r.Sku,
    Quantity: r.Quantity,
    IsRestockable: r.IsRestockable,
    WarehouseId: r.WarehouseLocation,
    RestockCondition: r.Condition switch
    {
        ItemCondition.AsExpected => "LikeNew",
        ItemCondition.BetterThanExpected => "New",
        _ => "Opened"
    })).ToList().AsReadOnly();

outgoing.Add(new Messages.Contracts.Returns.ReturnCompleted(
    ReturnId: command.ReturnId,
    OrderId: aggregate.OrderId,
    CustomerId: aggregate.CustomerId,
    FinalRefundAmount: finalRefund,
    Items: returnedItems,
    CompletedAt: now));
```

**Condition-to-RestockCondition mapping:**
| `ItemCondition` (domain) | `RestockCondition` (contract) | Rationale |
|---|---|---|
| `AsExpected` | `"LikeNew"` | Item matches reported condition; sellable as like-new |
| `BetterThanExpected` | `"New"` | Item is in better condition than expected; sellable as new |
| `WorseThanExpected` | `"Opened"` | Only reaches this branch in mixed results (see D6) |

#### 1d. Update `Order.Handle(ReturnCompleted)` in Orders saga

**File:** `src/Orders/Orders/Placement/Order.cs` *(MODIFY)*

The Order saga handler currently uses the old 4-parameter constructor. Update to match the new contract:

```csharp
// Current:
public OutgoingMessages Handle(Messages.Contracts.Returns.ReturnCompleted message)
// No signature change — the message record gains new properties but the handler
// only uses ReturnId, FinalRefundAmount, and Id (OrderId from saga state).
// The new Items/CustomerId fields are used by Inventory BC, not Orders.
```

**No code change needed in Orders saga handler itself** — it already destructures only `FinalRefundAmount`. The record expansion is backward-compatible for handlers that don't use the new fields. However, any **test mocks** that construct `ReturnCompleted` with positional syntax will need updating.

#### 1e. Update existing tests that construct `ReturnCompleted`

**Files to update:**
- `tests/Returns/Returns.Api.IntegrationTests/ReturnLifecycleEndpointTests.cs` — any assertions on the outgoing `ReturnCompleted` message shape
- `tests/Orders/Orders.UnitTests/` — any tests that construct `ReturnCompleted` for Order saga

Search for: `new Messages.Contracts.Returns.ReturnCompleted(` and `new ReturnCompleted(` across all test projects.

### Use Cases Supported

- UC-01 (Defective — full lifecycle): Inventory sees `IsRestockable: false`, disposes item
- UC-02 (Unwanted — restocking fee): Inventory sees `IsRestockable: true`, `RestockCondition: "LikeNew"`
- UC-04 (Partial return): `Items` list contains only returned items
- UC-05 (Failed inspection with mixed results): See D6

---

## D2: Verify `DeliveredAt` Persistence in Orders Saga (P0)

### Why

The Returns BC needs `DeliveredAt` for the 30-day eligibility window. Currently the `GetReturnableItems` endpoint in Orders.Api returns `DeliveredAt: null` with a comment:

```csharp
// DeliveredAt is not persisted on the Order saga (it belongs to the Shipment aggregate).
```

Returns BC gets `DeliveredAt` from `ShipmentDelivered` directly via the Fulfillment queue, so this is **not a blocker** for return eligibility. However, the BFF needs `DeliveredAt` for the "Return by {date}" display.

### Changes

#### 2a. Add `DeliveredAt` to Order saga state

**File:** `src/Orders/Orders/Placement/Order.cs` *(MODIFY)*

Add property to the Order saga:

```csharp
public DateTimeOffset? DeliveredAt { get; set; }
```

In `Handle(ShipmentDelivered)`, persist the value:

```csharp
DeliveredAt = message.DeliveredAt;
```

#### 2b. Update `GetReturnableItems` endpoint

**File:** `src/Orders/Orders.Api/Orders/GetReturnableItems.cs` *(MODIFY)*

Replace the null `DeliveredAt`:

```csharp
return Results.Ok(new ReturnableItemsResponse(orderId, items, order.DeliveredAt));
```

Remove the comment about `DeliveredAt` not being persisted.

### Use Cases Supported

- BFF "Return by {date}" display
- Cross-verification of return eligibility window

---

## D3: `ReturnRejected` Integration Event (P1)

### Why

Phase 1 handles rejection internally (`InspectionFailed` → `Rejected` terminal state) but never publishes an integration event. Per CONTEXTS.md:

> `ReturnRejected` — inspection failed; disposition applied (Dispose, ReturnToCustomer, Quarantine)

Customer Experience BC needs this for real-time SSE updates. Notifications BC needs it to send rejection emails. Orders BC needs it to clear the in-progress flag (currently only `ReturnDenied` clears it, but rejection after inspection is a different terminal path).

### Changes

#### 3a. New `ReturnRejected` contract

**File:** `src/Shared/Messages.Contracts/Returns/ReturnRejected.cs` *(NEW)*

```csharp
namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when a return fails inspection.
/// Customer Experience BC shows rejection details; Orders BC clears return-in-progress flag.
/// </summary>
public sealed record ReturnRejected(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    string Reason,
    IReadOnlyList<ReturnedItem> Items,
    DateTimeOffset RejectedAt);
```

**Why it carries `Items`:** The rejected items include disposition data (`IsRestockable: false`, `WarehouseId`, `RestockCondition`) so downstream BCs know what happened to the physical goods. Orders BC can decide on goodwill credit based on disposition.

#### 3b. Publish `ReturnRejected` from `SubmitInspectionHandler`

**File:** `src/Returns/Returns/Returns/ReturnCommandHandlers.cs` *(MODIFY — `SubmitInspectionHandler.Handle`, the `hasFailures` branch)*

Currently the failure branch only appends a domain event. Add integration message:

```csharp
// In the hasFailures (all-fail) branch — will be updated again in D6 for mixed:
var rejectedItems = command.Results.Select(r => new Messages.Contracts.Returns.ReturnedItem(
    Sku: r.Sku,
    Quantity: r.Quantity,
    IsRestockable: false,
    WarehouseId: r.WarehouseLocation,
    RestockCondition: null)).ToList().AsReadOnly();

outgoing.Add(new Messages.Contracts.Returns.ReturnRejected(
    ReturnId: command.ReturnId,
    OrderId: aggregate.OrderId,
    CustomerId: aggregate.CustomerId,
    Reason: "Inspection found items in unacceptable condition.",
    Items: rejectedItems,
    RejectedAt: now));
```

#### 3c. Add `ReturnRejected` handler in Orders saga

**File:** `src/Orders/Orders/Placement/Order.cs` *(MODIFY)*

```csharp
/// <summary>
/// Saga handler for return rejection from Returns BC (inspection failed).
/// Clears the return-in-progress flag. If return window already expired, closes saga.
/// </summary>
public void Handle(Messages.Contracts.Returns.ReturnRejected message)
{
    IsReturnInProgress = false;
    ActiveReturnId = null;
    if (ReturnWindowFired)
    {
        Status = OrderStatus.Closed;
        MarkCompleted();
    }
}
```

This mirrors the existing `Handle(ReturnDenied)` logic — rejection and denial are both terminal states that clear the return flag.

### Use Cases Supported

- UC-05 (Failed inspection): Customer Experience BC shows "Return Rejected" status
- UC-06 (Wrong item returned): Orders saga clears in-progress flag

---

## D4: `ReturnApproved` Integration Event (P1)

### Why

Per CONTEXTS.md, Returns BC publishes `ReturnApproved` for Customer Experience BC (real-time UI updates via SSE) and Notifications BC (approval confirmation email). Phase 1 handles approval internally but only publishes `ReturnRequested` to the Orders queue.

### Changes

#### 4a. New `ReturnApproved` contract

**File:** `src/Shared/Messages.Contracts/Returns/ReturnApproved.cs` *(NEW)*

```csharp
namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when a return is approved.
/// Customer Experience BC updates return status UI; Notifications BC sends approval email.
/// </summary>
public sealed record ReturnApproved(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    decimal EstimatedRefundAmount,
    decimal RestockingFeeAmount,
    DateTimeOffset ShipByDeadline,
    DateTimeOffset ApprovedAt);
```

#### 4b. Publish from `RequestReturnHandler` (auto-approval path)

**File:** `src/Returns/Returns/Returns/RequestReturnHandler.cs` *(MODIFY)*

In the `!requiresReview` branch, after publishing `ReturnRequested`, add:

```csharp
// Publish ReturnApproved for Customer Experience BC / Notifications BC
await bus.PublishAsync(new Messages.Contracts.Returns.ReturnApproved(
    ReturnId: returnId,
    OrderId: command.OrderId,
    CustomerId: command.CustomerId,
    EstimatedRefundAmount: estimatedRefund,
    RestockingFeeAmount: restockingFee,
    ShipByDeadline: shipByDeadline,
    ApprovedAt: now));
```

#### 4c. Publish from `ApproveReturnHandler` (manual CS approval path)

**File:** `src/Returns/Returns/Returns/ReturnCommandHandlers.cs` *(MODIFY — `ApproveReturnHandler.Handle`)*

The handler currently returns a single `ReturnApproved` domain event. It needs to also publish the integration event. Change the return type to include `OutgoingMessages`:

```csharp
[WolverinePost("/api/returns/{returnId}/approve")]
public static async Task<(ReturnApproved, OutgoingMessages)> Handle(
    ApproveReturn command,
    [WriteAggregate] Return aggregate,
    IMessageBus bus)
{
    var now = DateTimeOffset.UtcNow;
    var shipByDeadline = now.AddDays(ReturnEligibilityWindow.ReturnWindowDays);
    var (estimatedRefund, restockingFee) = Return.CalculateEstimatedRefund(aggregate.Items);

    // Schedule expiration
    await bus.ScheduleAsync(new ExpireReturn(command.ReturnId), shipByDeadline);

    var domainEvent = new ReturnApproved(
        ReturnId: command.ReturnId,
        EstimatedRefundAmount: estimatedRefund,
        RestockingFeeAmount: restockingFee,
        ShipByDeadline: shipByDeadline,
        ApprovedAt: now);

    var outgoing = new OutgoingMessages();
    outgoing.Add(new Messages.Contracts.Returns.ReturnApproved(
        ReturnId: command.ReturnId,
        OrderId: aggregate.OrderId,
        CustomerId: aggregate.CustomerId,
        EstimatedRefundAmount: estimatedRefund,
        RestockingFeeAmount: restockingFee,
        ShipByDeadline: shipByDeadline,
        ApprovedAt: now));

    return (domainEvent, outgoing);
}
```

**Note on return type:** Wolverine compound handlers support tuple returns with `(DomainEvent, OutgoingMessages)` for `[WriteAggregate]` handlers. The domain event is appended to the Marten stream; the `OutgoingMessages` are dispatched to RabbitMQ. This removes the need for `IMessageBus` for the publish (but we still need it for `ScheduleAsync`).

### Use Cases Supported

- Customer Experience BC: Real-time SSE push of "Your return has been approved"
- Notifications BC: Approval confirmation email with ship-by deadline

---

## D5: `ReturnExpired` Integration Event (P1)

### Why

Per CONTEXTS.md and the spec (FR-07):

> Customers **must** be notified of expiration (via Notifications BC reacting to `Returns.ReturnExpired` integration event).

Phase 1 appends the `ReturnExpired` domain event but never publishes an integration message. The `ExpireReturnHandler` uses `IDocumentSession` directly (not `[WriteAggregate]`), so we add `IMessageBus` publishing.

### Changes

#### 5a. New `ReturnExpired` contract

**File:** `src/Shared/Messages.Contracts/Returns/ReturnExpired.cs` *(NEW)*

```csharp
namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when an approved return expires
/// (customer never shipped within the 30-day window).
/// Notifications BC sends expiration notice; Orders saga clears return flag.
/// </summary>
public sealed record ReturnExpired(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset ExpiredAt);
```

#### 5b. Publish from `ExpireReturnHandler`

**File:** `src/Returns/Returns/Returns/ReturnCommandHandlers.cs` *(MODIFY — `ExpireReturnHandler.Handle`)*

```csharp
public static class ExpireReturnHandler
{
    public static async Task Handle(
        ExpireReturn command,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken ct)
    {
        var aggregate = await session.Events.AggregateStreamAsync<Return>(command.ReturnId, token: ct);

        if (aggregate is null || aggregate.Status != ReturnStatus.Approved)
            return;

        var expired = new ReturnExpired(
            ReturnId: command.ReturnId,
            ExpiredAt: DateTimeOffset.UtcNow);

        session.Events.Append(command.ReturnId, expired);

        // NEW: Publish integration event for Notifications BC and Orders saga
        await bus.PublishAsync(new Messages.Contracts.Returns.ReturnExpired(
            ReturnId: command.ReturnId,
            OrderId: aggregate.OrderId,
            CustomerId: aggregate.CustomerId,
            ExpiredAt: expired.ExpiredAt));
    }
}
```

#### 5c. Add `ReturnExpired` handler in Orders saga

**File:** `src/Orders/Orders/Placement/Order.cs` *(MODIFY)*

```csharp
/// <summary>
/// Saga handler for return expiration from Returns BC.
/// Customer never shipped; clears return-in-progress flag and closes saga if window already fired.
/// </summary>
public void Handle(Messages.Contracts.Returns.ReturnExpired message)
{
    IsReturnInProgress = false;
    ActiveReturnId = null;
    if (ReturnWindowFired)
    {
        Status = OrderStatus.Closed;
        MarkCompleted();
    }
}
```

### Use Cases Supported

- UC-07 (Customer never ships): Notifications BC sends expiration email
- Orders saga: Prevents dangling `IsReturnInProgress = true` state

---

## D6: Mixed Inspection Results (P1)

### Why

Phase 1 uses binary pass/fail logic: if **any** item has `WorseThanExpected` condition or `Dispose`/`Quarantine`/`ReturnToCustomer` disposition, the entire return fails. Real returns are messier:

> A customer returns 3 items. 2 pass inspection (restockable). 1 is damaged beyond what was reported (fails). The customer should get a refund for the 2 good items; the 1 damaged item is rejected.

The spec supports this (FR-05: "per-line-item condition assessment") and the domain events already carry `IReadOnlyList<InspectionLineResult>` — we just need the handler logic to split results.

### Changes

#### 6a. New domain event: `InspectionMixed`

**File:** `src/Returns/Returns/Returns/ReturnEvents.cs` *(MODIFY — add new record)*

```csharp
/// <summary>
/// Some items passed inspection and some failed. The return completes
/// with a partial refund for passed items. Failed items get their own disposition.
/// </summary>
public sealed record InspectionMixed(
    Guid ReturnId,
    IReadOnlyList<InspectionLineResult> PassedItems,
    IReadOnlyList<InspectionLineResult> FailedItems,
    decimal FinalRefundAmount,
    decimal RestockingFeeAmount,
    DateTimeOffset CompletedAt);
```

#### 6b. Add `Apply(InspectionMixed)` to Return aggregate

**File:** `src/Returns/Returns/Returns/Return.cs` *(MODIFY)*

```csharp
public Return Apply(InspectionMixed @event) => this with
{
    Status = ReturnStatus.Completed,
    InspectionResults = @event.PassedItems.Concat(@event.FailedItems).ToList().AsReadOnly(),
    FinalRefundAmount = @event.FinalRefundAmount,
    RestockingFeeAmount = @event.RestockingFeeAmount,
    CompletedAt = @event.CompletedAt
};
```

**Design decision:** Mixed results terminate in `Completed` (not `Rejected`). The return is "completed" — some items were refunded, some were not. The `Items` list in the `ReturnCompleted` integration event tells downstream BCs exactly which items are restockable and which are not.

#### 6c. Rewrite `SubmitInspectionHandler.Handle` with three-way logic

**File:** `src/Returns/Returns/Returns/ReturnCommandHandlers.cs` *(MODIFY — `SubmitInspectionHandler.Handle`)*

Replace the binary pass/fail with:

```csharp
[WolverinePost("/api/returns/{returnId}/inspection")]
public static (Events, OutgoingMessages) Handle(
    SubmitInspection command,
    [WriteAggregate] Return aggregate)
{
    var now = DateTimeOffset.UtcNow;
    var events = new Events();
    var outgoing = new OutgoingMessages();

    // If not already inspecting, start inspection
    if (aggregate.Status == ReturnStatus.Received)
    {
        events.Add(new InspectionStarted(
            ReturnId: command.ReturnId,
            InspectorId: "system",
            StartedAt: now));
    }

    // Partition results into passed and failed
    var passed = command.Results.Where(r =>
        r.Condition is not ItemCondition.WorseThanExpected &&
        r.Disposition is not (DispositionDecision.Dispose
            or DispositionDecision.Quarantine
            or DispositionDecision.ReturnToCustomer)).ToList();

    var failed = command.Results.Where(r =>
        r.Condition is ItemCondition.WorseThanExpected ||
        r.Disposition is DispositionDecision.Dispose
            or DispositionDecision.Quarantine
            or DispositionDecision.ReturnToCustomer).ToList();

    // Helper: map InspectionLineResult → ReturnedItem (for integration events)
    static IReadOnlyList<Messages.Contracts.Returns.ReturnedItem> ToReturnedItems(
        List<InspectionLineResult> results, bool isRestockable) =>
        results.Select(r => new Messages.Contracts.Returns.ReturnedItem(
            Sku: r.Sku,
            Quantity: r.Quantity,
            IsRestockable: isRestockable && r.IsRestockable,
            WarehouseId: r.WarehouseLocation,
            RestockCondition: isRestockable
                ? r.Condition switch
                {
                    ItemCondition.AsExpected => "LikeNew",
                    ItemCondition.BetterThanExpected => "New",
                    _ => "Opened"
                }
                : null)).ToList().AsReadOnly();

    if (failed.Count == 0)
    {
        // ALL PASSED — full refund
        var (finalRefund, restockingFee) = Return.CalculateEstimatedRefund(aggregate.Items);

        events.Add(new InspectionPassed(
            ReturnId: command.ReturnId,
            Results: command.Results,
            FinalRefundAmount: finalRefund,
            RestockingFeeAmount: restockingFee,
            CompletedAt: now));

        outgoing.Add(new Messages.Contracts.Returns.ReturnCompleted(
            ReturnId: command.ReturnId,
            OrderId: aggregate.OrderId,
            CustomerId: aggregate.CustomerId,
            FinalRefundAmount: finalRefund,
            Items: ToReturnedItems(passed, isRestockable: true),
            CompletedAt: now));
    }
    else if (passed.Count == 0)
    {
        // ALL FAILED — no refund
        events.Add(new InspectionFailed(
            ReturnId: command.ReturnId,
            Results: command.Results,
            FailureReason: "Inspection found items in unacceptable condition.",
            CompletedAt: now));

        outgoing.Add(new Messages.Contracts.Returns.ReturnRejected(
            ReturnId: command.ReturnId,
            OrderId: aggregate.OrderId,
            CustomerId: aggregate.CustomerId,
            Reason: "Inspection found items in unacceptable condition.",
            Items: ToReturnedItems(failed, isRestockable: false),
            RejectedAt: now));
    }
    else
    {
        // MIXED — partial refund for passed items only
        var passedSkus = passed.Select(p => p.Sku).ToHashSet();
        var passedLineItems = aggregate.Items
            .Where(li => passedSkus.Contains(li.Sku))
            .ToList().AsReadOnly();
        var (partialRefund, restockingFee) = Return.CalculateEstimatedRefund(passedLineItems);

        events.Add(new InspectionMixed(
            ReturnId: command.ReturnId,
            PassedItems: passed.AsReadOnly(),
            FailedItems: failed.AsReadOnly(),
            FinalRefundAmount: partialRefund,
            RestockingFeeAmount: restockingFee,
            CompletedAt: now));

        // Publish ReturnCompleted with ALL items — passed items are restockable,
        // failed items are not. This gives Inventory BC complete disposition data
        // and Orders BC the partial refund amount.
        var allItems = ToReturnedItems(passed, isRestockable: true)
            .Concat(ToReturnedItems(failed, isRestockable: false))
            .ToList().AsReadOnly();

        outgoing.Add(new Messages.Contracts.Returns.ReturnCompleted(
            ReturnId: command.ReturnId,
            OrderId: aggregate.OrderId,
            CustomerId: aggregate.CustomerId,
            FinalRefundAmount: partialRefund,
            Items: allItems,
            CompletedAt: now));
    }

    return (events, outgoing);
}
```

**Key design decisions:**
1. Mixed results → `Completed` (not `Rejected`). The customer gets a partial refund.
2. `ReturnCompleted` is always published for both all-pass and mixed (Inventory needs it). Only all-fail publishes `ReturnRejected`.
3. `FinalRefundAmount` covers only passed items. Failed items are excluded from refund calculation.
4. The `Items` list in `ReturnCompleted` for mixed results includes **all** items with accurate `IsRestockable` flags — failed items have `IsRestockable: false`.

### Use Cases Supported

- UC-05 extended: 3-item return, 2 pass, 1 fails → partial refund
- UC-06 extended: Some items correct, one is wrong item → mixed result

---

## D7: `GetReturnsForOrder` Query Fix (P1)

### Why

The `GET /api/returns?orderId={orderId}` endpoint currently returns an empty list:

```csharp
var returns = new List<ReturnSummaryResponse>();
return returns.AsReadOnly();
```

This blocks the BFF from showing "My Returns" for a given order. Phase 1 acknowledged this was a stub. Phase 2 implements it using Marten's inline snapshot projection, which is already configured.

### Changes

#### 7a. Rewrite `GetReturnsForOrderHandler`

**File:** `src/Returns/Returns/Returns/ReturnQueries.cs` *(MODIFY — `GetReturnsForOrderHandler`)*

Since Phase 1 already configures inline snapshots (`opts.Projections.Snapshot<Return>(SnapshotLifecycle.Inline)`), we can query the `Return` document directly. The snapshot is persisted after every event append, so it's always current.

```csharp
public static class GetReturnsForOrderHandler
{
    [WolverineGet("/api/returns")]
    public static async Task<IReadOnlyList<ReturnSummaryResponse>> Handle(
        [FromQuery] Guid orderId,
        IQuerySession session,
        CancellationToken ct)
    {
        var returns = await session.Query<Return>()
            .Where(r => r.OrderId == orderId)
            .ToListAsync(ct);

        return returns
            .Select(GetReturnHandler.ToResponse)
            .ToList()
            .AsReadOnly();
    }
}
```

**Why this works:** Marten inline snapshots persist the full `Return` aggregate as a document after every event append. `session.Query<Return>()` queries this document store. No additional projection is needed.

#### 7b. Add Marten index for `OrderId` queries

**File:** `src/Returns/Returns.Api/Program.cs` *(MODIFY — Marten configuration)*

Add an index on `OrderId` for efficient lookup:

```csharp
// In the AddMarten configuration block, after the snapshot registration:
opts.Schema.For<Return>()
    .Index(x => x.OrderId);
```

#### 7c. Add `[FromQuery]` attribute import

The `[FromQuery]` attribute is in `Microsoft.AspNetCore.Mvc`. Verify the `using` is present in `ReturnQueries.cs`. Currently it has `using Microsoft.AspNetCore.Http;` — add `using Microsoft.AspNetCore.Mvc;` if not present.

### Use Cases Supported

- BFF: "My Returns" page showing all returns for a given order
- CS Agent: View return history for an order

---

## D8: RabbitMQ Routing Updates (P1)

### Why

Phase 1 routes all outbound messages to `orders-returns-events`. Phase 2 adds new integration events that need to reach:
1. **Orders BC** — `ReturnRequested`, `ReturnCompleted`, `ReturnDenied`, `ReturnRejected`, `ReturnExpired`
2. **Customer Experience BC** — `ReturnRequested`, `ReturnApproved`, `ReturnDenied`, `ReturnRejected`, `ReturnExpired`, `ReturnCompleted`
3. **Inventory BC** — `ReturnCompleted` (when ready in a future phase)

Following the established pattern (Fulfillment publishes to both `orders-fulfillment-events` and `storefront-fulfillment-events`), Returns should publish to:
- `orders-returns-events` — consumed by Orders saga
- `storefront-returns-events` — consumed by Customer Experience BC (Storefront.Api)

### Changes

#### 8a. Update Returns.Api routing

**File:** `src/Returns/Returns.Api/Program.cs` *(MODIFY — Wolverine RabbitMQ configuration)*

Replace the current outbound routing block:

```csharp
// === Outbound: Orders BC ===
// Orders saga needs: ReturnRequested, ReturnCompleted, ReturnDenied, ReturnRejected, ReturnExpired
opts.PublishMessage<Messages.Contracts.Returns.ReturnRequested>()
    .ToRabbitQueue("orders-returns-events");
opts.PublishMessage<Messages.Contracts.Returns.ReturnCompleted>()
    .ToRabbitQueue("orders-returns-events");
opts.PublishMessage<Messages.Contracts.Returns.ReturnDenied>()
    .ToRabbitQueue("orders-returns-events");
opts.PublishMessage<Messages.Contracts.Returns.ReturnRejected>()
    .ToRabbitQueue("orders-returns-events");
opts.PublishMessage<Messages.Contracts.Returns.ReturnExpired>()
    .ToRabbitQueue("orders-returns-events");

// === Outbound: Customer Experience BC (Storefront) ===
// Real-time SSE updates for return status
opts.PublishMessage<Messages.Contracts.Returns.ReturnRequested>()
    .ToRabbitQueue("storefront-returns-events");
opts.PublishMessage<Messages.Contracts.Returns.ReturnApproved>()
    .ToRabbitQueue("storefront-returns-events");
opts.PublishMessage<Messages.Contracts.Returns.ReturnDenied>()
    .ToRabbitQueue("storefront-returns-events");
opts.PublishMessage<Messages.Contracts.Returns.ReturnRejected>()
    .ToRabbitQueue("storefront-returns-events");
opts.PublishMessage<Messages.Contracts.Returns.ReturnExpired>()
    .ToRabbitQueue("storefront-returns-events");
opts.PublishMessage<Messages.Contracts.Returns.ReturnCompleted>()
    .ToRabbitQueue("storefront-returns-events");
```

**Note:** `ReturnApproved` only goes to `storefront-returns-events`, not `orders-returns-events`. The Orders saga already knows the return exists from `ReturnRequested` — it doesn't need to know about approval (the saga is waiting for `ReturnCompleted`, `ReturnDenied`, `ReturnRejected`, or `ReturnExpired`).

#### 8b. Add listener in Orders.Api for new events

**File:** `src/Orders/Orders.Api/Program.cs` *(MODIFY)*

The `orders-returns-events` queue already exists and Orders listens to it. No change needed — Wolverine auto-discovers handlers for message types on the queue. But verify the Order saga has handlers for `ReturnRejected` and `ReturnExpired` (added in D3c and D5c).

#### 8c. Add listener in Storefront.Api

**File:** `src/Customer Experience/Storefront.Api/Program.cs` *(MODIFY)*

Add the new queue listener:

```csharp
opts.ListenToRabbitQueue("storefront-returns-events")
    .ProcessInline();
```

**Note:** Storefront.Api handlers for these events don't exist yet. They would be created in a Customer Experience integration cycle. For Phase 2, we just wire up the routing so messages are delivered. Storefront can add handlers when ready — messages will be queued but not consumed (Wolverine handles this gracefully with dead-lettering).

### Queue Topology Summary (After Phase 2)

| Queue | Producer | Consumer | Messages |
|-------|----------|----------|----------|
| `returns-fulfillment-events` | Fulfillment BC | Returns BC | `ShipmentDelivered` |
| `orders-returns-events` | Returns BC | Orders BC | `ReturnRequested`, `ReturnCompleted`, `ReturnDenied`, `ReturnRejected`, `ReturnExpired` |
| `storefront-returns-events` | Returns BC | Customer Experience BC | `ReturnRequested`, `ReturnApproved`, `ReturnDenied`, `ReturnRejected`, `ReturnExpired`, `ReturnCompleted` |

---

## D9: Fulfillment → Returns Queue Wiring (P1)

### Why

Phase 1 configures Returns to listen on `returns-fulfillment-events`, but Fulfillment BC **never publishes to that queue**. Fulfillment publishes `ShipmentDelivered` to `orders-fulfillment-events` and `storefront-fulfillment-events` — but not `returns-fulfillment-events`.

This means the `ShipmentDeliveredHandler` in Returns never fires in a live system. Integration tests work because they seed `ReturnEligibilityWindow` directly.

### Changes

#### 9a. Add Returns queue publishing to Fulfillment.Api

**File:** `src/Fulfillment/Fulfillment.Api/Program.cs` *(MODIFY)*

Add after the existing Storefront publishing block:

```csharp
// Publish to Returns BC for return eligibility window
opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDelivered>()
    .ToRabbitQueue("returns-fulfillment-events");
```

### Use Cases Supported

- All return use cases: Without this, no `ReturnEligibilityWindow` documents are created in production

---

## D10: CS Agent Runbook (P1)

### Why

CS agents need operational documentation for the approve/deny/inspection endpoints. Phase 1 retrospective flagged this as P1.

### Changes

**File:** `docs/returns/CS-AGENT-RUNBOOK.md` *(NEW)*

This is a brief operational guide covering:

1. **How to approve a return:** `POST /api/returns/{returnId}/approve` — when to use (return in `Requested` state after "Other" reason review)
2. **How to deny a return:** `POST /api/returns/{returnId}/deny` — required fields (`Reason`, `Message`), common denial reasons
3. **How to submit inspection results:** `POST /api/returns/{returnId}/inspection` — required `InspectionLineResult` fields, condition/disposition values
4. **How to look up a return:** `GET /api/returns/{returnId}` — status field meanings
5. **How to list returns for an order:** `GET /api/returns?orderId={orderId}`
6. **Status definitions:** Customer-facing labels for each state
7. **Escalation path:** What to do when automation fails (stuck returns, missing eligibility windows)

---

## D11: Test Plan

### Unit Tests (New/Modified)

| Test | File | What It Validates |
|------|------|-------------------|
| `Apply_InspectionMixed_SetsCompletedStatus` | `ReturnAggregateTests.cs` | Mixed event sets `Completed` status with partial refund |
| `Apply_InspectionMixed_StoresAllResults` | `ReturnAggregateTests.cs` | Both passed and failed results merged into `InspectionResults` |
| `CalculateEstimatedRefund_SubsetOfItems` | `ReturnCalculationTests.cs` | Partial refund calculation for passed-only items |
| `InspectionMixed_PartialRefund_ExcludesFailedItems` | `ReturnCalculationTests.cs` | Fee calculation on passed items only |

### Integration Tests (New/Modified)

| Test | File | What It Validates |
|------|------|-------------------|
| `SubmitInspection_AllPass_PublishesExpandedReturnCompleted` | `ReturnLifecycleEndpointTests.cs` | `ReturnCompleted` contract includes `Items` and `CustomerId` |
| `SubmitInspection_AllFail_PublishesReturnRejected` | `ReturnLifecycleEndpointTests.cs` | `ReturnRejected` integration event published with items |
| `SubmitInspection_MixedResults_PublishesReturnCompleted` | `ReturnLifecycleEndpointTests.cs` | Mixed → `Completed` with partial refund and all items |
| `SubmitInspection_MixedResults_PartialRefundCalculation` | `ReturnLifecycleEndpointTests.cs` | `FinalRefundAmount` covers only passed items |
| `ApproveReturn_PublishesReturnApprovedIntegration` | `ReturnLifecycleEndpointTests.cs` | `ReturnApproved` integration event dispatched |
| `GetReturnsForOrder_ReturnsMatchingReturns` | `RequestReturnEndpointTests.cs` | `GET /api/returns?orderId=` returns non-empty list |
| `GetReturnsForOrder_NoReturns_ReturnsEmptyList` | `RequestReturnEndpointTests.cs` | Empty list for order with no returns |
| Update existing `ReturnCompleted` construction sites | Various | Match new 6-parameter constructor |

### Existing Tests to Update

Any test that constructs `Messages.Contracts.Returns.ReturnCompleted` with the old 4-parameter constructor must be updated to include `CustomerId` and `Items`. Search for `new Messages.Contracts.Returns.ReturnCompleted(` and `new ReturnCompleted(` across:

- `tests/Returns/Returns.Api.IntegrationTests/`
- `tests/Orders/Orders.UnitTests/`

---

## Implementation Order

Execute deliverables in this order to minimize rework:

```
D1  ReturnCompleted expansion     ← Must be first (contract change ripples everywhere)
 │
 ├─ D2  DeliveredAt in Orders      ← Independent, can be parallel
 │
D3  ReturnRejected event          ← Depends on D1 (reuses ReturnedItem)
D4  ReturnApproved event          ← Independent of D3
D5  ReturnExpired event            ← Independent of D3/D4
 │
D6  Mixed inspection results      ← Depends on D1, D3 (rewrites SubmitInspectionHandler)
 │
D7  GetReturnsForOrder query      ← Independent
D8  RabbitMQ routing               ← Depends on D3, D4, D5 (new message types exist)
D9  Fulfillment queue wiring       ← Independent
D10 CS agent runbook               ← Independent (documentation)
D11 Tests                          ← Throughout; final pass after D6
```

**Recommended execution:**

1. **Batch 1 (Contracts):** D1 → D3 → D4 → D5 (all new/modified contracts in Messages.Contracts)
2. **Batch 2 (Handler Logic):** D6 (rewrite SubmitInspectionHandler with three-way logic)
3. **Batch 3 (Infrastructure):** D7 + D8 + D9 (query fix, routing, wiring)
4. **Batch 4 (Upstream):** D2 (Orders saga DeliveredAt)
5. **Batch 5 (Docs):** D10 (runbook)
6. **Batch 6 (Tests):** D11 (all new/modified tests)

---

## Files Changed Summary

### New Files (5)

| File | Type |
|------|------|
| `src/Shared/Messages.Contracts/Returns/ReturnedItem.cs` | Integration contract |
| `src/Shared/Messages.Contracts/Returns/ReturnApproved.cs` | Integration contract |
| `src/Shared/Messages.Contracts/Returns/ReturnRejected.cs` | Integration contract |
| `src/Shared/Messages.Contracts/Returns/ReturnExpired.cs` | Integration contract |
| `docs/returns/CS-AGENT-RUNBOOK.md` | Documentation |

### Modified Files (9)

| File | What Changes |
|------|-------------|
| `src/Shared/Messages.Contracts/Returns/ReturnCompleted.cs` | Add `CustomerId`, `Items` |
| `src/Returns/Returns/Returns/ReturnEvents.cs` | Add `InspectionMixed` record |
| `src/Returns/Returns/Returns/Return.cs` | Add `Apply(InspectionMixed)` |
| `src/Returns/Returns/Returns/ReturnCommandHandlers.cs` | Rewrite `SubmitInspectionHandler`, update `ApproveReturnHandler`, update `ExpireReturnHandler` |
| `src/Returns/Returns/Returns/RequestReturnHandler.cs` | Publish `ReturnApproved` integration event |
| `src/Returns/Returns/Returns/ReturnQueries.cs` | Implement `GetReturnsForOrder` with Marten query |
| `src/Returns/Returns.Api/Program.cs` | RabbitMQ routing + Marten index |
| `src/Fulfillment/Fulfillment.Api/Program.cs` | Add `returns-fulfillment-events` publish |
| `src/Orders/Orders/Placement/Order.cs` | Add `DeliveredAt`, add `ReturnRejected`/`ReturnExpired` handlers |

### Possibly Modified (test updates)

| File | What Changes |
|------|-------------|
| `tests/Returns/Returns.UnitTests/ReturnAggregateTests.cs` | Add `InspectionMixed` tests |
| `tests/Returns/Returns.UnitTests/ReturnCalculationTests.cs` | Add partial refund tests |
| `tests/Returns/Returns.Api.IntegrationTests/ReturnLifecycleEndpointTests.cs` | Update `ReturnCompleted` constructors, add new tests |
| `tests/Orders/Orders.UnitTests/*` | Update `ReturnCompleted` constructors |
| `src/Customer Experience/Storefront.Api/Program.cs` | Add `storefront-returns-events` listener |

---

## Acceptance Criteria

- [ ] `ReturnCompleted` integration event carries `CustomerId` and `IReadOnlyList<ReturnedItem>` with per-item `Sku`, `Quantity`, `IsRestockable`, `WarehouseId`, `RestockCondition`
- [ ] `ReturnRejected` integration event published when all inspection results fail
- [ ] `ReturnApproved` integration event published on both auto-approval and manual approval paths
- [ ] `ReturnExpired` integration event published when scheduled expiration fires
- [ ] Mixed inspection results: 2-pass/1-fail return → `Completed` status, partial refund for passed items only
- [ ] `GET /api/returns?orderId={orderId}` returns non-empty list when returns exist for that order
- [ ] Orders saga `DeliveredAt` property persisted from `ShipmentDelivered`
- [ ] Fulfillment BC publishes `ShipmentDelivered` to `returns-fulfillment-events` queue
- [ ] All new integration events routed to `orders-returns-events` and `storefront-returns-events`
- [ ] All existing tests updated for new `ReturnCompleted` constructor
- [ ] New unit tests for `InspectionMixed` aggregate behavior
- [ ] New integration tests for mixed inspection, `ReturnRejected`, expanded `ReturnCompleted`

---

## CONTEXTS.md Updates Required

After Phase 2 implementation, verify CONTEXTS.md Returns section reflects:

1. **What it publishes** list includes all 6 events (ReturnRequested, ReturnApproved, ReturnDenied, ReturnRejected, ReturnExpired, ReturnCompleted) — ✅ already documented
2. **ReturnCompleted** description mentions per-item disposition data — ✅ already documented  
3. **ReturnRejected** description mentions post-inspection rejection — ✅ already documented
4. No CONTEXTS.md changes needed (Phase 1 proactively documented Phase 2 events)

---

*Last Updated: 2026-03-13*
