namespace Inventory.Management;

/// <summary>
/// Per-warehouse quantity entry within a <see cref="NetworkInventorySummaryView"/>.
/// </summary>
public sealed record WarehouseQuantitySummary(
    string WarehouseId,
    int AvailableQuantity,
    int ReservedQuantity,
    int QuarantinedQuantity);

/// <summary>
/// Async multi-stream projection keyed by SKU that aggregates all warehouse-level
/// inventory data into a single network-wide summary.
/// Used by dashboard/reporting — not on the critical checkout path.
/// </summary>
public sealed class NetworkInventorySummaryView
{
    /// <summary>
    /// SKU serves as the document identity (string key).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public List<WarehouseQuantitySummary> Warehouses { get; set; } = new();

    public int TotalAvailable => Warehouses.Sum(w => w.AvailableQuantity);
    public int TotalReserved => Warehouses.Sum(w => w.ReservedQuantity);
    public int TotalQuarantined => Warehouses.Sum(w => w.QuarantinedQuantity);
    public int TotalNetworkQuantity => TotalAvailable + TotalReserved + TotalQuarantined;
}
