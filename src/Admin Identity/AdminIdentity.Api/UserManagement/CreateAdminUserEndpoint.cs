using AdminIdentity.UserManagement;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace AdminIdentity.Api.UserManagement;

/// <summary>
/// HTTP endpoint for creating admin users.
/// POST /api/admin-identity/users
/// Requires SystemAdmin role.
/// </summary>
public static class CreateAdminUserEndpoint
{
    [Authorize(Policy = "SystemAdmin")]
    [WolverinePost("/api/admin-identity/users")]
    public static IResult Handle(CreateAdminUserResponse? response, ProblemDetails? problem)
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
                detail: "Failed to create admin user.",
                statusCode: 500);
        }

        return Results.Created($"/api/admin-identity/users/{response.Id}", response);
    }
}
