namespace Backoffice.DashboardReporting;

/// <summary>
/// BFF-owned projection for executive dashboard KPIs.
/// Document keyed by date (YYYY-MM-DD) for daily aggregation.
/// Sourced from Orders and Payments integration messages via RabbitMQ.
/// </summary>
public sealed record AdminDailyMetrics
{
    /// <summary>
    /// Document ID: Date in YYYY-MM-DD format (e.g., "2026-03-16").
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Date of metrics (midnight UTC).
    /// </summary>
    public DateTimeOffset Date { get; init; }

    /// <summary>
    /// Total orders placed today.
    /// </summary>
    public int OrderCount { get; init; }

    /// <summary>
    /// Total orders cancelled today.
    /// </summary>
    public int CancelledOrderCount { get; init; }

    /// <summary>
    /// Total revenue captured today (sum of PaymentCaptured amounts).
    /// </summary>
    public decimal TotalRevenue { get; init; }

    /// <summary>
    /// Total number of payment failures today.
    /// </summary>
    public int PaymentFailureCount { get; init; }

    /// <summary>
    /// Average Order Value (TotalRevenue / OrderCount).
    /// Computed property for query convenience.
    /// </summary>
    public decimal AverageOrderValue =>
        OrderCount > 0 ? TotalRevenue / OrderCount : 0m;

    /// <summary>
    /// Payment failure rate (PaymentFailureCount / OrderCount * 100).
    /// Computed property for query convenience.
    /// </summary>
    public decimal PaymentFailureRate =>
        OrderCount > 0 ? (decimal)PaymentFailureCount / OrderCount * 100m : 0m;

    /// <summary>
    /// Last updated timestamp (for debugging).
    /// </summary>
    public DateTimeOffset LastUpdatedAt { get; init; }
}
