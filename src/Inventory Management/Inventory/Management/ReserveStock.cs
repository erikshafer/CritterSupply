namespace Inventory.Management;

/// <summary>
/// Command from Shopping or Orders context requesting a soft stock reservation.
/// </summary>
public sealed record ReserveStock(
    string SKU,
    string WarehouseId,
    Guid ReservationId,
    int Quantity);
