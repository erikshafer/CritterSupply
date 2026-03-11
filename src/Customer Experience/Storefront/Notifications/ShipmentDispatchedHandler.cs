using Messages.Contracts.Fulfillment;
using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles ShipmentDispatched integration message from Fulfillment BC.
/// Publishes ShipmentStatusChanged SignalR event to customer's UI via Wolverine.
/// Scoped to the authenticated customer's group to prevent cross-customer event leakage.
/// </summary>
public static class ShipmentDispatchedHandler
{
    public static SignalRMessage<ShipmentStatusChanged> Handle(ShipmentDispatched message)
    {
        // TODO: Query Orders BC to get CustomerId for the order
        // For now, using a stub CustomerId (in real implementation, need to fetch from Orders BC)
        var customerId = Guid.Empty; // Stub - would query Orders BC for order.CustomerId

        var shipmentStatusChanged = new ShipmentStatusChanged(
            message.ShipmentId,
            message.OrderId,
            customerId,
            "Dispatched",
            message.TrackingNumber,
            message.DispatchedAt);

        // Send only to the authenticated customer's group — not broadcast to all clients
        return shipmentStatusChanged.ToWebSocketGroup($"customer:{customerId}");
    }
}
