namespace Inventory.Api.Commands;

/// <summary>
/// Response DTO for inventory adjustment operations.
/// </summary>
public sealed record AdjustInventoryResult(
    Guid InventoryId,
    string Sku,
    string WarehouseId,
    int NewAvailableQuantity);
