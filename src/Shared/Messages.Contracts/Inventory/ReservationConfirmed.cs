namespace Messages.Contracts.Inventory;

/// <summary>
/// Integration message published by Inventory BC when stock reservation succeeds.
/// Consumed by Shopping and Orders BCs.
/// </summary>
public sealed record ReservationConfirmed(
    Guid InventoryId,
    Guid ReservationId,
    string SKU,
    string WarehouseId,
    int Quantity,
    DateTimeOffset ReservedAt);
