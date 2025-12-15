using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Inventory.Management;

/// <summary>
/// Wolverine handler for ReceiveStock commands.
/// Adds new stock from supplier or warehouse transfer.
/// </summary>
public static class ReceiveStockHandler
{
    /// <summary>
    /// Validates that the inventory exists before receiving stock.
    /// </summary>
    public static async Task<ProblemDetails> Before(
        ReceiveStock command,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var inventory = await session.LoadAsync<ProductInventory>(command.InventoryId, cancellationToken);

        if (inventory is null)
        {
            return new ProblemDetails
            {
                Detail = $"Inventory {command.InventoryId} not found",
                Status = 404
            };
        }

        return WolverineContinue.NoProblems;
    }

    /// <summary>
    /// Handles a ReceiveStock command by adding quantity to available stock.
    /// </summary>
    public static async Task Handle(
        ReceiveStock command,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // Load the inventory aggregate
        var inventory = await session.LoadAsync<ProductInventory>(command.InventoryId, cancellationToken);

        // Receive stock (pure function)
        var (updatedInventory, domainEvent) = inventory!.ReceiveStock(
            command.Quantity,
            command.Source);

        // Persist events to Marten event store
        session.Events.Append(inventory.Id, updatedInventory.PendingEvents.ToArray());
    }
}
