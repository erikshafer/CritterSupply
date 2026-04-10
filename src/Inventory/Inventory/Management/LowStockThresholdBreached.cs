namespace Inventory.Management;

/// <summary>
/// Domain event indicating that inventory crossed below the low-stock threshold.
/// Only appended when the transition is from above/at threshold to below threshold.
/// </summary>
public sealed record LowStockThresholdBreached(
    string Sku,
    string WarehouseId,
    int PreviousQuantity,
    int NewQuantity,
    int Threshold,
    DateTimeOffset DetectedAt);
