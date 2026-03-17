using Inventory.Management;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Inventory.Api.Commands;

/// <summary>
/// HTTP endpoint for receiving inbound stock shipments.
/// Used by warehouse clerks when new inventory arrives from suppliers.
/// </summary>
public static class ReceiveInboundStockEndpoint
{
    /// <summary>
    /// Records receipt of new stock from a supplier or transfer.
    /// </summary>
    [WolverinePost("/api/inventory/{sku}/receive")]
    [Authorize(Policy = "WarehouseClerk")]
    public static async Task<IResult> Handle(
        string sku,
        ReceiveInboundStockRequest request,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Inventory uses SKU + WarehouseId as composite key
        // For simplicity, assume "main" warehouse for now
        var warehouseId = "main";
        var inventoryId = ProductInventory.CombinedGuid(sku, warehouseId);

        // Load the aggregate
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId, ct);

        if (inventory is null)
        {
            return Results.NotFound(new
            {
                Error = $"Inventory for SKU '{sku}' not found"
            });
        }

        // Validate quantity
        if (request.Quantity <= 0)
        {
            return Results.BadRequest(new
            {
                Error = "Quantity must be greater than zero"
            });
        }

        if (string.IsNullOrWhiteSpace(request.Source))
        {
            return Results.BadRequest(new
            {
                Error = "Source is required"
            });
        }

        // Append event
        var domainEvent = new StockReceived(
            request.Quantity,
            request.Source,
            DateTimeOffset.UtcNow);

        session.Events.Append(inventoryId, domainEvent);
        await session.SaveChangesAsync(ct);

        // Reload to get updated state
        inventory = await session.LoadAsync<ProductInventory>(inventoryId, ct);

        return Results.Ok(new ReceiveInboundStockResult(
            inventory!.Id,
            inventory.Sku,
            inventory.WarehouseId,
            inventory.AvailableQuantity));
    }
}
