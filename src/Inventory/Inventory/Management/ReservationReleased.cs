namespace Inventory.Management;

/// <summary>
/// Domain event indicating a reservation has been cancelled and stock returned to available pool.
/// </summary>
public sealed record ReservationReleased(
    Guid ReservationId,
    int Quantity,
    string Reason,
    DateTimeOffset ReleasedAt);
