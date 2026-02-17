namespace Fulfillment.Shipments;

/// <summary>
/// Domain event when a shipment is successfully delivered.
/// </summary>
public sealed record ShipmentDelivered(
    DateTimeOffset DeliveredAt,
    string? RecipientName = null);
