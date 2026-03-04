using Storefront.RealTime;

namespace Storefront.Notifications;

/// <summary>
/// Handles Orders.OrderPlaced integration message and publishes SignalR update via Wolverine.
/// </summary>
public static class OrderPlacedHandler
{
    public static OrderStatusChanged Handle(Messages.Contracts.Orders.OrderPlaced message)
    {
        // Return SignalR message — Wolverine routes to hub based on IStorefrontWebSocketMessage
        return new OrderStatusChanged(
            message.OrderId,
            message.CustomerId,
            "Placed",
            DateTimeOffset.UtcNow);
    }
}
