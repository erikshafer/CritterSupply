namespace Payments.Processing;

/// <summary>
/// Integration event published when refund processing fails.
/// Orders saga handles refund failure.
/// </summary>
public sealed record RefundFailed(
    Guid PaymentId,
    Guid OrderId,
    string FailureReason,
    DateTimeOffset FailedAt);
