namespace Messages.Contracts.Inventory;

/// <summary>
/// Integration message published by Inventory BC when stock reservation succeeds.
/// Consumed by Shopping and Orders BCs.
/// </summary>
public sealed record ReservationConfirmed(
    Guid OrderId,
    Guid InventoryId,
    Guid ReservationId,
    string Sku,
    string WarehouseId,
    int Quantity,
    DateTimeOffset ReservedAt);
