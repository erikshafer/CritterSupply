using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine.Http;

namespace Backoffice.Api.Queries;

/// <summary>
/// Query: Get dashboard metrics for a specific date.
/// Returns aggregated metrics (order count, revenue, AOV, payment failure rate).
/// </summary>
public static class GetDashboardMetrics
{
    [WolverineGet("/api/backoffice/dashboard/metrics")]
    [Authorize(Policy = "Executive")]
    public static async Task<Results<Ok<DashboardMetricsDto>, NotFound>> Get(
        string? date,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Default to today's date if not provided
        var targetDate = string.IsNullOrWhiteSpace(date)
            ? DateTimeOffset.UtcNow.Date.ToString("yyyy-MM-dd")
            : date;

        // Query the AdminDailyMetrics projection
        var metrics = await session.LoadAsync<Backoffice.DashboardReporting.AdminDailyMetrics>(targetDate, ct);

        if (metrics is null)
        {
            return TypedResults.NotFound();
        }

        // Map to DTO
        var dto = new DashboardMetricsDto(
            metrics.Date,
            metrics.OrderCount,
            metrics.CancelledOrderCount,
            metrics.TotalRevenue,
            metrics.PaymentFailureCount,
            metrics.AverageOrderValue,
            metrics.PaymentFailureRate,
            metrics.LastUpdatedAt);

        return TypedResults.Ok(dto);
    }
}

/// <summary>
/// DTO for dashboard metrics read model.
/// </summary>
public sealed record DashboardMetricsDto(
    DateTimeOffset Date,
    int OrderCount,
    int CancelledOrderCount,
    decimal TotalRevenue,
    int PaymentFailureCount,
    decimal AverageOrderValue,
    decimal PaymentFailureRate,
    DateTimeOffset LastUpdatedAt);
