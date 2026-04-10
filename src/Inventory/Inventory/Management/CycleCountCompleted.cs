namespace Inventory.Management;

/// <summary>
/// Domain event indicating a cycle count has been completed for this SKU at this warehouse.
/// No direct state change on the aggregate — discrepancies are handled via InventoryAdjusted.
/// </summary>
public sealed record CycleCountCompleted(
    string Sku,
    string WarehouseId,
    int PhysicalCount,
    int SystemCount,
    string CountedBy,
    DateTimeOffset CompletedAt);
