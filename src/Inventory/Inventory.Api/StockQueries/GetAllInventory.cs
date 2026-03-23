using Inventory.Management;
using Marten;
using Marten.Pagination;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Inventory.Api.StockQueries;

/// <summary>
/// HTTP endpoint for listing all inventory.
/// Used by Backoffice for warehouse admin browse/search workflows.
/// </summary>
public static class GetAllInventory
{
    /// <summary>
    /// Lists all inventory with optional pagination.
    /// Returns all products that have been registered in the inventory system.
    /// </summary>
    [WolverineGet("/api/inventory")]
    [Authorize(Policy = "WarehouseClerk")]
    public static async Task<IReadOnlyList<InventoryListItem>> Handle(
        IDocumentSession session,
        int page = 1,
        int pageSize = 100,
        CancellationToken ct = default)
    {
        // Query all ProductInventory documents (not event streams)
        var inventoryList = await session.Query<ProductInventory>()
            .OrderBy(p => p.Sku)
            .ToPagedListAsync(page, pageSize, ct);

        return inventoryList.Select(inv => new InventoryListItem(
            inv.Sku,
            inv.Sku, // ProductName fallback to SKU (Product Catalog owns names)
            inv.AvailableQuantity,
            inv.ReservedQuantity,
            inv.TotalOnHand
        )).ToList();
    }
}

/// <summary>
/// Inventory list item DTO
/// </summary>
public sealed record InventoryListItem(
    string Sku,
    string ProductName,
    int AvailableQuantity,
    int ReservedQuantity,
    int TotalQuantity);
