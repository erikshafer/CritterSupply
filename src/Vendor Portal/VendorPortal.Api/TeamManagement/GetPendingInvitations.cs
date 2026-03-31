using Marten;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using VendorPortal.TeamManagement;
using Wolverine.Http;

namespace VendorPortal.Api.TeamManagement;

public sealed record PendingInvitationDto(
    Guid UserId,
    string Email,
    string Role,
    string Status,
    int ResendCount,
    DateTimeOffset InvitedAt,
    DateTimeOffset ExpiresAt);

public sealed record PendingInvitationsView(
    Guid VendorTenantId,
    IReadOnlyList<PendingInvitationDto> Invitations,
    int TotalCount);

public static class GetPendingInvitationsEndpoint
{
    [WolverineGet("/api/vendor-portal/team/invitations/pending")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public static async Task<IResult> GetPendingInvitations(
        HttpContext httpContext,
        IQuerySession querySession,
        CancellationToken ct)
    {
        var tenantIdString = httpContext.User.FindFirst("VendorTenantId")?.Value;
        var tenantStatus = httpContext.User.FindFirst("VendorTenantStatus")?.Value;

        if (tenantIdString is null || !Guid.TryParse(tenantIdString, out var tenantId))
            return Results.Unauthorized();

        if (tenantStatus == "Suspended")
            return Results.Forbid();

        var invitations = await querySession.Query<TeamInvitation>()
            .Where(i => i.VendorTenantId == tenantId && i.Status == "Pending")
            .OrderByDescending(i => i.InvitedAt)
            .ToListAsync(ct);

        var dtos = invitations.Select(i => new PendingInvitationDto(
            UserId: i.Id,
            Email: i.Email,
            Role: i.Role,
            Status: i.Status,
            ResendCount: i.ResendCount,
            InvitedAt: i.InvitedAt,
            ExpiresAt: i.ExpiresAt
        )).ToList().AsReadOnly();

        return Results.Ok(new PendingInvitationsView(
            VendorTenantId: tenantId,
            Invitations: dtos,
            TotalCount: dtos.Count));
    }
}
