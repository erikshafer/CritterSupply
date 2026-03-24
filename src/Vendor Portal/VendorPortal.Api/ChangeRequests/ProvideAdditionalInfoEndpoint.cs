using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using VendorPortal.ChangeRequests;
using Wolverine;
using Wolverine.Http;

namespace VendorPortal.Api.ChangeRequests;

public sealed record ProvideAdditionalInfoRequest(string Response);

/// <summary>
/// Provides the additional information requested by the Catalog BC.
/// Transitions the change request from NeedsMoreInfo → Submitted.
/// </summary>
public sealed class ProvideAdditionalInfoEndpoint
{
    [WolverinePost("/api/vendor-portal/change-requests/{requestId}/additional-info")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public static async Task<IResult> ProvideAdditionalInfoAction(
        Guid requestId,
        ProvideAdditionalInfoRequest body,
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

        if (string.IsNullOrWhiteSpace(body.Response))
            return Results.BadRequest("Response is required.");

        var command = new ProvideAdditionalInfo(
            RequestId: requestId,
            VendorTenantId: tenantId,
            Response: body.Response);

        await bus.InvokeAsync(command, ct);

        return Results.NoContent();
    }
}
