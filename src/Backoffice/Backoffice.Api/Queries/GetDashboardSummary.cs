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
    public static Ok<DashboardMetrics> Get()
    {
        // STUB: Return hardcoded metrics for now
        // Will be replaced with real Marten projections in Phase 3
        var metrics = new DashboardMetrics(
            ActiveOrders: 42,
            PendingReturns: 7,
            LowStockAlerts: 3,
            TodaysRevenue: 15_432.50m,
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
