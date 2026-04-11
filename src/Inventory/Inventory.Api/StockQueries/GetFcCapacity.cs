using Inventory.Management;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Inventory.Api.StockQueries;

/// <summary>
/// Response DTO for fulfillment center capacity queries.
/// Returns per-warehouse capacity utilization, consumed by Fulfillment's routing engine
/// for capacity-based routing decisions.
/// </summary>
public sealed record FcCapacityResponse(
    string WarehouseId,
    int SkuCount,
    int TotalAvailable,
    int TotalReserved,
    int TotalCommitted,
    int TotalPicked,
    int TotalQuarantined,
    int TotalOnHand,
    int TotalInTransitOut,
    DateTimeOffset LastUpdated);

/// <summary>
/// HTTP query endpoint for fulfillment center capacity by warehouse.
/// Slice 39 (P3): FC capacity data exposure for the routing engine.
/// </summary>
public static class GetFcCapacity
{
    [WolverineGet("/api/inventory/fc-capacity/{warehouseId}")]
    [Authorize(Policy = "WarehouseClerk")]
    public static async Task<IResult> Handle(
        string warehouseId,
        IDocumentSession session,
        CancellationToken ct)
    {
        var view = await session.LoadAsync<FulfillmentCenterCapacityView>(warehouseId, ct);

        if (view is null)
            return Results.Ok(new FcCapacityResponse(
                warehouseId, 0, 0, 0, 0, 0, 0, 0, 0, DateTimeOffset.MinValue));

        return Results.Ok(new FcCapacityResponse(
            view.Id,
            view.SkuCount,
            view.TotalAvailable,
            view.TotalReserved,
            view.TotalCommitted,
            view.TotalPicked,
            view.TotalQuarantined,
            view.TotalOnHand,
            view.TotalInTransitOut,
            view.LastUpdated));
    }
}
