namespace Payments.Processing;

/// <summary>
/// Domain event when payment capture fails.
/// Persisted to the Marten event store.
/// </summary>
public sealed record PaymentFailed(
    Guid PaymentId,
    string FailureReason,
    bool IsRetriable,
    DateTimeOffset FailedAt);
