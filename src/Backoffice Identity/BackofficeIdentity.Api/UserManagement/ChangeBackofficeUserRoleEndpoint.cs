using BackofficeIdentity.UserManagement;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BackofficeIdentity.Api.UserManagement;

/// <summary>
/// HTTP endpoint for changing an backoffice user's role.
/// PUT /api/backoffice-identity/users/{userId}/role
/// Requires SystemAdmin role.
/// </summary>
public static class ChangeBackofficeUserRoleEndpoint
{
    [Authorize(Policy = "SystemAdmin")]
    [WolverinePut("/api/backoffice-identity/users/{userId}/role")]
    public static IResult Handle(ChangeBackofficeUserRoleResponse? response, ProblemDetails? problem)
    {
        if (problem is not null)
        {
            return Results.Problem(
                detail: problem.Detail,
                statusCode: problem.Status);
        }

        if (response is null)
        {
            return Results.Problem(
                detail: "Failed to change user role.",
                statusCode: 500);
        }

        return Results.Ok(response);
    }
}
