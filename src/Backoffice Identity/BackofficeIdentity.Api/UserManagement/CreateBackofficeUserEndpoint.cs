using BackofficeIdentity.UserManagement;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BackofficeIdentity.Api.UserManagement;

/// <summary>
/// HTTP endpoint for creating backoffice users.
/// POST /api/backoffice-identity/users
/// Requires SystemAdmin role.
/// </summary>
public static class CreateBackofficeUserEndpoint
{
    [Authorize(Policy = "SystemAdmin")]
    [WolverinePost("/api/backoffice-identity/users")]
    public static IResult Handle(CreateBackofficeUserResponse? response, ProblemDetails? problem)
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
                detail: "Failed to create backoffice user.",
                statusCode: 500);
        }

        return Results.Created($"/api/backoffice-identity/users/{response.Id}", response);
    }
}
