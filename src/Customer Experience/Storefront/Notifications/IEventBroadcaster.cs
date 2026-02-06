namespace Storefront.Notifications;

/// <summary>
/// In-memory pub/sub for broadcasting SSE events to active client connections.
/// </summary>
public interface IEventBroadcaster
{
    /// <summary>
    /// Broadcast an event to all connected clients for a specific customer.
    /// </summary>
    Task BroadcastAsync(Guid customerId, StorefrontEvent @event, CancellationToken ct = default);

    /// <summary>
    /// Subscribe to events for a specific customer (returns async stream for SSE).
    /// </summary>
    IAsyncEnumerable<StorefrontEvent> SubscribeAsync(Guid customerId, CancellationToken ct);
}
