# Design Document: Order Placement

## Overview

The Order Placement feature is the entry point for the Orders bounded context in CritterSupply. It handles the creation of new order sagas when customers complete checkout, using Wolverine's stateful saga pattern persisted with Marten.

The Order is implemented as a **Wolverine Saga** - a long-running, stateful workflow that coordinates across multiple bounded contexts (Payments, Inventory, Fulfillment). The saga reacts to events, maintains state, and publishes commands to orchestrate the order lifecycle.

This implementation follows the A-Frame Architecture pattern, using pure functions for business logic with Wolverine handling infrastructure concerns (validation, persistence, messaging).

## Architecture

```mermaid
flowchart TB
    subgraph Shopping["Shopping Context"]
        Checkout[Checkout Process]
    end
    
    subgraph Orders["Orders Context"]
        subgraph API["Orders.Api"]
            QueryEndpoint[GET /api/orders/{id}]
        end
        
        subgraph Domain["Orders Domain"]
            Validator[CheckoutCompletedValidator]
            OrderSaga[Order Saga]
        end
        
        subgraph Infrastructure["Infrastructure"]
            Marten[(Marten Saga Storage)]
            Wolverine[Wolverine Messaging]
        end
    end
    
    subgraph Downstream["Downstream Contexts"]
        Payments[Payments Context]
        Inventory[Inventory Context]
    end
    
    Checkout -->|CheckoutCompleted| Validator
    Validator -->|Valid| OrderSaga
    OrderSaga --> Marten
    OrderSaga -->|OrderPlaced| Wolverine
    Wolverine -->|Publish| Payments
    Wolverine -->|Publish| Inventory
    QueryEndpoint --> Marten
```

### Key Design Decisions

1. **Wolverine Saga Pattern**: Orders are implemented as Wolverine sagas - stateful workflows that coordinate long-running processes across contexts
2. **Marten Saga Persistence**: Saga state is persisted using Marten's document storage, enabling durability and recovery
3. **Event-Driven Coordination**: The saga reacts to events from other contexts and publishes commands/events to orchestrate the workflow
4. **FluentValidation**: Validation is separated from business logic using FluentValidation
5. **Immutable Records**: All events, commands, and value objects are immutable records
6. **Pure Functions**: Saga handler methods are pure functions that return messages to publish

## Components and Interfaces

### Wolverine Saga

```csharp
// Wolverine saga for coordinating order lifecycle
// Sagas are identified by their Id property and persisted by Marten
public sealed class Order : Saga
{
    // Saga identity - used as correlation ID for all related messages
    public Guid Id { get; set; }
    
    // Order data captured at placement
    public Guid CustomerId { get; set; }
    public IReadOnlyList<OrderLineItem> LineItems { get; set; } = [];
    public ShippingAddress ShippingAddress { get; set; } = null!;
    public string ShippingMethod { get; set; } = string.Empty;
    public string PaymentMethodToken { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    
    // Saga state
    public OrderStatus Status { get; set; }
    public DateTimeOffset PlacedAt { get; set; }
    
    // Saga start handler - creates the saga from CheckoutCompleted
    public static (Order, OrderPlaced) Start(
        CheckoutCompleted command,
        IValidator<CheckoutCompleted> validator)
    {
        // Validation happens in Wolverine middleware via FluentValidation
        
        var orderId = Guid.CreateVersion7();
        var placedAt = DateTimeOffset.UtcNow;
        
        var lineItems = command.LineItems
            .Select(item => new OrderLineItem(
                item.Sku,
                item.Quantity,
                item.PriceAtPurchase,
                item.Quantity * item.PriceAtPurchase))
            .ToList();
            
        var totalAmount = lineItems.Sum(x => x.LineTotal);
        
        var saga = new Order
        {
            Id = orderId,
            CustomerId = command.CustomerId,
            LineItems = lineItems,
            ShippingAddress = command.ShippingAddress,
            ShippingMethod = command.ShippingMethod,
            PaymentMethodToken = command.PaymentMethodToken,
            TotalAmount = totalAmount,
            Status = OrderStatus.Placed,
            PlacedAt = placedAt
        };
        
        var @event = new OrderPlaced(
            orderId,
            command.CustomerId,
            lineItems,
            command.ShippingAddress,
            command.ShippingMethod,
            command.PaymentMethodToken,
            totalAmount,
            placedAt);
            
        return (saga, @event);
    }
    
    // Future saga handlers for other events will be added here:
    // Handle(PaymentCaptured) -> transition to PaymentConfirmed
    // Handle(PaymentFailed) -> transition to PaymentFailed or retry
    // Handle(ReservationCommitted) -> proceed to fulfillment
    // etc.
}
```

