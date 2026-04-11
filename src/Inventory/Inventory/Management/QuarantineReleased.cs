namespace Inventory.Management;

/// <summary>
/// Domain event: quarantined stock has been released back to available inventory (resalable).
/// Appended alongside a positive InventoryAdjusted to restore AvailableQuantity.
/// </summary>
public sealed record QuarantineReleased(
    string Sku,
    string WarehouseId,
    Guid QuarantineId,
    int Quantity,
    string ReleasedBy,
    DateTimeOffset ReleasedAt);
