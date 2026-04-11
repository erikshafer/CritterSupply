using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Inventory.Management;

// ---------------------------------------------------------------------------
// Slice 28: CancelTransfer — cancels a transfer pre-ship and compensates
// by reversing the StockTransferredOut on the source ProductInventory.
// ---------------------------------------------------------------------------

public sealed record CancelTransfer(
    Guid TransferId,
    string Reason,
    string CancelledBy)
{
    public sealed class Validator : AbstractValidator<CancelTransfer>
    {
        public Validator()
        {
            RuleFor(x => x.TransferId).NotEmpty();
            RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
            RuleFor(x => x.CancelledBy).NotEmpty().MaximumLength(100);
        }
    }
}

public static class CancelTransferHandler
{
    public static async Task<InventoryTransfer?> Load(
        CancelTransfer command,
        IDocumentSession session,
        CancellationToken ct)
    {
        return await session.LoadAsync<InventoryTransfer>(command.TransferId, ct);
    }

    public static ProblemDetails Before(
        CancelTransfer command,
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
                Detail = $"Transfer {command.TransferId} cannot be cancelled — current status is {transfer.Status}. Only pre-ship transfers can be cancelled.",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    public static OutgoingMessages Handle(
        CancelTransfer command,
        InventoryTransfer transfer,
        IDocumentSession session)
    {
        var now = DateTimeOffset.UtcNow;
        var outgoing = new OutgoingMessages();

        // Cancel the transfer
        session.Events.Append(transfer.Id,
            new TransferCancelled(
                transfer.Id,
                command.Reason,
                command.CancelledBy,
                now));

        // Compensation: reverse the StockTransferredOut by adding stock back to source
        var sourceId = InventoryStreamId.Compute(transfer.Sku, transfer.SourceWarehouseId);
        session.Events.Append(sourceId,
            new InventoryAdjusted(
                transfer.Sku,
                transfer.SourceWarehouseId,
                transfer.Quantity,
                $"Transfer cancellation: {command.Reason} (Transfer {transfer.Id})",
                command.CancelledBy,
                now));

        return outgoing;
    }
}
