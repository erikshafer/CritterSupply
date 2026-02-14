using Messages.Contracts.Payments;

namespace Storefront.Notifications;

/// <summary>
/// Handles PaymentAuthorized integration message from Payments BC.
/// Broadcasts OrderStatusChanged SSE event to customer's UI.
/// </summary>
public static class PaymentAuthorizedHandler
{
    public static async Task Handle(
        PaymentAuthorized message,
        IEventBroadcaster broadcaster,
        CancellationToken ct)
    {
        // TODO: Query Orders BC to get CustomerId for the order
        // For now, using a stub CustomerId (in real implementation, need to fetch from Orders BC)
        var customerId = Guid.Empty; // Stub - would query Orders BC for order.CustomerId

        var sseEvent = new OrderStatusChanged(
            message.OrderId,
            customerId,
            "PaymentAuthorized",
            message.AuthorizedAt);

        await broadcaster.BroadcastAsync(customerId, sseEvent, ct);
    }
}
