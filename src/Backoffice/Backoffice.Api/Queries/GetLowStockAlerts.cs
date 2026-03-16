using Backoffice.Clients;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Backoffice.Api.Queries;

/// <summary>
/// Query endpoint for warehouse clerks to view low-stock alerts.
/// Used for proactive inventory management and restocking workflows.
/// </summary>
public static class GetLowStockAlerts
{
    /// <summary>
    /// GET /api/backoffice/inventory/low-stock?threshold={n}
    /// Returns list of SKUs below threshold (defaults to BC-configured threshold).
    /// </summary>
    [WolverineGet("/api/backoffice/inventory/low-stock")]
    [Authorize(Policy = "WarehouseClerk")]
    public static async Task<IReadOnlyList<LowStockDto>> Get(
        int? threshold,
        IInventoryClient inventoryClient,
        CancellationToken ct)
    {
        return await inventoryClient.GetLowStockAsync(threshold, ct);
    }
}
