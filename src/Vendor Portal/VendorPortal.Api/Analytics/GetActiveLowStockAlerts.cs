using Marten;
using Marten.Linq;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using VendorPortal.Analytics;
using Wolverine.Http;

namespace VendorPortal.Api.Analytics;

/// <summary>
/// Response record for active low-stock alerts.
/// </summary>
public sealed record ActiveLowStockAlertsResponse(
    IReadOnlyList<LowStockAlertSummary> Alerts,
    int TotalCount,
    DateTimeOffset QueriedAt);

public sealed record LowStockAlertSummary(
    string Sku,
    string WarehouseId,
    int CurrentQuantity,
    int ThresholdQuantity,
    DateTimeOffset FirstDetectedAt,
    DateTimeOffset LastUpdatedAt);

/// <summary>
/// Returns active low-stock alerts for the authenticated vendor tenant.
/// Used by the Blazor client after SignalR reconnection to catch up on missed alerts.
/// The client provides the last-seen timestamp to filter only new alerts since reconnect.
/// </summary>
public sealed class GetActiveLowStockAlertsEndpoint
{
    [WolverineGet("/api/vendor-portal/analytics/alerts/low-stock")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public static async Task<IResult> GetActiveLowStockAlerts(
        DateTimeOffset? since,
        HttpContext httpContext,
        IQuerySession querySession,
        CancellationToken ct)
    {
        var tenantIdString = httpContext.User.FindFirst("VendorTenantId")?.Value;
        var tenantStatus = httpContext.User.FindFirst("VendorTenantStatus")?.Value;

        if (tenantIdString is null || !Guid.TryParse(tenantIdString, out var tenantId))
            return Results.Unauthorized();

        if (tenantStatus is "Suspended" or "Terminated")
            return Results.Forbid();

        // Load active alerts for this tenant, optionally filtered by last-seen timestamp.
        // The client sends its last-received timestamp on reconnect to request only missed alerts.
        // Using LastUpdatedAt (not FirstDetectedAt) to capture alerts that were first detected
        // before the disconnect but were updated (quantity changed) during the offline window.
        // Build the query conditionally in C# (Marten's LINQ provider cannot evaluate
        // since.Value inside the expression when since is null).
        var baseQuery = querySession.Query<LowStockAlert>()
            .Where(a => a.VendorTenantId == tenantId && a.IsActive);

        var filteredQuery = since.HasValue
            ? baseQuery.Where(a => a.LastUpdatedAt > since.Value)
            : baseQuery;

        var alerts = await filteredQuery
            .OrderByDescending(a => a.LastUpdatedAt)
            .ToListAsync(ct);

        var summaries = alerts.Select(a => new LowStockAlertSummary(
            Sku: a.Sku,
            WarehouseId: a.WarehouseId,
            CurrentQuantity: a.CurrentQuantity,
            ThresholdQuantity: a.ThresholdQuantity,
            FirstDetectedAt: a.FirstDetectedAt,
            LastUpdatedAt: a.LastUpdatedAt)).ToList();

        return Results.Ok(new ActiveLowStockAlertsResponse(
            Alerts: summaries,
            TotalCount: summaries.Count,
            QueriedAt: DateTimeOffset.UtcNow));
    }
}
