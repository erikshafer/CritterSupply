using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Inventory.Management;

public sealed record ReceiveStock(
    Guid InventoryId,
    int Quantity,
    string Source)
{
    public class ReceiveStockValidator : AbstractValidator<ReceiveStock>
    {
        public ReceiveStockValidator()
        {
            RuleFor(x => x.InventoryId).NotEmpty();
            RuleFor(x => x.Quantity).GreaterThan(0);
            RuleFor(x => x.Source).NotEmpty().MaximumLength(100);
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

    public static void Handle(
        ReceiveStock command,
        ProductInventory inventory,
        IDocumentSession session)
    {
        var receivedAt = DateTimeOffset.UtcNow;

        var domainEvent = new StockReceived(command.Quantity, command.Source, receivedAt);

        session.Events.Append(inventory.Id, domainEvent);
    }
}
