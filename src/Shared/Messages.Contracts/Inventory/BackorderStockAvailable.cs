namespace Messages.Contracts.Inventory;

/// <summary>
/// Integration message published by Inventory BC when stock arrives for a backordered SKU.
/// Fulfillment can then re-attempt routing for backordered shipments.
/// </summary>
public sealed record BackorderStockAvailable(
    string Sku,
    string WarehouseId,
    int AvailableQuantity,
    DateTimeOffset DetectedAt);
