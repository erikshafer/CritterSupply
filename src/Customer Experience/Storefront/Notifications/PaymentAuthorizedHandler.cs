using Messages.Contracts.Payments;
using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles PaymentAuthorized integration message from Payments BC.
/// Publishes OrderStatusChanged SignalR event to customer's UI via Wolverine.
/// Scoped to the authenticated customer's group to prevent cross-customer event leakage.
/// </summary>
public static class PaymentAuthorizedHandler
{
    public static SignalRMessage<OrderStatusChanged> Handle(PaymentAuthorized message)
    {
        // TODO: Query Orders BC to get CustomerId for the order
        // For now, using a stub CustomerId (in real implementation, need to fetch from Orders BC)
        var customerId = Guid.Empty; // Stub - would query Orders BC for order.CustomerId

        var orderStatusChanged = new OrderStatusChanged(
            message.OrderId,
            customerId,
            "PaymentAuthorized",
            message.AuthorizedAt);

        // Send only to the authenticated customer's group — not broadcast to all clients
        return orderStatusChanged.ToWebSocketGroup($"customer:{customerId}");
    }
}
