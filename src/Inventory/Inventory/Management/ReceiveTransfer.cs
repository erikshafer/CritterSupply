using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Inventory.Management;

// ---------------------------------------------------------------------------
// Slices 27 + 29: ReceiveTransfer — receives a transfer at the destination warehouse.
// If received quantity < shipped quantity, produces TransferShortReceived +
// StockDiscrepancyFound (Slice 29).
// ---------------------------------------------------------------------------

public sealed record ReceiveTransfer(
    Guid TransferId,
    int ReceivedQuantity,
    string ReceivedBy)
{
    public sealed class Validator : AbstractValidator<ReceiveTransfer>
    {
        public Validator()
        {
            RuleFor(x => x.TransferId).NotEmpty();
            RuleFor(x => x.ReceivedQuantity).GreaterThanOrEqualTo(0);
            RuleFor(x => x.ReceivedBy).NotEmpty().MaximumLength(100);
        }
    }
}

public static class ReceiveTransferHandler
{
    public static async Task<InventoryTransfer?> Load(
        ReceiveTransfer command,
        IDocumentSession session,
        CancellationToken ct)
    {
        return await session.LoadAsync<InventoryTransfer>(command.TransferId, ct);
    }

    public static ProblemDetails Before(
        ReceiveTransfer command,
        InventoryTransfer? transfer)
    {
        if (transfer is null)
            return new ProblemDetails
            {
                Detail = $"Transfer {command.TransferId} not found",
                Status = 404
            };

        if (transfer.Status != TransferStatus.Shipped)
            return new ProblemDetails
            {
                Detail = $"Transfer {command.TransferId} cannot be received — current status is {transfer.Status}",
                Status = 409
            };

        if (command.ReceivedQuantity > transfer.Quantity)
            return new ProblemDetails
            {
                Detail = $"Received quantity ({command.ReceivedQuantity}) exceeds shipped quantity ({transfer.Quantity})",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    public static OutgoingMessages Handle(
        ReceiveTransfer command,
        InventoryTransfer transfer,
        IDocumentSession session)
    {
        var now = DateTimeOffset.UtcNow;
        var outgoing = new OutgoingMessages();
        var destinationId = InventoryStreamId.Compute(transfer.Sku, transfer.DestinationWarehouseId);
        var isShort = command.ReceivedQuantity < transfer.Quantity;

        if (isShort)
        {
            // Slice 29: short receipt
            var shortQty = transfer.Quantity - command.ReceivedQuantity;

            session.Events.Append(transfer.Id,
                new TransferShortReceived(
                    transfer.Id,
                    transfer.Quantity,
                    command.ReceivedQuantity,
                    shortQty,
                    command.ReceivedBy,
                    now));

            // StockDiscrepancyFound on destination — surfaces in AlertFeedView
            session.Events.Append(destinationId,
                new StockDiscrepancyFound(
                    transfer.Sku,
                    transfer.DestinationWarehouseId,
                    transfer.Quantity,
                    command.ReceivedQuantity,
                    DiscrepancyType.ShortTransfer,
                    $"Transfer {transfer.Id} short received: shipped {transfer.Quantity}, received {command.ReceivedQuantity}",
                    now));
        }
        else
        {
            // Full receipt
            session.Events.Append(transfer.Id,
                new TransferReceived(
                    transfer.Id,
                    command.ReceivedQuantity,
                    command.ReceivedBy,
                    now));
        }

        // Add stock to destination ProductInventory (received quantity, not shipped quantity)
        if (command.ReceivedQuantity > 0)
        {
            session.Events.Append(destinationId,
                new StockTransferredIn(
                    transfer.Sku,
                    transfer.DestinationWarehouseId,
                    transfer.Id,
                    command.ReceivedQuantity,
                    now));
        }

        return outgoing;
    }
}
