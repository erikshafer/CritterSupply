using Marten;
using Messages.Contracts.Orders;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using IntegrationMessages = Messages.Contracts.Inventory;

namespace Inventory.Management;

/// <summary>
/// Wolverine handler for ReservationCommitRequested integration messages from Orders BC.
/// Orders orchestrates when to commit a reservation (after payment succeeds).
/// </summary>
public static class ReservationCommitRequestedHandler
{
    public static async Task<ProductInventory?> Load(
        ReservationCommitRequested message,
        IDocumentSession session,
        CancellationToken ct)
    {
        return await session.Query<ProductInventory>()
            .FirstOrDefaultAsync(i => i.Reservations.ContainsKey(message.ReservationId), ct);
    }

    public static ProblemDetails Before(
        ReservationCommitRequested message,
        ProductInventory? inventory)
    {
        if (inventory is null)
            return new ProblemDetails
            {
                Detail = $"No inventory found with reservation {message.ReservationId}",
                Status = 404
            };

        return WolverineContinue.NoProblems;
    }

    public static OutgoingMessages Handle(
        ReservationCommitRequested message,
        ProductInventory inventory,
        IDocumentSession session)
    {
        var committedAt = DateTimeOffset.UtcNow;

        var quantity = inventory.Reservations[message.ReservationId];

        var domainEvent = new ReservationCommitted(message.ReservationId, committedAt);

        session.Events.Append(inventory.Id, domainEvent);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.ReservationCommitted(
            message.OrderId,
            inventory.Id,
            message.ReservationId,
            inventory.Sku,
            inventory.WarehouseId,
            quantity,
            committedAt));

        return outgoing;
    }
}
