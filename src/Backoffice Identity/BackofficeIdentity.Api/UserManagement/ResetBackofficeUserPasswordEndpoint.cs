using BackofficeIdentity.UserManagement;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BackofficeIdentity.Api.UserManagement;

/// <summary>
/// HTTP endpoint for resetting backoffice user passwords.
/// POST /api/backoffice-identity/users/{userId}/reset-password
/// Requires SystemAdmin role.
/// </summary>
public static class ResetBackofficeUserPasswordEndpoint
{
    [Authorize(Policy = "SystemAdmin")]
    [WolverinePost("/api/backoffice-identity/users/{userId}/reset-password")]
    public static IResult Handle(
        Guid userId,
        string newPassword,
        ResetPasswordResponse? response,
        ProblemDetails? problem)
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
                detail: "Failed to reset password.",
                statusCode: 500);
        }

        return Results.Ok(response);
    }
}
