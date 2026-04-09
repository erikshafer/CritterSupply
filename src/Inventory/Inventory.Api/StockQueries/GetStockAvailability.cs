using Inventory.Management;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Inventory.Api.StockQueries;

/// <summary>
/// Response DTO for stock availability queries.
/// Returns per-warehouse breakdown for a SKU, consumed by Fulfillment's routing engine.
/// </summary>
public sealed record StockAvailabilityResponse(
    string Sku,
    IReadOnlyList<WarehouseAvailabilityItem> Warehouses,
    int TotalAvailable);

public sealed record WarehouseAvailabilityItem(string WarehouseId, int AvailableQuantity);

/// <summary>
/// HTTP query endpoint for stock availability by SKU.
/// Returns per-warehouse breakdown including zero-availability warehouses.
/// The routing engine needs the complete picture to make informed decisions.
/// </summary>
public static class GetStockAvailability
{
    [WolverineGet("/api/inventory/availability/{sku}")]
    [Authorize(Policy = "WarehouseClerk")]
    public static async Task<IResult> Handle(
        string sku,
        IDocumentSession session,
        CancellationToken ct)
    {
        var view = await session.LoadAsync<StockAvailabilityView>(sku, ct);

        if (view is null)
            return Results.Ok(new StockAvailabilityResponse(sku, [], 0));

        var warehouses = view.Warehouses
            .Select(w => new WarehouseAvailabilityItem(w.WarehouseId, w.AvailableQuantity))
            .ToList();

        return Results.Ok(new StockAvailabilityResponse(sku, warehouses, view.TotalAvailable));
    }
}
