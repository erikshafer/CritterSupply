using System.Security.Claims;
using Marten;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using VendorPortal.VendorAccount;
using VendorPortal.VendorAccount.Commands;
using Wolverine;
using Wolverine.Http;

namespace VendorPortal.Api.Account;

public sealed record SaveDashboardViewRequest(
    string ViewName,
    DashboardFilterCriteria? FilterCriteria);

public sealed record SaveDashboardViewResponse(
    Guid ViewId,
    string ViewName,
    DashboardFilterCriteria FilterCriteria,
    DateTimeOffset CreatedAt);

public sealed class SaveDashboardViewEndpoint
{
    [WolverinePost("/api/vendor-portal/account/dashboard-views")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public static async Task<IResult> SaveDashboardView(
        SaveDashboardViewRequest request,
        HttpContext httpContext,
        IMessageBus bus,
        CancellationToken ct)
    {
        var tenantIdString = httpContext.User.FindFirst("VendorTenantId")?.Value;
        if (tenantIdString is null || !Guid.TryParse(tenantIdString, out var tenantId))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.ViewName))
            return Results.BadRequest("View name is required.");

        var command = new SaveDashboardViewCommand(
            VendorTenantId: tenantId,
            ViewName: request.ViewName.Trim(),
            FilterCriteria: request.FilterCriteria ?? new DashboardFilterCriteria());

        var savedView = await bus.InvokeAsync<SavedDashboardView?>(command, ct);
        if (savedView is null)
            return Results.NotFound("Vendor account not found. Please contact support.");

        return Results.Created(
            $"/api/vendor-portal/account/dashboard-views/{savedView.ViewId}",
            new SaveDashboardViewResponse(
                savedView.ViewId,
                savedView.ViewName,
                savedView.FilterCriteria,
                savedView.CreatedAt));
    }
}
