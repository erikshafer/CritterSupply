namespace Payments.Processing;

/// <summary>
/// Domain event when a refund is processed against a payment.
/// Persisted to the Marten event store.
/// </summary>
public sealed record PaymentRefunded(
    Guid PaymentId,
    decimal RefundAmount,
    decimal TotalRefunded,
    string RefundTransactionId,
    DateTimeOffset RefundedAt);
