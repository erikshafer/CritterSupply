using AdminIdentity.UserManagement;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace AdminIdentity.Api.UserManagement;

/// <summary>
/// HTTP endpoint for changing an admin user's role.
/// PUT /api/admin-identity/users/{userId}/role
/// Requires SystemAdmin role.
/// </summary>
public static class ChangeAdminUserRoleEndpoint
{
    [Authorize(Policy = "SystemAdmin")]
    [WolverinePut("/api/admin-identity/users/{userId}/role")]
    public static IResult Handle(ChangeAdminUserRoleResponse? response, ProblemDetails? problem)
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
