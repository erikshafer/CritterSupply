using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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
    public static IResult GetDashboard(HttpContext httpContext)
    {
        var tenantIdString = httpContext.User.FindFirst("VendorTenantId")?.Value;
        var userEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value;
        var role = httpContext.User.FindFirst(ClaimTypes.Role)?.Value;
        var tenantStatus = httpContext.User.FindFirst("VendorTenantStatus")?.Value;

        if (tenantIdString is null || !Guid.TryParse(tenantIdString, out var tenantId))
            return Results.Unauthorized();

        if (tenantStatus == "Suspended")
            return Results.Forbid();

        var summary = new DashboardSummary(
            VendorTenantId: tenantId,
            TenantName: "Acme Pet Supplies",
            UserEmail: userEmail ?? "unknown",
            UserRole: role ?? "unknown",
            TotalSkus: 42,
            PendingChangeRequests: 3,
            ActiveLowStockAlerts: 7,
            Message: "Welcome to VendorPortal.Api — POC dashboard. Real data comes in Cycle 22 Phase 2+.");

        return Results.Ok(summary);
    }
}
