using Messages.Contracts.Fulfillment;
using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles BackorderCreated integration message from Fulfillment BC.
/// Publishes ShipmentStatusChanged SignalR event — notifies customer their order is backordered.
/// Stub handler: the storefront tracking page will render backorder status with expected timeline.
/// </summary>
public static class BackorderCreatedHandler
{
    public static SignalRMessage<ShipmentStatusChanged> Handle(BackorderCreated message)
    {
        // TODO: Query Orders BC for the actual CustomerId so SignalR group targeting works.
        var customerId = Guid.Empty;

        var shipmentStatusChanged = new ShipmentStatusChanged(
            message.ShipmentId,
            message.OrderId,
            customerId,
            "Backordered",
            null,
            message.CreatedAt);

        return shipmentStatusChanged.ToWebSocketGroup($"customer:{customerId}");
    }
}
