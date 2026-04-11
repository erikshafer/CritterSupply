using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Inventory.Management;

// ---------------------------------------------------------------------------
// Slice 26: ShipTransfer — marks a transfer as physically shipped
// ---------------------------------------------------------------------------

public sealed record ShipTransfer(
    Guid TransferId,
    string ShippedBy)
{
    public sealed class Validator : AbstractValidator<ShipTransfer>
    {
        public Validator()
        {
            RuleFor(x => x.TransferId).NotEmpty();
            RuleFor(x => x.ShippedBy).NotEmpty().MaximumLength(100);
        }
    }
}

public static class ShipTransferHandler
{
    public static async Task<InventoryTransfer?> Load(
        ShipTransfer command,
        IDocumentSession session,
        CancellationToken ct)
    {
        return await session.LoadAsync<InventoryTransfer>(command.TransferId, ct);
    }

    public static ProblemDetails Before(
        ShipTransfer command,
        InventoryTransfer? transfer)
    {
        if (transfer is null)
            return new ProblemDetails
            {
                Detail = $"Transfer {command.TransferId} not found",
                Status = 404
            };

        if (transfer.Status != TransferStatus.Requested)
            return new ProblemDetails
            {
                Detail = $"Transfer {command.TransferId} cannot be shipped — current status is {transfer.Status}",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    public static void Handle(
        ShipTransfer command,
        InventoryTransfer transfer,
        IDocumentSession session)
    {
        session.Events.Append(transfer.Id,
            new TransferShipped(
                transfer.Id,
                command.ShippedBy,
                DateTimeOffset.UtcNow));
    }
}
