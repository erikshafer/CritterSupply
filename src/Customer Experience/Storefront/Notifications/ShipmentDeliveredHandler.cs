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
        var customerId = Guid.Empty; // Stub - TODO: query Orders BC for CustomerId in future cycle

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
