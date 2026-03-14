using AdminIdentity.UserManagement;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace AdminIdentity.Api.UserManagement;

/// <summary>
/// HTTP endpoint for listing all admin users.
/// GET /api/admin-identity/users
/// Requires SystemAdmin role.
/// </summary>
public static class GetAdminUsersEndpoint
{
    [Authorize(Policy = "SystemAdmin")]
    [WolverineGet("/api/admin-identity/users")]
    public static IResult Handle(IReadOnlyList<AdminUserSummary> users)
    {
        return Results.Ok(users);
    }
}
