using System.Security.Claims;
using Marten;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using VendorPortal.ChangeRequests;
using Wolverine.Http;

namespace VendorPortal.Api.Dashboard;

public sealed record DashboardSummary(
    Guid VendorTenantId,
    string TenantName,
    string UserEmail,
    string UserRole,
    int TotalSkus,
    int PendingChangeRequests,
    int ActiveLowStockAlerts,
    string Message);

public sealed class GetDashboardEndpoint
{
    [WolverineGet("/api/vendor-portal/dashboard")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public static async Task<IResult> GetDashboard(
        HttpContext httpContext,
        IQuerySession querySession,
        CancellationToken ct)
    {
        var tenantIdString = httpContext.User.FindFirst("VendorTenantId")?.Value;
        var userEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value;
        var role = httpContext.User.FindFirst(ClaimTypes.Role)?.Value;
        var tenantStatus = httpContext.User.FindFirst("VendorTenantStatus")?.Value;

        if (tenantIdString is null || !Guid.TryParse(tenantIdString, out var tenantId))
            return Results.Unauthorized();

        if (tenantStatus == "Suspended")
            return Results.Forbid();

        // Query real pending change request count using the canonical active states
        // (Draft, Submitted, NeedsMoreInfo — see ChangeRequest.ActiveStatuses).
        // Explicit OR conditions are required because Marten LINQ cannot parameterize enum arrays.
        var pendingCount = await querySession.Query<ChangeRequest>()
            .CountAsync(r =>
                r.VendorTenantId == tenantId &&
                (r.Status == ChangeRequestStatus.Draft ||
                 r.Status == ChangeRequestStatus.Submitted ||
                 r.Status == ChangeRequestStatus.NeedsMoreInfo),
                ct);

        var summary = new DashboardSummary(
            VendorTenantId: tenantId,
            TenantName: "Acme Pet Supplies",
            UserEmail: userEmail ?? "unknown",
            UserRole: role ?? "unknown",
            TotalSkus: 42,
            PendingChangeRequests: (int)pendingCount,
            ActiveLowStockAlerts: 0,
            Message: "Welcome to VendorPortal.Api — Phase 4 dashboard with live change request count.");

        return Results.Ok(summary);
    }
}
