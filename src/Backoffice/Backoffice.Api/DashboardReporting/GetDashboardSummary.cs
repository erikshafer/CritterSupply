using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine.Http;

namespace Backoffice.Api.Queries;

/// <summary>
/// Query: Get dashboard summary with live metrics.
/// Returns current operational metrics for the executive dashboard.
/// </summary>
public static class GetDashboardSummary
{
    [WolverineGet("/api/backoffice/dashboard")]
    [Authorize(Policy = "Executive")]
    public static async Task<Ok<DashboardMetrics>> Get(IDocumentSession session, CancellationToken ct)
    {
        // Query today's metrics from AdminDailyMetrics projection
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dateKey = today.ToString("yyyy-MM-dd");

        var dailyMetrics = await session.LoadAsync<Backoffice.DashboardReporting.AdminDailyMetrics>(dateKey, ct);

        // Query active returns count from ReturnMetricsView projection (M33.0 Session 2)
        var returnMetrics = await session.LoadAsync<Backoffice.DashboardReporting.ReturnMetricsView>("current", ct);

        // Query low stock alerts from AlertFeedView projection
        var alerts = await session.Query<Backoffice.AlertManagement.AlertFeedView>()
            .Where(a => !a.AcknowledgedAt.HasValue && a.AlertType == Backoffice.AlertManagement.AlertType.LowStock)
            .CountAsync(ct);

        // Map projection to DTO (use zero values if no data exists for today)
        var metrics = new DashboardMetrics(
            ActiveOrders: dailyMetrics?.OrderCount ?? 0,
            PendingReturns: returnMetrics?.ActiveReturnCount ?? 0,
            LowStockAlerts: alerts,
            TodaysRevenue: dailyMetrics?.TotalRevenue ?? 0m,
            GeneratedAt: DateTimeOffset.UtcNow);

        return TypedResults.Ok(metrics);
    }
}

public sealed record DashboardMetrics(
    int ActiveOrders,
    int PendingReturns,
    int LowStockAlerts,
    decimal TodaysRevenue,
    DateTimeOffset GeneratedAt);
