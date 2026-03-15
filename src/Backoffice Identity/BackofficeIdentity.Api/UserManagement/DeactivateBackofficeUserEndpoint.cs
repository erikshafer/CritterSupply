using BackofficeIdentity.UserManagement;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BackofficeIdentity.Api.UserManagement;

/// <summary>
/// HTTP endpoint for deactivating an backoffice user.
/// DELETE /api/backoffice-identity/users/{userId}
/// Requires SystemAdmin role.
/// </summary>
public static class DeactivateBackofficeUserEndpoint
{
    [Authorize(Policy = "SystemAdmin")]
    [WolverineDelete("/api/backoffice-identity/users/{userId}")]
    public static IResult Handle(DeactivateBackofficeUserResponse? response, ProblemDetails? problem)
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
