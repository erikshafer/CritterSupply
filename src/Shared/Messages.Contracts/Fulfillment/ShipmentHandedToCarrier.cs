namespace Messages.Contracts.Fulfillment;

/// <summary>
/// Integration message from Fulfillment to Orders.
/// Published when a shipment is physically handed to the carrier (custody transfer).
/// Replaces ShipmentDispatched with more precise semantics.
/// </summary>
public sealed record ShipmentHandedToCarrier(
    Guid OrderId,
    Guid ShipmentId,
    string Carrier,
    string TrackingNumber,
    DateTimeOffset HandedAt);
