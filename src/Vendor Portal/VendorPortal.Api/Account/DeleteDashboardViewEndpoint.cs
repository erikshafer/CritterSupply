using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using VendorPortal.VendorAccount;
using Wolverine;
using Wolverine.Http;

namespace VendorPortal.Api.Account;

public sealed class DeleteDashboardViewEndpoint
{
    [WolverineDelete("/api/vendor-portal/account/dashboard-views/{viewId}")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public static async Task<IResult> DeleteDashboardView(
        Guid viewId,
        HttpContext httpContext,
        IMessageBus bus,
        CancellationToken ct)
    {
        var tenantIdString = httpContext.User.FindFirst("VendorTenantId")?.Value;
        if (tenantIdString is null || !Guid.TryParse(tenantIdString, out var tenantId))
            return Results.Unauthorized();

        var command = new DeleteDashboardViewCommand(
            VendorTenantId: tenantId,
            ViewId: viewId);

        var deleted = await bus.InvokeAsync<bool>(command, ct);

        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
