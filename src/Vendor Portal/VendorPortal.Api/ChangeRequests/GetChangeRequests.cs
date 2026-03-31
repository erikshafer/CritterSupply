using Marten;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using VendorPortal.ChangeRequests;
using Wolverine.Http;

namespace VendorPortal.Api.ChangeRequests;

public sealed record ChangeRequestListItem(
    Guid Id,
    Guid VendorTenantId,
    string Sku,
    string Type,
    string Status,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ResolvedAt);

public sealed record ChangeRequestListResponse(
    IReadOnlyList<ChangeRequestListItem> Items,
    int TotalCount);

/// <summary>
/// Returns change requests for the authenticated vendor tenant.
/// Supports optional status filter. Ordered by most recent first.
/// </summary>
public sealed class GetChangeRequestsEndpoint
{
    [WolverineGet("/api/vendor-portal/change-requests")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public static async Task<IResult> GetChangeRequests(
        string? status,
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

        ChangeRequestStatus? statusFilter = null;
        if (status is not null && Enum.TryParse<ChangeRequestStatus>(status, ignoreCase: true, out var parsed))
        {
            statusFilter = parsed;
        }

        var baseQuery = querySession.Query<ChangeRequest>()
            .Where(r => r.VendorTenantId == tenantId);

        var filteredQuery = statusFilter.HasValue
            ? baseQuery.Where(r => r.Status == statusFilter.Value)
            : baseQuery;

        var requests = await filteredQuery
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        var summaries = requests
            .Select(r => new ChangeRequestListItem(
                Id: r.Id,
                VendorTenantId: r.VendorTenantId,
                Sku: r.Sku,
                Type: r.Type.ToString(),
                Status: r.Status.ToString(),
                Title: r.Title,
                CreatedAt: r.CreatedAt,
                SubmittedAt: r.SubmittedAt,
                ResolvedAt: r.ResolvedAt))
            .ToList();

        return Results.Ok(new ChangeRequestListResponse(Items: summaries, TotalCount: summaries.Count));
    }
}
