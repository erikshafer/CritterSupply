namespace Messages.Contracts.Inventory;

/// <summary>
/// Integration message published by Inventory BC when stock reservation fails (insufficient stock).
/// Consumed by Shopping and Orders BCs.
/// </summary>
public sealed record ReservationFailed(
    Guid OrderId,
    Guid ReservationId,
    string SKU,
    string WarehouseId,
    int RequestedQuantity,
    int AvailableQuantity,
    string Reason,
    DateTimeOffset FailedAt);
