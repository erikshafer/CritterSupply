using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using VendorPortal.ChangeRequests;
using VendorPortal.ChangeRequests.Commands;
using Wolverine;
using Wolverine.Http;

namespace VendorPortal.Api.ChangeRequests;

public sealed record DraftChangeRequestRequest(
    Guid RequestId,
    string Sku,
    string Type,
    string Title,
    string Details,
    string? AdditionalNotes = null,
    IReadOnlyList<string>? ImageStorageKeys = null);

/// <summary>
/// Creates a new change request in Draft state for the authenticated vendor tenant.
/// Requires Admin or CatalogManager role (CanSubmitChangeRequests).
/// </summary>
public sealed class DraftChangeRequestEndpoint
{
    [WolverinePost("/api/vendor-portal/change-requests/draft")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public static async Task<IResult> DraftChangeRequestAction(
        DraftChangeRequestRequest body,
        HttpContext httpContext,
        IMessageBus bus,
        CancellationToken ct)
    {
        var tenantIdString = httpContext.User.FindFirst("VendorTenantId")?.Value;
        var userIdString = httpContext.User.FindFirst("VendorUserId")?.Value;
        var role = httpContext.User.FindFirst(ClaimTypes.Role)?.Value;
        var tenantStatus = httpContext.User.FindFirst("VendorTenantStatus")?.Value;

        if (tenantIdString is null || !Guid.TryParse(tenantIdString, out var tenantId))
            return Results.Unauthorized();

        if (userIdString is null || !Guid.TryParse(userIdString, out var userId))
            return Results.Unauthorized();

        if (tenantStatus is "Suspended" or "Terminated")
            return Results.Forbid();

        // Only Admin and CatalogManager can submit change requests
        if (role is not ("Admin" or "CatalogManager"))
            return Results.Forbid();

        if (string.IsNullOrWhiteSpace(body.Sku))
            return Results.BadRequest("Sku is required.");

        if (string.IsNullOrWhiteSpace(body.Title))
            return Results.BadRequest("Title is required.");

        if (string.IsNullOrWhiteSpace(body.Details))
            return Results.BadRequest("Details is required.");

        if (!Enum.TryParse<ChangeRequestType>(body.Type, ignoreCase: true, out var changeRequestType))
            return Results.BadRequest("Invalid change request type. Valid values: Description, Image, DataCorrection.");

        var command = new DraftChangeRequest(
            RequestId: body.RequestId == Guid.Empty ? Guid.NewGuid() : body.RequestId,
            VendorTenantId: tenantId,
            SubmittedByUserId: userId,
            Sku: body.Sku,
            Type: changeRequestType,
            Title: body.Title,
            Details: body.Details,
            AdditionalNotes: body.AdditionalNotes,
            ImageStorageKeys: body.ImageStorageKeys);

        await bus.InvokeAsync(command, ct);

        return Results.Created($"/api/vendor-portal/change-requests/{command.RequestId}", new { RequestId = command.RequestId });
    }
}
