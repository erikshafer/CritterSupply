using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Inventory.Management;

// ---------------------------------------------------------------------------
// Slice 35: DisposeQuarantine — permanently disposes quarantined stock
// ---------------------------------------------------------------------------

public sealed record DisposeQuarantine(
    string Sku,
    string WarehouseId,
    Guid QuarantineId,
    int Quantity,
    string DisposalReason,
    string DisposedBy)
{
    public sealed class Validator : AbstractValidator<DisposeQuarantine>
    {
        public Validator()
        {
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
            RuleFor(x => x.WarehouseId).NotEmpty().MaximumLength(50);
            RuleFor(x => x.QuarantineId).NotEmpty();
            RuleFor(x => x.Quantity).GreaterThan(0);
            RuleFor(x => x.DisposalReason).NotEmpty().MaximumLength(500);
            RuleFor(x => x.DisposedBy).NotEmpty().MaximumLength(100);
        }
    }
}

public static class DisposeQuarantineHandler
{
    public static async Task<ProductInventory?> Load(
        DisposeQuarantine command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var inventoryId = InventoryStreamId.Compute(command.Sku, command.WarehouseId);
        return await session.LoadAsync<ProductInventory>(inventoryId, ct);
    }

    public static ProblemDetails Before(
        DisposeQuarantine command,
        ProductInventory? inventory)
    {
        if (inventory is null)
            return new ProblemDetails
            {
                Detail = $"No inventory found for SKU {command.Sku} at warehouse {command.WarehouseId}",
                Status = 404
            };

        if (command.Quantity > inventory.QuarantinedQuantity)
            return new ProblemDetails
            {
                Detail = $"Cannot dispose {command.Quantity} units — only {inventory.QuarantinedQuantity} in quarantine",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    public static OutgoingMessages Handle(
        DisposeQuarantine command,
        ProductInventory inventory,
        IDocumentSession session)
    {
        var now = DateTimeOffset.UtcNow;
        var outgoing = new OutgoingMessages();

        session.Events.Append(inventory.Id,
            new QuarantineDisposed(
                inventory.Sku,
                inventory.WarehouseId,
                command.QuarantineId,
                command.Quantity,
                command.DisposalReason,
                command.DisposedBy,
                now));

        // StockWrittenOff — permanent destruction, no resurrection
        session.Events.Append(inventory.Id,
            new StockWrittenOff(
                inventory.Sku,
                inventory.WarehouseId,
                command.Quantity,
                $"Quarantine disposal: {command.DisposalReason}",
                command.DisposedBy,
                now));

        // Inline low stock + replenishment checks
        // Note: AvailableQuantity was already decremented when quarantined.
        // QuarantinedQuantity is decremented here. No further availability change.
        // However, TotalOnHand is now lower — check if we need replenishment.
        if (ReplenishmentPolicy.ShouldTrigger(inventory.AvailableQuantity, inventory.HasPendingBackorders))
        {
            session.Events.Append(inventory.Id,
                new ReplenishmentTriggered(
                    inventory.Sku,
                    inventory.WarehouseId,
                    inventory.AvailableQuantity,
                    LowStockPolicy.DefaultThreshold,
                    inventory.HasPendingBackorders,
                    now));
        }

        return outgoing;
    }
}
