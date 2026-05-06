using Fulfillment.Routing;
using Fulfillment.WorkOrders;
using Marten;
using Wolverine;
using IntegrationMessages = Messages.Contracts.Fulfillment;

namespace Fulfillment.Shipments;

/// <summary>
/// Integration handler for FulfillmentRequested from Orders BC.
/// Creates the Shipment stream, invokes the routing engine for FC assignment,
/// creates a WorkOrder at the assigned FC, and emits one
/// <see cref="IntegrationMessages.StockReservationRequested"/> per line item to
/// Inventory BC at the routing-informed warehouse (M43.0 — Slice 12 retirement).
/// Choreography pattern: Fulfillment autonomously reacts to create a shipment
/// and drives Inventory's reservation flow.
/// </summary>
public static class FulfillmentRequestedHandler
{
    public static async Task<OutgoingMessages> Handle(
        IntegrationMessages.FulfillmentRequested message,
        IFulfillmentRoutingEngine routingEngine,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // Map integration message types to domain event types
        var shippingAddress = new ShippingAddress(
            message.ShippingAddress.AddressLine1,
            message.ShippingAddress.AddressLine2,
            message.ShippingAddress.City,
            message.ShippingAddress.StateProvince,
            message.ShippingAddress.PostalCode,
            message.ShippingAddress.Country);

        var lineItems = message.LineItems
            .Select(item => new FulfillmentLineItem(item.Sku, item.Quantity))
            .ToList();

        // Create a deterministic UUID v5 from the OrderId to ensure idempotency.
        var shipmentId = Shipment.StreamId(message.OrderId);

        // Idempotency guard: if the stream already exists (at-least-once delivery duplicate),
        // skip to avoid ExistingStreamIdCollisionException AND avoid re-emitting
        // StockReservationRequested for an already-routed order.
        var existingState = await session.Events.FetchStreamStateAsync(shipmentId, cancellationToken);
        if (existingState is not null)
            return new OutgoingMessages();

        // Create the domain event
        var fulfillmentRequested = new FulfillmentRequested(
            message.OrderId,
            message.CustomerId,
            shippingAddress,
            lineItems,
            message.ShippingMethod,
            message.RequestedAt);

        // Route to FC
        var fc = await routingEngine.SelectFulfillmentCenterAsync(
            shippingAddress, lineItems, cancellationToken);

        var fcAssigned = new FulfillmentCenterAssigned(fc, DateTimeOffset.UtcNow);

        // Start the Shipment stream with both events
        session.Events.StartStream<Shipment>(shipmentId, fulfillmentRequested, fcAssigned);

        // Create WorkOrder stream at the assigned FC
        var workOrderId = WorkOrder.StreamId(shipmentId, fc);
        var workOrderLineItems = lineItems
            .Select(item => new WorkOrderLineItem(item.Sku, item.Quantity))
            .ToList();

        var workOrderCreated = new WorkOrderCreated(
            workOrderId,
            shipmentId,
            fc,
            workOrderLineItems,
            DateTimeOffset.UtcNow);

        session.Events.StartStream<WorkOrder>(workOrderId, workOrderCreated);

        // Slice 37: Check for hazmat items and flag if needed
        HazmatPolicy.CheckAndApply(workOrderId, workOrderLineItems, session);

        // M43.0 — Slice 12: drive Inventory's routing-aware reservation flow.
        // One StockReservationRequested per line item at the routing-informed FC.
        // Aggregating duplicate SKUs is the responsibility of the upstream Order
        // (line items are already de-duplicated per SKU in OrderDecider.Start).
        var outgoing = new OutgoingMessages();
        foreach (var item in lineItems)
        {
            outgoing.Add(new IntegrationMessages.StockReservationRequested(
                message.OrderId,
                item.Sku,
                fc,
                Guid.CreateVersion7(),
                item.Quantity));
        }

        return outgoing;
    }
}
