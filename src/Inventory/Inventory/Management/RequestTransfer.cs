using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Inventory.Management;

// ---------------------------------------------------------------------------
// Slice 25: RequestTransfer — initiates an inter-warehouse stock transfer
// ---------------------------------------------------------------------------

public sealed record RequestTransfer(
    string Sku,
    string SourceWarehouseId,
    string DestinationWarehouseId,
    int Quantity,
    string RequestedBy)
{
    public sealed class Validator : AbstractValidator<RequestTransfer>
    {
        public Validator()
        {
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
            RuleFor(x => x.SourceWarehouseId).NotEmpty().MaximumLength(50);
            RuleFor(x => x.DestinationWarehouseId).NotEmpty().MaximumLength(50);
            RuleFor(x => x.Quantity).GreaterThan(0);
            RuleFor(x => x.RequestedBy).NotEmpty().MaximumLength(100);
            RuleFor(x => x.SourceWarehouseId)
                .NotEqual(x => x.DestinationWarehouseId)
                .WithMessage("Source and destination warehouses must be different");
        }
    }
}

/// <summary>
/// Handles RequestTransfer by creating a new InventoryTransfer stream (Guid.CreateVersion7())
/// and appending StockTransferredOut to the source ProductInventory stream.
/// Multi-stream handler following BackorderCreatedHandler pattern.
/// </summary>
public static class RequestTransferHandler
{
    public static async Task<ProductInventory?> Load(
        RequestTransfer command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var sourceId = InventoryStreamId.Compute(command.Sku, command.SourceWarehouseId);
        return await session.LoadAsync<ProductInventory>(sourceId, ct);
    }

    public static ProblemDetails Before(
        RequestTransfer command,
        ProductInventory? inventory)
    {
        if (inventory is null)
            return new ProblemDetails
            {
                Detail = $"No inventory found for SKU {command.Sku} at source warehouse {command.SourceWarehouseId}",
                Status = 404
            };

        if (command.Quantity > inventory.AvailableQuantity)
            return new ProblemDetails
            {
                Detail = $"Insufficient stock: requested {command.Quantity} but only {inventory.AvailableQuantity} available at {command.SourceWarehouseId}",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    public static OutgoingMessages Handle(
        RequestTransfer command,
        ProductInventory inventory,
        IDocumentSession session)
    {
        var now = DateTimeOffset.UtcNow;
        var transferId = Guid.CreateVersion7();
        var outgoing = new OutgoingMessages();

        // 1. Create the InventoryTransfer stream
        session.Events.StartStream<InventoryTransfer>(transferId,
            new TransferRequested(
                transferId,
                command.Sku,
                command.SourceWarehouseId,
                command.DestinationWarehouseId,
                command.Quantity,
                command.RequestedBy,
                now));

        // 2. Deduct from source ProductInventory
        session.Events.Append(inventory.Id,
            new StockTransferredOut(
                inventory.Sku,
                inventory.WarehouseId,
                transferId,
                command.Quantity,
                now));

        // Inline low stock check after transfer-out
        var newAvailable = inventory.AvailableQuantity - command.Quantity;
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
