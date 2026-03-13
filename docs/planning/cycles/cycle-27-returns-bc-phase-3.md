# Cycle 27: Returns BC Phase 3 — Implementation Plan

**Status:** ✅ **APPROVED** — UX Engineer sign-off obtained (2026-03-13)
**Date:** 2026-03-13
**BC:** Returns + Customer Experience
**Ports:** 5245 (Returns.Api), 5237 (Storefront.Api)
**Depends On:** Cycles 25-26 (Returns BC Phases 1-2) ✅ COMPLETE

---

## UX Engineer Review & Sign-Off

**Reviewer:** UX Engineer
**Date:** 2026-03-13
**Status:** ✅ **APPROVED** (with incorporated revisions)

### Revision Requests (All Addressed)

**R1: Missing Exchange Events in SignalR Handlers (HIGH PRIORITY)** ✅ RESOLVED
- Added 4 exchange event handlers in D6 (`ExchangeRequestedHandler`, `ExchangeApprovedHandler`, `ExchangeCompletedHandler`, `ExchangeFailedHandler`)
- Expanded `StorefrontEvent` discriminated union in D7 with 4 exchange event types
- Updated deliverables count: 7 refund events + 4 exchange events = **11 total SignalR handlers**

**R2: Anticorruption Layer Missing Exchange Context (MEDIUM PRIORITY)** ✅ RESOLVED
- Added 2 exchange-specific translation methods to `ReturnStatusMapper` in D8:
  - `ToCustomerFacingExchangeStatus()` — 6 exchange-specific status translations
  - `ToCustomerFacingExchangeFailureReason()` — 3 exchange failure reason translations
- Customer-facing text is empathetic and actionable (e.g., "Exchange complete — your replacement is on the way")

**R3: Sequential Return UI Context (LOW PRIORITY)** 📋 DEFERRED
- Noted as non-blocking; can be addressed during implementation if time permits
- Recommendation: Add `ReturnSequenceNumber` to `ReturnRequestedEvent` for UI disambiguation

### Approval Summary

All **blocking concerns (R1, R2) are resolved**. The plan comprehensively covers:
- ✅ Exchange workflow (UC-11) — P0 headline feature with complete domain events, integration contracts, and Orders BC handlers
- ✅ SignalR push handlers for **11 lifecycle events** (7 refund + 4 exchange)
- ✅ Anticorruption layer with customer-facing translations for both refund AND exchange paths
- ✅ Sequential returns support (D9-D10)
- ✅ P2 items (DeliveredAt fix, $0 refund guard, cross-BC smoke tests)

**No further revisions required. Implementation may proceed.**

---

## Objective

Phase 3 implements the **Exchange workflow (UC-11)** — the headline deliverable representing 20-30% of real-world return volume — alongside high-priority P1 items from the Phase 2 retrospective. After this cycle:

- Customers can exchange item A for item B (different size/color/SKU) instead of receiving a refund
- Customer Experience BC pushes real-time return status updates via SignalR for all 7 return lifecycle events
- Sequential returns are supported (multiple returns per order before window expires)
- P2 items (DeliveredAt endpoint fix, anticorruption layer design, cross-BC smoke tests) round out the scope

**Priority Alignment (Phase 2 Retrospective Consensus):**
- 🔴 **P0:** Exchange workflow (UC-11) — Product Owner
- 🟡 **P1:** Customer Experience BC return handlers (SignalR push) — UX Engineer
- 🟡 **P1:** Sequential returns support — Product Owner
- 🟢 **P2:** DeliveredAt endpoint fix, $0 refund guard, SSE→SignalR doc fixes, anticorruption layer, cross-BC smoke tests

### What Phase 3 Does NOT Include

- Fraud detection / risk scoring (UC-09) — deferred to Phase 4+
- RBAC for CS agent endpoints — deferred to Admin Portal cycle
- Carrier integration / label generation (external API) — deferred (stub implementation continues)
- Store credit for rejected returns — deferred to Ledger/Wallet BC
- Order History UI ("Request Return" button) — deferred to CE frontend cycle
- Warranty claims workflow (UC-12) — distinct from returns, future BC

---

## Deliverables Overview

| # | Deliverable | Priority | Stakeholder | Spec Ref |
|---|-------------|----------|-------------|----------|
| **Exchange Workflow (P0)** |
| D1 | Exchange domain events + aggregate state | P0 | PO | UC-11, FR-10 |
| D2 | `RequestExchange` command handler | P0 | PO | UC-11 |
| D3 | Exchange approval logic + replacement order creation | P0 | PO | UC-11 |
| D4 | Exchange integration events (`ExchangeRequested`, `ExchangeApproved`, `ExchangeCompleted`, `ExchangeFailed`) | P0 | PO | UC-11, CONTEXTS.md |
| D5 | Orders BC exchange handlers (refund diff, replacement order) | P0 | PO | UC-11 |
| **Customer Experience BC SignalR Handlers (P1)** |
| D6 | SignalR push handlers for 7 refund return events + 4 exchange events (11 total) | P1 | UXE | Phase 2 retrospective, UXE R1 |
| D7 | `StorefrontEvent` discriminated union for return + exchange events | P1 | UXE | bff-realtime-patterns.md, UXE R1 |
| D8 | Anticorruption layer for customer-facing enum translation (refund + exchange) | P1 | UXE | Phase 2 retrospective, UXE R2 |
| **Sequential Returns (P1)** |
| D9 | Remove unconditional saga close on `ReturnCompleted` | P1 | PO | Phase 2 retrospective |
| D10 | Sequential return validation (ensure no duplicate SKUs across returns) | P1 | PO | FR-02 |
| **P2 Items** |
| D11 | `DeliveredAt` endpoint fix in `GetReturnableItems` | P2 | PO | Phase 2 retrospective |
| D12 | $0 refund defensive guard in Orders saga | P2 | PO | Phase 2 retrospective |
| D13 | SSE→SignalR doc amendments in cycle-26 plan | P2 | UXE | Phase 2 retrospective |
| D14 | Cross-BC integration smoke tests (RabbitMQ pipelines) | P2 | PSA | Phase 2 L1 |
| **Testing & Documentation** |
| D15 | Unit tests for exchange workflow | — | QA | Testing Strategy |
| D16 | Integration tests for exchange + sequential returns | — | QA | Testing Strategy |
| D17 | Update CS agent runbook with exchange guidance | — | PO | OR-01 |

