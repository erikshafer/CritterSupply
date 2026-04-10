namespace Inventory.Management;

/// <summary>
/// Domain event indicating a backorder has been registered for this SKU at this warehouse.
/// Sets HasPendingBackorders = true on the aggregate.
/// </summary>
public sealed record BackorderRegistered(
    string Sku,
    string WarehouseId,
    Guid OrderId,
    Guid ShipmentId,
    int Quantity,
    DateTimeOffset RegisteredAt);
