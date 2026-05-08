namespace Fulfillment.Shipments;

/// <summary>
/// Domain event when a shipment is successfully delivered.
/// <para>
/// <c>OrderId</c> is denormalized so that <see cref="MultiShipmentView"/> (keyed
/// by <c>OrderId</c>) can mark the right shipment entry as delivered without
/// having to reverse the one-way UUID v5 hash that maps <c>OrderId</c> to the
/// shipment stream id.
/// </para>
/// </summary>
public sealed record ShipmentDelivered(
    Guid OrderId,
    DateTimeOffset DeliveredAt,
    string? RecipientName = null);
