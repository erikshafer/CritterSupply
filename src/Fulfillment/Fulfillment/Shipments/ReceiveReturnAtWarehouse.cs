using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Fulfillment.Shipments;

/// <summary>
/// Slice 28: Receive a returned package at the warehouse.
/// </summary>
public sealed record ReceiveReturnAtWarehouse(
    Guid ShipmentId,
    string WarehouseId)
{
    public sealed class Validator : AbstractValidator<ReceiveReturnAtWarehouse>
    {
        public Validator()
        {
            RuleFor(x => x.ShipmentId).NotEmpty();
            RuleFor(x => x.WarehouseId).NotEmpty().MaximumLength(50);
        }
    }
}

/// <summary>
/// Handler for receiving a returned package at the warehouse.
/// Transitions shipment to ReturnReceived, awaiting customer decision (reship vs. refund — P2).
/// </summary>
public static class ReceiveReturnAtWarehouseHandler
{
    public static async Task<ProblemDetails> Before(
        ReceiveReturnAtWarehouse command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null)
            return new ProblemDetails { Detail = "Shipment not found", Status = 404 };

        if (shipment.Status != ShipmentStatus.ReturningToSender)
            return new ProblemDetails
            {
                Detail = $"Cannot receive return for shipment in {shipment.Status} status. Must be ReturningToSender.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static void Handle(ReceiveReturnAtWarehouse command, IDocumentSession session)
    {
        session.Events.Append(command.ShipmentId,
            new ReturnReceivedAtWarehouse(DateTimeOffset.UtcNow, command.WarehouseId));
    }
}
