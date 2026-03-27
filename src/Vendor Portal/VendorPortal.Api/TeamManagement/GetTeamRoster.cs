using System.Security.Claims;
using Marten;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using VendorPortal.TeamManagement;
using Wolverine.Http;

namespace VendorPortal.Api.TeamManagement;

public sealed record TeamRosterMemberDto(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    string Status,
    DateTimeOffset? InvitedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? DeactivatedAt);

public sealed record TeamRosterView(
    Guid VendorTenantId,
    IReadOnlyList<TeamRosterMemberDto> Members,
    int TotalCount);

public static class GetTeamRosterEndpoint
{
    [WolverineGet("/api/vendor-portal/team/roster")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public static async Task<IResult> GetRoster(
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

        var members = await querySession.Query<TeamMember>()
            .Where(m => m.VendorTenantId == tenantId)
            .OrderBy(m => m.Email)
            .ToListAsync(ct);

        var dtos = members.Select(m => new TeamRosterMemberDto(
            UserId: m.Id,
            Email: m.Email,
            FirstName: m.FirstName,
            LastName: m.LastName,
            Role: m.Role,
            Status: m.Status,
            InvitedAt: m.InvitedAt,
            ActivatedAt: m.ActivatedAt,
            DeactivatedAt: m.DeactivatedAt
        )).ToList().AsReadOnly();

        return Results.Ok(new TeamRosterView(
            VendorTenantId: tenantId,
            Members: dtos,
            TotalCount: dtos.Count));
    }
}
