using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Messages.Contracts.Orders;
using Messages.Contracts.Payments;

namespace Backoffice.Projections;

/// <summary>
/// Inline Marten projection for AdminDailyMetrics using MultiStreamProjection.
/// Lifecycle: ProjectionLifecycle.Inline (zero lag, same transaction as message handling).
/// Maps: Integration messages from Orders/Payments → date-keyed documents (YYYY-MM-DD).
/// </summary>
public sealed class AdminDailyMetricsProjection : MultiStreamProjection<AdminDailyMetrics, string>
{
    public AdminDailyMetricsProjection()
    {
        // Tell Marten which property to use as the document ID for each event type
        // We use the date portion of timestamps to group events by day
        Identity<OrderPlaced>(x => ToDateKey(x.PlacedAt));
        Identity<OrderCancelled>(x => ToDateKey(x.CancelledAt));
        Identity<PaymentCaptured>(x => ToDateKey(x.CapturedAt));
        Identity<PaymentFailed>(x => ToDateKey(x.FailedAt));
    }

    /// <summary>
    /// Helper method to convert DateTimeOffset to date key (YYYY-MM-DD).
    /// </summary>
    private static string ToDateKey(DateTimeOffset timestamp)
    {
        // Use UTC date to avoid timezone ambiguity
        return timestamp.UtcDateTime.Date.ToString("yyyy-MM-dd");
    }

    /// <summary>
    /// Create document on first event of the day (any of the 4 event types can create it).
    /// </summary>
    public AdminDailyMetrics Create(OrderPlaced evt)
    {
        var date = evt.PlacedAt.UtcDateTime.Date;
        return new AdminDailyMetrics
        {
            Id = ToDateKey(evt.PlacedAt),
            Date = new DateTimeOffset(date, TimeSpan.Zero),
            OrderCount = 1,
            CancelledOrderCount = 0,
            TotalRevenue = 0m,
            PaymentFailureCount = 0,
            LastUpdatedAt = evt.PlacedAt
        };
    }

    public AdminDailyMetrics Create(OrderCancelled evt)
    {
        var date = evt.CancelledAt.UtcDateTime.Date;
        return new AdminDailyMetrics
        {
            Id = ToDateKey(evt.CancelledAt),
            Date = new DateTimeOffset(date, TimeSpan.Zero),
            OrderCount = 0,
            CancelledOrderCount = 1,
            TotalRevenue = 0m,
            PaymentFailureCount = 0,
            LastUpdatedAt = evt.CancelledAt
        };
    }

    public AdminDailyMetrics Create(PaymentCaptured evt)
    {
        var date = evt.CapturedAt.UtcDateTime.Date;
        return new AdminDailyMetrics
        {
            Id = ToDateKey(evt.CapturedAt),
            Date = new DateTimeOffset(date, TimeSpan.Zero),
            OrderCount = 0,
            CancelledOrderCount = 0,
            TotalRevenue = evt.Amount,
            PaymentFailureCount = 0,
            LastUpdatedAt = evt.CapturedAt
        };
    }

    public AdminDailyMetrics Create(PaymentFailed evt)
    {
        var date = evt.FailedAt.UtcDateTime.Date;
        return new AdminDailyMetrics
        {
            Id = ToDateKey(evt.FailedAt),
            Date = new DateTimeOffset(date, TimeSpan.Zero),
            OrderCount = 0,
            CancelledOrderCount = 0,
            TotalRevenue = 0m,
            PaymentFailureCount = 1,
            LastUpdatedAt = evt.FailedAt
        };
    }

    /// <summary>
    /// Apply OrderPlaced: increment order count.
    /// </summary>
    public static AdminDailyMetrics Apply(AdminDailyMetrics current, OrderPlaced evt)
    {
        return current with
        {
            OrderCount = current.OrderCount + 1,
            LastUpdatedAt = evt.PlacedAt
        };
    }

    /// <summary>
    /// Apply OrderCancelled: increment cancelled count.
    /// </summary>
    public static AdminDailyMetrics Apply(AdminDailyMetrics current, OrderCancelled evt)
    {
        return current with
        {
            CancelledOrderCount = current.CancelledOrderCount + 1,
            LastUpdatedAt = evt.CancelledAt
        };
    }

    /// <summary>
    /// Apply PaymentCaptured: add to total revenue.
    /// </summary>
    public static AdminDailyMetrics Apply(AdminDailyMetrics current, PaymentCaptured evt)
    {
        return current with
        {
            TotalRevenue = current.TotalRevenue + evt.Amount,
            LastUpdatedAt = evt.CapturedAt
        };
    }

    /// <summary>
    /// Apply PaymentFailed: increment failure count.
    /// </summary>
    public static AdminDailyMetrics Apply(AdminDailyMetrics current, PaymentFailed evt)
    {
        return current with
        {
            PaymentFailureCount = current.PaymentFailureCount + 1,
            LastUpdatedAt = evt.FailedAt
        };
    }
}
