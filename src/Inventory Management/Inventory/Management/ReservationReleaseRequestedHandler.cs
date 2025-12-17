using Marten;
using Messages.Contracts.Orders;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Inventory.Management;

/// <summary>
/// Wolverine handler for ReservationReleaseRequested integration messages from Orders BC.
/// Orders orchestrates when to release a reservation (compensation flows).
/// </summary>
public static class ReservationReleaseRequestedHandler
{
    /// <summary>
    /// Validates that the reservation exists and can be released.
    /// </summary>
    public static async Task<ProblemDetails> Before(
        ReservationReleaseRequested message,
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
    /// Handles a ReservationReleaseRequested message by releasing the reservation.
    /// </summary>
    public static async Task<object> Handle(
        ReservationReleaseRequested message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // Load the inventory aggregate
        var inventory = await session.Query<ProductInventory>()
            .FirstAsync(i => i.Reservations.ContainsKey(message.ReservationId), cancellationToken);

        // Release reservation (pure function)
        var (updatedInventory, domainEvent, integrationMessage) = inventory.Release(
            message.ReservationId,
            message.Reason);

        // Persist events to Marten event store
        session.Events.Append(inventory.Id, updatedInventory.PendingEvents.ToArray());

        return integrationMessage;
    }
}
