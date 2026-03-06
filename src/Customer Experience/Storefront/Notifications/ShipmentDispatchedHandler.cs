using Messages.Contracts.Fulfillment;
using Storefront.RealTime;

namespace Storefront.Notifications;

/// <summary>
/// Handles ShipmentDispatched integration message from Fulfillment BC.
/// Publishes ShipmentStatusChanged SignalR event to customer's UI via Wolverine.
/// </summary>
public static class ShipmentDispatchedHandler
{
    public static ShipmentStatusChanged Handle(ShipmentDispatched message)
    {
        // TODO: Query Orders BC to get CustomerId for the order
        // For now, using a stub CustomerId (in real implementation, need to fetch from Orders BC)
        var customerId = Guid.Empty; // Stub - would query Orders BC for order.CustomerId

        // Return SignalR message — Wolverine routes to hub based on IStorefrontWebSocketMessage
        return new ShipmentStatusChanged(
            message.ShipmentId,
            message.OrderId,
            customerId,
            "Shipped",
            message.TrackingNumber,
            message.DispatchedAt);
    }
}
