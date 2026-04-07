using Messages.Contracts.Fulfillment;
using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles ShipmentLostInTransit integration message from Fulfillment BC.
/// Publishes ShipmentStatusChanged SignalR event — notifies customer of lost shipment investigation.
/// Stub handler: the storefront tracking page will render investigation status and reshipment info.
/// </summary>
public static class ShipmentLostInTransitHandler
{
    public static SignalRMessage<ShipmentStatusChanged> Handle(ShipmentLostInTransit message)
    {
        // TODO: Query Orders BC for the actual CustomerId so SignalR group targeting works.
        var customerId = Guid.Empty;

        var shipmentStatusChanged = new ShipmentStatusChanged(
            message.ShipmentId,
            message.OrderId,
            customerId,
            "Lost in transit — under investigation",
            null,
            message.DetectedAt);

        return shipmentStatusChanged.ToWebSocketGroup($"customer:{customerId}");
    }
}
