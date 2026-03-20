using Backoffice.Clients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine.Http;

namespace Backoffice.Api.Queries;

/// <summary>
/// Query endpoint for warehouse clerks to view stock level for a specific SKU.
/// Used for inventory visibility and fulfillment workflows.
/// </summary>
public static class GetStockLevel
{
    /// <summary>
    /// GET /api/backoffice/inventory/{sku}
    /// Returns stock level details for a SKU (available, reserved, total quantities).
    /// </summary>
    [WolverineGet("/api/inventory/{sku}")]
    [Authorize(Policy = "WarehouseClerk")]
    public static async Task<Results<Ok<StockLevelDto>, NotFound>> Get(
        string sku,
        IInventoryClient inventoryClient,
        CancellationToken ct)
    {
        var stockLevel = await inventoryClient.GetStockLevelAsync(sku, ct);

        return stockLevel is not null
            ? TypedResults.Ok(stockLevel)
            : TypedResults.NotFound();
    }
}
