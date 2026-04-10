using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Inventory.Management;

/// <summary>
/// Slice 23: Records physical damage to inventory items.
/// Appends DamageRecorded (audit) + InventoryAdjusted (negative quantity correction).
/// Delegates to LowStockPolicy inline if threshold crossed.
/// </summary>
public sealed record RecordDamage(
    Guid InventoryId,
    int Quantity,
    string DamageReason,
    string RecordedBy)
{
    public sealed class Validator : AbstractValidator<RecordDamage>
    {
        public Validator()
        {
            RuleFor(x => x.InventoryId).NotEmpty();
            RuleFor(x => x.Quantity).GreaterThan(0);
            RuleFor(x => x.DamageReason).NotEmpty().MaximumLength(500);
            RuleFor(x => x.RecordedBy).NotEmpty().MaximumLength(100);
        }
    }
}

public static class RecordDamageHandler
{
    public static async Task<ProductInventory?> Load(
        RecordDamage command,
        IDocumentSession session,
        CancellationToken ct)
    {
        return await session.LoadAsync<ProductInventory>(command.InventoryId, ct);
    }

    public static ProblemDetails Before(
        RecordDamage command,
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
                Detail = $"Cannot record damage of {command.Quantity} units — only {inventory.AvailableQuantity} available",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    public static OutgoingMessages Handle(
        RecordDamage command,
        ProductInventory inventory,
        IDocumentSession session)
    {
        var now = DateTimeOffset.UtcNow;

        session.Events.Append(inventory.Id,
            new DamageRecorded(
                inventory.Sku,
                inventory.WarehouseId,
                command.Quantity,
                command.DamageReason,
                command.RecordedBy,
                now));

        session.Events.Append(inventory.Id,
            new InventoryAdjusted(
                inventory.Sku,
                inventory.WarehouseId,
                -command.Quantity,
                $"Damage: {command.DamageReason}",
                command.RecordedBy,
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
