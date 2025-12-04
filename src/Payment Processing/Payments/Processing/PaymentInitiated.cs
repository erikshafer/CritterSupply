namespace Payments.Processing;

/// <summary>
/// Domain event when payment processing is initiated.
/// Persisted to the Marten event store.
/// </summary>
public sealed record PaymentInitiated(
    Guid PaymentId,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string PaymentMethodToken,
    DateTimeOffset InitiatedAt);
