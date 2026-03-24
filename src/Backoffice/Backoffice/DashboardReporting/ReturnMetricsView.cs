namespace Backoffice.DashboardReporting;

/// <summary>
/// BFF-owned projection for active returns metrics.
/// Singleton document (ID: "current") tracking all active returns pipeline status.
/// Sourced from Returns integration messages via RabbitMQ.
/// </summary>
public sealed record ReturnMetricsView
{
    /// <summary>
    /// Document ID: Always "current" (singleton pattern for aggregate metrics).
    /// </summary>
    public string Id { get; init; } = "current";

    /// <summary>
    /// Total active returns (not in terminal states: completed, denied, rejected, expired).
    /// </summary>
    public int ActiveReturnCount { get; init; }

    /// <summary>
    /// Returns in 'requested' stage (awaiting approval).
    /// </summary>
    public int PendingApprovalCount { get; init; }

    /// <summary>
    /// Returns in 'approved' stage (awaiting customer shipment).
    /// </summary>
    public int ApprovedCount { get; init; }

    /// <summary>
    /// Returns in 'received' stage (awaiting completion).
    /// </summary>
    public int ReceivedCount { get; init; }

    /// <summary>
    /// Last updated timestamp (for debugging).
    /// </summary>
    public DateTimeOffset LastUpdatedAt { get; init; }
}
