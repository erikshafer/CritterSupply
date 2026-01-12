using Marten;
using Messages.Contracts.Orders;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using IntegrationMessages = Messages.Contracts.Inventory;

namespace Inventory.Management;

/// <summary>
/// Wolverine handler for ReservationReleaseRequested integration messages from Orders BC.
/// Orders orchestrates when to release a reservation (compensation flows).
/// </summary>
public static class ReservationReleaseRequestedHandler
{
    public static async Task<ProductInventory?> Load(
        ReservationReleaseRequested message,
        IDocumentSession session,
        CancellationToken ct)
    {
        return await session.Query<ProductInventory>()
            .FirstOrDefaultAsync(i => i.Reservations.ContainsKey(message.ReservationId), ct);
    }

    public static ProblemDetails Before(
        ReservationReleaseRequested message,
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
        ReservationReleaseRequested message,
        ProductInventory inventory,
        IDocumentSession session)
    {
        var releasedAt = DateTimeOffset.UtcNow;

        var quantity = inventory.Reservations[message.ReservationId];

        var domainEvent = new ReservationReleased(message.ReservationId, quantity, message.Reason, releasedAt);

        session.Events.Append(inventory.Id, domainEvent);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.ReservationReleased(
            message.OrderId,
            inventory.Id,
            message.ReservationId,
            inventory.Sku,
            inventory.WarehouseId,
            quantity,
            message.Reason,
            releasedAt));

        return outgoing;
    }
}
