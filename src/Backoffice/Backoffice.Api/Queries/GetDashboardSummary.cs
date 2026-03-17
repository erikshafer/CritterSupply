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

        var dailyMetrics = await session.LoadAsync<Backoffice.Projections.AdminDailyMetrics>(dateKey, ct);

        // Map projection to DTO (use zero values if no data exists for today)
        var metrics = new DashboardMetrics(
            ActiveOrders: dailyMetrics?.OrderCount ?? 0,
            PendingReturns: 0, // STUB: Will be populated from Returns projection in Phase 3
            LowStockAlerts: 0, // STUB: Will be populated from Inventory projection in Phase 3
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
