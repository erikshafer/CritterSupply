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
        var customerId = Guid.Empty; // Stub - TODO: query Orders BC for CustomerId in future cycle

        var deliveryFailed = new ShipmentDeliveryFailed(
            message.ShipmentId,
            message.OrderId,
            customerId,
            message.Reason,
            message.FailedAt);

        return deliveryFailed.ToWebSocketGroup($"customer:{customerId}");
    }
}
