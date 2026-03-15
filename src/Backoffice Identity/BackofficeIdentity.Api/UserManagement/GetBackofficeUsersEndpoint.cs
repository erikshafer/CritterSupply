using BackofficeIdentity.UserManagement;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BackofficeIdentity.Api.UserManagement;

/// <summary>
/// HTTP endpoint for listing all backoffice users.
/// GET /api/backoffice-identity/users
/// Requires SystemAdmin role.
/// </summary>
public static class GetBackofficeUsersEndpoint
{
    [Authorize(Policy = "SystemAdmin")]
    [WolverineGet("/api/backoffice-identity/users")]
    public static IResult Handle(IReadOnlyList<BackofficeUserSummary> users)
    {
        return Results.Ok(users);
    }
}
