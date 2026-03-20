using Backoffice.Clients;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Backoffice.Api.Commands.Inventory;

/// <summary>
/// BFF proxy endpoint for receiving inbound stock shipments.
/// Warehouse clerks use this when new inventory arrives from suppliers.
/// </summary>
public static class ReceiveStockProxy
{
    /// <summary>
    /// Receive inbound stock shipment
    /// POST /api/inventory/{sku}/receive
    /// </summary>
    [WolverinePost("/api/inventory/{sku}/receive")]
    [Authorize(Policy = "WarehouseClerk")]
    public static async Task<ReceiveStockResultDto?> Handle(
        string sku,
        ReceiveStockRequest request,
        IInventoryClient client)
    {
        return await client.ReceiveInboundStockAsync(
            sku,
            request.Quantity,
            request.Source);
    }
}

/// <summary>
/// Request DTO for receiving inbound stock
/// </summary>
public sealed record ReceiveStockRequest(
    int Quantity,
    string Source);
