using Backoffice.RealTime;
using Marten;
using Messages.Contracts.Orders;

namespace Backoffice.Notifications;

/// <summary>
/// Integration message handler for OrderPlaced events from Orders BC.
/// Updates AdminDailyMetrics projection via Marten inline projection and publishes
/// real-time metric updates to executive dashboard via SignalR.
/// </summary>
public static class OrderPlacedHandler
{
    /// <summary>
    /// Handle OrderPlaced: Append to event store (triggers projection update) and
    /// publish LiveMetricUpdated SignalR event for executive dashboard.
    /// </summary>
    public static async Task<LiveMetricUpdated> Handle(OrderPlaced message, IDocumentSession session)
    {
        // Append the integration message as an event to Marten's event store
        // The AdminDailyMetricsProjection will automatically process it (inline lifecycle)
        session.Events.Append(Guid.NewGuid(), message);

        // Save changes to update projection before querying
        await session.SaveChangesAsync();

        // Query updated metrics from AdminDailyMetrics projection
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var metrics = await session.LoadAsync<Backoffice.Projections.AdminDailyMetrics>(today.ToString("yyyy-MM-dd"));

        // Publish LiveMetricUpdated for executive dashboard (SignalR to role:executive group)
        return new LiveMetricUpdated(
            OrderCount: metrics?.OrderCount ?? 0,
            Revenue: metrics?.TotalRevenue ?? 0m,
            PaymentFailureRate: metrics?.PaymentFailureRate ?? 0m,
            OccurredAt: DateTimeOffset.UtcNow);
    }
}
