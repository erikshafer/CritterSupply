namespace Payments.Processing;

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
