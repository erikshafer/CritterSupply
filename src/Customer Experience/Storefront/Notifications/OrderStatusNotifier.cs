namespace Storefront.Notifications;

/// <summary>
/// Handles order-related integration messages and pushes SSE updates to connected clients.
/// </summary>
public static class OrderStatusNotifier
{
    /// <summary>
    /// Order placed → broadcast SSE update
    /// </summary>
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

    /// <summary>
    /// Payment captured → broadcast SSE update
    /// </summary>
    public static async Task Handle(
        Messages.Contracts.Payments.PaymentCaptured message,
        IEventBroadcaster broadcaster,
        CancellationToken ct)
    {
        // Payment messages don't include CustomerId, so we need to extract OrderId
        // and query Orders BC for CustomerId (deferred to Phase 3 when we have query endpoint)
        // For now, skip this handler - will implement when we have GetOrder endpoint
        await Task.CompletedTask;
    }
}
