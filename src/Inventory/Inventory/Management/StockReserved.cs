namespace Inventory.Management;

/// <summary>
/// Domain event indicating stock has been soft-held for an order (checkout).
/// </summary>
public sealed record StockReserved(
    Guid OrderId,
    Guid ReservationId,
    string Sku,
    string WarehouseId,
    int Quantity,
    DateTimeOffset ReservedAt);
