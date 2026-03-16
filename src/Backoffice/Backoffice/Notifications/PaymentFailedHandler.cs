using Backoffice.RealTime;
using Marten;
using Messages.Contracts.Payments;

namespace Backoffice.Notifications;

/// <summary>
/// Integration message handler for PaymentFailed events from Payments BC.
/// Updates AdminDailyMetrics and AlertFeedView projections via Marten inline projection
/// and publishes real-time alert notifications to operations team via SignalR.
/// </summary>
public static class PaymentFailedHandler
{
    /// <summary>
    /// Handle PaymentFailed: Append to event store (triggers projection update) and
    /// publish AlertCreated SignalR event for operations team.
    /// </summary>
    public static AlertCreated Handle(PaymentFailed message, IDocumentSession session)
    {
        // Append the integration message as an event to Marten's event store
        // The AdminDailyMetricsProjection and AlertFeedViewProjection will automatically process it (inline lifecycle)
        session.Events.Append(Guid.NewGuid(), message);

        // Publish AlertCreated for operations dashboard (SignalR to role:operations group)
        return new AlertCreated(
            AlertType: "PaymentFailed",
            Severity: "High",
            Message: $"Payment failed for order {message.OrderId}: {message.FailureReason}",
            OccurredAt: DateTimeOffset.UtcNow);
    }
}
