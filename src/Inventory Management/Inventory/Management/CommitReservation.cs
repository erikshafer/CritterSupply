namespace Inventory.Management;

/// <summary>
/// Command from Orders context requesting to convert a soft reservation to hard allocation.
/// </summary>
public sealed record CommitReservation(
    Guid InventoryId,
    Guid ReservationId);