### Validator

```csharp
// FluentValidation validator for CheckoutCompleted
public sealed class CheckoutCompletedValidator : AbstractValidator<CheckoutCompleted>
{
    public CheckoutCompletedValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty()
            .WithMessage("Customer identifier is required");
            
        RuleFor(x => x.LineItems)
            .NotEmpty()
            .WithMessage("Order must contain at least one line item");
            
        RuleForEach(x => x.LineItems)
            .ChildRules(item =>
            {
                item.RuleFor(x => x.Quantity)
                    .GreaterThan(0)
                    .WithMessage("Quantity must be positive");
                    
                item.RuleFor(x => x.PriceAtPurchase)
                    .GreaterThan(0)
                    .WithMessage("Price must be positive");
            });
            
        RuleFor(x => x.ShippingAddress)
            .NotNull()
            .WithMessage("Shipping address is required");
            
        RuleFor(x => x.PaymentMethodToken)
            .NotEmpty()
            .WithMessage("Payment method token is required");
    }
}
```

### Query Endpoint

```csharp
// Wolverine HTTP endpoint for querying orders
public static class GetOrderEndpoint
{
    [WolverineGet("/api/orders/{orderId}")]
    public static async Task<IResult> Get(
        Guid orderId,
        IQuerySession session,
        CancellationToken cancellationToken)
    {
        var order = await session.LoadAsync<Order>(orderId, cancellationToken);
            
        return order is null
            ? Results.NotFound()
            : Results.Ok(OrderResponse.From(order));
    }
}

// Response DTO to avoid exposing saga internals
public sealed record OrderResponse(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<OrderLineItem> LineItems,
    ShippingAddress ShippingAddress,
    string ShippingMethod,
    decimal TotalAmount,
    OrderStatus Status,
    DateTimeOffset PlacedAt)
{
    public static OrderResponse From(Order saga) => new(
        saga.Id,
        saga.CustomerId,
        saga.LineItems,
        saga.ShippingAddress,
        saga.ShippingMethod,
        saga.TotalAmount,
        saga.Status,
        saga.PlacedAt);
}
```

## Data Models

### Integration Event (from Shopping)

```csharp
// Received from Shopping context when checkout completes
// This message starts the Order saga
public sealed record CheckoutCompleted(
    Guid CartId,
    Guid CustomerId,
    IReadOnlyList<CheckoutLineItem> LineItems,
    ShippingAddress ShippingAddress,
    string ShippingMethod,
    string PaymentMethodToken,
    IReadOnlyList<AppliedDiscount>? AppliedDiscounts,
    DateTimeOffset CompletedAt);

public sealed record CheckoutLineItem(
    string Sku,
    int Quantity,
    decimal PriceAtPurchase);

public sealed record ShippingAddress(
    string Street,
    string? Street2,
    string City,
    string State,
    string PostalCode,
    string Country);

public sealed record AppliedDiscount(
    string Code,
    decimal Amount);
```

### Domain Event

```csharp
// Domain event published when order saga is started
// Triggers downstream contexts (Payments, Inventory)
public sealed record OrderPlaced(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<OrderLineItem> LineItems,
    ShippingAddress ShippingAddress,
    string ShippingMethod,
    string PaymentMethodToken,
    decimal TotalAmount,
    DateTimeOffset PlacedAt);

public sealed record OrderLineItem(
    string Sku,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);
```

### Order Status

```csharp
// Saga states from CONTEXTS.md
public enum OrderStatus
{
    Placed,              // Order created, awaiting payment and inventory confirmation
    PendingPayment,      // Awaiting async payment confirmation
    PaymentConfirmed,    // Funds captured successfully
    PaymentFailed,       // Payment declined (terminal or retry branch)
    OnHold,              // Flagged for fraud review or inventory issues
    Fulfilling,          // Handed off to Fulfillment BC
    Shipped,             // Integration event from Fulfillment
    Delivered,           // Integration event from Fulfillment
    Cancelled,           // Compensation triggered
    ReturnRequested,     // Customer initiated return
    Closed               // Terminal state
}
```

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system-essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Saga creation produces valid Order with Placed status

