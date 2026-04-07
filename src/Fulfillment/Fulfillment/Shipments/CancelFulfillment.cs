using Wolverine.Http;
using Fulfillment.WorkOrders;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using IntegrationMessages = Messages.Contracts.Fulfillment;

namespace Fulfillment.Shipments;

/// <summary>
/// Slice 35: Fulfillment cancellation command.
/// Cancels a shipment before it has been handed to the carrier.
/// Also cancels the active WorkOrder if one exists.
/// </summary>
public sealed record CancelFulfillment(
    Guid ShipmentId,
    string Reason);

/// <summary>
/// Slice 35: Fulfillment cancellation handler.
/// Valid cancellation window: Pending, Assigned, Rerouted, Backordered.
/// Appends FulfillmentCancelled to Shipment stream and WorkOrderCancelled to WorkOrder stream.
/// </summary>
public static class CancelFulfillmentHandler
{
    private static readonly HashSet<ShipmentStatus> CancellableStatuses =
    [
        ShipmentStatus.Pending,
        ShipmentStatus.Assigned,
        ShipmentStatus.Rerouted,
        ShipmentStatus.Backordered
    ];

    public static async Task<ProblemDetails> Before(
        CancelFulfillment command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null)
            return new ProblemDetails { Detail = "Shipment not found", Status = 404 };

        if (!CancellableStatuses.Contains(shipment.Status))
            return new ProblemDetails
            {
                Detail = $"Cannot cancel fulfillment for shipment in {shipment.Status} status. " +
                         "Valid only for: Pending, Assigned, Rerouted, Backordered.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static async Task Handle(
        CancelFulfillment command,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null) return;

        var now = DateTimeOffset.UtcNow;

        // Cancel the shipment
        session.Events.Append(command.ShipmentId,
            new FulfillmentCancelled(now, command.Reason));

        // Cancel the active WorkOrder if one exists
        if (shipment.AssignedFulfillmentCenter is not null)
        {
            var workOrderId = WorkOrder.StreamId(command.ShipmentId, shipment.AssignedFulfillmentCenter);
            var wo = await session.LoadAsync<WorkOrder>(workOrderId, ct);
            if (wo is not null && wo.Status is not (WorkOrderStatus.PackingCompleted
                or WorkOrderStatus.PickExceptionClosed or WorkOrderStatus.Cancelled))
            {
                session.Events.Append(workOrderId,
                    new WorkOrderCancelled(command.Reason, now));
            }
        }

        // Publish FulfillmentCancelled integration event
        await bus.PublishAsync(new IntegrationMessages.FulfillmentCancelled(
            shipment.OrderId,
            command.ShipmentId,
            command.Reason,
            now));
    }
}
