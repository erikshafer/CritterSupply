using Wolverine.Http;
using Fulfillment.Routing;
using Fulfillment.WorkOrders;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using IntegrationMessages = Messages.Contracts.Fulfillment;

namespace Fulfillment.Shipments;

/// <summary>
/// Slice 30: Reshipment creation command.
/// Creates a new Shipment stream for the replacement and records ReshipmentCreated
/// on the original stream.
/// </summary>
public sealed record CreateReshipment(
    Guid OriginalShipmentId,
    string Reason);

/// <summary>
/// Slice 30: Reshipment creation handler.
/// The most architecturally significant P2 slice — dual-stream write.
/// Appends ReshipmentCreated to the original stream, then creates a new Shipment stream
/// with FulfillmentRequested (flagged as reshipment) to kick off the full P0 flow.
/// </summary>
public static class CreateReshipmentHandler
{
    public static async Task<ProblemDetails> Before(
        CreateReshipment command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.OriginalShipmentId, ct);
        if (shipment is null)
            return new ProblemDetails { Detail = "Shipment not found", Status = 404 };

        if (shipment.Status is not (ShipmentStatus.LostInTransit or ShipmentStatus.ReturnReceived
            or ShipmentStatus.Delivered or ShipmentStatus.DeliveryDisputed))
            return new ProblemDetails
            {
                Detail = $"Cannot create reshipment for shipment in {shipment.Status} status. " +
                         "Must be LostInTransit, ReturnReceived, Delivered, or DeliveryDisputed.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static async Task Handle(
        CreateReshipment command,
        IDocumentSession session,
        IFulfillmentRoutingEngine routingEngine,
        IMessageBus bus,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.OriginalShipmentId, ct);
        if (shipment is null) return;

        var now = DateTimeOffset.UtcNow;
        var newShipmentId = Guid.CreateVersion7();

        // 1. Append ReshipmentCreated to the ORIGINAL stream
        var reshipmentCreated = new ReshipmentCreated(
            newShipmentId, command.OriginalShipmentId, command.Reason, now);
        session.Events.Append(command.OriginalShipmentId, reshipmentCreated);

        // 2. Create the NEW Shipment stream with FulfillmentRequested
        var fulfillmentRequested = new FulfillmentRequested(
            shipment.OrderId,
            shipment.CustomerId,
            shipment.ShippingAddress,
            shipment.LineItems,
            shipment.ShippingMethod,
            now);

        // Route to FC
        var fc = await routingEngine.SelectFulfillmentCenterAsync(
            shipment.ShippingAddress, shipment.LineItems, ct);

        var fcAssigned = new FulfillmentCenterAssigned(fc, now);

        session.Events.StartStream<Shipment>(newShipmentId, fulfillmentRequested, fcAssigned);

        // 3. Create WorkOrder at the assigned FC for the new shipment
        var workOrderId = WorkOrder.StreamId(newShipmentId, fc);
        var workOrderLineItems = shipment.LineItems
            .Select(item => new WorkOrderLineItem(item.Sku, item.Quantity))
            .ToList();

        var workOrderCreated = new WorkOrderCreated(
            workOrderId, newShipmentId, fc, workOrderLineItems, now);
        session.Events.StartStream<WorkOrder>(workOrderId, workOrderCreated);

        // Slice 37: Check for hazmat items
        HazmatPolicy.CheckAndApply(workOrderId, workOrderLineItems, session);

        // 4. Publish ReshipmentCreated integration event
        await bus.PublishAsync(new IntegrationMessages.ReshipmentCreated(
            shipment.OrderId,
            command.OriginalShipmentId,
            newShipmentId,
            command.Reason,
            now));
    }
}
