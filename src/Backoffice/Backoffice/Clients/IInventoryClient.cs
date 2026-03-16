namespace Backoffice.Clients;

/// <summary>
/// HTTP client for querying Inventory BC (admin use)
/// </summary>
public interface IInventoryClient
{
    /// <summary>
    /// Get stock level for a SKU (Warehouse workflow: stock inquiry)
    /// </summary>
    Task<StockLevelDto?> GetStockLevelAsync(string sku, CancellationToken ct = default);

    /// <summary>
    /// Get low-stock alerts (Operations workflow: inventory monitoring)
    /// </summary>
    Task<IReadOnlyList<LowStockDto>> GetLowStockAsync(
        int? threshold = null,
        CancellationToken ct = default);
}

/// <summary>
/// Stock level DTO from Inventory BC
/// </summary>
public sealed record StockLevelDto(
    string Sku,
    int AvailableQuantity,
    int ReservedQuantity,
    int TotalQuantity,
    string? WarehouseId);

/// <summary>
/// Low stock DTO from Inventory BC
/// </summary>
public sealed record LowStockDto(
    string Sku,
    string ProductName,
    int AvailableQuantity,
    int ThresholdQuantity);
