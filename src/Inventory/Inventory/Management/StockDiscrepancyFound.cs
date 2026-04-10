namespace Inventory.Management;

/// <summary>
/// Domain event indicating a stock discrepancy was detected.
/// May be appended during short pick detection, zero pick, or cycle count completion.
/// No state change on the aggregate — serves as an audit/alert record.
/// </summary>
public sealed record StockDiscrepancyFound(
    string Sku,
    string WarehouseId,
    int ExpectedQuantity,
    int ActualQuantity,
    DiscrepancyType DiscrepancyType,
    string Description,
    DateTimeOffset DetectedAt);
