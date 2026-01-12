using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using IntegrationMessages = Messages.Contracts.Inventory;

namespace Inventory.Management;

public sealed record ReserveStock(
    Guid OrderId,
    string Sku,
    string WarehouseId,
    Guid ReservationId,
    int Quantity)
{
    public Guid InventoryId => ProductInventory.CombinedGuid(Sku, WarehouseId);

    public class ReserveStockValidator : AbstractValidator<ReserveStock>
    {
        public ReserveStockValidator()
        {
            RuleFor(x => x.OrderId).NotEmpty();
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
            RuleFor(x => x.WarehouseId).NotEmpty().MaximumLength(50);
            RuleFor(x => x.ReservationId).NotEmpty();
            RuleFor(x => x.Quantity).GreaterThan(0);
        }
    }
}

public static class ReserveStockHandler
{
    public static async Task<ProductInventory?> Load(
        ReserveStock command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var inventoryId = command.InventoryId;
        return await session.LoadAsync<ProductInventory>(inventoryId, ct);
    }

    public static ProblemDetails Before(
        ReserveStock command,
        ProductInventory? inventory)
    {
        if (inventory is null)
            return new ProblemDetails
            {
                Detail = $"No inventory found for SKU {command.Sku} at warehouse {command.WarehouseId}",
                Status = 404
            };

        if (inventory.AvailableQuantity < command.Quantity)
            return new ProblemDetails
            {
                Detail = $"Insufficient stock for SKU {command.Sku}. Requested: {command.Quantity}, Available: {inventory.AvailableQuantity}",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    public static OutgoingMessages Handle(
        ReserveStock command,
        ProductInventory inventory,
        IDocumentSession session)
    {
        var reservedAt = DateTimeOffset.UtcNow;

        var domainEvent = new StockReserved(
            command.OrderId,
            command.ReservationId,
            command.Quantity,
            reservedAt);

        session.Events.Append(inventory.Id, domainEvent);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.ReservationConfirmed(
            command.OrderId,
            inventory.Id,
            command.ReservationId,
            command.Sku,
            command.WarehouseId,
            command.Quantity,
            reservedAt));

        return outgoing;
    }
}
