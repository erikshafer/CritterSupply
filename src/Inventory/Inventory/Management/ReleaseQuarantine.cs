using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Inventory.Management;

// ---------------------------------------------------------------------------
// Slice 34: ReleaseQuarantine — releases quarantined stock back to available
// ---------------------------------------------------------------------------

public sealed record ReleaseQuarantine(
    string Sku,
    string WarehouseId,
    Guid QuarantineId,
    int Quantity,
    string ReleasedBy)
{
    public sealed class Validator : AbstractValidator<ReleaseQuarantine>
    {
        public Validator()
        {
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
            RuleFor(x => x.WarehouseId).NotEmpty().MaximumLength(50);
            RuleFor(x => x.QuarantineId).NotEmpty();
            RuleFor(x => x.Quantity).GreaterThan(0);
            RuleFor(x => x.ReleasedBy).NotEmpty().MaximumLength(100);
        }
    }
}

public static class ReleaseQuarantineHandler
{
    public static async Task<ProductInventory?> Load(
        ReleaseQuarantine command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var inventoryId = InventoryStreamId.Compute(command.Sku, command.WarehouseId);
        return await session.LoadAsync<ProductInventory>(inventoryId, ct);
    }

    public static ProblemDetails Before(
        ReleaseQuarantine command,
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
                Detail = $"Cannot release {command.Quantity} units — only {inventory.QuarantinedQuantity} in quarantine",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    public static OutgoingMessages Handle(
        ReleaseQuarantine command,
        ProductInventory inventory,
        IDocumentSession session)
    {
        var now = DateTimeOffset.UtcNow;
        var outgoing = new OutgoingMessages();

        session.Events.Append(inventory.Id,
            new QuarantineReleased(
                inventory.Sku,
                inventory.WarehouseId,
                command.QuarantineId,
                command.Quantity,
                command.ReleasedBy,
                now));

        // Positive InventoryAdjusted to restore AvailableQuantity
        session.Events.Append(inventory.Id,
            new InventoryAdjusted(
                inventory.Sku,
                inventory.WarehouseId,
                command.Quantity,
                $"Quarantine release: stock returned to available",
                command.ReleasedBy,
                now));

        return outgoing;
    }
}
