namespace Messages.Contracts.Correspondence;

/// <summary>
/// Published when a message permanently fails after max retries.
/// Consumed by: Admin Portal (future) for alerting CS agents
/// </summary>
public sealed record CorrespondenceFailed(
    Guid MessageId,
    Guid CustomerId,
    string Channel,
    string FailureReason,
    DateTimeOffset FailedAt
);
