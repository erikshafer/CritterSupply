using Marten;
using Wolverine.Http;

namespace Fulfillment.Shipments;

/// <summary>
/// Handler for RequestFulfillment command.
/// Starts a new shipment event stream.
/// </summary>
public static class RequestFulfillmentHandler
{
    [WolverinePost("/api/fulfillment/shipments")]
    public static CreationResponse Handle(
        RequestFulfillment command,
        IDocumentSession session)
    {
        var shipmentId = Guid.CreateVersion7();

        // Create the initial event
        var @event = new FulfillmentRequested(
            command.OrderId,
            command.CustomerId,
            command.ShippingAddress,
            command.LineItems,
            command.ShippingMethod,
            DateTimeOffset.UtcNow);

        // Start the event stream - Marten will project this to a Shipment aggregate
        session.Events.StartStream<Shipment>(shipmentId, @event);

        return new CreationResponse($"/api/fulfillment/shipments/{shipmentId}");
    }
}
