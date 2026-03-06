namespace Messages.Contracts.Payments;

/// <summary>
/// Integration message published by Orders BC when a refund needs to be processed.
/// Sent when an order is cancelled after payment was captured, or when inventory fails
/// after payment was already taken. Consumed by Payments BC.
/// </summary>
public sealed record RefundRequested(
    Guid OrderId,
    decimal Amount,
    string Reason,
    DateTimeOffset RequestedAt);
