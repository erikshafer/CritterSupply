namespace Messages.Contracts.Inventory;

/// <summary>
/// Integration message published by Inventory BC when a SKU's quantity drops below its threshold.
/// Consumed by Vendor Portal BC to raise low-stock alerts for the owning vendor.
/// </summary>
public sealed record LowStockDetected(
    string Sku,
    string WarehouseId,
    int CurrentQuantity,
    int ThresholdQuantity,
    DateTimeOffset DetectedAt);
