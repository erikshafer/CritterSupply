using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

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
    public const int LowStockThreshold = 10;

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
        var previousQuantity = inventory.AvailableQuantity;
        var newQuantity = previousQuantity + command.AdjustmentQuantity;

        var domainEvent = new InventoryAdjusted(
            inventory.Sku,
            inventory.WarehouseId,
            command.AdjustmentQuantity,
            command.Reason,
            command.AdjustedBy,
            adjustedAt);

        session.Events.Append(inventory.Id, domainEvent);

        var outgoing = new OutgoingMessages();

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

            session.Events.Append(inventory.Id, breachedEvent);

            outgoing.Add(new Messages.Contracts.Inventory.LowStockDetected(
                inventory.Sku,
                inventory.WarehouseId,
                newQuantity,
                LowStockPolicy.DefaultThreshold,
                adjustedAt));
        }

        return outgoing;
    }

    /// <summary>
    /// Helper to determine if low stock threshold was crossed downward.
    /// Used by endpoint to conditionally publish LowStockDetected integration message.
    /// </summary>
    [Obsolete("Use LowStockPolicy.CrossedThresholdDownward() instead.")]
    public static bool CrossedLowStockThreshold(int previousQuantity, int newQuantity)
    {
        return LowStockPolicy.CrossedThresholdDownward(previousQuantity, newQuantity);
    }
}
