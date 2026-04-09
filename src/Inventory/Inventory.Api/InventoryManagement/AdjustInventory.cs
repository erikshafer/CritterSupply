using FluentValidation;
using Inventory.Management;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine;
using Wolverine.Http;

namespace Inventory.Api.InventoryManagement;

/// <summary>
/// Request DTO for adjusting inventory quantities.
/// </summary>
public sealed record AdjustInventoryRequest(
    int AdjustmentQuantity,
    string Reason,
    string AdjustedBy);

/// <summary>
/// Response DTO for inventory adjustment operations.
/// </summary>
public sealed record AdjustInventoryResult(
    Guid InventoryId,
    string Sku,
    string WarehouseId,
    int NewAvailableQuantity);

/// <summary>
/// Validator for AdjustInventoryRequest.
/// Ensures non-zero adjustment quantities and non-empty audit trail fields.
/// </summary>
public sealed class AdjustInventoryRequestValidator : AbstractValidator<AdjustInventoryRequest>
{
    public AdjustInventoryRequestValidator()
    {
        RuleFor(x => x.AdjustmentQuantity)
            .NotEqual(0)
            .WithMessage("Adjustment quantity must be non-zero");

        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("Reason is required")
            .MaximumLength(500)
            .WithMessage("Reason cannot exceed 500 characters");

        RuleFor(x => x.AdjustedBy)
            .NotEmpty()
            .WithMessage("AdjustedBy is required")
            .MaximumLength(100)
            .WithMessage("AdjustedBy cannot exceed 100 characters");
    }
}

/// <summary>
/// HTTP endpoint for manually adjusting inventory quantities.
/// Used by warehouse clerks for cycle counts, corrections, damage write-offs, etc.
/// </summary>
public static class AdjustInventoryEndpoint
{
    /// <summary>
    /// Adjusts inventory quantity (positive or negative).
    /// Validates manually, then appends domain event and returns integration messages
    /// via OutgoingMessages. Wolverine's auto-transaction handles persistence.
    /// </summary>
    [WolverinePost("/api/inventory/{sku}/adjust")]
    [Authorize(Policy = "WarehouseClerk")]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        string sku,
        AdjustInventoryRequest request,
        IDocumentSession session,
        CancellationToken ct)
    {
        var outgoing = new OutgoingMessages();

        // Inventory uses SKU + WarehouseId as composite key
        // For simplicity, assume "main" warehouse for now
        var warehouseId = "main";
        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);

        // Load inventory
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId, ct);

        if (inventory is null)
        {
            return (Results.NotFound(new { Error = $"Inventory for SKU '{sku}' not found" }), outgoing);
        }

        // Validate: Check if negative adjustment would result in negative available quantity
        if (request.AdjustmentQuantity < 0 &&
            inventory.AvailableQuantity + request.AdjustmentQuantity < 0)
        {
            return (Results.BadRequest(new
            {
                Error = $"Cannot adjust by {request.AdjustmentQuantity}. Available quantity is {inventory.AvailableQuantity}"
            }), outgoing);
        }

        var previousQuantity = inventory.AvailableQuantity;
        var newQuantity = previousQuantity + request.AdjustmentQuantity;
        var adjustedAt = DateTimeOffset.UtcNow;

        // Append domain event — Wolverine's auto-transaction handles SaveChangesAsync
        var domainEvent = new InventoryAdjusted(
            inventory.Sku,
            inventory.WarehouseId,
            request.AdjustmentQuantity,
            request.Reason,
            request.AdjustedBy,
            adjustedAt);

        session.Events.Append(inventoryId, domainEvent);

        // Integration messages via OutgoingMessages (replaces bus.PublishAsync)
        outgoing.Add(new Messages.Contracts.Inventory.InventoryAdjusted(
            inventory.Sku,
            inventory.WarehouseId,
            request.AdjustmentQuantity,
            newQuantity,
            adjustedAt));

        // Check if low stock threshold crossed downward
        if (LowStockPolicy.CrossedThresholdDownward(previousQuantity, newQuantity))
        {
            var breachedEvent = new LowStockThresholdBreached(
                inventory.Sku,
                inventory.WarehouseId,
                previousQuantity,
                newQuantity,
                LowStockPolicy.DefaultThreshold,
                adjustedAt);

            session.Events.Append(inventoryId, breachedEvent);

            outgoing.Add(new Messages.Contracts.Inventory.LowStockDetected(
                inventory.Sku,
                inventory.WarehouseId,
                newQuantity,
                LowStockPolicy.DefaultThreshold,
                adjustedAt));
        }

        return (Results.Ok(new AdjustInventoryResult(
            inventory.Id,
            inventory.Sku,
            inventory.WarehouseId,
            newQuantity)), outgoing);
    }
}
