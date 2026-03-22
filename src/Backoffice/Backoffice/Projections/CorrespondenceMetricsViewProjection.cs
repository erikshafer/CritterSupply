using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Messages.Contracts.Correspondence;

namespace Backoffice.Projections;

/// <summary>
/// Inline Marten projection for CorrespondenceMetricsView using MultiStreamProjection.
/// Lifecycle: ProjectionLifecycle.Inline (zero lag, same transaction as message handling).
/// Maps: Integration messages from Correspondence BC → singleton document (ID: "current").
/// Tracks email queue health metrics for operations dashboard.
/// </summary>
public sealed class CorrespondenceMetricsViewProjection : MultiStreamProjection<CorrespondenceMetricsView, string>
{
    public CorrespondenceMetricsViewProjection()
    {
        // All events map to the same singleton document (ID: "current")
        Identity<CorrespondenceQueued>(_ => "current");
        Identity<CorrespondenceDelivered>(_ => "current");
        Identity<CorrespondenceFailed>(_ => "current");
    }

    /// <summary>
    /// Create document on first correspondence event (CorrespondenceQueued is the initial event).
    /// </summary>
    public CorrespondenceMetricsView Create(CorrespondenceQueued evt)
    {
        return new CorrespondenceMetricsView
        {
            Id = "current",
            PendingEmailCount = 1,
            DeliveredEmailCount = 0,
            FailedEmailCount = 0,
            LastUpdatedAt = evt.QueuedAt
        };
    }

    /// <summary>
    /// Apply CorrespondenceQueued: Increment pending count.
    /// </summary>
    public static CorrespondenceMetricsView Apply(CorrespondenceMetricsView current, CorrespondenceQueued evt)
    {
        return current with
        {
            PendingEmailCount = current.PendingEmailCount + 1,
            LastUpdatedAt = evt.QueuedAt
        };
    }

    /// <summary>
    /// Apply CorrespondenceDelivered: Decrement pending count, increment delivered count.
    /// </summary>
    public static CorrespondenceMetricsView Apply(CorrespondenceMetricsView current, CorrespondenceDelivered evt)
    {
        return current with
        {
            PendingEmailCount = current.PendingEmailCount - 1,
            DeliveredEmailCount = current.DeliveredEmailCount + 1,
            LastUpdatedAt = evt.DeliveredAt
        };
    }

    /// <summary>
    /// Apply CorrespondenceFailed: Decrement pending count, increment failed count.
    /// </summary>
    public static CorrespondenceMetricsView Apply(CorrespondenceMetricsView current, CorrespondenceFailed evt)
    {
        return current with
        {
            PendingEmailCount = current.PendingEmailCount - 1,
            FailedEmailCount = current.FailedEmailCount + 1,
            LastUpdatedAt = evt.FailedAt
        };
    }
}
