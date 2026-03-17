# Integration Messaging in CritterSupply

Patterns, conventions, and pitfalls for asynchronous message-based communication between bounded contexts in the Critter Stack.

---

## Table of Contents

1. [Overview](#overview)
2. [Integration Messages vs Domain Events](#integration-messages-vs-domain-events)
3. [Message Contracts](#message-contracts)
4. [Publishing Integration Messages](#publishing-integration-messages)
5. [Subscribing to Message Queues](#subscribing-to-message-queues)
6. [Integration Message Handlers](#integration-message-handlers)
7. [End-to-End Message Flow](#end-to-end-message-flow)
8. [Adding a New Integration](#adding-a-new-integration)
9. [RabbitMQ Transport Configuration](#rabbitmq-transport-configuration)
10. [Relationship to CONTEXTS.md](#relationship-to-contextsmd)
11. [Critical Warnings](#critical-warnings)
12. [Lessons Learned](#lessons-learned)
13. [Appendix](#appendix)

---

## Overview

**What this document covers:**
- Asynchronous **integration messages** that cross bounded context (BC) boundaries
- Publishing and subscribing to messages via Wolverine + RabbitMQ
- Message contract conventions in `src/Shared/Messages.Contracts/`
- Queue naming patterns and routing configuration
- Common integration anti-patterns and footguns from CritterSupply's development history

**What this document does NOT cover:**
- Domain events (internal to a BC) — see `docs/skills/marten-event-sourcing.md`
- HTTP-based synchronous inter-service queries (planned for separate skill)
- SignalR real-time UI updates — see `docs/skills/wolverine-signalr.md`
- Message handler patterns (command handlers, validation, return types) — see `docs/skills/wolverine-message-handlers.md`

**Where this fits in the Critter Stack:**
- **Wolverine:** Message routing, inbox/outbox, handler discovery
- **RabbitMQ:** Transport layer (current implementation; Wolverine's transport abstraction means patterns here are largely transport-agnostic)
- **Marten:** Transactional inbox/outbox persistence

---

## Integration Messages vs Domain Events

**This distinction is foundational and must be clear from the start.**

| Aspect | Domain Events | Integration Messages |
|--------|---------------|---------------------|
| **Scope** | Inside a single BC | Cross BC boundaries |
| **Namespace** | BC-specific (e.g., `Orders.Order`) | `Messages.Contracts.*` |
| **Persistence** | Marten event streams | RabbitMQ durable queues + transactional outbox |
| **Consumers** | Handlers within the same BC | Handlers in downstream BCs |
| **Purpose** | Reconstruct aggregate state | Choreography and orchestration across BCs |
| **Example** | `CheckoutStarted` (Orders BC internal) | `OrderPlaced` (Orders → Payments, Inventory, Correspondence) |

**Domain events** are persisted in Marten event streams and used to reconstitute aggregate state. They are the source of truth for a single BC's data.

**Integration messages** are published to RabbitMQ and consumed by other BCs. They enable choreography (autonomous reactions) and orchestration (saga-driven coordination).

**Key Rule:** An event can be **both** a domain event and an integration message. Example:
- `OrderPlaced` is a domain event in the `Order` saga's event stream (Orders BC)
- `Messages.Contracts.Orders.OrderPlaced` is an integration message published to RabbitMQ for consumption by Payments, Inventory, Correspondence, and Customer Experience BCs

**Converting domain events to integration messages:** Handlers in the owning BC typically listen to domain events (via Marten projections or Wolverine handlers) and publish corresponding integration messages. See [End-to-End Message Flow](#end-to-end-message-flow) for examples.

---

## Message Contracts

**The source of truth for all integration messages crossing BC boundaries.**

**Location:** `src/Shared/Messages.Contracts/`

**Structure:**
```
src/Shared/Messages.Contracts/
├── Messages.Contracts.csproj    # No dependencies (pure contracts)
├── Common/                      # Shared value objects (Money, Address)
├── Orders/                      # Messages published by Orders BC
│   ├── OrderPlaced.cs
│   ├── OrderCancelled.cs
│   └── OrderLineItem.cs         # Value object used in OrderPlaced
├── Shopping/                    # Messages published by Shopping BC
│   ├── CheckoutInitiated.cs
│   ├── ItemAdded.cs
│   └── CheckoutLineItem.cs
├── Fulfillment/
│   ├── ShipmentDispatched.cs
│   ├── ShipmentDelivered.cs
│   └── ShipmentDeliveryFailed.cs
├── Returns/
│   ├── ReturnRequested.cs
│   ├── ReturnApproved.cs
│   └── ReturnCompleted.cs
├── Payments/
├── Correspondence/
├── Inventory/
├── Pricing/
├── ProductCatalog/
├── VendorIdentity/
└── VendorPortal/
```

### Namespace Convention

**Pattern:** `Messages.Contracts.<PublisherBcName>`

**Examples:**
- `Messages.Contracts.Orders.OrderPlaced` — published by Orders BC
- `Messages.Contracts.Shopping.CheckoutInitiated` — published by Shopping BC
- `Messages.Contracts.Fulfillment.ShipmentDispatched` — published by Fulfillment BC

**Key Rule:** The namespace reflects the **publisher**, not the consumer(s). `OrderPlaced` is under `Orders/` because Orders BC publishes it, even though Payments, Inventory, Correspondence, and Customer Experience all consume it.

### Contract Structure

**All integration message contracts follow this pattern:**

```csharp
namespace Messages.Contracts.Orders;

/// <summary>
/// Integration message published by Orders BC when an order saga is successfully started.
/// Consumed by Payments and Inventory BCs to initiate their respective workflows.
/// </summary>
public sealed record OrderPlaced(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<OrderLineItem> LineItems,
    ShippingAddress ShippingAddress,
    string ShippingMethod,
    string PaymentMethodToken,
    decimal TotalAmount,
    DateTimeOffset PlacedAt);
```

**Key Characteristics:**
- `sealed record` — immutable, value-based equality
- XML doc comment documenting **who publishes** and **who consumes**
- `DateTimeOffset` timestamp fields (always include `*At` or `*On` suffix)
- `IReadOnlyList<T>` for collections (never `List<T>` or `T[]`)
- Rich payload — include all data consumers need to avoid follow-up queries (see [Lessons Learned](#lessons-learned))

### Value Objects in Contracts

Value objects used by multiple messages live in the same namespace folder:

```csharp
// src/Shared/Messages.Contracts/Orders/OrderLineItem.cs
namespace Messages.Contracts.Orders;

public sealed record OrderLineItem(
    string Sku,
    int Quantity,
    decimal PriceAtPurchase,
    decimal DiscountAmount);
```

**Common value objects** shared across BCs live in `Messages.Contracts.Common/`:
- `Money` (from Pricing BC)
- `Address` (from Customer Identity BC)

### Contract Design Principles

**1. Required Fields Only**

Use **required, non-nullable** parameters for fields that are always populated. Avoid optional-with-default patterns:

```csharp
// ❌ WRONG: Optional field invites "I'll fill it in later" anti-pattern
public sealed record ReturnCompleted(
    Guid ReturnId,
    string? Reason = null);

// ✅ CORRECT: Required field enforced at construction time
public sealed record ReturnCompleted(
    Guid ReturnId,
    string Reason);
```

**Lesson:** This was a footgun in Cycle 22 (Vendor Identity). See [Lessons Learned](#lessons-learned).

**2. Rich Payloads**

Include all context needed by **every known consumer**. Document consumer requirements before finalizing contracts.

```csharp
// ❌ WRONG: Minimal contract forces downstream HTTP queries
public sealed record ReturnCompleted(
    Guid ReturnId);

// ✅ CORRECT: Rich contract includes per-item disposition for Inventory BC
public sealed record ReturnCompleted(
    Guid ReturnId,
    Guid OrderId,
    IReadOnlyList<ReturnedItem> Items,  // Disposition per item (restock, dispose, etc.)
    decimal FinalRefundAmount,          // For Orders saga
    DateTimeOffset CompletedAt);
```

**Lesson:** Phase 1 `ReturnCompleted` was too minimal, requiring contract expansion in Phase 2. See [Lessons Learned](#lessons-learned).

**3. Document All Consumers**

XML doc comments must list **all known consumers** to prevent incomplete contract design:

```csharp
/// <summary>
/// Integration message published by Returns BC when a return is completed.
///
/// Consumed by:
/// - Orders BC (OrderSaga): Close return tracking, update order history
/// - Inventory BC: Restock or dispose returned items based on disposition
/// - Customer Experience BC: Real-time return status update via SignalR
/// </summary>
public sealed record ReturnCompleted(...);
```

**Lesson:** When adding new integrations, verify **all terminal state handlers exist** in consuming sagas. See [Lessons Learned](#lessons-learned).

---

## Publishing Integration Messages

**How services declare what they publish via Wolverine configuration.**

### Publisher Configuration (Program.cs)

**Pattern:** `opts.PublishMessage<T>().ToRabbitQueue("queue-name")`

**Example from Orders.Api/Program.cs:**

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.UseRabbitMq(rabbit =>
    {
        rabbit.HostName = rabbitConfig["hostname"] ?? "localhost";
        rabbit.UserName = rabbitConfig["username"] ?? "guest";
        rabbit.Password = rabbitConfig["password"] ?? "guest";
    })
    .AutoProvision();  // Creates queues/exchanges automatically

    // Publish OrderPlaced to multiple downstream consumers
    opts.PublishMessage<Messages.Contracts.Orders.OrderPlaced>()
        .ToRabbitQueue("storefront-notifications");  // Customer Experience BC
});
```

**Multi-destination publishing** (same message to multiple queues):

```csharp
// Fulfillment publishes ShipmentDelivered to 3 different queues
opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDelivered>()
    .ToRabbitQueue("orders-fulfillment-events");      // Orders saga

opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDelivered>()
    .ToRabbitQueue("storefront-fulfillment-events");  // Customer Experience

opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDelivered>()
    .ToRabbitQueue("returns-fulfillment-events");     // Returns eligibility window
```

**Why multiple declarations?** Each `PublishMessage<T>()` call creates a separate outbox entry. Wolverine guarantees at-least-once delivery to each queue independently.

### Publishing from Handlers

**Handlers publish integration messages via return types or `IMessageBus`.**

**Pattern 1: Return `OutgoingMessages`**

```csharp
public static class PlaceOrderHandler
{
    public static (Order order, OutgoingMessages outgoing) Handle(PlaceOrder cmd)
    {
        var order = Order.Create(cmd.CustomerId, cmd.Items);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Orders.OrderPlaced(
            order.Id,
            cmd.CustomerId,
            cmd.Items,
            cmd.ShippingAddress,
            cmd.ShippingMethod,
            cmd.PaymentMethodToken,
            order.TotalAmount,
            DateTimeOffset.UtcNow));

        return (order, outgoing);
    }
}
```

**Pattern 2: Inject `IMessageBus` for conditional publishing**

```csharp
public static async Task Handle(
    CompleteReturn cmd,
    [WriteAggregate] Return @return,
    IMessageBus bus)
{
    var evt = @return.Complete(cmd.ActualRefundAmount);

    // Conditionally publish integration message based on business logic
    if (@return.Type == ReturnType.Refund)
    {
        await bus.PublishAsync(new Messages.Contracts.Returns.ReturnCompleted(
            @return.Id,
            @return.OrderId,
            @return.Items,
            cmd.ActualRefundAmount,
            DateTimeOffset.UtcNow));
    }
}
```

**Wolverine Transactional Outbox:** All published messages are enrolled in the transactional outbox automatically when `opts.Policies.UseDurableOutboxOnAllSendingEndpoints()` is configured (which it is in all CritterSupply APIs). Messages are durably persisted **before** the handler transaction commits, guaranteeing at-least-once delivery.

### Ownership of Message Types

**Key Rule:** A BC "owns" a message type if it declares `PublishMessage<T>()` for that type.

**Example:**
- **Orders BC owns** `Messages.Contracts.Orders.OrderPlaced`
- **Shopping BC owns** `Messages.Contracts.Shopping.CheckoutInitiated`
- **Fulfillment BC owns** `Messages.Contracts.Fulfillment.ShipmentDispatched`

Only the owning BC should publish a given message type. Consumers must never publish messages from another BC's namespace.

---

## Subscribing to Message Queues

**How services declare what they consume via Wolverine configuration.**

### Subscriber Configuration (Program.cs)

**Pattern:** `opts.ListenToRabbitQueue("queue-name")`

**Example from Orders.Api/Program.cs:**

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.UseRabbitMq(/* ... */).AutoProvision();

    // Listen for CheckoutInitiated from Shopping BC
    opts.ListenToRabbitQueue("orders-checkout-initiated")
        .ProcessInline();  // Process messages synchronously (default)

    // Listen for Fulfillment BC integration messages
    opts.ListenToRabbitQueue("orders-fulfillment-events")
        .ProcessInline();

    // Listen for Returns BC integration messages
    opts.ListenToRabbitQueue("orders-returns-events")
        .ProcessInline();
});
```

**`.ProcessInline()` vs `.UseDurableInbox()`:**
- **`.ProcessInline()`**: Process messages synchronously as they arrive (default, recommended for most integrations)
- **`.UseDurableInbox()`**: Buffer messages in Wolverine's durable inbox for async processing (use for high-volume scenarios or when processing order doesn't matter)

### Queue Naming Conventions

**Pattern:** `<consumer-bc>-<publisher-bc>-<message-category>`

**Examples:**
- `orders-checkout-initiated` — Orders consumes checkout handoff from Shopping
- `orders-fulfillment-events` — Orders consumes fulfillment lifecycle from Fulfillment
- `orders-returns-events` — Orders consumes return outcomes from Returns
- `storefront-notifications` — Customer Experience consumes cart/order events for real-time UI
- `correspondence-orders-events` — Correspondence consumes order lifecycle for emails

**Why this pattern?**
- Queue name explicitly documents **who consumes** and **who publishes**
- Prevents queue name collisions across BCs
- Easy to trace message flow in RabbitMQ management UI

**Exceptions:**
- Some older queues use simpler patterns (e.g., `fulfillment-requests`). These are grandfathered in but should follow the standard pattern for new integrations.

### Handler Discovery

**Wolverine automatically discovers handlers for subscribed messages.**

When you call `opts.ListenToRabbitQueue("orders-returns-events")`, Wolverine:
1. Looks for handlers that accept message types in the `Messages.Contracts.Returns` namespace
2. Routes incoming messages to the appropriate handler based on message type
3. Enrolls handlers in the transactional inbox/outbox automatically

**No explicit routing configuration needed** — Wolverine matches message types to handlers via convention.

---

## Integration Message Handlers

**Handlers for integration messages follow the same patterns as command handlers** (see `docs/skills/wolverine-message-handlers.md`), with a few integration-specific considerations.

### Basic Integration Handler Pattern

```csharp
using Messages.Contracts.Shopping;
using Wolverine.Marten;

namespace Orders.Checkout;

/// <summary>
/// Integration handler that receives CheckoutInitiated from Shopping BC
/// and starts the Checkout aggregate in Orders BC.
/// </summary>
public static class CheckoutInitiatedHandler
{
    public static IStartStream Handle(CheckoutInitiated message)
    {
        var startedEvent = new CheckoutStarted(
            message.CheckoutId,
            message.CartId,
            message.CustomerId,
            message.Items,
            message.InitiatedAt);

        // MartenOps.StartStream returns IStartStream
        // Wolverine automatically persists to Marten and enrolls in outbox
        return MartenOps.StartStream<Checkout>(message.CheckoutId, startedEvent);
    }
}
```

**Key Points:**
- Handler signature: `public static T Handle(MessageType message, ...)`
- Wolverine routes based on parameter type (`CheckoutInitiated` in this example)
- Return `IStartStream` for new event streams
- Return `Events` or `OutgoingMessages` for existing aggregates (see `wolverine-message-handlers.md`)

### Choreography Pattern (Autonomous Reactions)

**Use case:** Downstream BC autonomously reacts to events from upstream BC without coordination.

**Example: Correspondence BC sends email when order is placed**

```csharp
// Correspondence/Messages/OrderPlacedHandler.cs
using Messages.Contracts.Orders;

public sealed class OrderPlacedHandler
{
    public async Task<OutgoingMessages> Handle(
        OrderPlaced @event,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Create Message aggregate
        var (message, messageQueued) = MessageFactory.Create(
            customerId: @event.CustomerId,
            channel: "Email",
            templateId: "order-confirmation",
            subject: $"Order Confirmation - Order #{@event.OrderId}",
            body: BuildEmailBody(@event)
        );

        // Persist event stream
        session.Events.StartStream<Message>(message.Id, messageQueued);

        // Publish integration event for monitoring
        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Correspondence.CorrespondenceQueued(
            message.Id,
            @event.CustomerId,
            "Email",
            messageQueued.QueuedAt
        ));

        // Trigger send command (processed by SendMessageHandler)
        outgoing.Add(new SendMessage(message.Id));

        return outgoing;
    }
}
```

**Characteristics:**
- **Loosely coupled:** Correspondence BC doesn't know about Orders saga; it just reacts to `OrderPlaced`
- **Autonomous:** No coordination with Orders BC required
- **Eventual consistency:** Email is sent asynchronously after order placement

### Saga Orchestration Pattern

**Use case:** One BC actively coordinates others via commands/requests.

**Example: Order saga coordinates Payments, Inventory, Fulfillment**

```csharp
// Orders/Order/OrderSaga.cs
public class OrderSaga : Saga
{
    public Guid Id { get; set; }
    public OrderStatus Status { get; set; }

    // Orchestrate payment and inventory reservation
    public OutgoingMessages Handle(OrderPlaced evt)
    {
        Status = OrderStatus.AwaitingPayment;

        var outgoing = new OutgoingMessages();
        outgoing.Add(new AuthorizePayment(evt.OrderId, evt.TotalAmount));
        outgoing.Add(new ReserveStock(evt.OrderId, evt.Items));

        return outgoing;
    }

    // React to payment authorization
    public OutgoingMessages? Handle(PaymentAuthorized evt)
    {
        if (Status == OrderStatus.AwaitingPayment && IsStockReserved)
        {
            Status = OrderStatus.Confirmed;
            return new OutgoingMessages()
                .Add(new RequestFulfillment(Id));
        }
        return null;
    }

    // Compensate if payment fails
    public OutgoingMessages Handle(PaymentFailed evt)
    {
        Status = OrderStatus.Cancelled;
        MarkCompleted();  // Saga terminal state

        return new OutgoingMessages()
            .Add(new ReleaseStockReservation(Id));
    }
}
```

**Characteristics:**
- **Centralized control:** Order saga actively sends commands to other BCs
- **Error compensation:** Saga handles failures (e.g., release stock if payment fails)
- **Tight coupling:** Saga must handle **all terminal states** from orchestrated BCs (see [Critical Warnings](#critical-warnings))

For comprehensive saga patterns, see `docs/skills/wolverine-sagas.md`.

### Idempotency Guards

**Problem:** Integration messages use at-least-once delivery, so handlers may receive the same message multiple times.

**Pattern: Upsert-based idempotency**

```csharp
public static async Task Handle(
    OrderPlaced evt,
    IDocumentSession session)
{
    // Upsert idempotency record
    var idempotencyKey = new IdempotencyRecord
    {
        Id = $"OrderPlaced:{evt.OrderId}",
        ProcessedAt = DateTimeOffset.UtcNow
    };
    session.Store(idempotencyKey);

    // Process event (second delivery with same ID is no-op due to upsert)
    // ...
}
```

**Pattern: Stream-based idempotency**

For event-sourced handlers, Marten's optimistic concurrency provides implicit idempotency:

```csharp
public static IStartStream Handle(CheckoutInitiated message)
{
    // If stream already exists with this ID, Marten throws ConcurrencyException
    // Wolverine retries with exponential backoff (configured in Program.cs)
    return MartenOps.StartStream<Checkout>(message.CheckoutId, new CheckoutStarted(...));
}
```

Wolverine's default retry policy (configured via `opts.OnException<ConcurrencyException>()`) handles retries automatically.

---

## End-to-End Message Flow

**Walking a complete real example: `CheckoutInitiated` from Shopping BC to Orders BC**

### Step 1: Define the Contract

**File:** `src/Shared/Messages.Contracts/Shopping/CheckoutInitiated.cs`

```csharp
namespace Messages.Contracts.Shopping;

/// <summary>
/// Integration message published by Shopping BC when a cart transitions to checkout.
/// Consumed by Orders BC to start the Checkout aggregate.
/// </summary>
public sealed record CheckoutInitiated(
    Guid CheckoutId,
    Guid CartId,
    Guid? CustomerId,
    IReadOnlyList<CheckoutLineItem> Items,
    DateTimeOffset InitiatedAt);
```

### Step 2: Declare the Publisher

**File:** `src/Shopping/Shopping.Api/Program.cs`

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.UseRabbitMq(/* ... */).AutoProvision();

    // Publish CheckoutInitiated to Orders BC
    opts.PublishMessage<Messages.Contracts.Shopping.CheckoutInitiated>()
        .ToRabbitQueue("orders-checkout-initiated");
});
```

### Step 3: Publish from Handler

**File:** `src/Shopping/Shopping/Cart/StartCheckout.cs`

```csharp
public static class StartCheckoutHandler
{
    public static async Task<OutgoingMessages> Handle(
        StartCheckout cmd,
        [WriteAggregate] Cart cart,
        IMessageBus bus)
    {
        // Validate cart state
        if (cart.Items.Count == 0)
            throw new InvalidOperationException("Cannot checkout empty cart");

        // Publish integration message
        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Shopping.CheckoutInitiated(
            cmd.CheckoutId,
            cart.Id,
            cart.CustomerId,
            cart.Items.Select(i => new CheckoutLineItem(i.Sku, i.Quantity, i.Price)).ToList(),
            DateTimeOffset.UtcNow));

        return outgoing;
    }
}
```

### Step 4: Declare the Subscriber

**File:** `src/Orders/Orders.Api/Program.cs`

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.UseRabbitMq(/* ... */).AutoProvision();

    // Listen for CheckoutInitiated from Shopping BC
    opts.ListenToRabbitQueue("orders-checkout-initiated")
        .ProcessInline();
});
```

### Step 5: Implement the Handler

**File:** `src/Orders/Orders/Checkout/CheckoutInitiatedHandler.cs`

```csharp
using Wolverine.Marten;
using ShoppingContracts = Messages.Contracts.Shopping;

namespace Orders.Checkout;

public static class CheckoutInitiatedHandler
{
    public static IStartStream Handle(ShoppingContracts.CheckoutInitiated message)
    {
        var startedEvent = new CheckoutStarted(
            message.CheckoutId,
            message.CartId,
            message.CustomerId,
            message.Items,
            message.InitiatedAt);

        return MartenOps.StartStream<Checkout>(message.CheckoutId, startedEvent);
    }
}
```

### Step 6: Verify Configuration

**RabbitMQ Management UI:**
1. Navigate to `http://localhost:15672` (default RabbitMQ management UI)
2. Check **Queues** tab for `orders-checkout-initiated` queue
3. Verify messages are being delivered (consumer count > 0)

**Alba Integration Test:**

```csharp
[Fact]
public async Task StartCheckout_PublishesCheckoutInitiated_ToOrdersQueue()
{
    // Arrange: Create cart with items
    var cartId = Guid.NewGuid();
    await _fixture.AddItemToCart(cartId, "SKU-123", 2);

    // Act: Start checkout
    var checkoutId = Guid.NewGuid();
    await _fixture.ExecuteAndWaitAsync(new StartCheckout(cartId, checkoutId));

    // Assert: CheckoutInitiated was published
    var published = _fixture.Tracker.Sent.Single<Messages.Contracts.Shopping.CheckoutInitiated>();
    published.CheckoutId.ShouldBe(checkoutId);
    published.CartId.ShouldBe(cartId);
    published.Items.ShouldNotBeEmpty();
}
```

**Cross-BC smoke test** (recommended for all new integrations):

```csharp
// Test fixture with Shopping + Orders APIs
public class ShoppingToOrdersPipelineTests : IClassFixture<CrossBcTestFixture>
{
    [Fact]
    public async Task Checkout_CreatesCheckoutAggregateInOrdersBC()
    {
        // Arrange: Create cart in Shopping BC
        var cartId = await _shoppingClient.CreateCart();
        await _shoppingClient.AddItem(cartId, "SKU-123", 2);

        // Act: Start checkout (triggers integration message)
        var checkoutId = await _shoppingClient.StartCheckout(cartId);

        // Assert: Checkout exists in Orders BC (verifies RabbitMQ pipeline)
        var checkout = await _ordersClient.GetCheckout(checkoutId);
        checkout.ShouldNotBeNull();
        checkout.Items.Count.ShouldBe(1);
    }
}
```

---

## Adding a New Integration

**Step-by-step guide for wiring a new inter-service message in CritterSupply.**

### Checklist

1. **Define the contract** in `src/Shared/Messages.Contracts/<PublisherBc>/`
2. **Document consumers** in XML doc comments (list ALL known consumers)
3. **Declare the publisher** in `<PublisherBc>.Api/Program.cs` via `opts.PublishMessage<T>()`
4. **Declare the subscriber(s)** in `<ConsumerBc>.Api/Program.cs` via `opts.ListenToRabbitQueue()`
5. **Implement the handler** in `<ConsumerBc>/<Feature>/<MessageName>Handler.cs`
6. **Verify queue naming** follows convention: `<consumer-bc>-<publisher-bc>-<category>`
7. **Write cross-BC smoke test** to verify RabbitMQ pipeline end-to-end
8. **Update `CONTEXTS.md`** only if new BC integration is introduced (not for additional messages in existing integration)

### Example: Add `ReturnShipmentReceived` Integration

**Scenario:** Fulfillment BC publishes `ReturnShipmentReceived` when a return shipment arrives at the warehouse. Returns BC needs to consume this event to start inspection.

**Step 1: Define the Contract**

**File:** `src/Shared/Messages.Contracts/Fulfillment/ReturnShipmentReceived.cs`

```csharp
namespace Messages.Contracts.Fulfillment;

/// <summary>
/// Integration message published by Fulfillment BC when a return shipment arrives at the warehouse.
///
/// Consumed by:
/// - Returns BC: Trigger inspection workflow
/// </summary>
public sealed record ReturnShipmentReceived(
    Guid ReturnId,
    Guid ShipmentId,
    DateTimeOffset ReceivedAt,
    string WarehouseLocation);
```

**Step 2: Declare the Publisher**

**File:** `src/Fulfillment/Fulfillment.Api/Program.cs`

```csharp
builder.Host.UseWolverine(opts =>
{
    // ... existing config ...

    // Publish ReturnShipmentReceived to Returns BC
    opts.PublishMessage<Messages.Contracts.Fulfillment.ReturnShipmentReceived>()
        .ToRabbitQueue("returns-fulfillment-events");  // Reuse existing queue
});
```

**Step 3: Publish from Handler**

**File:** `src/Fulfillment/Fulfillment/Returns/ReceiveReturnShipment.cs`

```csharp
public static class ReceiveReturnShipmentHandler
{
    public static OutgoingMessages Handle(
        ReceiveReturnShipment cmd,
        [WriteAggregate] ReturnShipment shipment)
    {
        var evt = shipment.MarkReceived(cmd.WarehouseLocation);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Fulfillment.ReturnShipmentReceived(
            shipment.ReturnId,
            shipment.Id,
            DateTimeOffset.UtcNow,
            cmd.WarehouseLocation));

        return outgoing;
    }
}
```

**Step 4: Verify Subscriber Configuration**

**File:** `src/Returns/Returns.Api/Program.cs`

```csharp
builder.Host.UseWolverine(opts =>
{
    // ... existing config ...

    // Already listening to this queue (from Step 2)
    opts.ListenToRabbitQueue("returns-fulfillment-events")
        .ProcessInline();
});
```

**Step 5: Implement the Handler**

**File:** `src/Returns/Returns/Returns/ReturnShipmentReceivedHandler.cs`

```csharp
using Messages.Contracts.Fulfillment;

namespace Returns.Returns;

public static class ReturnShipmentReceivedHandler
{
    public static async Task<Events> Handle(
        ReturnShipmentReceived message,
        [WriteAggregate] Return @return)
    {
        var evt = @return.StartInspection(message.ReceivedAt);
        return new Events { evt };
    }
}
```

**Step 6: Write Cross-BC Smoke Test**

**File:** `tests/Returns/Returns.Api.IntegrationTests/CrossBcSmokeTests/FulfillmentToReturnsPipelineTests.cs`

```csharp
[Fact]
public async Task ReceiveReturnShipment_TriggersInspectionInReturnsBC()
{
    // Arrange: Create return request
    var returnId = await _returnsClient.CreateReturn(orderId);

    // Act: Fulfillment receives return shipment (publishes ReturnShipmentReceived)
    await _fulfillmentClient.ReceiveReturnShipment(returnId, "Warehouse-A");

    // Assert: Return status is now "Inspecting" in Returns BC
    var @return = await _returnsClient.GetReturn(returnId);
    @return.Status.ShouldBe(ReturnStatus.Inspecting);
}
```

**Step 7: Update Documentation**

**Update `CONTEXTS.md`?** Only if this is a **new integration direction**. If Fulfillment already communicates with Returns (it does in CritterSupply), adding a new message type does NOT require updating `CONTEXTS.md`. The high-level integration is already documented.

**Update this skills document?** Add the new message to the [End-to-End Message Flow](#end-to-end-message-flow) examples if it represents a common pattern not yet covered.

---

## RabbitMQ Transport Configuration

**CritterSupply uses RabbitMQ as the current transport implementation.** Wolverine's transport abstraction means the patterns in this document are largely transport-agnostic, but RabbitMQ-specific configuration is documented here.

### Connection Configuration

**Standard pattern in all APIs:**

```csharp
builder.Host.UseWolverine(opts =>
{
    var rabbitConfig = builder.Configuration.GetSection("RabbitMQ");
    opts.UseRabbitMq(rabbit =>
    {
        rabbit.HostName = rabbitConfig["hostname"] ?? "localhost";
        rabbit.VirtualHost = rabbitConfig["virtualhost"] ?? "/";
        rabbit.Port = rabbitConfig.GetValue<int?>("port") ?? 5672;
        rabbit.UserName = rabbitConfig["username"] ?? "guest";
        rabbit.Password = rabbitConfig["password"] ?? "guest";
    })
    .AutoProvision();  // Creates queues/exchanges automatically
});
```

### Environment-Specific Connection Strings

| Environment | Host | Port | Why |
|-------------|------|------|-----|
| **Native Development** | `localhost` | `5672` | APIs run on host; RabbitMQ in Docker on standard port |
| **Containerized** | `rabbitmq` | `5672` | APIs in containers use Docker service name |

**Implementation:** `appsettings.json` defaults to `localhost:5672`. `docker-compose.yml` overrides via environment variables for containerized deployments.

### AutoProvision vs Manual Queue Creation

**`AutoProvision()`** (recommended): Wolverine automatically creates queues and exchanges based on `PublishMessage<T>()` and `ListenToRabbitQueue()` declarations. This is the standard pattern in CritterSupply.

**Manual creation:** Use RabbitMQ management UI or `rabbitmqctl` for production environments where infrastructure-as-code is required.

### Durable Queues and Persistent Messages

**All CritterSupply queues are durable** (survive RabbitMQ restarts) and messages are persistent (survive node failures). This is configured automatically by Wolverine when using `AutoProvision()`.

### Exchange Configuration

**Wolverine creates topic exchanges** for message routing. Exchange names are derived from queue names. In most cases, you don't need to configure exchanges explicitly — `AutoProvision()` handles this.

---

## Relationship to CONTEXTS.md

**`CONTEXTS.md` is a high-level at-a-glance reference** showing which BCs integrate with which. It answers *"what does this BC own and who does it talk to?"* — nothing more.

**Code is always the source of truth** for:
- Message contract shapes (`Messages.Contracts.*`)
- Event names and payloads
- Queue names and routing configuration (`Program.cs`)
- Handler implementations

**Known drift problem:** `CONTEXTS.md` can fall behind as new integrations are added. When in doubt:
1. **Check `Program.cs`** in the publisher and consumer BCs to see actual queue configuration
2. **Check `Messages.Contracts/`** to see actual contract definitions
3. **Check handlers** in the consumer BC to see how messages are processed

**When to update `CONTEXTS.md`:**
- Adding a **new BC-to-BC integration direction** (e.g., first message from Pricing to Inventory)
- **Not** when adding a new message type to an existing integration direction (e.g., adding `ShipmentDeliveryFailed` when Fulfillment already publishes to Orders)

---

## Critical Warnings

**These are footguns that have caused real production issues or significant rework in CritterSupply's development. Impossible to miss.**

### ⚠️ Warning 1: Missing `PublishMessage<T>()` Declaration Causes Silent Failures

**Problem:** If you publish a message from a handler but forget to declare `opts.PublishMessage<T>()` in `Program.cs`, Wolverine will **silently drop the message**. No exception, no log entry, no indication of failure.

**Symptoms:**
- Downstream BC never receives the message
- RabbitMQ management UI shows zero messages in the queue
- Tests pass because they use `_fixture.Tracker.Sent` (in-memory tracking)

**Solution:**
Always declare `PublishMessage<T>()` in `Program.cs` for every integration message your BC publishes. Use cross-BC smoke tests (with real RabbitMQ) to catch this.

**Lesson:** This was discovered in Cycle 26 when Returns BC published `ReturnCompleted` but Orders saga never received it. See [Lessons Learned](#lessons-learned).

---

### ⚠️ Warning 2: Queue Name Mismatch Between Publisher and Subscriber

**Problem:** Publisher sends to `orders-returns-events`, subscriber listens on `order-return-events` (singular vs plural typo). Messages are routed to the wrong queue and never consumed.

**Symptoms:**
- RabbitMQ management UI shows unconsumed messages accumulating in the **publisher's queue**
- Subscriber queue remains empty

**Solution:**
1. Use the [queue naming convention](#queue-naming-conventions): `<consumer-bc>-<publisher-bc>-<category>`
2. Verify queue names match **exactly** between `PublishMessage<T>().ToRabbitQueue("queue-name")` and `ListenToRabbitQueue("queue-name")`
3. Write cross-BC smoke tests that verify messages flow end-to-end

---

### ⚠️ Warning 3: Contract Changes Without Coordinating Consumers

**Problem:** You expand a message contract (e.g., add required field to `ReturnCompleted`) and deploy the publisher BC. Downstream consumers still expect the old contract shape and fail to deserialize messages.

**Symptoms:**
- RabbitMQ messages stuck in queue (repeated retries)
- Consumer logs show JSON deserialization errors

**Solution:**
1. **Document all consumers** in the contract's XML doc comment
2. **Expand contracts, don't replace them:** Add optional fields first, then make them required in a later deployment after all consumers are updated
3. **Coordinate deployments:** Deploy consumers first (they can handle new optional fields), then deploy publisher

**Lesson:** This was a footgun in Cycle 26 Phase 2 when `ReturnCompleted` was expanded for Inventory BC integration. See [Lessons Learned](#lessons-learned).

---

### ⚠️ Warning 4: Saga Missing Terminal State Handlers

**Problem:** Saga handles `ReturnApproved` and `ReturnCompleted` but **not** `ReturnRejected` or `ReturnExpired`. Saga is left in dangling state with `IsReturnInProgress = true` forever.

**Symptoms:**
- Saga never completes (`MarkCompleted()` never called)
- Saga document accumulates in database indefinitely
- Business logic expects return to be closed but saga state disagrees

**Solution:**
When adding integration events, verify **all consumers** (especially sagas) handle **all terminal states**. For Returns BC integration, Orders saga must handle:
- `ReturnApproved`
- `ReturnDenied`
- `ReturnRejected`
- `ReturnCompleted`
- `ReturnExpired`

All terminal handlers must call `MarkCompleted()`.

**Lesson:** This was discovered in Cycle 26 when Orders saga only handled 3 of 6 return-related messages. See [Lessons Learned](#lessons-learned).

---

### ⚠️ Warning 5: Integration Message Handlers Returning Tuples Don't Persist Events

**Problem:** Handler returns `(Aggregate, Event)` tuple instead of `Events` collection or `IStartStream`. Wolverine does not persist the event.

**Symptoms:**
- Event is not written to Marten event stream
- Aggregate state is not updated
- Tests using `_fixture.Tracker.Sent.Single<Event>()` fail

**Solution:**
Use the correct return type for your use case:
- **New stream:** `IStartStream` (via `MartenOps.StartStream<T>()`)
- **Existing stream:** `Events` collection (wrap single event: `new Events { evt }`)
- **Manual persistence:** Call `session.Events.Append(streamId, evt)` directly

**Never return `(Aggregate, Event)` tuples** — Wolverine does not track these for persistence.

**Lesson:** This was discovered in Cycle 18 Bug 5 and again in M30.0 (Promotions BC). See [Lessons Learned](#lessons-learned).

---

### ⚠️ Warning 6: Handler Builds Message from Entity State Instead of Command Values

**Problem:** Handler reads entity state to populate outgoing integration message instead of using transient command values. Data loss occurs when entity state doesn't reflect the command.

**Example:**

```csharp
// ❌ WRONG: Reads entity state
request.InfoResponses.Add(new VendorInfoResponse(command.Response, now));
var msg = BuildCatalogMessage(request, now);  // Reads request.AdditionalNotes

// ✅ CORRECT: Passes command value explicitly
request.InfoResponses.Add(new VendorInfoResponse(command.Response, now));
var msg = BuildCatalogMessage(request, command.Response, now);
```

**Symptoms:**
- Downstream BC receives stale or incorrect data
- Integration message payload doesn't match what user submitted

**Solution:**
When building outgoing integration messages, pass **transient command values explicitly** as parameters. Do not re-read from entity state unless the entity state **is** the source of truth for that field.

**Lesson:** This was discovered in Cycle 22, Lesson 2. Vendor's actual response was silently lost in Catalog BC message. See [Lessons Learned](#lessons-learned).

---

## Lessons Learned

**Direct extracts from retrospective documents across Cycles 18–31.**

### Lesson 1: Integration Queue Wiring Must Be Verified End-to-End (Cycle 26, L1)

**Problem:** Returns BC configured to listen on `returns-fulfillment-events` queue, but Fulfillment BC never published `ShipmentDelivered` events to it. Tests worked because they seeded data directly, masking the production bug.

**Root Cause:** No cross-BC integration test verifying the publish → consume RabbitMQ pipeline.

**Pattern to Adopt:** Create 3+ host Alba fixtures to test RabbitMQ pipelines across BCs (e.g., Returns + Orders + Fulfillment in single fixture).

**Code Example:**

```csharp
// Test fixture with Returns + Orders + Fulfillment APIs
public class CrossBcSmokeTests : IClassFixture<CrossBcTestFixture>
{
    [Fact]
    public async Task Fulfillment_ShipmentDelivered_CreatesReturnEligibilityWindow()
    {
        // Arrange: Dispatch shipment via Fulfillment API
        var orderId = await _fulfillmentClient.DispatchShipment(/* ... */);

        // Act: Mark delivered (publishes ShipmentDelivered)
        await _fulfillmentClient.MarkDelivered(orderId);

        // Assert: Returns BC received and processed (created eligibility window)
        var window = await _returnsClient.GetEligibilityWindow(orderId);
        window.ShouldNotBeNull();
    }
}
```

---

### Lesson 2: Contract Expansion Must Include ALL Downstream Consumers (Cycle 26, L2)

**Problem:** Phase 1's `ReturnCompleted` only carried `FinalRefundAmount`. Inventory BC needs per-item disposition; Customer Experience BC needs per-item refund breakdown. Contract was designed for immediate consumer (Orders saga) without considering future consumers.

**Pattern to Adopt:** Document all known consumers and their data requirements **before** finalizing integration contracts.

**Integration Contract Table Format:**

| Event | Orders Saga | Inventory BC | Customer Experience | Correspondence |
|-------|-------------|-------------|---------------------|----------------|
| `ReturnCompleted` | ✅ Uses `FinalRefundAmount` | ✅ Uses `Items[].Disposition` | ✅ Uses `Items[].RefundAmount` | ✅ Uses `FinalRefundAmount` |

---

### Lesson 3: Saga Terminal State Handlers Must Cover ALL Terminal Events (Cycle 26, L4)

**Problem:** Orders saga only handled 3 of 6 return-related messages. `ReturnRejected` and `ReturnExpired` left the saga in dangling state with `IsReturnInProgress = true` permanently.

**Implementation Checklist:**
- [ ] Event is published from source BC
- [ ] Event is listened to by all downstream BCs
- [ ] All sagas have handlers for terminal events
- [ ] All sagas call `MarkCompleted()` when reaching terminal state

---

### Lesson 4: Integration Event Payload Schema Must Be Rich (Cycle 27, UXE Revision)

**Problem:** SignalR events didn't include full context. Clients needed follow-up HTTP calls to retrieve event details.

**Example — Before:**
```json
{ "OrderId": "..." }
```

**Example — After:**
```json
{
  "OrderId": "...",
  "CustomerId": "...",
  "TotalAmount": 99.99,
  "Items": [...]
}
```

**Rule:** Document expected payload richness for integration events. Include all context needed by consumers to avoid follow-up queries.

---

### Lesson 5: Required Non-Nullable Fields on Message Records (Cycle 22, L6)

**Problem:** `ChangeRequestDecisionPersonal` had `string? ChangeType = null` despite being always populated in all 7 handlers.

**Example — Before:**
```csharp
public sealed record ChangeRequestDecisionPersonal(
    ...
    string? ChangeType = null) : IVendorUserMessage;
```

**Example — After:**
```csharp
public sealed record ChangeRequestDecisionPersonal(
    ...
    string ChangeType) : IVendorUserMessage;  // Required at construction
```

**Rule:** Message record fields that are always populated should be **required and non-nullable**. Optional-with-default invites "I'll fill it in later" patterns.

---

### Lesson 6: Event Tuple Returns Don't Persist Events (Cycle 18 Bug 5 + M30.0)

**Problem:** Handlers returning `(ItemAdded, OutgoingMessages)` tuples didn't persist events.

**Root Cause:** Wolverine's aggregate handler pattern requires specific return types:
- `Events` (collection) with `[WriteAggregate]` → Persisted automatically
- `IStartStream` → Persisted for new streams
- `(Aggregate, Event)` tuple → **Not persisted**
- `session.Events.Append()` → Manually persisted

**Correct Patterns:**

**Pattern 1: Using `[WriteAggregate]` (for aggregate methods):**
```csharp
public static (Events, OutgoingMessages) Handle(
    AddItemToCart cmd,
    [WriteAggregate] ShoppingCart cart)
{
    var evt = new ItemAdded(...);
    return (new Events { evt }, new OutgoingMessages());  // Wrapped in collection
}
```

**Pattern 2: Using `IStartStream` (for new streams):**
```csharp
public static async Task<IStartStream> Handle(
    CreatePromotion cmd,
    IDocumentSession session)
{
    var promotion = Promotion.Create(cmd.Name);
    var evt = new PromotionCreated(...);
    return MartenOps.StartStream<Promotion>(cmd.PromotionId, evt);
}
```

**Pattern 3: Manual loading with `session.Events.Append()` (for deterministic IDs):**
```csharp
public static async Task Handle(
    RedeemCoupon cmd,
    IDocumentSession session)
{
    var streamId = Coupon.StreamId(cmd.CouponCode);  // UUID v5 from code
    var coupon = await session.Events.AggregateStreamAsync<Coupon>(streamId);
    var evt = new CouponRedeemed(...);
    session.Events.Append(streamId, evt);  // Manual append
}
```

---

### Lesson 7: Publisher Configuration Must Match Subscriber Expectations (Cycle 28)

**Problem:** Correspondence BC consumed `OrderPlaced` events from Orders BC. Must ensure:
- Orders BC publishes to `orders-correspondence-events` queue
- Correspondence BC configures Wolverine to listen on that queue
- Message schema matches on both ends

**Pattern:**
```csharp
// Orders.Api/Program.cs
opts.PublishMessage<OrderPlaced>()
    .ToRabbitQueue("correspondence-orders-events");

// Correspondence.Api/Program.cs
opts.ListenToRabbitQueue("correspondence-orders-events")
    .ProcessInline();
```

---

### Lesson 8: Commands Should Build Messages from Command Values, Not Entity State (Cycle 22, L2)

**Problem:** `ProvideAdditionalInfoHandler` read `request.AdditionalNotes` from entity state instead of passing `command.Response`. The vendor's actual response was silently lost in the Catalog BC message.

**Pattern — Before:**
```csharp
request.InfoResponses.Add(new VendorInfoResponse(command.Response, now));
var msg = BuildCatalogMessage(request, now);  // Reads request.AdditionalNotes — WRONG
```

**Pattern — After:**
```csharp
request.InfoResponses.Add(new VendorInfoResponse(command.Response, now));
var msg = BuildCatalogMessage(request, command.Response, now);  // Explicit — CORRECT
```

**Rule:** When handlers build outgoing integration messages, pass transient command values explicitly, don't re-read from entity state.

---

### Lesson 9: Fan-Out Pattern via `OutgoingMessages` (M30.0, Pattern 2)

**Use Case:** One parent command creates N child commands (e.g., batch coupon generation).

**Pattern:**
```csharp
public static async Task<OutgoingMessages> Handle(
    GenerateCouponBatch cmd,
    IDocumentSession session)
{
    var outgoing = new OutgoingMessages();

    for (int i = 1; i <= cmd.Count; i++)
    {
        var couponCode = $"{cmd.Prefix.ToUpperInvariant()}-{i:D4}";
        outgoing.Add(new IssueCoupon(couponCode, cmd.PromotionId));
    }

    return outgoing;
}
```

**Key Points:**
- Wolverine dispatches each message as separate handler invocation
- Each handler runs in own transaction
- Parent handler runs first; Wolverine queues child messages
- Optimistic concurrency on parent stream prevents duplicate fan-outs

---

### Lesson 10: Test Timing for Fan-Out Workflows (M30.0, D4)

**Problem:** Initial 300ms delay was insufficient for N async `IssueCoupon` commands + projection updates.

**Pattern:**
```csharp
// Generate batch → fan-out N IssueCoupon commands
await fixture.ExecuteAndWaitAsync(new GenerateCouponBatch(...));

// Wait for async processing + projection updates
await Task.Delay(1000);  // Increased from 300ms

// Verify all coupons created
for (int i = 1; i <= batchSize; i++)
{
    var coupon = await session.LoadAsync<CouponLookupView>($"CODE-{i:D4}");
    coupon.ShouldNotBeNull();
}
```

---

### Lesson 11: Integration Contract Assertion Tests

**Rule:** Always assert the **full payload** of outgoing integration messages, not just event type.

**Pattern — Before (Insufficient):**
```csharp
[Fact]
public async Task ProvideAdditionalInfo_CreatesCorrectEvent()
{
    await fixture.ExecuteAndWaitAsync(cmd);
    // WRONG: Only checks event type was published
    var evt = fixture.Tracker.Sent.Single<ChangeRequestDecisionPersonal>();
}
```

**Pattern — After (Comprehensive):**
```csharp
[Fact]
public async Task ProvideAdditionalInfo_CreatesMessageWithFullPayload()
{
    await fixture.ExecuteAndWaitAsync(cmd);
    var evt = fixture.Tracker.Sent.Single<ChangeRequestDecisionPersonal>();

    evt.ChangeRequestId.ShouldBe(cmd.ChangeRequestId);
    evt.Response.ShouldBe(cmd.Response);  // Actual content!
    evt.ChangeType.ShouldBe("Description");
    evt.Timestamp.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddSeconds(-5));
}
```

---

### Lesson 12: Integration Message Handler → SignalR Broadcast Pattern (M32.0, W3)

**Use Case:** BFF integration message handlers that need to broadcast real-time updates via SignalR after processing external BC events.

**Key Insight from M32.0:** Handlers must call `await session.SaveChangesAsync()` **before** returning SignalR events to ensure projection updates are committed.

**Pattern — Async Handler with SaveChanges:**

```csharp
public static class OrderPlacedHandler
{
    // ✅ CORRECT: Async handler with explicit SaveChanges
    public static async Task<LiveMetricUpdated> Handle(
        Messages.Contracts.Orders.OrderPlaced message,
        IDocumentSession session)
    {
        // 1. Append event to trigger inline projection
        session.Events.Append(Guid.NewGuid(), message);

        // 2. Commit transaction (inline projection updates here)
        await session.SaveChangesAsync();

        // 3. Now query the updated projection
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var metrics = await session.LoadAsync<AdminDailyMetrics>(today);

        // 4. Return SignalR event with current metrics
        return new LiveMetricUpdated(
            metrics.TodaysOrders,
            metrics.TodaysRevenue,
            DateTimeOffset.UtcNow);
    }
}
```

**Why This Pattern:**
- ✅ Inline projections update during `SaveChangesAsync()` (not before)
- ✅ SignalR event contains current projection state (no stale data)
- ✅ Clients receive real-time updates with accurate metrics

**Common Pitfall — Missing SaveChanges:**

```csharp
// ❌ WRONG: Synchronous handler can't call SaveChangesAsync()
public static LiveMetricUpdated Handle(
    Messages.Contracts.Orders.OrderPlaced message,
    IDocumentSession session)
{
    session.Events.Append(Guid.NewGuid(), message);

    // ❌ Projection not updated yet!
    var metrics = session.Load<AdminDailyMetrics>(today);

    // ❌ SignalR event contains stale metrics
    return new LiveMetricUpdated(metrics.TodaysOrders, ...);
}
```

**Async vs Sync Handler Decision Matrix:**

| Handler Needs | Return Type | Signature |
|---------------|-------------|-----------|
| Query projection after append | `Task<SignalREvent>` | `async Task<T> Handle(..., IDocumentSession session)` |
| Just return SignalR event | `SignalREvent` | `T Handle(...)` |
| Multi-BC orchestration | `Task<OutgoingMessages>` | `async Task<OutgoingMessages> Handle(..., IMessageBus bus)` |

**Key Rule:** If your handler appends events and queries projections in the same transaction, it MUST be `async Task<T>` to call `await session.SaveChangesAsync()`.

**Cross-Reference:** See `docs/skills/event-sourcing-projections.md` → "Lesson 0: Inline Projections Require Explicit SaveChanges" for projection-specific details.

---

## Appendix

### Cross-References to Related Skills

- **`docs/skills/wolverine-message-handlers.md`** — Handler patterns, return types, compound handler lifecycle
- **`docs/skills/marten-event-sourcing.md`** — Domain events, event-sourced aggregates, projections
- **`docs/skills/wolverine-sagas.md`** — Orchestration sagas, `MarkCompleted()`, terminal state handling
- **`docs/skills/event-sourcing-projections.md`** — Marten projections for building read models from events
- **`docs/skills/wolverine-signalr.md`** — Real-time UI updates via SignalR (not integration messaging)

### Wolverine Documentation Links

- [Wolverine Transport Fundamentals](https://wolverine.netlify.app/guide/messaging/transports/)
- [RabbitMQ Transport](https://wolverine.netlify.app/guide/messaging/transports/rabbitmq/)
- [Transactional Inbox/Outbox](https://wolverine.netlify.app/guide/durability/)
- [Message Handler Conventions](https://wolverine.netlify.app/guide/handlers/)

### RabbitMQ Management

- **Local Management UI:** `http://localhost:15672` (username: `guest`, password: `guest`)
- **Queue inspection:** Navigate to **Queues** tab to see message counts, consumer activity, and delivery rates
- **Exchange inspection:** Navigate to **Exchanges** tab to see routing rules

### Exemplary Integration Implementations in the Codebase

**Simple choreography (autonomous reaction):**
- `src/Correspondence/Correspondence/Messages/OrderPlacedHandler.cs` — Correspondence reacts to `OrderPlaced` to send email
- `src/Customer Experience/Storefront/Notifications/ItemAddedHandler.cs` — Storefront pushes `ItemAdded` to SignalR for real-time UI update

**Saga orchestration (centralized coordination):**
- `src/Orders/Orders/Order/OrderSaga.cs` — Orders saga orchestrates Payments, Inventory, Fulfillment
- `src/Returns/Returns/Returns/ReturnSaga.cs` — Returns saga orchestrates Fulfillment (reverse logistics) and Payments (refunds)

**Fan-out pattern (parent command → N child commands):**
- `src/Promotions/Promotions/Coupon/GenerateCouponBatchHandler.cs` — Batch coupon generation

**Multi-destination publishing (same message to multiple queues):**
- `src/Fulfillment/Fulfillment.Api/Program.cs` — `ShipmentDelivered` published to 3 different queues (Orders, Customer Experience, Returns)

---

**Document Version:** 1.0
**Last Updated:** 2026-03-15
**Author:** Claude (based on comprehensive CritterSupply codebase analysis)
