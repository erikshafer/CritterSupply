using Messages.Contracts.Inventory;

namespace Storefront.Notifications;

/// <summary>
/// Handles ReservationConfirmed integration message from Inventory BC.
/// Broadcasts OrderStatusChanged SSE event to customer's UI.
/// </summary>
public static class ReservationConfirmedHandler
{
    public static async Task Handle(
        ReservationConfirmed message,
        IEventBroadcaster broadcaster,
        CancellationToken ct)
    {
        // TODO: Query Orders BC to get CustomerId for the order
        // For now, using a stub CustomerId (in real implementation, need to fetch from Orders BC)
        var customerId = Guid.Empty; // Stub - would query Orders BC for order.CustomerId

        var sseEvent = new OrderStatusChanged(
            message.OrderId,
            customerId,
            "InventoryReserved",
            message.ReservedAt);

        await broadcaster.BroadcastAsync(customerId, sseEvent, ct);
    }
}
