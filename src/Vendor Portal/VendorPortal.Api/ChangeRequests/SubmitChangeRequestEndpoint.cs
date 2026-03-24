using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using VendorPortal.ChangeRequests;
using Wolverine;
using Wolverine.Http;

namespace VendorPortal.Api.ChangeRequests;

/// <summary>
/// Submits a Draft change request to the Catalog BC for review.
/// Enforces the one-active-per-SKU+Type invariant (auto-supersedes any existing active request).
/// Requires Admin or CatalogManager role.
/// </summary>
public sealed class SubmitChangeRequestEndpoint
{
    [WolverinePost("/api/vendor-portal/change-requests/{requestId}/submit")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public static async Task<IResult> SubmitChangeRequestAction(
        Guid requestId,
        HttpContext httpContext,
        IMessageBus bus,
        CancellationToken ct)
    {
        var tenantIdString = httpContext.User.FindFirst("VendorTenantId")?.Value;
        var role = httpContext.User.FindFirst(ClaimTypes.Role)?.Value;
        var tenantStatus = httpContext.User.FindFirst("VendorTenantStatus")?.Value;

        if (tenantIdString is null || !Guid.TryParse(tenantIdString, out var tenantId))
            return Results.Unauthorized();

        if (tenantStatus is "Suspended" or "Terminated")
            return Results.Forbid();

        if (role is not ("Admin" or "CatalogManager"))
            return Results.Forbid();

        var command = new SubmitChangeRequest(RequestId: requestId, VendorTenantId: tenantId);

        await bus.InvokeAsync(command, ct);

        return Results.NoContent();
    }
}
