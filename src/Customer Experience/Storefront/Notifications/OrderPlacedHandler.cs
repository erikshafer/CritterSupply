using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles Orders.OrderPlaced integration message and publishes SignalR update via Wolverine.
/// Scoped to the authenticated customer's group to prevent cross-customer event leakage.
/// </summary>
public static class OrderPlacedHandler
{
    public static SignalRMessage<OrderStatusChanged> Handle(Messages.Contracts.Orders.OrderPlaced message)
    {
        var orderStatusChanged = new OrderStatusChanged(
            message.OrderId,
            message.CustomerId,
            "Placed",
            DateTimeOffset.UtcNow);

        // Send only to the authenticated customer's group — not broadcast to all clients
        return orderStatusChanged.ToWebSocketGroup($"customer:{message.CustomerId}");
    }
}
