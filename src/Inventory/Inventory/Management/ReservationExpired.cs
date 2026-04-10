namespace Inventory.Management;

/// <summary>
/// Domain event indicating a reservation has expired after a timeout period.
/// Applies the same state transition as ReservationReleased — stock returns to available pool.
/// </summary>
public sealed record ReservationExpired(
    Guid ReservationId,
    string Sku,
    string WarehouseId,
    int Quantity,
    string Reason,
    DateTimeOffset ExpiredAt);
