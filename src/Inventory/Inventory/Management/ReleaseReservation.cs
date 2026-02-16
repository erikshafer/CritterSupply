using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using IntegrationMessages = Messages.Contracts.Inventory;

namespace Inventory.Management;

public sealed record ReleaseReservation(
    Guid InventoryId,
    Guid ReservationId,
    string Reason)
{
    public class ReleaseReservationValidator : AbstractValidator<ReleaseReservation>
    {
        public ReleaseReservationValidator()
        {
            RuleFor(x => x.InventoryId).NotEmpty();
            RuleFor(x => x.ReservationId).NotEmpty();
            RuleFor(x => x.Reason).NotEmpty().MaximumLength(256);
        }
    }
}

public static class ReleaseReservationHandler
{
    public static async Task<ProductInventory?> Load(
        ReleaseReservation command,
        IDocumentSession session,
        CancellationToken ct)
    {
        return await session.LoadAsync<ProductInventory>(command.InventoryId, ct);
    }

    public static ProblemDetails Before(
        ReleaseReservation command,
        ProductInventory? inventory)
    {
        if (inventory is null)
            return new ProblemDetails
            {
                Detail = $"Inventory {command.InventoryId} not found",
                Status = 404
            };

        if (!inventory.Reservations.ContainsKey(command.ReservationId))
            return new ProblemDetails
            {
                Detail = $"Reservation {command.ReservationId} not found in inventory {command.InventoryId}",
                Status = 404
            };

        return WolverineContinue.NoProblems;
    }

    public static OutgoingMessages Handle(
        ReleaseReservation command,
        ProductInventory inventory,
        IDocumentSession session)
    {
        var releasedAt = DateTimeOffset.UtcNow;

        var quantity = inventory.Reservations[command.ReservationId];
        var orderId = inventory.ReservationOrderIds[command.ReservationId];

        var domainEvent = new ReservationReleased(command.ReservationId, quantity, command.Reason, releasedAt);

        session.Events.Append(inventory.Id, domainEvent);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.ReservationReleased(
            orderId,
            inventory.Id,
            command.ReservationId,
            inventory.Sku,
            inventory.WarehouseId,
            quantity,
            command.Reason,
            releasedAt));

        return outgoing;
    }
}
