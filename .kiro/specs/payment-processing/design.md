# Design Document: Payment Processing

## Overview

The Payment Processing feature is the core of the Payments bounded context in CritterSupply. It handles payment capture requests from the Orders context, communicates with external payment gateways, and publishes results back to drive order saga state transitions.

Payments are implemented as event-sourced aggregates using Marten, capturing the full audit trail of payment lifecycle events. The payment gateway is abstracted behind an `IPaymentGateway` interface, enabling easy testing with stubs and swapping providers in production.

## Architecture

```mermaid
flowchart TB
    subgraph Orders["Orders Context"]
        OrderSaga[Order Saga]
    end
    
    subgraph Payments["Payments Context"]
        subgraph API["Payments.Api"]
            QueryEndpoint[GET /api/payments/{id}]
        end
        
        subgraph Domain["Payments Domain"]
            Validator[PaymentRequestedValidator]
            Payment[Payment Aggregate]
            Handler[PaymentRequestedHandler]
        end
        
        subgraph Infrastructure["Infrastructure"]
            Gateway[IPaymentGateway]
            Marten[(Marten Event Store)]
            Wolverine[Wolverine Messaging]
        end
        
        subgraph Gateways["Gateway Implementations"]
            StubGateway[StubPaymentGateway]
            StripeGateway[StripePaymentGateway]
        end
    end
    
    OrderSaga -->|PaymentRequested| Validator
    Validator -->|Valid| Handler
    Handler --> Gateway
    Gateway --> StubGateway
    Gateway -.-> StripeGateway
    Handler --> Payment
    Payment --> Marten
    Handler -->|PaymentCaptured/Failed| Wolverine
    Wolverine --> OrderSaga
    QueryEndpoint --> Marten
```

### Key Design Decisions

1. **Event Sourcing**: Payments are event-sourced aggregates for complete audit trail
2. **Gateway Abstraction**: `IPaymentGateway` interface enables testing and provider flexibility
3. **Stub Gateway**: Test implementation simulates success/failure based on token patterns
4. **Wolverine Messaging**: Cross-context communication via RabbitMQ (stubbed in tests)
5. **Immutable Records**: All events, commands, and value objects are immutable records
6. **Pure Functions**: Handler methods are pure functions that return events

## Components and Interfaces

### Payment Gateway Interface

```csharp
/// <summary>
/// Abstraction for payment gateway operations.
/// Implementations handle provider-specific details.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Captures funds for a payment.
    /// </summary>
    Task<GatewayResult> CaptureAsync(
        decimal amount,
        string currency,
        string paymentMethodToken,
        CancellationToken cancellationToken);
    
    /// <summary>
    /// Refunds a previously captured payment.
    /// </summary>
    Task<GatewayResult> RefundAsync(
        string transactionId,
        decimal amount,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result from a gateway operation.
/// </summary>
public sealed record GatewayResult(
    bool Success,
    string? TransactionId,
    string? FailureReason,
    bool IsRetriable);
```

### Stub Gateway Implementation

```csharp
/// <summary>
/// Stub gateway for testing. Behavior controlled by token patterns:
/// - "tok_success_*" -> Success
/// - "tok_decline_*" -> Decline failure
/// - "tok_timeout_*" -> Retriable timeout
/// </summary>
public sealed class StubPaymentGateway : IPaymentGateway
{
    public Task<GatewayResult> CaptureAsync(
        decimal amount,
        string currency,
        string paymentMethodToken,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(paymentMethodToken switch
        {
            _ when paymentMethodToken.StartsWith("tok_success") => 
                new GatewayResult(true, $"txn_{Guid.NewGuid():N}", null, false),
            _ when paymentMethodToken.StartsWith("tok_decline") => 
                new GatewayResult(false, null, "card_declined", false),
            _ when paymentMethodToken.StartsWith("tok_timeout") => 
                new GatewayResult(false, null, "gateway_timeout", true),
            _ => new GatewayResult(true, $"txn_{Guid.NewGuid():N}", null, false)
        });
    }
    
    public Task<GatewayResult> RefundAsync(
        string transactionId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            new GatewayResult(true, $"ref_{Guid.NewGuid():N}", null, false));
    }
}
```

### Message Handler

```csharp
/// <summary>
/// Wolverine handler for PaymentRequested commands.
/// </summary>
public static class PaymentRequestedHandler
{
    public static async Task<object[]> Handle(
        PaymentRequested command,
        IPaymentGateway gateway,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // Create payment aggregate
        var payment = Payment.Create(command);
        
        // Call gateway
        var result = await gateway.CaptureAsync(
            command.Amount,
            command.Currency,
            command.PaymentMethodToken,
            cancellationToken);
        
        // Apply result to payment
        var (updatedPayment, domainEvent) = result.Success
            ? payment.Capture(result.TransactionId!, DateTimeOffset.UtcNow)
            : payment.Fail(result.FailureReason!, result.IsRetriable, DateTimeOffset.UtcNow);
        
        // Persist events
        session.Events.StartStream<Payment>(updatedPayment.Id, updatedPayment.PendingEvents);
        
        // Return integration event for Orders context
        return [domainEvent];
    }
}
```

