using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Storefront.Notifications;

/// <summary>
/// In-memory pub/sub for broadcasting SSE events to active client connections.
/// Thread-safe using Channel<T> for each customer subscription.
/// </summary>
public sealed class EventBroadcaster : IEventBroadcaster
{
    // Map: CustomerId → List of Channel writers (one per active SSE connection)
    private readonly ConcurrentDictionary<Guid, List<Channel<StorefrontEvent>>> _subscriptions = new();

    public async Task BroadcastAsync(Guid customerId, StorefrontEvent @event, CancellationToken ct = default)
    {
        if (!_subscriptions.TryGetValue(customerId, out var channels))
            return; // No active subscribers for this customer

        // Write event to all active channels for this customer
        var tasks = channels
            .Select(channel => channel.Writer.WriteAsync(@event, ct).AsTask())
            .ToList();

        await Task.WhenAll(tasks);
    }

    public async IAsyncEnumerable<StorefrontEvent> SubscribeAsync(
        Guid customerId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<StorefrontEvent>();

        // Register this channel for the customer
        _subscriptions.AddOrUpdate(
            customerId,
            _ => [channel],
            (_, existing) =>
            {
                existing.Add(channel);
                return existing;
            });

        try
        {
            // Stream events from channel until cancellation
            await foreach (var @event in channel.Reader.ReadAllAsync(ct))
            {
                yield return @event;
            }
        }
        finally
        {
            // Cleanup: Remove channel when client disconnects
            if (_subscriptions.TryGetValue(customerId, out var channels))
            {
                channels.Remove(channel);

                // Remove customer entry if no more active connections
                if (channels.Count == 0)
                {
                    _subscriptions.TryRemove(customerId, out _);
                }
            }

            channel.Writer.Complete();
        }
    }
}
