namespace Inventory.Management;

/// <summary>
/// Domain event indicating initial inventory setup for a SKU at a warehouse.
/// </summary>
public sealed record InventoryInitialized(
    string SKU,
    string WarehouseId,
    int InitialQuantity,
    DateTimeOffset InitializedAt);
