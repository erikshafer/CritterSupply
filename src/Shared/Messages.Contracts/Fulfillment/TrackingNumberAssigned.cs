namespace Messages.Contracts.Fulfillment;

/// <summary>
/// Integration message from Fulfillment to Customer Experience BFF.
/// Published when a tracking number is assigned to a shipment — first customer-visible event.
/// </summary>
public sealed record TrackingNumberAssigned(
    Guid OrderId,
    Guid ShipmentId,
    string TrackingNumber,
    string Carrier,
    DateTimeOffset AssignedAt);
