using Inventory.Management;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine;
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
    /// Validates manually, then dispatches command for event appending and integration message publishing.
    /// </summary>
    [WolverinePost("/api/inventory/{sku}/adjust")]
    [Authorize(Policy = "WarehouseClerk")]
    public static async Task<IResult> Handle(
        string sku,
        AdjustInventoryRequest request,
        IMessageBus bus,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Inventory uses SKU + WarehouseId as composite key
        // For simplicity, assume "main" warehouse for now
        var warehouseId = "main";
        var inventoryId = ProductInventory.CombinedGuid(sku, warehouseId);

        // Load inventory
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId, ct);

        if (inventory is null)
        {
            return Results.NotFound(new { Error = $"Inventory for SKU '{sku}' not found" });
        }

        // Validate: Check if negative adjustment would result in negative available quantity
        if (request.AdjustmentQuantity < 0 &&
            inventory.AvailableQuantity + request.AdjustmentQuantity < 0)
        {
            return Results.BadRequest(new
            {
                Error = $"Cannot adjust by {request.AdjustmentQuantity}. Available quantity is {inventory.AvailableQuantity}"
            });
        }

        var previousQuantity = inventory.AvailableQuantity;
        var adjustedAt = DateTimeOffset.UtcNow;

        // Append domain event directly (don't go through handler since we already validated)
        var domainEvent = new InventoryAdjusted(
            request.AdjustmentQuantity,
            request.Reason,
            request.AdjustedBy,
            adjustedAt);

        session.Events.Append(inventoryId, domainEvent);
        await session.SaveChangesAsync(ct);

        // Reload to get fresh state after event appending
        inventory = await session.LoadAsync<ProductInventory>(inventoryId, ct);
        if (inventory is null)
        {
            return Results.Problem("Inventory not found after successful adjustment");
        }

        var newQuantity = inventory.AvailableQuantity;

        // Publish InventoryAdjusted integration message via message bus
        await bus.PublishAsync(new Messages.Contracts.Inventory.InventoryAdjusted(
            inventory.Sku,
            inventory.WarehouseId,
            request.AdjustmentQuantity,
            newQuantity,
            adjustedAt));

        // Check if low stock threshold crossed downward
        if (AdjustInventoryHandler.CrossedLowStockThreshold(previousQuantity, newQuantity))
        {
            // Publish LowStockDetected integration message
            await bus.PublishAsync(new Messages.Contracts.Inventory.LowStockDetected(
                inventory.Sku,
                inventory.WarehouseId,
                newQuantity,
                AdjustInventoryHandler.LowStockThreshold,
                adjustedAt));
        }

        return Results.Ok(new AdjustInventoryResult(
            inventory.Id,
            inventory.Sku,
            inventory.WarehouseId,
            inventory.AvailableQuantity));
    }
}
