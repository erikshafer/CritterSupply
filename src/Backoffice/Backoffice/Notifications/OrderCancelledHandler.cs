using Marten;
using Messages.Contracts.Orders;

namespace Backoffice.Notifications;

/// <summary>
/// Integration message handler for OrderCancelled events from Orders BC.
/// Updates AdminDailyMetrics projection via Marten inline projection.
/// </summary>
public static class OrderCancelledHandler
{
    /// <summary>
    /// Handle OrderCancelled: Wolverine will append to event store, triggering inline projection update.
    /// </summary>
    public static void Handle(OrderCancelled message, IDocumentSession session)
    {
        // Append the integration message as an event to Marten's event store
        // The AdminDailyMetricsProjection will automatically process it (inline lifecycle)
        session.Events.Append(Guid.NewGuid(), message);
    }
}
