namespace Inventory.Management;

/// <summary>
/// Command to create initial inventory tracking for a SKU at a warehouse.
/// </summary>
public sealed record InitializeInventory(
    string SKU,
    string WarehouseId,
    int InitialQuantity);
