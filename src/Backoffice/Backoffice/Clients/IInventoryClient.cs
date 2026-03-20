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

    /// <summary>
    /// List all inventory (Warehouse workflow: browse inventory)
    /// </summary>
    Task<IReadOnlyList<InventoryListItemDto>> ListInventoryAsync(
        int? page = null,
        int? pageSize = null,
        CancellationToken ct = default);

    /// <summary>
    /// Adjust inventory quantity (positive or negative)
    /// POST /api/inventory/{sku}/adjust
    /// </summary>
    Task<AdjustInventoryResultDto?> AdjustInventoryAsync(
        string sku,
        int adjustmentQuantity,
        string reason,
        string adjustedBy,
        CancellationToken ct = default);

    /// <summary>
    /// Receive inbound stock shipment
    /// POST /api/inventory/{sku}/receive
    /// </summary>
    Task<ReceiveStockResultDto?> ReceiveInboundStockAsync(
        string sku,
        int quantity,
        string source,
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

/// <summary>
/// Inventory list item DTO for browse UI
/// </summary>
public sealed record InventoryListItemDto(
    string Sku,
    string ProductName,
    int AvailableQuantity,
    int ReservedQuantity,
    int TotalQuantity);

/// <summary>
/// Result from adjusting inventory
/// </summary>
public sealed record AdjustInventoryResultDto(
    Guid Id,
    string Sku,
    string WarehouseId,
    int AvailableQuantity);

/// <summary>
/// Result from receiving inbound stock
/// </summary>
public sealed record ReceiveStockResultDto(
    Guid Id,
    string Sku,
    string WarehouseId,
    int AvailableQuantity);
