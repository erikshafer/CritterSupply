namespace Messages.Contracts.Inventory;

/// <summary>
/// Integration message published by Inventory BC when stock is replenished for a SKU.
/// Consumed by Vendor Portal BC to update the InventorySnapshot projection.
/// </summary>
public sealed record StockReplenished(
    string Sku,
    string WarehouseId,
    int QuantityAdded,
    int NewQuantity,
    DateTimeOffset ReplenishedAt);
