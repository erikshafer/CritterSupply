using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Inventory.Management;

/// <summary>
/// Wolverine handler for ReserveStock commands.
/// Validates availability and creates/updates inventory aggregate.
/// </summary>
public static class ReserveStockHandler
{
    /// <summary>
    /// Validates that sufficient stock is available before reservation.
    /// </summary>
    public static async Task<ProblemDetails> Before(
        ReserveStock command,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // Query for existing inventory by SKU and WarehouseId
        var inventory = await session.Query<ProductInventory>()
            .FirstOrDefaultAsync(i => i.SKU == command.SKU && i.WarehouseId == command.WarehouseId, cancellationToken);

        if (inventory is null)
        {
            return new ProblemDetails
            {
                Detail = $"No inventory found for SKU {command.SKU} at warehouse {command.WarehouseId}",
                Status = 404
            };
        }

        if (inventory.AvailableQuantity < command.Quantity)
        {
            return new ProblemDetails
            {
                Detail = $"Insufficient stock for SKU {command.SKU}. Requested: {command.Quantity}, Available: {inventory.AvailableQuantity}",
                Status = 409
            };
        }

        return WolverineContinue.NoProblems;
    }

    /// <summary>
    /// Handles a ReserveStock command by creating reservation and publishing integration message.
    /// </summary>
    public static async Task<object> Handle(
        ReserveStock command,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // Load the inventory aggregate
        var inventory = await session.Query<ProductInventory>()
            .FirstAsync(i => i.SKU == command.SKU && i.WarehouseId == command.WarehouseId, cancellationToken);

        // Reserve stock (pure function)
        var (updatedInventory, domainEvent, integrationMessage) = inventory.Reserve(
            command.ReservationId,
            command.Quantity);

        // Persist events to Marten event store
        session.Events.Append(inventory.Id, updatedInventory.PendingEvents.ToArray());

        return integrationMessage;
    }
}
