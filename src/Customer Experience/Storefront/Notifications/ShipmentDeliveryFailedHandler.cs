using FulfillmentContracts = Messages.Contracts.Fulfillment;
using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles ShipmentDeliveryFailed integration message from Fulfillment BC.
/// Publishes ShipmentDeliveryFailed SignalR event to customer's UI via Wolverine.
/// </summary>
public static class ShipmentDeliveryFailedHandler
{
    public static SignalRMessage<ShipmentDeliveryFailed> Handle(FulfillmentContracts.ShipmentDeliveryFailed message)
    {
        // TODO: Query Orders BC for the actual CustomerId so SignalR group targeting works.
        // Currently sends to group "customer:00000000-..." which no real customer is subscribed to.
        // Tracked for future cycle — requires an HTTP call to Orders API or a read-model lookup.
        var customerId = Guid.Empty;

        var deliveryFailed = new ShipmentDeliveryFailed(
            message.ShipmentId,
            message.OrderId,
            customerId,
            message.Reason,
            message.FailedAt);

        return deliveryFailed.ToWebSocketGroup($"customer:{customerId}");
    }
}
