using System.Security.Claims;
using Marten;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using VendorPortal.Analytics;
using VendorPortal.ChangeRequests;
using VendorPortal.VendorProductCatalog;
using Wolverine.Http;
using VendorAccountDocument = VendorPortal.VendorAccount.VendorAccount;

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

        // Query real SKU count from VendorProductCatalogEntry documents.
        // Returns 0 in a fresh dev environment without upstream ProductCatalog.Api running.
        var totalSkus = await querySession.Query<VendorProductCatalogEntry>()
            .CountAsync(e => e.VendorTenantId == tenantId && e.IsActive, ct);

        // Query real active low-stock alert count.
        // Returns 0 in a fresh dev environment without upstream Inventory.Api running.
        var activeLowStockAlerts = await querySession.Query<LowStockAlert>()
            .CountAsync(a => a.VendorTenantId == tenantId && a.IsActive, ct);

        // Resolve organization name from the VendorAccount document (set on onboarding).
        // Falls back to "Unknown Tenant" if the account has not yet been seeded.
        var account = await querySession.LoadAsync<VendorAccountDocument>(tenantId, ct);
        var tenantName = account?.OrganizationName ?? "Unknown Tenant";

        var summary = new DashboardSummary(
            VendorTenantId: tenantId,
            TenantName: tenantName,
            UserEmail: userEmail ?? "unknown",
            UserRole: role ?? "unknown",
            TotalSkus: (int)totalSkus,
            PendingChangeRequests: (int)pendingCount,
            ActiveLowStockAlerts: (int)activeLowStockAlerts,
            Message: "Welcome to VendorPortal.Api — dashboard with live data.");

        return Results.Ok(summary);
    }
}
