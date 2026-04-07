using Messages.Contracts.Fulfillment;
using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles ShipmentHandedToCarrier integration message from Fulfillment BC.
/// Publishes ShipmentStatusChanged SignalR event to customer's UI via Wolverine.
/// Replaces ShipmentDispatchedHandler (retired in M41.0 S5).
/// </summary>
public static class ShipmentHandedToCarrierHandler
{
    public static SignalRMessage<ShipmentStatusChanged> Handle(ShipmentHandedToCarrier message)
    {
        // TODO: Query Orders BC for the actual CustomerId so SignalR group targeting works.
        // Currently sends to group "customer:00000000-..." which no real customer is subscribed to.
        // Tracked for future cycle — requires an HTTP call to Orders API or a read-model lookup.
        var customerId = Guid.Empty;

        var shipmentStatusChanged = new ShipmentStatusChanged(
            message.ShipmentId,
            message.OrderId,
            customerId,
            "Shipped",
            message.TrackingNumber,
            message.HandedAt);

        return shipmentStatusChanged.ToWebSocketGroup($"customer:{customerId}");
    }
}
