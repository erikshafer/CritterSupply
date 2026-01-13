namespace Messages.Contracts.Inventory;

/// <summary>
/// Integration message published by Inventory BC when a reservation is released (stock returned to pool).
/// Consumed by Orders BC for tracking purposes.
/// </summary>
public sealed record ReservationReleased(
    Guid OrderId,
    Guid InventoryId,
    Guid ReservationId,
    string Sku,
    string WarehouseId,
    int Quantity,
    string Reason,
    DateTimeOffset ReleasedAt);
