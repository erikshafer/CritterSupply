using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Inventory.Management;

public sealed record ReceiveStock(
    Guid InventoryId,
    int Quantity,
    string SupplierId,
    string? PurchaseOrderId)
{
    public class ReceiveStockValidator : AbstractValidator<ReceiveStock>
    {
        public ReceiveStockValidator()
        {
            RuleFor(x => x.InventoryId).NotEmpty();
            RuleFor(x => x.Quantity).GreaterThan(0);
            RuleFor(x => x.SupplierId).NotEmpty().MaximumLength(100);
            RuleFor(x => x.PurchaseOrderId).MaximumLength(100);
        }
    }
}

public static class ReceiveStockHandler
{
    public static async Task<ProductInventory?> Load(
        ReceiveStock command,
        IDocumentSession session,
        CancellationToken ct)
    {
        return await session.LoadAsync<ProductInventory>(command.InventoryId, ct);
    }

    public static ProblemDetails Before(
        ReceiveStock command,
        ProductInventory? inventory)
    {
        if (inventory is null)
            return new ProblemDetails
            {
                Detail = $"Inventory {command.InventoryId} not found",
                Status = 404
            };

        return WolverineContinue.NoProblems;
    }

    public static OutgoingMessages Handle(
        ReceiveStock command,
        ProductInventory inventory,
        IDocumentSession session)
    {
        var receivedAt = DateTimeOffset.UtcNow;

        var domainEvent = new StockReceived(
            inventory.Sku,
            inventory.WarehouseId,
            command.SupplierId,
            command.PurchaseOrderId,
            command.Quantity,
            receivedAt);

        session.Events.Append(inventory.Id, domainEvent);

        var newAvailableQuantity = inventory.AvailableQuantity + command.Quantity;

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Inventory.StockReplenished(
            inventory.Sku,
            inventory.WarehouseId,
            command.Quantity,
            newAvailableQuantity,
            receivedAt));

        // BackorderPolicy: if this SKU has pending backorders, notify Fulfillment
        var backorderMessages = BackorderPolicy.CheckAndPublish(inventory, newAvailableQuantity);
        if (backorderMessages is not null)
        {
            session.Events.Append(inventory.Id,
                new BackorderCleared(inventory.Sku, inventory.WarehouseId, receivedAt));

            foreach (var msg in backorderMessages)
                outgoing.Add(msg);
        }

        return outgoing;
    }
}
