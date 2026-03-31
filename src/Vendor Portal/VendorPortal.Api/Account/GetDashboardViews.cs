using Marten;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using VendorPortal.VendorAccount;
using Wolverine.Http;

namespace VendorPortal.Api.Account;

public sealed record DashboardViewsResponse(
    IReadOnlyList<DashboardViewItem> Views);

public sealed record DashboardViewItem(
    Guid ViewId,
    string ViewName,
    DashboardFilterCriteria FilterCriteria,
    DateTimeOffset CreatedAt);

public sealed class GetDashboardViewsEndpoint
{
    [WolverineGet("/api/vendor-portal/account/dashboard-views")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public static async Task<IResult> GetDashboardViews(
        HttpContext httpContext,
        IQuerySession querySession,
        CancellationToken ct)
    {
        var tenantIdString = httpContext.User.FindFirst("VendorTenantId")?.Value;
        if (tenantIdString is null || !Guid.TryParse(tenantIdString, out var tenantId))
            return Results.Unauthorized();

        var account = await querySession.LoadAsync<VendorPortal.VendorAccount.VendorAccount>(tenantId, ct);
        if (account is null)
            return Results.Ok(new DashboardViewsResponse([]));

        var views = account.SavedDashboardViews
            .Select(v => new DashboardViewItem(v.ViewId, v.ViewName, v.FilterCriteria, v.CreatedAt))
            .ToList();

        return Results.Ok(new DashboardViewsResponse(views));
    }
}
