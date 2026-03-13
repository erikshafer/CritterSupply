namespace Messages.Contracts.Correspondence;

/// <summary>
/// Published when a message is successfully delivered.
/// Consumed by: Analytics BC (future) for delivery metrics
/// </summary>
public sealed record CorrespondenceDelivered(
    Guid MessageId,
    Guid CustomerId,
    string Channel,
    DateTimeOffset DeliveredAt,
    int AttemptCount
);
