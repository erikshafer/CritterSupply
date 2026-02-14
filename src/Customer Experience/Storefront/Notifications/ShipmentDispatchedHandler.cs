using Messages.Contracts.Fulfillment;

namespace Storefront.Notifications;

/// <summary>
/// Handles ShipmentDispatched integration message from Fulfillment BC.
/// Broadcasts ShipmentStatusChanged SSE event to customer's UI.
/// </summary>
public static class ShipmentDispatchedHandler
{
    public static async Task Handle(
        ShipmentDispatched message,
        IEventBroadcaster broadcaster,
        CancellationToken ct)
    {
        // TODO: Query Orders BC to get CustomerId for the order
        // For now, using a stub CustomerId (in real implementation, need to fetch from Orders BC)
        var customerId = Guid.Empty; // Stub - would query Orders BC for order.CustomerId

        var sseEvent = new ShipmentStatusChanged(
            message.ShipmentId,
            message.OrderId,
            customerId,
            "Dispatched",
            message.TrackingNumber,
            message.DispatchedAt);

        await broadcaster.BroadcastAsync(customerId, sseEvent, ct);
    }
}
