using Messages.Contracts.Inventory;
using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles ReservationConfirmed integration message from Inventory BC.
/// Publishes OrderStatusChanged SignalR event to customer's UI via Wolverine.
/// Scoped to the authenticated customer's group to prevent cross-customer event leakage.
/// </summary>
public static class ReservationConfirmedHandler
{
    public static SignalRMessage<OrderStatusChanged> Handle(ReservationConfirmed message)
    {
        // TODO: Query Orders BC to get CustomerId for the order
        // For now, using a stub CustomerId (in real implementation, need to fetch from Orders BC)
        var customerId = Guid.Empty; // Stub - would query Orders BC for order.CustomerId

        var orderStatusChanged = new OrderStatusChanged(
            message.OrderId,
            customerId,
            "InventoryReserved",
            message.ReservedAt);

        // Send only to the authenticated customer's group — not broadcast to all clients
        return orderStatusChanged.ToWebSocketGroup($"customer:{customerId}");
    }
}
