using Backoffice.Clients;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Backoffice.Api.Queries.Inventory;

/// <summary>
/// BFF proxy endpoint for listing all inventory.
/// Warehouse clerks use this to browse inventory levels across all products.
/// </summary>
public static class GetInventoryList
{
    /// <summary>
    /// List all inventory with optional pagination
    /// GET /api/inventory
    /// </summary>
    [WolverineGet("/api/inventory")]
    [Authorize(Policy = "WarehouseClerk")]
    public static async Task<IReadOnlyList<InventoryListItemDto>> Handle(
        IInventoryClient client,
        int? page = null,
        int? pageSize = null)
    {
        return await client.ListInventoryAsync(page, pageSize);
    }
}
