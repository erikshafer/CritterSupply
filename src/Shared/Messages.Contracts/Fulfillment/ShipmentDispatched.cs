namespace Messages.Contracts.Fulfillment;

/// <summary>
/// Integration message from Fulfillment to Orders.
/// Published when a shipment leaves the warehouse with carrier and tracking info.
/// </summary>
public sealed record ShipmentDispatched(
    Guid OrderId,
    Guid ShipmentId,
    string Carrier,
    string TrackingNumber,
    DateTimeOffset DispatchedAt);
