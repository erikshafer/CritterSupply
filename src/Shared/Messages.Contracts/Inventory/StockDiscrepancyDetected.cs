namespace Messages.Contracts.Inventory;

/// <summary>
/// Integration event published by Inventory BC when a stock discrepancy is detected.
/// Consumed by downstream BCs (Operations, Vendor Portal) for alerting and dashboards.
/// </summary>
public sealed record StockDiscrepancyDetected(
    string Sku,
    string WarehouseId,
    int ExpectedQuantity,
    int ActualQuantity,
    string DiscrepancyType,
    string Description,
    DateTimeOffset DetectedAt);
