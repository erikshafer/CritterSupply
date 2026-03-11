using Messages.Contracts.Fulfillment;
using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles ShipmentDelivered integration message from Fulfillment BC.
/// Publishes ShipmentStatusChanged SignalR event to customer's UI via Wolverine.
/// </summary>
public static class ShipmentDeliveredHandler
{
    public static SignalRMessage<ShipmentStatusChanged> Handle(ShipmentDelivered message)
    {
        // TODO: Query Orders BC for the actual CustomerId so SignalR group targeting works.
        // Currently sends to group "customer:00000000-..." which no real customer is subscribed to.
        // Tracked for future cycle — requires an HTTP call to Orders API or a read-model lookup.
        var customerId = Guid.Empty;

        var shipmentStatusChanged = new ShipmentStatusChanged(
            message.ShipmentId,
            message.OrderId,
            customerId,
            "Delivered",
            null,
            message.DeliveredAt);

        return shipmentStatusChanged.ToWebSocketGroup($"customer:{customerId}");
    }
}
