using Marten;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine.Http;

namespace Inventory.Api.Queries;

/// <summary>
/// Response DTO for low stock items.
/// </summary>
public sealed record LowStockItem(
    string Sku,
    string WarehouseId,
    int AvailableQuantity,
    int ReservedQuantity,
    int TotalOnHand);

/// <summary>
/// Response DTO for low stock summary.
/// </summary>
public sealed record LowStockResponse(
    int TotalLowStockItems,
    IReadOnlyList<LowStockItem> Items);

/// <summary>
/// HTTP GET endpoint to retrieve low stock items.
/// Used by WarehouseClerk alert feed and OperationsManager dashboard KPIs.
/// Phase 1: Simple threshold check (AvailableQuantity < 10).
/// Phase 2+: Add configurable thresholds per SKU, demand forecasting.
/// </summary>
public sealed class GetLowStock
{
    [WolverineGet("/api/inventory/low-stock")]
    public static async Task<Ok<LowStockResponse>> Handle(
        int? threshold,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Default threshold: 10 units (Phase 1 hardcoded threshold)
        var minStock = threshold ?? 10;

        // Query all ProductInventory snapshots where AvailableQuantity < threshold
        var lowStockItems = await session.Query<Management.ProductInventory>()
            .Where(i => i.AvailableQuantity < minStock)
            .OrderBy(i => i.AvailableQuantity)
            .ThenBy(i => i.Sku)
            .Select(i => new LowStockItem(
                i.Sku,
                i.WarehouseId,
                i.AvailableQuantity,
                i.ReservedQuantity,
                i.TotalOnHand))
            .ToListAsync(ct);

        return TypedResults.Ok(new LowStockResponse(
            lowStockItems.Count,
            lowStockItems));
    }
}
