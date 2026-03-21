using Inventory.Management;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    /// Dispatches through Wolverine handler to ensure proper event publication.
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

        // Dispatch command through Wolverine to ensure transactional outbox and integration events
        var result = await bus.InvokeAsync<object>(
            new AdjustInventory(
                inventoryId,
                request.AdjustmentQuantity,
                request.Reason,
                request.AdjustedBy),
            ct);

        // Wolverine handler returns ProblemDetails for validation failures
        if (result is ProblemDetails problem)
        {
            return problem.Status == 404
                ? Results.NotFound(new { Error = problem.Detail })
                : Results.BadRequest(new { Error = problem.Detail });
        }

        // On success, reload the aggregate to get fresh state
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId, ct);

        if (inventory is null)
        {
            return Results.Problem("Inventory not found after successful adjustment");
        }

        return Results.Ok(new AdjustInventoryResult(
            inventory.Id,
            inventory.Sku,
            inventory.WarehouseId,
            inventory.AvailableQuantity));
    }
}
