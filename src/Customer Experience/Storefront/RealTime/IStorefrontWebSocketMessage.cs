namespace Storefront.RealTime;

/// <summary>
/// Marker interface for messages routed to SignalR hub via Wolverine.
/// Enables type-safe routing: opts.Publish(x => x.MessagesImplementing&lt;IStorefrontWebSocketMessage&gt;().ToSignalR())
/// </summary>
public interface IStorefrontWebSocketMessage
{
    /// <summary>
    /// Customer ID for group targeting (customer:{customerId})
    /// </summary>
    Guid CustomerId { get; }
}
