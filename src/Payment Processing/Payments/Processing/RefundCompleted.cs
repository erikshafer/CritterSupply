namespace Payments.Processing;

/// <summary>
/// Integration event published when refund is successfully processed.
/// Orders saga handles refund completion.
/// </summary>
public sealed record RefundCompleted(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string TransactionId,
    DateTimeOffset RefundedAt);
