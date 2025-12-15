using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Inventory.Management;

/// <summary>
/// Wolverine handler for CommitReservation commands.
/// Converts soft reservation to hard allocation.
/// </summary>
public static class CommitReservationHandler
{
    /// <summary>
    /// Validates that the inventory and reservation exist before committing.
    /// </summary>
    public static async Task<ProblemDetails> Before(
        CommitReservation command,
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

        if (!inventory.Reservations.ContainsKey(command.ReservationId))
        {
            return new ProblemDetails
            {
                Detail = $"Reservation {command.ReservationId} not found in inventory {command.InventoryId}",
                Status = 404
            };
        }

        return WolverineContinue.NoProblems;
    }

    /// <summary>
    /// Handles a CommitReservation command by converting reservation to committed allocation.
    /// </summary>
    public static async Task<object> Handle(
        CommitReservation command,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // Load the inventory aggregate
        var inventory = await session.LoadAsync<ProductInventory>(command.InventoryId, cancellationToken);

        // Commit reservation (pure function)
        var (updatedInventory, domainEvent, integrationMessage) = inventory!.Commit(command.ReservationId);

        // Persist events to Marten event store
        session.Events.Append(inventory.Id, updatedInventory.PendingEvents.ToArray());

        return integrationMessage;
    }
}