---

## D1: Exchange Domain Events + Aggregate State (P0)

### Why

UC-11 (Exchange workflow) represents 20-30% of real-world return volume. Customers frequently want to exchange for a different size, color, or related SKU rather than go through a full refund-and-repurchase cycle. This is especially common for:
- Apparel (wrong size)
- Pet supplies (wrong formulation — e.g., "Adult Dog Food" → "Senior Dog Food")
- Seasonal items (immediate need, can't wait for refund)

**Business value:** Retains order value, reduces refund processing, improves customer satisfaction.

### Changes

#### 1a. New Exchange-specific domain events

**File:** `src/Returns/Returns/Returns/ReturnEvents.cs` *(MODIFY — add 4 new events)*

```csharp
/// <summary>
/// Customer requests to exchange returned item(s) for different SKU(s).
/// Exchange request is validated against return eligibility window + SKU availability.
/// </summary>
public sealed record ExchangeRequested(
    Guid ReturnId,
    IReadOnlyList<ExchangeLineItem> ExchangeItems,
    DateTimeOffset RequestedAt);

/// <summary>
/// Exchange request approved. Replacement order will be created after inspection passes.
/// </summary>
public sealed record ExchangeApproved(
    Guid ReturnId,
    string ApprovedBy,  // "System" or CS agent ID
    DateTimeOffset ApprovedAt,
    decimal RefundDifference,  // Positive = customer owes, Negative = credit
    DateTimeOffset ShipByDate);

/// <summary>
/// Exchange completed. Original items inspected + approved, replacement order created.
/// </summary>
public sealed record ExchangeCompleted(
    Guid ReturnId,
    Guid ReplacementOrderId,
    decimal FinalRefundDifference,
    DateTimeOffset CompletedAt);

/// <summary>
/// Exchange failed inspection. Original items not in acceptable condition.
/// </summary>
public sealed record ExchangeFailed(
    Guid ReturnId,
    string FailureReason,
    DateTimeOffset FailedAt);

// Supporting record for ExchangeRequested
public sealed record ExchangeLineItem(
    string OriginalSku,
    int OriginalQuantity,
    string ReplacementSku,
    int ReplacementQuantity,
    string Reason);  // "WrongSize", "WrongVariant", "PreferDifferentItem"
```

**Why separate events for exchange:** Exchange has a distinct lifecycle from refund-only returns:
- ExchangeRequested → ExchangeApproved → (customer ships) → (inspection) → ExchangeCompleted → ReplacementOrderId tracked
- Terminal states: `ExchangeCompleted`, `ExchangeFailed`, `ReturnExpired` (if customer never ships)

#### 1b. Add exchange state to Return aggregate

**File:** `src/Returns/Returns/Returns/Return.cs` *(MODIFY)*

Add properties to track exchange-specific state:

```csharp
public sealed class Return
{
    // Existing properties...
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public ReturnStatus Status { get; set; }
    // ... (all existing Phase 1-2 properties)

    // NEW: Exchange workflow properties
    public bool IsExchange { get; set; }
    public IReadOnlyList<ExchangeLineItem>? ExchangeItems { get; set; }
    public decimal RefundDifference { get; set; }  // Tracked after approval
    public Guid? ReplacementOrderId { get; set; }  // Set on ExchangeCompleted

    // Apply methods for exchange events
    public void Apply(ExchangeRequested evt)
    {
        IsExchange = true;
        ExchangeItems = evt.ExchangeItems;
    }

    public void Apply(ExchangeApproved evt)
    {
        Status = ReturnStatus.Approved;
        RefundDifference = evt.RefundDifference;
        ApprovedAt = evt.ApprovedAt;
        ShipByDeadline = evt.ShipByDate;
    }

    public void Apply(ExchangeCompleted evt)
    {
        Status = ReturnStatus.Completed;
        ReplacementOrderId = evt.ReplacementOrderId;
        CompletedAt = evt.CompletedAt;
    }

    public void Apply(ExchangeFailed evt)
    {
        Status = ReturnStatus.Rejected;
    }
}
```

**Why track ReplacementOrderId:** Enables customer service agents to trace the full exchange lifecycle (original order → return → replacement order). Also allows sequential returns against the *replacement* order if needed.

---

## D2: `RequestExchange` Command Handler (P0)

### Why

The exchange workflow begins with a customer selecting "Exchange" instead of "Refund" during return initiation. This command captures which items to return and which replacements to receive.

### Changes

#### 2a. New `RequestExchange` command

**File:** `src/Returns/Returns/Returns/ReturnCommands.cs` *(MODIFY)*

```csharp
/// <summary>
/// Customer requests an exchange (return item A, receive item B).
/// </summary>
public sealed record RequestExchange(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<ExchangeLineItem> ExchangeItems);
```

**Validation rules (FluentValidation):**
- `ReturnId` must be new Guid (cannot exchange existing return)
- `OrderId` must exist in `ReturnEligibilityWindow`
- `ExchangeItems` must not be empty
- Each `OriginalSku` must be in the original order's line items
- Each `ReplacementSku` must exist in Product Catalog (future: check availability in Inventory)

#### 2b. `RequestExchangeHandler`

**File:** `src/Returns/Returns/Returns/ReturnCommandHandlers.cs` *(MODIFY — add new handler)*

```csharp
public sealed record RequestExchangeHandler(
    IDocumentSession Session,
    TimeProvider TimeProvider)
{
    public async Task<IStartStream> Handle(RequestExchange command)
    {
        var now = TimeProvider.GetUtcNow();

        // Validate eligibility window exists
        var eligibility = await Session.LoadAsync<ReturnEligibilityWindow>(command.OrderId);
        if (eligibility == null)
            throw new InvalidOperationException($"Order {command.OrderId} has no return eligibility window.");

        if (now > eligibility.WindowClosesAt)
            throw new InvalidOperationException($"Return window expired on {eligibility.WindowClosesAt:yyyy-MM-dd}.");

        // Validate original SKUs exist in order
        var originalSkus = command.ExchangeItems.Select(e => e.OriginalSku).ToHashSet();
        var eligibleSkus = eligibility.EligibleItems.Select(i => i.Sku).ToHashSet();
        var invalidSkus = originalSkus.Except(eligibleSkus).ToList();
        if (invalidSkus.Count != 0)
            throw new InvalidOperationException($"SKUs not eligible for return: {string.Join(", ", invalidSkus)}");

        // TODO (Phase 3): Query Pricing BC for replacement item prices
        // For now, assume replacement items have known prices (stub in tests)

        var @event = new ExchangeRequested(
            ReturnId: command.ReturnId,
            ExchangeItems: command.ExchangeItems,
            RequestedAt: now);

        return StreamAction.Start<Return>(command.ReturnId, @event);
    }
}
```

**Why `IStartStream`:** Exchange requests create a new Return aggregate stream (same as `RequestReturn`). The stream ID is the `ReturnId`.

---

## D3: Exchange Approval Logic + Replacement Order Creation (P0)

### Why

After the customer ships the return and inspection passes, Returns BC must:
1. Calculate refund difference (original price - replacement price)
2. Publish `ExchangeCompleted` integration event
3. Orders BC creates replacement order and handles payment difference

**Refund difference scenarios:**
- **Negative difference** (replacement cheaper): Customer gets partial refund
- **Zero difference** (same price): No refund, no charge
- **Positive difference** (replacement more expensive): Customer is charged the difference (or exchange denied if not pre-authorized)

### Changes

#### 3a. Update `SubmitInspectionHandler` for exchange path

**File:** `src/Returns/Returns/Returns/ReturnCommandHandlers.cs` *(MODIFY — `SubmitInspectionHandler`)*

Current logic has two branches:
- All items pass → `InspectionPassed` + `ReturnCompleted`
- Any items fail → branch on mixed/all-fail

Add **third branch** for exchange:

```csharp
public sealed record SubmitInspectionHandler(
    IDocumentSession Session,
    TimeProvider TimeProvider)
{
    [WriteAggregate]
    public async Task<(Return, Events, OutgoingMessages)> Handle(
        SubmitInspection command,
        Return aggregate)
    {
        var now = TimeProvider.GetUtcNow();

        // Partition inspection results (existing logic)
        var passed = command.Results.Where(r => r.Condition != ItemCondition.WorseThanExpected).ToList();
        var failed = command.Results.Where(r => r.Condition == ItemCondition.WorseThanExpected).ToList();
        bool hasFailures = failed.Count != 0;

        var events = new List<object>();
        var outgoing = new OutgoingMessages();

        // **NEW: Check if this is an exchange**
        if (aggregate.IsExchange)
        {
            if (hasFailures)
            {
                // Exchange inspection failed → no replacement order
                events.Add(new InspectionFailed(
                    ReturnId: command.ReturnId,
                    InspectorId: command.InspectorId,
                    FailureReason: "Items not in acceptable condition for exchange",
                    FailedAt: now,
                    Items: failed.Select(MapToDispositionItem).ToList()));

                events.Add(new ExchangeFailed(
                    ReturnId: command.ReturnId,
                    FailureReason: "Inspection failed",
                    FailedAt: now));

                // Publish ExchangeFailed integration event (no refund, no replacement)
                outgoing.Add(new Messages.Contracts.Returns.ExchangeFailed(
                    ReturnId: command.ReturnId,
                    OrderId: aggregate.OrderId,
                    CustomerId: aggregate.CustomerId,
                    FailureReason: "Items not in acceptable condition",
                    FailedAt: now));
            }
            else
            {
                // Exchange inspection passed → create replacement order
                events.Add(new InspectionPassed(
                    ReturnId: command.ReturnId,
                    InspectorId: command.InspectorId,
                    PassedAt: now,
                    Items: passed.Select(MapToInspectedItem).ToList()));

                // TODO: Query Pricing BC for final replacement prices
                // For now, use RefundDifference calculated at approval time
                var finalRefundDiff = aggregate.RefundDifference;

                events.Add(new ExchangeCompleted(
                    ReturnId: command.ReturnId,
                    ReplacementOrderId: Guid.NewGuid(),  // Generated here; Orders BC uses this
                    FinalRefundDifference: finalRefundDiff,
                    CompletedAt: now));

                // Publish ExchangeCompleted integration event
                outgoing.Add(new Messages.Contracts.Returns.ExchangeCompleted(
                    ReturnId: command.ReturnId,
                    OrderId: aggregate.OrderId,
                    CustomerId: aggregate.CustomerId,
                    ReplacementOrderId: events.OfType<ExchangeCompleted>().First().ReplacementOrderId,
                    ExchangeItems: aggregate.ExchangeItems!,
                    FinalRefundDifference: finalRefundDiff,
                    CompletedAt: now));
            }
        }
        else
        {
            // Existing refund-only logic (unchanged from Phase 2)
            // ... (all-pass, all-fail, mixed branches)
        }

        return (aggregate, new Events(events), outgoing);
    }
}
```

**Why generate `ReplacementOrderId` in Returns BC:** Returns owns the exchange lifecycle; Orders reacts to the `ExchangeCompleted` event. The `ReplacementOrderId` must be deterministic so if the message is reprocessed, Orders doesn't create duplicate replacement orders.

**Alternative considered:** Use UUID v5 deterministic ID based on `(OriginalOrderId, ReturnId)` to ensure idempotency.

---

## D4: Exchange Integration Events (P0)

### Why

Downstream BCs (Orders, Customer Experience, Notifications) need to react to exchange lifecycle transitions.

### Changes

#### 4a. New integration contracts in Messages.Contracts

**File:** `src/Shared/Messages.Contracts/Returns/ExchangeRequested.cs` *(NEW)*

```csharp
namespace Messages.Contracts.Returns;

/// <summary>
/// Published when customer requests an exchange instead of refund.
/// Customer Experience BC uses this for real-time UI updates.
/// </summary>
public sealed record ExchangeRequested(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<ExchangeLineItem> ExchangeItems,
    DateTimeOffset RequestedAt);
```

**File:** `src/Shared/Messages.Contracts/Returns/ExchangeApproved.cs` *(NEW)*

```csharp
namespace Messages.Contracts.Returns;

/// <summary>
/// Published when exchange is approved. Customer must ship by ShipByDate.
/// </summary>
public sealed record ExchangeApproved(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    decimal RefundDifference,  // Negative = customer gets credit, Positive = customer owes
    DateTimeOffset ApprovedAt,
    DateTimeOffset ShipByDate,
    string TrackingNumber);
```

**File:** `src/Shared/Messages.Contracts/Returns/ExchangeCompleted.cs` *(NEW)*

```csharp
namespace Messages.Contracts.Returns;

/// <summary>
/// Published when exchange inspection passes and replacement order is created.
/// Orders BC handles this to create the replacement order and process refund difference.
/// </summary>
public sealed record ExchangeCompleted(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    Guid ReplacementOrderId,
    IReadOnlyList<ExchangeLineItem> ExchangeItems,
    decimal FinalRefundDifference,
    DateTimeOffset CompletedAt);
```

**File:** `src/Shared/Messages.Contracts/Returns/ExchangeFailed.cs` *(NEW)*

```csharp
namespace Messages.Contracts.Returns;

/// <summary>
/// Published when exchange inspection fails. No replacement order, no refund.
/// </summary>
public sealed record ExchangeFailed(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    string FailureReason,
    DateTimeOffset FailedAt);
```

**File:** `src/Shared/Messages.Contracts/Returns/ExchangeLineItem.cs` *(NEW)*

```csharp
namespace Messages.Contracts.Returns;

/// <summary>
/// Represents one line in an exchange request: return X, receive Y.
/// </summary>
public sealed record ExchangeLineItem(
    string OriginalSku,
    int OriginalQuantity,
    string ReplacementSku,
    int ReplacementQuantity,
    string Reason);
```

#### 4b. RabbitMQ routing for exchange events

**File:** `src/Returns/Returns.Api/Program.cs` *(MODIFY — Wolverine RabbitMQ config)*

```csharp
opts.PublishMessage<Messages.Contracts.Returns.ExchangeRequested>()
    .ToRabbitQueue("orders-returns-events")
    .ToRabbitQueue("storefront-returns-events");

opts.PublishMessage<Messages.Contracts.Returns.ExchangeApproved>()
    .ToRabbitQueue("orders-returns-events")
    .ToRabbitQueue("storefront-returns-events");

opts.PublishMessage<Messages.Contracts.Returns.ExchangeCompleted>()
    .ToRabbitQueue("orders-returns-events")
    .ToRabbitQueue("storefront-returns-events");

opts.PublishMessage<Messages.Contracts.Returns.ExchangeFailed>()
    .ToRabbitQueue("orders-returns-events")
    .ToRabbitQueue("storefront-returns-events");
```

**Why dual routing:** Same pattern as Phase 2 — Orders saga handles orchestration, Customer Experience BC handles real-time UI.

---

## D5: Orders BC Exchange Handlers (P0)

### Why

Orders saga must react to `ExchangeCompleted` by:
1. Creating a replacement order (via Shopping BC `InitializeCart` → `InitiateCheckout` → `PlaceOrder` flow)
2. Processing refund difference (charge if positive, refund if negative)
3. Closing the original order's return-in-progress flag

### Changes

#### 5a. Add `ExchangeCompleted` handler to Order saga

**File:** `src/Orders/Orders/Placement/Order.cs` *(MODIFY)*

```csharp
public OutgoingMessages Handle(Messages.Contracts.Returns.ExchangeCompleted message)
{
    var outgoing = new OutgoingMessages();

    // Clear return-in-progress flag
    IsReturnInProgress = false;

    // Create replacement order
    // TODO: Integrate with Shopping BC to initialize cart with replacement items
    // For Phase 3, publish a command to Shopping BC:
    outgoing.Add(new Shopping.InitializeCart(
        CartId: Guid.NewGuid(),
        CustomerId: message.CustomerId));

    // Add replacement items to cart
    foreach (var item in message.ExchangeItems)
    {
        outgoing.Add(new Shopping.AddItemToCart(
            CartId: /* tracked in saga state */,
            Sku: item.ReplacementSku,
            Quantity: item.ReplacementQuantity,
            UnitPrice: /* query Pricing BC */));
    }

    // Initiate checkout for replacement order
    outgoing.Add(new Shopping.InitiateCheckout(
        CartId: /* tracked */,
        CustomerId: message.CustomerId));

    // Handle refund difference
    if (message.FinalRefundDifference < 0)
    {
        // Customer gets partial refund
        outgoing.Add(new Payments.RefundRequested(
            RefundId: Guid.NewGuid(),
            PaymentId: this.PaymentId,
            Amount: Math.Abs(message.FinalRefundDifference),
            Reason: $"Exchange price difference (Order {message.OrderId} → {message.ReplacementOrderId})"));
    }
    else if (message.FinalRefundDifference > 0)
    {
        // Customer owes difference — create payment for replacement order
        // (handled by replacement order's payment flow, not here)
    }

    // Mark original order's saga as ready to close (after window expires)
    // Do NOT close immediately — sequential returns may still be filed

    return outgoing;
}
```

**Design decision:** Replacement order follows the full Shopping → Orders flow (cart → checkout → order). This ensures:
- Price-at-checkout immutability
- Payment processing consistency
- Inventory reservation standard path

**Alternative considered (rejected):** Orders BC directly creates Order saga without Shopping BC. **Why rejected:** Bypasses checkout validation, payment token capture, address snapshot.

---

## D6: SignalR Push Handlers for 7 Return Events (P1)

### Why

UX Engineer priority from Phase 2 retrospective. Customer Experience BC currently has no handlers for the 7 return integration events published by Returns BC. Real-time UI updates are missing for:
- ReturnRequested, ReturnApproved, ReturnDenied, ReturnReceived, ReturnCompleted, ReturnRejected, ReturnExpired

**Customer impact:** Users must refresh the page to see return status changes.

### Changes

#### 6a. Create return event handlers in Storefront domain

**File:** `src/Customer Experience/Storefront/Notifications/ReturnRequestedHandler.cs` *(NEW)*

```csharp
namespace Storefront.Notifications;

public sealed class ReturnRequestedHandler(IEventBroadcaster broadcaster)
{
    public async Task Handle(Messages.Contracts.Returns.ReturnRequested message)
    {
        await broadcaster.PublishAsync(new ReturnRequestedEvent(
            ReturnId: message.ReturnId,
            OrderId: message.OrderId,
            CustomerId: message.CustomerId,
            RequestedAt: message.RequestedAt));
    }
}
```

**File:** `src/Customer Experience/Storefront/Notifications/ReturnApprovedHandler.cs` *(NEW)*

```csharp
namespace Storefront.Notifications;

public sealed class ReturnApprovedHandler(IEventBroadcaster broadcaster)
{
    public async Task Handle(Messages.Contracts.Returns.ReturnApproved message)
    {
        await broadcaster.PublishAsync(new ReturnApprovedEvent(
            ReturnId: message.ReturnId,
            OrderId: message.OrderId,
            CustomerId: message.CustomerId,
            ShipByDate: message.ShipByDate,
            EstimatedRefundAmount: message.EstimatedRefundAmount,
            ApprovedAt: message.ApprovedAt));
    }
}
```

**Repeat pattern for:**
- `ReturnDeniedHandler`
- `ReturnReceivedHandler`
- `ReturnCompletedHandler`
- `ReturnRejectedHandler`
- `ReturnExpiredHandler`

**NEW: Exchange event handlers (addresses UXE R1)**

**File:** `src/Customer Experience/Storefront/Notifications/ExchangeRequestedHandler.cs` *(NEW)*

```csharp
namespace Storefront.Notifications;

public sealed class ExchangeRequestedHandler(IEventBroadcaster broadcaster)
{
    public async Task Handle(Messages.Contracts.Returns.ExchangeRequested message)
    {
        await broadcaster.PublishAsync(new ExchangeRequestedEvent(
            ReturnId: message.ReturnId,
            OrderId: message.OrderId,
            CustomerId: message.CustomerId,
            ExchangeItems: message.ExchangeItems,
            RequestedAt: message.RequestedAt));
    }
}
```

**Repeat pattern for:**
- `ExchangeApprovedHandler`
- `ExchangeCompletedHandler`
- `ExchangeFailedHandler`

**Why separate handler per event:** Follows Storefront's existing pattern (one handler per Shopping/Orders event). Clean, testable, single responsibility.

---

## D7: `StorefrontEvent` Discriminated Union for Return Events (P1)

### Why

Storefront's `EventBroadcaster` uses a discriminated union pattern for type-safe event routing. Return events must be added to this union.

### Changes

#### 7a. Expand `StorefrontEvent` discriminated union

**File:** `src/Customer Experience/Storefront/Notifications/StorefrontEvent.cs` *(MODIFY)*

```csharp
namespace Storefront.Notifications;

/// <summary>
/// Discriminated union for all Storefront real-time events.
/// Serialized with System.Text.Json $type discriminator.
/// </summary>
[JsonDerivedType(typeof(CartUpdatedEvent), "cart-updated")]
[JsonDerivedType(typeof(OrderPlacedEvent), "order-placed")]
[JsonDerivedType(typeof(PaymentCapturedEvent), "payment-captured")]
[JsonDerivedType(typeof(ShipmentDispatchedEvent), "shipment-dispatched")]
[JsonDerivedType(typeof(ShipmentDeliveredEvent), "shipment-delivered")]
// NEW: Return events
[JsonDerivedType(typeof(ReturnRequestedEvent), "return-requested")]
[JsonDerivedType(typeof(ReturnApprovedEvent), "return-approved")]
[JsonDerivedType(typeof(ReturnDeniedEvent), "return-denied")]
[JsonDerivedType(typeof(ReturnReceivedEvent), "return-received")]
[JsonDerivedType(typeof(ReturnCompletedEvent), "return-completed")]
[JsonDerivedType(typeof(ReturnRejectedEvent), "return-rejected")]
[JsonDerivedType(typeof(ReturnExpiredEvent), "return-expired")]
// NEW: Exchange events (addresses UXE R1)
[JsonDerivedType(typeof(ExchangeRequestedEvent), "exchange-requested")]
[JsonDerivedType(typeof(ExchangeApprovedEvent), "exchange-approved")]
[JsonDerivedType(typeof(ExchangeCompletedEvent), "exchange-completed")]
[JsonDerivedType(typeof(ExchangeFailedEvent), "exchange-failed")]
public abstract record StorefrontEvent;
```

#### 7b. Define return event records

**File:** `src/Customer Experience/Storefront/Notifications/ReturnEvents.cs` *(NEW)*

```csharp
namespace Storefront.Notifications;

public sealed record ReturnRequestedEvent(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset RequestedAt) : StorefrontEvent;

public sealed record ReturnApprovedEvent(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset ShipByDate,
    decimal EstimatedRefundAmount,
    DateTimeOffset ApprovedAt) : StorefrontEvent;

public sealed record ReturnDeniedEvent(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    string Message,  // Customer-facing denial reason
    DateTimeOffset DeniedAt) : StorefrontEvent;

public sealed record ReturnReceivedEvent(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset ReceivedAt) : StorefrontEvent;

public sealed record ReturnCompletedEvent(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    decimal FinalRefundAmount,
    DateTimeOffset CompletedAt) : StorefrontEvent;

public sealed record ReturnRejectedEvent(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    string RejectionReason,  // Translated at anticorruption layer
    DateTimeOffset RejectedAt) : StorefrontEvent;

public sealed record ReturnExpiredEvent(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset ExpiredAt) : StorefrontEvent;

// NEW: Exchange events (addresses UXE R1)
public sealed record ExchangeRequestedEvent(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<Messages.Contracts.Returns.ExchangeLineItem> ExchangeItems,
    DateTimeOffset RequestedAt) : StorefrontEvent;

public sealed record ExchangeApprovedEvent(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    decimal RefundDifference,
    DateTimeOffset ApprovedAt,
    DateTimeOffset ShipByDate) : StorefrontEvent;

public sealed record ExchangeCompletedEvent(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    Guid ReplacementOrderId,
    decimal FinalRefundDifference,
    DateTimeOffset CompletedAt) : StorefrontEvent;

public sealed record ExchangeFailedEvent(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    string FailureReason,  // Translated at anticorruption layer
    DateTimeOffset FailedAt) : StorefrontEvent;
```

---

## D8: Anticorruption Layer for Customer-Facing Enum Translation (P1)

### Why

UX Engineer concern from Phase 2 retrospective:
> Internal enum values must be translated to customer-friendly text at the anticorruption layer

**Example:** Returns BC uses `ReturnStatus.Inspecting` internally, but customers should see "We're checking your items."

### Changes

#### 8a. Enum-to-text mapper in Storefront.Notifications

**File:** `src/Customer Experience/Storefront/Notifications/ReturnStatusMapper.cs` *(NEW)*

```csharp
namespace Storefront.Notifications;

/// <summary>
/// Anticorruption layer: Translates internal Return BC enum values to customer-friendly text.
/// </summary>
public static class ReturnStatusMapper
{
    public static string ToCustomerFacingText(string internalStatus) => internalStatus switch
    {
        "Requested" => "Return requested",
        "Approved" => "Return approved — ship by the date shown",
        "Denied" => "Return not eligible",
        "LabelGenerated" => "Label ready to print",
        "InTransit" => "We've received your shipment",
        "Received" => "Package received at warehouse",
        "Inspecting" => "We're checking your items",
        "Completed" => "Return complete — refund processed",
        "Rejected" => "Items didn't pass inspection",
        "Expired" => "Return window expired",
        _ => "Status unknown"
    };

    public static string ToCustomerFacingRejectionReason(string internalReason) => internalReason switch
    {
        "WorseThanExpected" => "Items not in expected condition",
        "NotReceived" => "Items not found in package",
        "PolicyViolation" => "Return doesn't meet policy requirements",
        _ => "Inspection failed"
    };

    public static string ToCustomerFacingDenialReason(string internalCode) => internalCode switch
    {
        "OutsideWindow" => "Return window has closed (30 days from delivery)",
        "NonReturnableItem" => "This item can't be returned per our policy",
        "DuplicateReturn" => "A return for this item is already in progress",
        _ => "Return not eligible"
    };

    // NEW: Exchange-specific translations (addresses UXE R2)
    public static string ToCustomerFacingExchangeStatus(string internalStatus) => internalStatus switch
    {
        "ExchangeRequested" => "Exchange requested",
        "ExchangeApproved" => "Exchange approved — ship your items by the date shown",
        "ExchangeInspecting" => "We're checking your items for exchange",
        "ExchangeCompleted" => "Exchange complete — your replacement is on the way",
        "ExchangeFailed" => "Items didn't qualify for exchange",
        _ => "Exchange status unknown"
    };

    public static string ToCustomerFacingExchangeFailureReason(string internalReason) => internalReason switch
    {
        "InspectionFailed" => "Items not in acceptable condition for exchange",
        "ReplacementUnavailable" => "Replacement item is currently out of stock",
        "PriceDifferenceExceeded" => "Price difference exceeds exchange policy limits",
        _ => "Exchange not possible"
    };
}
```

**Usage in handlers:**

```csharp
// In ReturnDeniedHandler:
await broadcaster.PublishAsync(new ReturnDeniedEvent(
    ReturnId: message.ReturnId,
    OrderId: message.OrderId,
    CustomerId: message.CustomerId,
    Message: ReturnStatusMapper.ToCustomerFacingDenialReason(message.DenialCode),
    DeniedAt: message.DeniedAt));
```

**Why static methods:** Stateless mapper, no DI needed. Easy to test in isolation.

---

## D9: Remove Unconditional Saga Close on `ReturnCompleted` (P1)

### Why

Product Owner priority from Phase 2 retrospective:
> Orders saga unconditionally closes on ReturnCompleted — no sequential returns possible yet

**Current behavior:** Order saga's `Handle(ReturnCompleted)` marks saga as complete via `MarkCompleted()`. This prevents subsequent return requests for the same order.

**Desired behavior:** Allow multiple returns per order until eligibility window expires.

### Changes

#### 9a. Remove `MarkCompleted()` from `ReturnCompleted` handler

**File:** `src/Orders/Orders/Placement/Order.cs` *(MODIFY)*

```csharp
// BEFORE (Phase 2):
public OutgoingMessages Handle(Messages.Contracts.Returns.ReturnCompleted message)
{
    var outgoing = new OutgoingMessages();

    IsReturnInProgress = false;

    outgoing.Add(new Payments.RefundRequested(
        RefundId: Guid.NewGuid(),
        PaymentId: this.PaymentId,
        Amount: message.FinalRefundAmount,
        Reason: $"Return {message.ReturnId} approved"));

    MarkCompleted();  // ❌ REMOVE THIS

    return outgoing;
}

// AFTER (Phase 3):
public OutgoingMessages Handle(Messages.Contracts.Returns.ReturnCompleted message)
{
    var outgoing = new OutgoingMessages();

    IsReturnInProgress = false;

    outgoing.Add(new Payments.RefundRequested(
        RefundId: Guid.NewGuid(),
        PaymentId: this.PaymentId,
        Amount: message.FinalRefundAmount,
        Reason: $"Return {message.ReturnId} approved"));

    // Do NOT mark completed — allow sequential returns
    // Saga will close naturally when:
    // 1. Return window expires (handle ReturnWindowExpired)
    // 2. All line items have been returned

    return outgoing;
}
```

#### 9b. Add saga close logic on return window expiry

**File:** `src/Orders/Orders/Placement/Order.cs` *(MODIFY — add new handler)*

```csharp
public void Handle(Returns.ReturnWindowExpired message)
{
    // No more returns possible for this order
    // If no return is in progress, mark saga complete
    if (!IsReturnInProgress)
    {
        MarkCompleted();
    }
}
```

**File:** `src/Returns/Returns/Returns/ReturnEligibilityWindow.cs` *(MODIFY — schedule expiry message)*

```csharp
// In handler that creates ReturnEligibilityWindow:
outgoing.ScheduleToLocalQueue(
    new Returns.ReturnWindowExpired(OrderId: message.OrderId),
    eligibility.WindowClosesAt);
```

---

## D10: Sequential Return Validation (P1)

### Why

Prevent duplicate returns for the same SKU. If a customer returns "Dog Food (5kg)" in Return #1, they shouldn't be able to return the same SKU in Return #2 unless they received it in a replacement order.

### Changes

#### 10a. Query existing returns in `RequestReturn` handler

**File:** `src/Returns/Returns/Returns/ReturnCommandHandlers.cs` *(MODIFY — `RequestReturnHandler`)*

```csharp
public async Task<IStartStream> Handle(RequestReturn command)
{
    var now = TimeProvider.GetUtcNow();

    // Existing eligibility window check...

    // NEW: Check for duplicate SKUs in existing returns
    var existingReturns = await Session.Query<Return>()
        .Where(r => r.OrderId == command.OrderId)
        .Where(r => r.Status != ReturnStatus.Completed
                 && r.Status != ReturnStatus.Rejected
                 && r.Status != ReturnStatus.Expired
                 && r.Status != ReturnStatus.Denied)
        .ToListAsync();

    var inProgressSkus = existingReturns
        .SelectMany(r => r.LineItems.Select(li => li.Sku))
        .ToHashSet();

    var duplicateSkus = command.LineItems
        .Select(li => li.Sku)
        .Intersect(inProgressSkus)
        .ToList();

    if (duplicateSkus.Count != 0)
        throw new InvalidOperationException(
            $"Return already in progress for SKUs: {string.Join(", ", duplicateSkus)}");

    // Continue with existing logic...
}
```

**Why query live returns:** Marten's document store allows efficient LINQ queries. No need for a separate read model.

---

## D11-D14: P2 Items (Quick Wins)

### D11: `DeliveredAt` Endpoint Fix

**File:** `src/Orders/Orders.Api/Queries/GetReturnableItems.cs` *(MODIFY)*

```csharp
// Change from:
DeliveredAt = null,  // ❌

// To:
DeliveredAt = order.DeliveredAt,  // ✅ (Phase 2 persisted this)
```

### D12: $0 Refund Defensive Guard

**File:** `src/Orders/Orders/Placement/Order.cs` *(MODIFY)*

```csharp
public OutgoingMessages Handle(Messages.Contracts.Returns.ReturnCompleted message)
{
    var outgoing = new OutgoingMessages();

    IsReturnInProgress = false;

    // NEW: Defensive guard against $0 refunds
    if (message.FinalRefundAmount > 0)
    {
        outgoing.Add(new Payments.RefundRequested(
            RefundId: Guid.NewGuid(),
            PaymentId: this.PaymentId,
            Amount: message.FinalRefundAmount,
            Reason: $"Return {message.ReturnId} approved"));
    }

    return outgoing;
}
```

### D13: SSE→SignalR Doc Amendments

**File:** `docs/planning/cycles/cycle-26-returns-bc-phase-2.md` *(MODIFY)*

Replace 4 instances of "SSE" with "SignalR" (lines 13, 70, 996, 1017 per UXE review).

### D14: Cross-BC Integration Smoke Tests

**File:** `tests/Returns/Returns.Api.IntegrationTests/CrossBcIntegrationTests.cs` *(NEW)*

```csharp
using Alba;
using Messages.Contracts.Returns;
using Shouldly;
using Wolverine.Tracking;
using Xunit;

namespace Returns.Api.IntegrationTests;

public sealed class CrossBcIntegrationTests(ReturnsApiFixture fixture)
    : IClassFixture<ReturnsApiFixture>
{
    [Fact]
    public async Task ReturnCompleted_is_published_to_orders_returns_events_queue()
    {
        // Arrange: Create return eligibility window + return
        // Act: Submit inspection (all pass)
        // Assert: Verify ReturnCompleted published to RabbitMQ
    }

    [Fact]
    public async Task ReturnCompleted_is_published_to_storefront_returns_events_queue()
    {
        // Same as above but verify storefront queue receives message
    }

    [Fact]
    public async Task ShipmentDelivered_from_fulfillment_creates_return_eligibility_window()
    {
        // Verify Fulfillment → Returns queue wiring (Phase 2 bug fix)
    }
}
```

**Why smoke tests:** Verifies end-to-end RabbitMQ publish→consume pipeline. Phase 2 L1 lesson learned.

---

## D15-D17: Testing & Documentation

### D15: Exchange Unit Tests

**Files:**
- `tests/Returns/Returns.UnitTests/ExchangeWorkflowTests.cs` *(NEW)*
- `tests/Returns/Returns.UnitTests/ExchangeDeciderTests.cs` *(NEW)*

**Coverage:**
- Exchange request validation
- Approval logic with refund difference calculation
- Inspection pass/fail for exchanges
- ReplacementOrderId generation (idempotency)

### D16: Exchange Integration Tests

**File:** `tests/Returns/Returns.Api.IntegrationTests/ExchangeLifecycleTests.cs` *(NEW)*

**Scenarios:**
- Happy path: Request → Approve → Ship → Inspect (pass) → Complete
- Inspection failure: Request → Approve → Ship → Inspect (fail) → Failed
- Expiry: Request → Approve → (never ships) → Expired

### D17: CS Agent Runbook Update

**File:** `docs/returns/CS-AGENT-RUNBOOK.md` *(MODIFY — add Exchange section)*

**Content:**
- When to offer exchange vs refund
- How to calculate refund difference
- Exchange approval checklist
- Escalation paths for price discrepancies

---

## Testing Strategy

### Unit Tests (QA Engineer)

**Returns BC:**
- Exchange decider logic (20 tests)
- Sequential return validation (5 tests)
- Anticorruption layer mappings (10 tests)

**Orders BC:**
- ExchangeCompleted handler (3 tests)
- Sequential return saga behavior (5 tests)

**Estimated:** 43 new unit tests

### Integration Tests (QA Engineer)

**Returns.Api.IntegrationTests:**
- Exchange full lifecycle (3 scenarios)
- Sequential returns (2 scenarios)
- Cross-BC smoke tests (3 tests)

**Storefront.Api.IntegrationTests:**
- SignalR push for 7 refund return events (7 tests)
- SignalR push for 4 exchange events (4 tests)

**Orders.IntegrationTests:**
- ExchangeCompleted handler (2 tests)

**Estimated:** 21 new integration tests (was 17, +4 for exchange SignalR)

### Manual Testing (Product Owner + UX Engineer)

**Exchange workflow:**
1. Request exchange (wrong size)
2. Verify approval with price difference
3. Simulate shipping
4. Submit inspection (pass)
5. Verify replacement order created
6. Verify refund difference processed

**SignalR real-time:**
1. Open Storefront in browser
2. Submit return via API
3. Verify real-time status update in UI (no page refresh)
4. Repeat for all 7 refund return events
5. Submit exchange via API
6. Verify real-time exchange status updates for all 4 exchange events
7. Verify anticorruption layer translations appear correctly in UI

---

## Success Criteria

### Functional

- ✅ Exchange workflow (UC-11) fully implemented and tested
- ✅ All 7 return events push to Storefront via SignalR
- ✅ All 4 exchange events push to Storefront via SignalR (11 total real-time events)
- ✅ Sequential returns supported (multiple returns per order)
- ✅ Customer-facing text translation at anticorruption layer
- ✅ Cross-BC smoke tests verify RabbitMQ pipelines

### Non-Functional

- ✅ Exchange inspection completes within 2 business days (manual process)
- ✅ SignalR push latency <500ms from Returns BC event publish
- ✅ Replacement order creation idempotent (duplicate message handling)

### Stakeholder Sign-Offs

- ✅ **Principal Software Architect:** Test suite covers all deliverables, architecture clean
- ✅ **Product Owner:** Exchange workflow meets business needs, sequential returns functional
- ✅ **UX Engineer:** SignalR handlers implemented (11 events: 7 refund + 4 exchange), anticorruption layer validates customer-facing text for both refund and exchange workflows

---

## Risks & Mitigations

### R1: Pricing BC Dependency for Exchange Refund Difference

**Risk:** Replacement item prices may change between exchange approval and completion.

**Mitigation:**
- Phase 3: Use snapshot prices at approval time (stored in `ExchangeApproved` event)
- Phase 4: Integrate with Pricing BC for real-time price lookups

### R2: Inventory Availability for Replacement Items

**Risk:** Replacement SKU may be out of stock when exchange completes.

**Mitigation:**
- Phase 3: No pre-validation (customer service handles OOS scenarios manually)
- Phase 4: Query Inventory BC at exchange request time, reserve replacement items

### R3: Payment Difference Handling

**Risk:** Customer owes price difference but payment fails.

**Mitigation:**
- Phase 3: Exchange denied if replacement more expensive (policy decision)
- Phase 4: Pre-authorize payment difference at approval time

---

## Open Questions (Requires Decisions)

### Q1: Exchange Price Difference Policy

**Question:** If replacement item costs MORE than original, do we:
1. Deny exchange automatically?
2. Charge customer the difference?
3. Offer store credit instead?

**Recommendation:** Option 1 for Phase 3 (deny if positive difference). Option 2 for Phase 4 (pre-authorize payment).

### Q2: Replacement Order Association

**Question:** Should `ReplacementOrderId` be a new Order saga or reuse original Order saga?

**Recommendation:** New Order saga. Clean separation, allows independent returns on replacement order.

### Q3: Exchange Eligibility Window

**Question:** Does exchange count toward 30-day return window, or is it extended?

**Recommendation:** Same 30-day window (from original delivery). Customer must request exchange before window expires.

---

## Timeline Estimate

**Total Effort:** 5-7 sessions

| Phase | Deliverables | Effort |
|-------|--------------|--------|
| Phase A: Planning & UX Review | D1-D17 plan document | 1 session |
| Phase B: Exchange Core (PSA) | D1-D5 (domain + handlers + integration) | 2 sessions |
| Phase C: CE SignalR + Sequential (PSA) | D6-D10 (handlers + validation) | 1 session |
| Phase D: P2 Items (PSA) | D11-D14 (quick wins) | 0.5 session |
| Phase E: Testing (QA) | D15-D16 (60 new tests) | 1.5 sessions |
| Phase F: Show-and-Tell + Retrospective | D17 + sign-offs | 1 session |

---

## References

- [Phase 2 Retrospective](cycle-26-returns-bc-phase-2-retrospective.md) — Priority ranking
- [Returns BC Spec](../../returns/RETURNS-BC-SPEC.md) — UC-11 definition
- [CONTEXTS.md](../../CONTEXTS.md) — Integration contracts
- [ADR 0013: Wolverine SignalR Transport](../../decisions/0013-wolverine-signalr-transport.md)
- [Skill: BFF Real-Time Patterns](../../skills/bff-realtime-patterns.md)
- [Skill: Wolverine SignalR](../../skills/wolverine-signalr.md)

---

*Created: 2026-03-13*
*Status: ✅ APPROVED — UX Engineer review complete, all revision requests addressed*
*Next: Principal Software Architect begins implementation (D1-D5 exchange core, then D6-D8 CE SignalR)*
