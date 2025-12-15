namespace Messages.Contracts.Inventory;

/// <summary>
/// Integration message published by Inventory BC when a reservation is committed (hard allocation).
/// Consumed by Orders BC.
/// </summary>
public sealed record ReservationCommitted(
    Guid InventoryId,
    Guid ReservationId,
    string SKU,
    string WarehouseId,
    int Quantity,
    DateTimeOffset CommittedAt);
