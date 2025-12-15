using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Inventory.Management;

/// <summary>
/// Wolverine handler for ReleaseReservation commands.
/// Cancels reservation and returns stock to available pool.
/// </summary>
public static class ReleaseReservationHandler
{
    /// <summary>
    /// Validates that the inventory and reservation exist before releasing.
    /// </summary>
    public static async Task<ProblemDetails> Before(
        ReleaseReservation command,
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
    /// Handles a ReleaseReservation command by returning stock to available pool.
    /// </summary>
    public static async Task<object> Handle(
        ReleaseReservation command,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // Load the inventory aggregate
        var inventory = await session.LoadAsync<ProductInventory>(command.InventoryId, cancellationToken);

        // Release reservation (pure function)
        var (updatedInventory, domainEvent, integrationMessage) = inventory!.Release(
            command.ReservationId,
            command.Reason);

        // Persist events to Marten event store
        session.Events.Append(inventory.Id, updatedInventory.PendingEvents.ToArray());

        return integrationMessage;
    }
}
