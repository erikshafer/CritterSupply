namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when the replacement item ships.
/// Customer Experience BC updates UI with tracking; Notifications BC sends shipment notification.
/// </summary>
public sealed record ExchangeReplacementShipped(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    string ShipmentId,
    string TrackingNumber,
    DateTimeOffset ShippedAt);
