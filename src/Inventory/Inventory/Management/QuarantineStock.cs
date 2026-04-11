using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Inventory.Management;

// ---------------------------------------------------------------------------
// Slice 33: QuarantineStock — places suspect stock in quarantine
// ---------------------------------------------------------------------------

public sealed record QuarantineStock(
    string Sku,
    string WarehouseId,
    int Quantity,
    string Reason,
    string QuarantinedBy)
{
    public sealed class Validator : AbstractValidator<QuarantineStock>
    {
        public Validator()
        {
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
            RuleFor(x => x.WarehouseId).NotEmpty().MaximumLength(50);
            RuleFor(x => x.Quantity).GreaterThan(0);
            RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
            RuleFor(x => x.QuarantinedBy).NotEmpty().MaximumLength(100);
        }
    }
}

public static class QuarantineStockHandler
{
    public static async Task<ProductInventory?> Load(
        QuarantineStock command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var inventoryId = InventoryStreamId.Compute(command.Sku, command.WarehouseId);
        return await session.LoadAsync<ProductInventory>(inventoryId, ct);
    }

    public static ProblemDetails Before(
        QuarantineStock command,
        ProductInventory? inventory)
    {
        if (inventory is null)
            return new ProblemDetails
            {
                Detail = $"No inventory found for SKU {command.Sku} at warehouse {command.WarehouseId}",
                Status = 404
            };

        if (command.Quantity > inventory.AvailableQuantity)
            return new ProblemDetails
            {
                Detail = $"Cannot quarantine {command.Quantity} units — only {inventory.AvailableQuantity} available",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    public static OutgoingMessages Handle(
        QuarantineStock command,
        ProductInventory inventory,
        IDocumentSession session)
    {
        var now = DateTimeOffset.UtcNow;
        var quarantineId = Guid.CreateVersion7();
        var outgoing = new OutgoingMessages();

        session.Events.Append(inventory.Id,
            new StockQuarantined(
                inventory.Sku,
                inventory.WarehouseId,
                quarantineId,
                command.Quantity,
                command.Reason,
                command.QuarantinedBy,
                now));

        // Negative InventoryAdjusted to decrement AvailableQuantity
        session.Events.Append(inventory.Id,
            new InventoryAdjusted(
                inventory.Sku,
                inventory.WarehouseId,
                -command.Quantity,
                $"Quarantine: {command.Reason}",
                command.QuarantinedBy,
                now));

        var newAvailable = inventory.AvailableQuantity - command.Quantity;

        // Inline low stock check
        if (LowStockPolicy.CrossedThresholdDownward(inventory.AvailableQuantity, newAvailable))
        {
            session.Events.Append(inventory.Id,
                new LowStockThresholdBreached(
                    inventory.Sku,
                    inventory.WarehouseId,
                    inventory.AvailableQuantity,
                    newAvailable,
                    LowStockPolicy.DefaultThreshold,
                    now));
        }

        // Inline replenishment check
        if (ReplenishmentPolicy.ShouldTrigger(newAvailable, inventory.HasPendingBackorders))
        {
            session.Events.Append(inventory.Id,
                new ReplenishmentTriggered(
                    inventory.Sku,
                    inventory.WarehouseId,
                    newAvailable,
                    LowStockPolicy.DefaultThreshold,
                    inventory.HasPendingBackorders,
                    now));
        }

        return outgoing;
    }
}
