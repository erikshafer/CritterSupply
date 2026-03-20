using Backoffice.Clients;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Backoffice.Api.Commands.Inventory;

/// <summary>
/// BFF proxy endpoint for adjusting inventory.
/// Warehouse clerks use this for cycle counts, corrections, damage write-offs.
/// </summary>
public static class AdjustInventoryProxy
{
    /// <summary>
    /// Adjust inventory quantity (positive or negative)
    /// POST /api/inventory/{sku}/adjust
    /// </summary>
    [WolverinePost("/api/inventory/{sku}/adjust")]
    [Authorize(Policy = "WarehouseClerk")]
    public static async Task<AdjustInventoryResultDto?> Handle(
        string sku,
        AdjustInventoryRequest request,
        IInventoryClient client)
    {
        return await client.AdjustInventoryAsync(
            sku,
            request.AdjustmentQuantity,
            request.Reason,
            request.AdjustedBy);
    }
}

/// <summary>
/// Request DTO for adjusting inventory
/// </summary>
public sealed record AdjustInventoryRequest(
    int AdjustmentQuantity,
    string Reason,
    string AdjustedBy);
