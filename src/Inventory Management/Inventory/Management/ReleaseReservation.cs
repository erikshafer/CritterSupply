namespace Inventory.Management;

/// <summary>
/// Command from Orders context requesting to cancel a reservation and return stock to pool.
/// </summary>
public sealed record ReleaseReservation(
    Guid InventoryId,
    Guid ReservationId,
    string Reason);
