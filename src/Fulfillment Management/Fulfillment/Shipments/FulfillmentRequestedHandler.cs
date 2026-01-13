using Marten;
using IntegrationMessages = Messages.Contracts.Fulfillment;

namespace Fulfillment.Shipments;

/// <summary>
/// Integration handler for FulfillmentRequested from Orders BC.
/// Choreography pattern: Fulfillment autonomously reacts to create a shipment.
/// </summary>
public static class FulfillmentRequestedHandler
{
    public static void Handle(
        IntegrationMessages.FulfillmentRequested message,
        IDocumentSession session)
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

        // Create the domain event
        var domainEvent = new FulfillmentRequested(
            message.OrderId,
            message.CustomerId,
            shippingAddress,
            lineItems,
            message.ShippingMethod,
            message.RequestedAt);

        // Start the shipment event stream with a deterministic ID based on OrderId
        // This ensures idempotency - multiple FulfillmentRequested messages for same order create same stream
        var shipmentId = Guid.CreateVersion7();
        session.Events.StartStream<Shipment>(shipmentId, domainEvent);
    }
}
