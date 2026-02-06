using System.Runtime.CompilerServices;
using Storefront.Notifications;
using Wolverine.Http;

namespace Storefront.Api;

/// <summary>
/// SSE endpoint for real-time storefront updates.
/// Returns IAsyncEnumerable for native .NET 10 Server-Sent Events support.
/// </summary>
public static class StorefrontHub
{
    /// <summary>
    /// Subscribe to real-time updates for a customer.
    /// Multiplexes cart updates, order status changes, and shipment tracking over single SSE stream.
    /// </summary>
    /// <param name="customerId">Customer to subscribe to updates for</param>
    /// <param name="broadcaster">Event broadcaster (injected by Wolverine)</param>
    /// <param name="ct">Cancellation token (client disconnect)</param>
    /// <returns>Async stream of StorefrontEvent for SSE</returns>
    [WolverineGet("/sse/storefront")]
    public static async IAsyncEnumerable<StorefrontEvent> SubscribeToUpdates(
        Guid customerId,
        IEventBroadcaster broadcaster,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var @event in broadcaster.SubscribeAsync(customerId, ct))
        {
            yield return @event;
        }
    }
}
