namespace Messages.Contracts.Fulfillment;

/// <summary>
/// Integration message from Fulfillment to Orders.
/// Published when delivery attempt fails (address issue, recipient unavailable, etc.).
/// </summary>
public sealed record ShipmentDeliveryFailed(
    Guid OrderId,
    Guid ShipmentId,
    string Reason,
    DateTimeOffset FailedAt);
