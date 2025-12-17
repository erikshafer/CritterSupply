using Marten;
using Messages.Contracts.Orders;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Inventory.Management;

/// <summary>
/// Wolverine handler for ReservationCommitRequested integration messages from Orders BC.
/// Orders orchestrates when to commit a reservation (after payment succeeds).
/// </summary>
public static class ReservationCommitRequestedHandler
{
    /// <summary>
    /// Validates that the reservation exists and can be committed.
    /// </summary>
    public static async Task<ProblemDetails> Before(
        ReservationCommitRequested message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // Find inventory by reservation (we need to query for it since we don't have InventoryId)
        var inventory = await session.Query<ProductInventory>()
            .FirstOrDefaultAsync(i => i.Reservations.ContainsKey(message.ReservationId), cancellationToken);

        if (inventory is null)
        {
            return new ProblemDetails
            {
                Detail = $"No inventory found with reservation {message.ReservationId}",
                Status = 404
            };
        }

        return WolverineContinue.NoProblems;
    }

    /// <summary>
    /// Handles a ReservationCommitRequested message by committing the reservation.
    /// </summary>
    public static async Task<object> Handle(
        ReservationCommitRequested message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // Load the inventory aggregate
        var inventory = await session.Query<ProductInventory>()
            .FirstAsync(i => i.Reservations.ContainsKey(message.ReservationId), cancellationToken);

        // Commit reservation (pure function)
        var (updatedInventory, domainEvent, integrationMessage) = inventory.Commit(message.ReservationId);

        // Persist events to Marten event store
        session.Events.Append(inventory.Id, updatedInventory.PendingEvents.ToArray());

        return integrationMessage;
    }
}
