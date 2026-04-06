namespace Messages.Contracts.Fulfillment;

/// <summary>
/// Integration message from Fulfillment to Orders.
/// Published when a shipment is determined lost in transit.
/// Orders saga should trigger the reshipment flow (P2).
/// </summary>
public sealed record ShipmentLostInTransit(
    Guid OrderId,
    Guid ShipmentId,
    string Carrier,
    TimeSpan TimeSinceHandoff,
    DateTimeOffset DetectedAt);
