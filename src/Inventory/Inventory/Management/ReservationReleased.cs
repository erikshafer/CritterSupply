namespace Inventory.Management;

/// <summary>
/// Domain event indicating a reservation has been cancelled and stock returned to available pool.
/// </summary>
public sealed record ReservationReleased(
    Guid ReservationId,
    string Sku,
    string WarehouseId,
    int Quantity,
    string Reason,
    DateTimeOffset ReleasedAt);
