namespace Storefront.Notifications;

/// <summary>
/// Handles Orders.OrderPlaced integration message and broadcasts SSE update to connected clients.
/// </summary>
public static class OrderPlacedHandler
{
    public static async Task Handle(
        Messages.Contracts.Orders.OrderPlaced message,
        IEventBroadcaster broadcaster,
        CancellationToken ct)
    {
        // Compose SSE event
        var @event = new OrderStatusChanged(
            message.OrderId,
            message.CustomerId,
            "Placed",
            DateTimeOffset.UtcNow);

        // Broadcast to all connected clients for this customer
        await broadcaster.BroadcastAsync(message.CustomerId, @event, ct);
    }
}
