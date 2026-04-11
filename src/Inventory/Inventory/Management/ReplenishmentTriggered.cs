namespace Inventory.Management;

/// <summary>
/// Domain event: a replenishment has been triggered for a SKU at a warehouse.
/// Appended by the ReplenishmentPolicy when low stock coincides with pending backorders.
/// </summary>
public sealed record ReplenishmentTriggered(
    string Sku,
    string WarehouseId,
    int CurrentAvailable,
    int Threshold,
    bool HasPendingBackorders,
    DateTimeOffset TriggeredAt);
