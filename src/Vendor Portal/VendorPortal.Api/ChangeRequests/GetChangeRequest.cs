using System.Security.Claims;
using Marten;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using VendorPortal.ChangeRequests;
using Wolverine.Http;

namespace VendorPortal.Api.ChangeRequests;

public sealed record ChangeRequestDetailResponse(
    Guid Id,
    string Sku,
    string Type,
    string Status,
    string Title,
    string Details,
    string? AdditionalNotes,
    string? RejectionReason,
    string? Question,
    Guid? ReplacedByRequestId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ResolvedAt);

/// <summary>
/// Returns a single change request by ID for the authenticated vendor tenant.
/// Returns 404 if the request does not exist or belongs to a different tenant (never reveal existence).
/// </summary>
public sealed class GetChangeRequestEndpoint
{
    [WolverineGet("/api/vendor-portal/change-requests/{requestId}")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public static async Task<IResult> GetChangeRequest(
        Guid requestId,
        HttpContext httpContext,
        IQuerySession querySession,
        CancellationToken ct)
    {
        var tenantIdString = httpContext.User.FindFirst("VendorTenantId")?.Value;
        var tenantStatus = httpContext.User.FindFirst("VendorTenantStatus")?.Value;

        if (tenantIdString is null || !Guid.TryParse(tenantIdString, out var tenantId))
            return Results.Unauthorized();

        if (tenantStatus is "Suspended" or "Terminated")
            return Results.Forbid();

        var request = await querySession.LoadAsync<ChangeRequest>(requestId, ct);

        // Return 404 for cross-tenant access — never reveal existence
        if (request is null || request.VendorTenantId != tenantId)
            return Results.NotFound();

        return Results.Ok(new ChangeRequestDetailResponse(
            Id: request.Id,
            Sku: request.Sku,
            Type: request.Type.ToString(),
            Status: request.Status.ToString(),
            Title: request.Title,
            Details: request.Details,
            AdditionalNotes: request.AdditionalNotes,
            RejectionReason: request.RejectionReason,
            Question: request.Question,
            ReplacedByRequestId: request.ReplacedByRequestId,
            CreatedAt: request.CreatedAt,
            SubmittedAt: request.SubmittedAt,
            ResolvedAt: request.ResolvedAt));
    }
}
