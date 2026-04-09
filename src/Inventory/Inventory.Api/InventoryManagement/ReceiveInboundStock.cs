using FluentValidation;
using Inventory.Management;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Inventory.Api.InventoryManagement;

/// <summary>
/// Request DTO for receiving inbound stock shipments.
/// </summary>
public sealed record ReceiveInboundStockRequest(
    int Quantity,
    string Source);

/// <summary>
/// Response DTO for inbound stock receipt operations.
/// </summary>
public sealed record ReceiveInboundStockResult(
    Guid InventoryId,
    string Sku,
    string WarehouseId,
    int NewAvailableQuantity);

/// <summary>
/// Validator for ReceiveInboundStockRequest.
/// Ensures positive quantities and non-empty source field.
/// </summary>
public sealed class ReceiveInboundStockRequestValidator : AbstractValidator<ReceiveInboundStockRequest>
{
    public ReceiveInboundStockRequestValidator()
    {
        RuleFor(x => x.Quantity)
            .GreaterThan(0)
            .WithMessage("Quantity must be greater than zero");

        RuleFor(x => x.Source)
            .NotEmpty()
            .WithMessage("Source is required")
            .MaximumLength(200)
            .WithMessage("Source cannot exceed 200 characters");
    }
}

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
        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);

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
            inventory.Sku,
            inventory.WarehouseId,
            request.Source,
            null,
            request.Quantity,
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
