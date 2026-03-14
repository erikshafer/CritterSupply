namespace Messages.Contracts.Correspondence;

/// <summary>
/// Published when a message is queued for delivery.
/// Consumed by: Operations Dashboard (future) for monitoring
/// </summary>
public sealed record CorrespondenceQueued(
    Guid MessageId,
    Guid CustomerId,
    string Channel, // Email, SMS, Push
    DateTimeOffset QueuedAt
);
