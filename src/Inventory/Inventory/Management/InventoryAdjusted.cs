namespace Inventory.Management;

/// <summary>
/// Domain event indicating a manual inventory adjustment was made (increase or decrease).
/// Used for cycle counts, damage write-offs, theft adjustments, etc.
/// </summary>
public sealed record InventoryAdjusted(
    string Sku,
    string WarehouseId,
    int AdjustmentQuantity,
    string Reason,
    string AdjustedBy,
    DateTimeOffset AdjustedAt);
