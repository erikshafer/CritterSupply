namespace Messages.Contracts.Fulfillment;

/// <summary>
/// Integration message from Fulfillment to Orders.
/// Published when a carrier initiates return-to-sender after exhausting delivery attempts.
/// Replaces the terminal ShipmentDeliveryFailed with explicit attempt chain semantics.
/// </summary>
public sealed record ReturnToSenderInitiated(
    Guid OrderId,
    Guid ShipmentId,
    string Carrier,
    int TotalAttempts,
    int EstimatedReturnDays,
    DateTimeOffset InitiatedAt);
