using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Messages.Contracts.Returns;

namespace Backoffice.Projections;

/// <summary>
/// Inline Marten projection for ReturnMetricsView using MultiStreamProjection.
/// Lifecycle: ProjectionLifecycle.Inline (zero lag, same transaction as message handling).
/// Maps: Integration messages from Returns BC → singleton document (ID: "current").
/// Tracks active returns pipeline metrics for executive dashboard.
/// </summary>
public sealed class ReturnMetricsViewProjection : MultiStreamProjection<ReturnMetricsView, string>
{
    public ReturnMetricsViewProjection()
    {
        // All events map to the same singleton document (ID: "current")
        Identity<ReturnRequested>(_ => "current");
        Identity<ReturnApproved>(_ => "current");
        Identity<ReturnDenied>(_ => "current");
        Identity<ReturnRejected>(_ => "current");
        Identity<ReturnReceived>(_ => "current");
        Identity<ReturnCompleted>(_ => "current");
        Identity<ReturnExpired>(_ => "current");
    }

    /// <summary>
    /// Create document on first return event (ReturnRequested is the initial event).
    /// </summary>
    public ReturnMetricsView Create(ReturnRequested evt)
    {
        return new ReturnMetricsView
        {
            Id = "current",
            ActiveReturnCount = 1,
            PendingApprovalCount = 1,
            ApprovedCount = 0,
            ReceivedCount = 0,
            LastUpdatedAt = evt.RequestedAt
        };
    }

    /// <summary>
    /// Apply ReturnRequested: Increment active count and pending approval count.
    /// </summary>
    public static ReturnMetricsView Apply(ReturnMetricsView current, ReturnRequested evt)
    {
        return current with
        {
            ActiveReturnCount = current.ActiveReturnCount + 1,
            PendingApprovalCount = current.PendingApprovalCount + 1,
            LastUpdatedAt = evt.RequestedAt
        };
    }

    /// <summary>
    /// Apply ReturnApproved: Move from pending to approved stage.
    /// </summary>
    public static ReturnMetricsView Apply(ReturnMetricsView current, ReturnApproved evt)
    {
        return current with
        {
            PendingApprovalCount = current.PendingApprovalCount - 1,
            ApprovedCount = current.ApprovedCount + 1,
            LastUpdatedAt = evt.ApprovedAt
        };
    }

    /// <summary>
    /// Apply ReturnDenied: Remove from active count and pending count (terminal state).
    /// </summary>
    public static ReturnMetricsView Apply(ReturnMetricsView current, ReturnDenied evt)
    {
        return current with
        {
            ActiveReturnCount = current.ActiveReturnCount - 1,
            PendingApprovalCount = current.PendingApprovalCount - 1,
            LastUpdatedAt = evt.DeniedAt
        };
    }

    /// <summary>
    /// Apply ReturnRejected: Remove from active count (terminal state from any stage).
    /// Note: Returns can be rejected from any stage (not just pending approval).
    /// Need to decrement the correct stage count based on current distribution.
    /// For simplicity, we assume rejection happens before approval (from pending stage).
    /// </summary>
    public static ReturnMetricsView Apply(ReturnMetricsView current, ReturnRejected evt)
    {
        // Assumption: Most rejections occur from pending approval stage
        // If this assumption is wrong, we may need to track return state separately
        var newPendingCount = current.PendingApprovalCount > 0
            ? current.PendingApprovalCount - 1
            : current.PendingApprovalCount;

        return current with
        {
            ActiveReturnCount = current.ActiveReturnCount - 1,
            PendingApprovalCount = newPendingCount,
            LastUpdatedAt = evt.RejectedAt
        };
    }

    /// <summary>
    /// Apply ReturnReceived: Move from approved to received stage.
    /// </summary>
    public static ReturnMetricsView Apply(ReturnMetricsView current, ReturnReceived evt)
    {
        return current with
        {
            ApprovedCount = current.ApprovedCount - 1,
            ReceivedCount = current.ReceivedCount + 1,
            LastUpdatedAt = evt.ReceivedAt
        };
    }

    /// <summary>
    /// Apply ReturnCompleted: Remove from active count and received count (terminal state).
    /// </summary>
    public static ReturnMetricsView Apply(ReturnMetricsView current, ReturnCompleted evt)
    {
        return current with
        {
            ActiveReturnCount = current.ActiveReturnCount - 1,
            ReceivedCount = current.ReceivedCount - 1,
            LastUpdatedAt = evt.CompletedAt
        };
    }

    /// <summary>
    /// Apply ReturnExpired: Remove from active count (terminal state).
    /// Expired returns are typically in pending approval stage.
    /// </summary>
    public static ReturnMetricsView Apply(ReturnMetricsView current, ReturnExpired evt)
    {
        // Assumption: Expired returns are in pending approval stage
        var newPendingCount = current.PendingApprovalCount > 0
            ? current.PendingApprovalCount - 1
            : current.PendingApprovalCount;

        return current with
        {
            ActiveReturnCount = current.ActiveReturnCount - 1,
            PendingApprovalCount = newPendingCount,
            LastUpdatedAt = evt.ExpiredAt
        };
    }
}
