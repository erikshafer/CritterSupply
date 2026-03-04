using Messages.Contracts.Payments;
using Storefront.RealTime;

namespace Storefront.Notifications;

/// <summary>
/// Handles PaymentAuthorized integration message from Payments BC.
/// Publishes OrderStatusChanged SignalR event to customer's UI via Wolverine.
/// </summary>
public static class PaymentAuthorizedHandler
{
    public static OrderStatusChanged Handle(PaymentAuthorized message)
    {
        // TODO: Query Orders BC to get CustomerId for the order
        // For now, using a stub CustomerId (in real implementation, need to fetch from Orders BC)
        var customerId = Guid.Empty; // Stub - would query Orders BC for order.CustomerId

        // Return SignalR message — Wolverine routes to hub based on IStorefrontWebSocketMessage
        return new OrderStatusChanged(
            message.OrderId,
            customerId,
            "PaymentAuthorized",
            message.AuthorizedAt);
    }
}
