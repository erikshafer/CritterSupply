using Inventory.Management;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Inventory.Api.Commands;

/// <summary>
/// HTTP endpoint for manually adjusting inventory quantities.
/// Used by warehouse clerks for cycle counts, corrections, damage write-offs, etc.
/// </summary>
public static class AdjustInventoryEndpoint
{
    /// <summary>
    /// Adjusts inventory quantity (positive or negative).
    /// </summary>
    [WolverinePost("/api/inventory/{sku}/adjust")]
    [Authorize(Policy = "WarehouseClerk")]
    public static async Task<IResult> Handle(
        string sku,
        AdjustInventoryRequest request,
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

        // Validate negative adjustment doesn't result in negative quantity
        if (request.AdjustmentQuantity < 0 &&
            inventory.AvailableQuantity + request.AdjustmentQuantity < 0)
        {
            return Results.BadRequest(new
            {
                Error = $"Cannot adjust by {request.AdjustmentQuantity}. Available quantity is {inventory.AvailableQuantity}"
            });
        }

        // Append event
        var domainEvent = new InventoryAdjusted(
            request.AdjustmentQuantity,
            request.Reason,
            request.AdjustedBy,
            DateTimeOffset.UtcNow);

        session.Events.Append(inventoryId, domainEvent);
        await session.SaveChangesAsync(ct);

        // Reload to get updated state
        inventory = await session.LoadAsync<ProductInventory>(inventoryId, ct);

        return Results.Ok(new AdjustInventoryResult(
            inventory!.Id,
            inventory.Sku,
            inventory.WarehouseId,
            inventory.AvailableQuantity));
    }
}
