namespace Inventory.Management;

/// <summary>
/// Domain event indicating stock has been physically handed to a carrier.
/// Removes quantity from PickedAllocations and decrements TotalOnHand
/// (stock has left the building).
/// </summary>
public sealed record StockShipped(
    string Sku,
    string WarehouseId,
    Guid ReservationId,
    int Quantity,
    Guid ShipmentId,
    DateTimeOffset ShippedAt);
