using FluentValidation;
using Fulfillment.Shipments;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using IntegrationMessages = Messages.Contracts.Fulfillment;

namespace Fulfillment.WorkOrders;

/// <summary>
/// Slice 19: Short pick — no stock anywhere, create a backorder.
/// </summary>
public sealed record CreateBackorder(
    Guid WorkOrderId,
    string Reason)
{
    public sealed class Validator : AbstractValidator<CreateBackorder>
    {
        public Validator()
        {
            RuleFor(x => x.WorkOrderId).NotEmpty();
            RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        }
    }
}

/// <summary>
/// Handler for creating a backorder when no stock is available anywhere.
/// Closes the WorkOrder and transitions the Shipment to Backordered.
/// Publishes BackorderCreated integration event for the Orders saga.
/// </summary>
public static class CreateBackorderHandler
{
    public static async Task<ProblemDetails> Before(
        CreateBackorder command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var wo = await session.LoadAsync<WorkOrder>(command.WorkOrderId, ct);
        if (wo is null)
            return new ProblemDetails { Detail = "Work order not found", Status = 404 };

        if (wo.Status != WorkOrderStatus.ShortPickPending)
            return new ProblemDetails
            {
                Detail = $"Cannot create backorder for work order in {wo.Status} status. Must be ShortPickPending.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static async Task Handle(
        CreateBackorder command,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken ct)
    {
        var wo = await session.LoadAsync<WorkOrder>(command.WorkOrderId, ct);
        if (wo is null) return;

        var now = DateTimeOffset.UtcNow;

        // Close the WorkOrder
        session.Events.Append(command.WorkOrderId,
            new PickExceptionRaised($"Backordered: {command.Reason}", now));

        // Backorder the Shipment
        session.Events.Append(wo.ShipmentId,
            new Shipments.BackorderCreated(command.Reason, now));

        // Load Shipment for OrderId
        var shipment = await session.LoadAsync<Shipment>(wo.ShipmentId, ct);
        if (shipment is null) return;

        // Publish integration event
        await bus.PublishAsync(new IntegrationMessages.BackorderCreated(
            shipment.OrderId,
            wo.ShipmentId,
            command.Reason,
            now));
    }
}
