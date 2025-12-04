namespace Payments.Processing;

/// <summary>
/// Integration message published when payment fails.
/// Orders saga decides retry or cancellation.
/// </summary>
public sealed record PaymentFailedIntegration(
    Guid PaymentId,
    Guid OrderId,
    string FailureReason,
    bool IsRetriable,
    DateTimeOffset FailedAt);
