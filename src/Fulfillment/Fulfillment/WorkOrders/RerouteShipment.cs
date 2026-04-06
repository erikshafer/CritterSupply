using FluentValidation;
using Fulfillment.Shipments;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Fulfillment.WorkOrders;

/// <summary>
/// Slice 18: Short pick — no stock at FC, reroute to another FC.
/// Crosses both aggregates: closes original WorkOrder, reroutes Shipment, creates new WorkOrder.
/// </summary>
public sealed record RerouteShipment(
    Guid WorkOrderId,
    string NewFulfillmentCenterId)
{
    public sealed class Validator : AbstractValidator<RerouteShipment>
    {
        public Validator()
        {
            RuleFor(x => x.WorkOrderId).NotEmpty();
            RuleFor(x => x.NewFulfillmentCenterId).NotEmpty().MaximumLength(50);
        }
    }
}

/// <summary>
/// Handler for rerouting a shipment to a different fulfillment center.
/// 1. Closes the original WorkOrder (PickExceptionRaised)
/// 2. Reroutes the Shipment (ShipmentRerouted)
/// 3. Creates a new WorkOrder at the new FC
/// </summary>
public static class RerouteShipmentHandler
{
    public static async Task<ProblemDetails> Before(
        RerouteShipment command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var wo = await session.LoadAsync<WorkOrder>(command.WorkOrderId, ct);
        if (wo is null)
            return new ProblemDetails { Detail = "Work order not found", Status = 404 };

        if (wo.Status != WorkOrderStatus.ShortPickPending)
            return new ProblemDetails
            {
                Detail = $"Cannot reroute for work order in {wo.Status} status. Must be ShortPickPending.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static async Task Handle(
        RerouteShipment command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var wo = await session.LoadAsync<WorkOrder>(command.WorkOrderId, ct);
        if (wo is null) return;

        var now = DateTimeOffset.UtcNow;

        // 1. Close the original WorkOrder
        session.Events.Append(command.WorkOrderId,
            new PickExceptionRaised($"Rerouted to {command.NewFulfillmentCenterId}", now));

        // 2. Reroute the Shipment
        session.Events.Append(wo.ShipmentId,
            new ShipmentRerouted(wo.FulfillmentCenterId, command.NewFulfillmentCenterId, now));

        // 3. Create a new WorkOrder at the new FC (re-uses WorkOrderCreated path)
        var newWorkOrderId = WorkOrder.StreamId(wo.ShipmentId, command.NewFulfillmentCenterId);
        var workOrderLineItems = wo.LineItems.ToList();

        var workOrderCreated = new WorkOrderCreated(
            newWorkOrderId,
            wo.ShipmentId,
            command.NewFulfillmentCenterId,
            workOrderLineItems,
            now);

        session.Events.StartStream<WorkOrder>(newWorkOrderId, workOrderCreated);
    }
}
