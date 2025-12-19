namespace Fulfillment.Shipments;

/// <summary>
/// Domain event when a shipment is dispatched to the carrier.
/// </summary>
public sealed record ShipmentDispatched(
    string Carrier,
    string TrackingNumber,
    DateTimeOffset DispatchedAt);
