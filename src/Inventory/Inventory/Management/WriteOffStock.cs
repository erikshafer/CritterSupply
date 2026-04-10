using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Inventory.Management;

/// <summary>
/// Slice 24: Permanently writes off stock (regulatory recall, disposal, etc.).
/// Appends StockWrittenOff (audit) + InventoryAdjusted (negative quantity correction).
/// More destructive than damage recording — requires OperationsManager policy.
/// </summary>
public sealed record WriteOffStock(
    Guid InventoryId,
    int Quantity,
    string Reason,
    string WrittenOffBy)
{
    public sealed class Validator : AbstractValidator<WriteOffStock>
    {
        public Validator()
        {
            RuleFor(x => x.InventoryId).NotEmpty();
            RuleFor(x => x.Quantity).GreaterThan(0);
            RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
            RuleFor(x => x.WrittenOffBy).NotEmpty().MaximumLength(100);
        }
    }
}

public static class WriteOffStockHandler
{
    public static async Task<ProductInventory?> Load(
        WriteOffStock command,
        IDocumentSession session,
        CancellationToken ct)
    {
        return await session.LoadAsync<ProductInventory>(command.InventoryId, ct);
    }

    public static ProblemDetails Before(
        WriteOffStock command,
        ProductInventory? inventory)
    {
        if (inventory is null)
            return new ProblemDetails
            {
                Detail = $"Inventory {command.InventoryId} not found",
                Status = 404
            };

        if (command.Quantity > inventory.AvailableQuantity)
            return new ProblemDetails
            {
                Detail = $"Cannot write off {command.Quantity} units — only {inventory.AvailableQuantity} available",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    public static OutgoingMessages Handle(
        WriteOffStock command,
        ProductInventory inventory,
        IDocumentSession session)
    {
        var now = DateTimeOffset.UtcNow;

        session.Events.Append(inventory.Id,
            new StockWrittenOff(
                inventory.Sku,
                inventory.WarehouseId,
                command.Quantity,
                command.Reason,
                command.WrittenOffBy,
                now));

        session.Events.Append(inventory.Id,
            new InventoryAdjusted(
                inventory.Sku,
                inventory.WarehouseId,
                -command.Quantity,
                $"Write-off: {command.Reason}",
                command.WrittenOffBy,
                now));

        var outgoing = new OutgoingMessages();
        var newAvailable = inventory.AvailableQuantity - command.Quantity;

        // Low stock alert — inline check (same pattern as S1)
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

        return outgoing;
    }
}
