namespace Messages.Contracts.Inventory;

/// <summary>
/// Integration message published by Inventory BC when an inventory adjustment is made for a SKU.
/// Consumed by Vendor Portal BC to update the InventorySnapshot projection and push
/// real-time updates via SignalR.
/// </summary>
public sealed record InventoryAdjusted(
    string Sku,
    string WarehouseId,
    int QuantityChange,
    int NewQuantity,
    DateTimeOffset AdjustedAt);
