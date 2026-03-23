using System.Security.Claims;
using Marten;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using VendorPortal.VendorAccount;
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
        IQuerySession querySession,
        IMessageBus bus,
        CancellationToken ct)
    {
        var tenantIdString = httpContext.User.FindFirst("VendorTenantId")?.Value;
        if (tenantIdString is null || !Guid.TryParse(tenantIdString, out var tenantId))
            return Results.Unauthorized();

        var viewName = request.ViewName?.Trim();
        if (string.IsNullOrWhiteSpace(viewName))
            return Results.BadRequest("View name is required.");

        // Pre-check: distinguish between 404 (no account) and 409 (duplicate name)
        var account = await querySession.LoadAsync<VendorPortal.VendorAccount.VendorAccount>(tenantId, ct);
        if (account is null)
            return Results.NotFound("Vendor account not found. Please contact support.");

        var command = new SaveDashboardViewCommand(
            VendorTenantId: tenantId,
            ViewName: viewName,
            FilterCriteria: request.FilterCriteria ?? new DashboardFilterCriteria());

        var savedView = await bus.InvokeAsync<SavedDashboardView?>(command, ct);
        if (savedView is null)
            return Results.Conflict("A view with this name already exists.");

        return Results.Created(
            $"/api/vendor-portal/account/dashboard-views/{savedView.ViewId}",
            new SaveDashboardViewResponse(
                savedView.ViewId,
                savedView.ViewName,
                savedView.FilterCriteria,
                savedView.CreatedAt));
    }
}