### Query Endpoint

```csharp
/// <summary>
/// Wolverine HTTP endpoint for querying payments.
/// </summary>
public static class GetPaymentEndpoint
{
    [WolverineGet("/api/payments/{paymentId}")]
    public static async Task<IResult> Get(
        Guid paymentId,
        IQuerySession session,
        CancellationToken cancellationToken)
    {
        var payment = await session.Events
            .AggregateStreamAsync<Payment>(paymentId, token: cancellationToken);
            
        return payment is null
            ? Results.NotFound()
            : Results.Ok(PaymentResponse.From(payment));
    }
}
```

## Data Models

### Commands (from Orders)

```csharp
/// <summary>
/// Command from Orders requesting payment capture.
/// </summary>
public sealed record PaymentRequested(
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string PaymentMethodToken);

/// <summary>
/// Command from Orders requesting a refund.
/// </summary>
public sealed record RefundRequested(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount);
```

### Domain Events (persisted)

```csharp
/// <summary>
/// Event when payment processing is initiated.
/// </summary>
public sealed record PaymentInitiated(
    Guid PaymentId,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string PaymentMethodToken,
    DateTimeOffset InitiatedAt);

/// <summary>
/// Event when payment is successfully captured.
/// </summary>
public sealed record PaymentCapturedEvent(
    Guid PaymentId,
    string TransactionId,
    DateTimeOffset CapturedAt);

/// <summary>
/// Event when payment capture fails.
/// </summary>
public sealed record PaymentFailedEvent(
    Guid PaymentId,
    string FailureReason,
    bool IsRetriable,
    DateTimeOffset FailedAt);
```

### Integration Events (published to Orders)

```csharp
/// <summary>
/// Integration event published when payment is captured.
/// Orders saga transitions to PaymentConfirmed.
/// </summary>
public sealed record PaymentCaptured(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string TransactionId,
    DateTimeOffset CapturedAt);

/// <summary>
/// Integration event published when payment fails.
/// Orders saga decides retry or cancellation.
/// </summary>
public sealed record PaymentFailed(
    Guid PaymentId,
    Guid OrderId,
    string FailureReason,
    bool IsRetriable,
    DateTimeOffset FailedAt);
```

### Payment Aggregate

```csharp
/// <summary>
/// Event-sourced aggregate representing a payment.
/// </summary>
public sealed record Payment(
    Guid Id,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string PaymentMethodToken,
    PaymentStatus Status,
    string? TransactionId,
    string? FailureReason,
    bool IsRetriable,
    DateTimeOffset InitiatedAt,
    DateTimeOffset? ProcessedAt)
{
    internal List<object> PendingEvents { get; } = [];
    
    public static Payment Create(PaymentRequested command)
    {
        var paymentId = Guid.CreateVersion7();
        var initiatedAt = DateTimeOffset.UtcNow;
        
        var payment = new Payment(
            paymentId,
            command.OrderId,
            command.CustomerId,
            command.Amount,
            command.Currency,
            command.PaymentMethodToken,
            PaymentStatus.Pending,
            null, null, false,
            initiatedAt, null);
            
        payment.PendingEvents.Add(new PaymentInitiated(
            paymentId,
            command.OrderId,
            command.CustomerId,
            command.Amount,
            command.Currency,
            command.PaymentMethodToken,
            initiatedAt));
            
        return payment;
    }
    
    public (Payment, PaymentCaptured) Capture(string transactionId, DateTimeOffset capturedAt)
    {
        var updated = this with
        {
            Status = PaymentStatus.Captured,
            TransactionId = transactionId,
            ProcessedAt = capturedAt
        };
        
        updated.PendingEvents.AddRange(PendingEvents);
        updated.PendingEvents.Add(new PaymentCapturedEvent(Id, transactionId, capturedAt));
        
        var integrationEvent = new PaymentCaptured(Id, OrderId, Amount, transactionId, capturedAt);
        
        return (updated, integrationEvent);
    }
    
    public (Payment, PaymentFailed) Fail(string reason, bool isRetriable, DateTimeOffset failedAt)
    {
        var updated = this with
        {
            Status = PaymentStatus.Failed,
            FailureReason = reason,
            IsRetriable = isRetriable,
            ProcessedAt = failedAt
        };
        
        updated.PendingEvents.AddRange(PendingEvents);
        updated.PendingEvents.Add(new PaymentFailedEvent(Id, reason, isRetriable, failedAt));
        
        var integrationEvent = new PaymentFailed(Id, OrderId, reason, isRetriable, failedAt);
        
        return (updated, integrationEvent);
    }
    
    // Marten event sourcing: apply events to rebuild state
    public static Payment Create(IEvent<PaymentInitiated> @event) =>
        new(@event.StreamId,
            @event.Data.OrderId,
            @event.Data.CustomerId,
            @event.Data.Amount,
            @event.Data.Currency,
            @event.Data.PaymentMethodToken,
            PaymentStatus.Pending,
            null, null, false,
            @event.Data.InitiatedAt, null);
    
    public Payment Apply(PaymentCapturedEvent @event) =>
        this with
        {
            Status = PaymentStatus.Captured,
            TransactionId = @event.TransactionId,
            ProcessedAt = @event.CapturedAt
        };
    
    public Payment Apply(PaymentFailedEvent @event) =>
        this with
        {
            Status = PaymentStatus.Failed,
            FailureReason = @event.FailureReason,
            IsRetriable = @event.IsRetriable,
            ProcessedAt = @event.FailedAt
        };
}

public enum PaymentStatus
{
    Pending,
    Captured,
    Failed,
    Refunded
}
```

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system-essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Payment creation produces valid Payment with Pending status

