namespace Messages.Contracts.Inventory;

/// <summary>
/// Integration message published by Inventory BC when a reservation is released (stock returned to pool).
/// Consumed by Orders BC for tracking purposes.
/// </summary>
public sealed record ReservationReleased(
    Guid InventoryId,
    Guid ReservationId,
    string SKU,
    string WarehouseId,
    int Quantity,
    string Reason,
    DateTimeOffset ReleasedAt);
