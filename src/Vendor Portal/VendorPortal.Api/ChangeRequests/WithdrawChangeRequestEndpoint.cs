using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using VendorPortal.ChangeRequests;
using Wolverine;
using Wolverine.Http;

namespace VendorPortal.Api.ChangeRequests;

/// <summary>
/// Withdraws a change request in Draft, Submitted, or NeedsMoreInfo state.
/// CatalogManager users may only withdraw their own requests (enforced in handler by matching SubmittedByUserId).
/// Admin users may withdraw any request in their tenant.
/// </summary>
public sealed class WithdrawChangeRequestEndpoint
{
    [WolverinePost("/api/vendor-portal/change-requests/{requestId}/withdraw")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public static async Task<IResult> WithdrawChangeRequestAction(
        Guid requestId,
        HttpContext httpContext,
        IMessageBus bus,
        CancellationToken ct)
    {
        var tenantIdString = httpContext.User.FindFirst("VendorTenantId")?.Value;
        var tenantStatus = httpContext.User.FindFirst("VendorTenantStatus")?.Value;

        if (tenantIdString is null || !Guid.TryParse(tenantIdString, out var tenantId))
            return Results.Unauthorized();

        if (tenantStatus is "Suspended" or "Terminated")
            return Results.Forbid();

        var command = new WithdrawChangeRequest(RequestId: requestId, VendorTenantId: tenantId);

        await bus.InvokeAsync(command, ct);

        return Results.NoContent();
    }
}
