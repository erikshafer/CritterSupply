using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine.Http;

namespace Inventory.Api.StockQueries;

/// <summary>
/// Response DTO for stock level queries.
/// </summary>
public sealed record StockLevelResponse(
    string Sku,
    string WarehouseId,
    int AvailableQuantity,
    int ReservedQuantity,
    int CommittedQuantity,
    int TotalOnHand);

/// <summary>
/// HTTP GET endpoint to retrieve stock level for a specific SKU.
/// Used by WarehouseClerk dashboard and OperationsManager KPIs.
/// </summary>
public sealed class GetStockLevel
{
    [WolverineGet("/api/inventory/{sku}")]
    [Authorize(Policy = "WarehouseClerk")]
    public static async Task<Results<Ok<StockLevelResponse>, NotFound>> Handle(
        string sku,
        string? warehouseId,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Default to WH-01 if not specified (Phase 1 single-warehouse constraint)
        var warehouse = warehouseId ?? "WH-01";

        // Load ProductInventory from snapshot projection
        var aggregateId = Management.InventoryStreamId.Compute(sku, warehouse);
        var inventory = await session.LoadAsync<Management.ProductInventory>(aggregateId, ct);

        if (inventory is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new StockLevelResponse(
            inventory.Sku,
            inventory.WarehouseId,
            inventory.AvailableQuantity,
            inventory.ReservedQuantity,
            inventory.CommittedQuantity,
            inventory.TotalOnHand));
    }
}