*For any* valid `CheckoutCompleted` event (non-empty line items, valid quantities and prices, present customer/shipping/payment info), starting the Order saga SHALL produce an Order with status `Placed`, a unique identifier, and a placement timestamp.

**Validates: Requirements 1.1, 1.4, 1.5**

### Property 2: Order saga preserves all CheckoutCompleted data

*For any* valid `CheckoutCompleted` event, the resulting Order saga SHALL contain all line items with matching SKU, quantity, and price, plus the customer identifier, shipping address, shipping method, and payment method token from the original event.

**Validates: Requirements 1.2, 1.3**

### Property 3: OrderPlaced event contains complete order data

*For any* successfully started Order saga, the published `OrderPlaced` event SHALL contain the order identifier, customer identifier, all line items with their details, shipping information, payment method token, and calculated total amount.

**Validates: Requirements 2.1, 2.2, 2.3**

### Property 4: Validation rejects invalid line items

*For any* `CheckoutCompleted` event containing a line item with quantity ≤ 0 or price ≤ 0, validation SHALL fail and saga creation SHALL be rejected.

**Validates: Requirements 3.2, 3.3**

### Property 5: Saga is persisted and retrievable

*For any* successfully started Order saga, the saga SHALL be persisted to Marten and retrievable by its identifier.

**Validates: Requirements 4.1, 4.2**

### Property 6: Event serialization round-trip

*For any* valid `OrderPlaced` event, serializing to JSON and then deserializing SHALL produce an event equivalent to the original.

**Validates: Requirements 5.2, 5.3**

### Property 7: Order query returns existing orders

*For any* Order saga that has been successfully started and persisted, querying by the Order identifier SHALL return the Order with its current state.

**Validates: Requirements 6.1**

## Error Handling

### Validation Errors

Validation failures are handled by Wolverine's FluentValidation integration:
- Returns HTTP 400 Bad Request with ProblemDetails
- Includes specific validation error messages
- Does not create saga or publish events

### Concurrency Errors

Marten handles optimistic concurrency for saga persistence:
- `ConcurrencyException` triggers Wolverine retry policy (configured in Program.cs)
- Retries once immediately, then with cooldown (100ms, 250ms)
- Discards after retries exhausted (logged for investigation)

### Not Found Errors

Query endpoints return HTTP 404 when order doesn't exist:
- No exception thrown
- Clean `Results.NotFound()` response

## Testing Strategy

### Dual Testing Approach

This feature uses both unit tests and property-based tests:
- **Unit tests**: Verify specific examples, edge cases, and integration points
- **Property-based tests**: Verify universal properties hold across all valid inputs

### Property-Based Testing

**Library**: FsCheck with xUnit integration (`FsCheck.Xunit`)

FsCheck is chosen because:
- Mature .NET property-based testing library
- Excellent xUnit integration
- Good support for custom generators
- Active maintenance

**Configuration**: Each property test runs minimum 100 iterations.

**Test Annotation Format**: Each property-based test includes a comment:
```csharp
// **Feature: order-placement, Property {number}: {property_text}**
```

### Unit Testing

Unit tests cover:
- Validator behavior for specific invalid inputs (edge cases from requirements 3.1, 3.4, 3.5, 3.6)
- Query endpoint returns 404 for non-existent orders (requirement 6.2)
- HTTP endpoint existence and routing (requirement 6.3)

### Integration Testing

Integration tests using Alba and TestContainers:
- Full request/response cycle through Wolverine
- Real PostgreSQL database via TestContainers
- Verify saga is persisted to Marten
- Verify order can be queried after creation

### Test Project Structure

```
tests/Order Management/
├── Orders.UnitTests/
│   ├── Placement/
│   │   ├── CheckoutCompletedValidatorTests.cs
│   │   ├── OrderSagaCreationPropertyTests.cs
│   │   └── OrderSerializationPropertyTests.cs
│   └── Orders.UnitTests.csproj
└── Orders.Api.IntegrationTests/
    ├── Placement/
    │   ├── PlaceOrderTests.cs
    │   └── GetOrderTests.cs
    ├── TestFixture.cs
    └── Orders.Api.IntegrationTests.csproj
```
