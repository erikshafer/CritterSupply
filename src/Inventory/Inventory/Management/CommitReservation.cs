using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using IntegrationMessages = Messages.Contracts.Inventory;

namespace Inventory.Management;

public sealed record CommitReservation(
    Guid InventoryId,
    Guid ReservationId)
{
    public class CommitReservationValidator : AbstractValidator<CommitReservation>
    {
        public CommitReservationValidator()
        {
            RuleFor(x => x.InventoryId).NotEmpty();
            RuleFor(x => x.ReservationId).NotEmpty();
        }
    }
}

public static class CommitReservationHandler
{
    public static async Task<ProductInventory?> Load(
        CommitReservation command,
        IDocumentSession session,
        CancellationToken ct)
    {
        return await session.LoadAsync<ProductInventory>(command.InventoryId, ct);
    }

    public static ProblemDetails Before(
        CommitReservation command,
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
        CommitReservation command,
        ProductInventory inventory,
        IDocumentSession session)
    {
        var committedAt = DateTimeOffset.UtcNow;

        var quantity = inventory.Reservations[command.ReservationId];
        var orderId = inventory.ReservationOrderIds[command.ReservationId];

        var domainEvent = new ReservationCommitted(command.ReservationId, committedAt);

        session.Events.Append(inventory.Id, domainEvent);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.ReservationCommitted(
            orderId,
            inventory.Id,
            command.ReservationId,
            inventory.Sku,
            inventory.WarehouseId,
            quantity,
            committedAt));

        return outgoing;
    }
}