*For any* valid `PaymentRequested` command, creating a Payment SHALL produce a Payment with status `Pending`, a unique identifier, and an initiation timestamp.

**Validates: Requirements 1.1, 1.3, 1.4**

### Property 2: Payment preserves all PaymentRequested data

*For any* valid `PaymentRequested` command, the resulting Payment SHALL contain the order identifier, customer identifier, amount, currency, and payment method token from the original command.

**Validates: Requirements 1.2**

### Property 3: Successful capture updates Payment and publishes event

*For any* Payment where the gateway returns success, the Payment status SHALL be `Captured`, the transaction ID SHALL be recorded, and a `PaymentCaptured` event SHALL be published with correct data.

**Validates: Requirements 2.2, 2.3, 2.4, 2.5**

### Property 4: Failed capture updates Payment and publishes event with reason

*For any* Payment where the gateway returns failure, the Payment status SHALL be `Failed`, the failure reason SHALL be recorded, and a `PaymentFailed` event SHALL be published containing the reason code.

**Validates: Requirements 3.1, 3.2, 3.3, 3.4**

### Property 5: Validation rejects invalid payment amounts

*For any* `PaymentRequested` command with amount ≤ 0, validation SHALL fail and payment processing SHALL be rejected.

**Validates: Requirements 4.1**

### Property 6: Refund validation rejects invalid requests

*For any* `RefundRequested` command where the original payment doesn't exist, wasn't captured, or the refund amount exceeds the captured amount, the refund SHALL be rejected.

**Validates: Requirements 5.1, 5.3**

### Property 7: Successful refund publishes RefundCompleted event

*For any* valid refund request where the gateway returns success, a `RefundCompleted` event SHALL be published.

**Validates: Requirements 5.4**

### Property 8: Event sourcing state reconstruction

*For any* Payment with persisted events, aggregating the event stream SHALL produce a Payment with state equivalent to the original Payment at the time of the last event.

**Validates: Requirements 6.4**

### Property 9: Payment query returns existing payments

*For any* Payment that has been successfully created and persisted, querying by the Payment identifier SHALL return the Payment with its current state.

**Validates: Requirements 8.1**

## Error Handling

### Validation Errors

Validation failures are handled by Wolverine's FluentValidation integration:
- Returns validation exception
- Includes specific validation error messages
- Does not create payment or call gateway

### Gateway Errors

Gateway failures are captured in the Payment aggregate:
- `IsRetriable` flag indicates if Orders should retry
- Failure reason codes enable Orders to make decisions
- Timeouts are treated as retriable failures

### Not Found Errors

Query endpoints return HTTP 404 when payment doesn't exist.

## Testing Strategy

### Dual Testing Approach

This feature uses both unit tests and property-based tests:
- **Unit tests**: Verify specific examples, edge cases, and gateway interactions
- **Property-based tests**: Verify universal properties hold across all valid inputs

### Property-Based Testing

**Library**: FsCheck with xUnit integration (`FsCheck.Xunit`)

**Configuration**: Each property test runs minimum 100 iterations.

**Test Annotation Format**: Each property-based test includes a comment:
```csharp
// **Feature: payment-processing, Property {number}: {property_text}**
```

### Gateway Testing

The `StubPaymentGateway` enables deterministic testing:
- `tok_success_*` tokens → successful capture
- `tok_decline_*` tokens → decline failure
- `tok_timeout_*` tokens → retriable timeout

### Test Project Structure

```
tests/Payment Processing/
├── Payments.UnitTests/
│   ├── Processing/
│   │   ├── PaymentRequestedValidatorTests.cs
│   │   ├── PaymentCreationPropertyTests.cs
│   │   ├── PaymentCapturePropertyTests.cs
│   │   └── PaymentSerializationPropertyTests.cs
│   └── Payments.UnitTests.csproj
└── Payments.Api.IntegrationTests/
    ├── Processing/
    │   ├── PaymentFlowTests.cs
    │   └── GetPaymentTests.cs
    ├── TestFixture.cs
    └── Payments.Api.IntegrationTests.csproj
```
