using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using IntegrationMessages = Messages.Contracts.Inventory;

namespace Inventory.Management;

/// <summary>
/// Command to manually adjust inventory quantity (positive or negative).
/// Used for cycle counts, damage write-offs, theft, corrections, etc.
/// </summary>
public sealed record AdjustInventory(
    Guid InventoryId,
    int AdjustmentQuantity,
    string Reason,
    string AdjustedBy)
{
    public class AdjustInventoryValidator : AbstractValidator<AdjustInventory>
    {
        public AdjustInventoryValidator()
        {
            RuleFor(x => x.InventoryId).NotEmpty();
            RuleFor(x => x.AdjustmentQuantity).NotEqual(0)
                .WithMessage("Adjustment quantity must be non-zero");
            RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
            RuleFor(x => x.AdjustedBy).NotEmpty().MaximumLength(100);
        }
    }
}

public static class AdjustInventoryHandler
{
    // Hardcoded low stock threshold (Phase 1 - matches GetLowStock query default)
    private const int LowStockThreshold = 10;

    public static async Task<ProductInventory?> Load(
        AdjustInventory command,
        IDocumentSession session,
        CancellationToken ct)
    {
        return await session.LoadAsync<ProductInventory>(command.InventoryId, ct);
    }

    public static ProblemDetails Before(
        AdjustInventory command,
        ProductInventory? inventory)
    {
        if (inventory is null)
            return new ProblemDetails
            {
                Detail = $"Inventory {command.InventoryId} not found",
                Status = 404
            };

        // Check if negative adjustment would result in negative available quantity
        if (command.AdjustmentQuantity < 0 &&
            inventory.AvailableQuantity + command.AdjustmentQuantity < 0)
        {
            return new ProblemDetails
            {
                Detail = $"Cannot adjust by {command.AdjustmentQuantity}. Available quantity is {inventory.AvailableQuantity}",
                Status = 400
            };
        }

        return WolverineContinue.NoProblems;
    }

    public static OutgoingMessages Handle(
        AdjustInventory command,
        ProductInventory inventory,
        IDocumentSession session)
    {
        var adjustedAt = DateTimeOffset.UtcNow;

        var domainEvent = new InventoryAdjusted(
            command.AdjustmentQuantity,
            command.Reason,
            command.AdjustedBy,
            adjustedAt);

        session.Events.Append(inventory.Id, domainEvent);

        var outgoing = new OutgoingMessages();

        // Calculate new quantity after adjustment
        var newQuantity = inventory.AvailableQuantity + command.AdjustmentQuantity;

        // Publish InventoryAdjusted integration message
        outgoing.Add(new IntegrationMessages.InventoryAdjusted(
            inventory.Sku,
            inventory.WarehouseId,
            command.AdjustmentQuantity,
            newQuantity,
            adjustedAt));

        // Check if low stock threshold crossed
        var wasAboveThreshold = inventory.AvailableQuantity >= LowStockThreshold;
        var isNowBelowThreshold = newQuantity < LowStockThreshold;

        if (wasAboveThreshold && isNowBelowThreshold)
        {
            // Crossed threshold downward - publish LowStockDetected
            outgoing.Add(new IntegrationMessages.LowStockDetected(
                inventory.Sku,
                inventory.WarehouseId,
                newQuantity,
                LowStockThreshold,
                adjustedAt));
        }

        return outgoing;
    }
}
