using AdminIdentity.UserManagement;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace AdminIdentity.Api.UserManagement;

/// <summary>
/// HTTP endpoint for deactivating an admin user.
/// DELETE /api/admin-identity/users/{userId}
/// Requires SystemAdmin role.
/// </summary>
public static class DeactivateAdminUserEndpoint
{
    [Authorize(Policy = "SystemAdmin")]
    [WolverineDelete("/api/admin-identity/users/{userId}")]
    public static IResult Handle(DeactivateAdminUserResponse? response, ProblemDetails? problem)
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
                detail: "Failed to deactivate user.",
                statusCode: 500);
        }

        return Results.Ok(response);
    }
}
