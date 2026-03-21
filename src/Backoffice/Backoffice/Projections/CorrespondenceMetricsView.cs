namespace Backoffice.Projections;

/// <summary>
/// Email queue health metrics for the Backoffice dashboard.
/// Singleton document (ID: "current") tracking active email queue status.
/// Updated via inline projection from Correspondence BC integration messages.
/// </summary>
public sealed record CorrespondenceMetricsView
{
    /// <summary>
    /// Fixed document ID for singleton pattern.
    /// </summary>
    public string Id { get; init; } = "current";

    /// <summary>
    /// Number of emails queued for delivery (not yet delivered or failed).
    /// </summary>
    public int PendingEmailCount { get; init; }

    /// <summary>
    /// Number of emails successfully delivered.
    /// </summary>
    public int DeliveredEmailCount { get; init; }

    /// <summary>
    /// Number of emails that permanently failed after max retries.
    /// </summary>
    public int FailedEmailCount { get; init; }

    /// <summary>
    /// Timestamp of last metrics update.
    /// </summary>
    public DateTimeOffset LastUpdatedAt { get; init; }
}
