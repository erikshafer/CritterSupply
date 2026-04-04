using BackofficeIdentity.Identity;
using BackofficeIdentity.UserManagement;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Wolverine.Http;

namespace BackofficeIdentity.Api.UserManagement;

/// <summary>
/// Request body for password reset.
/// </summary>
public sealed record ResetPasswordRequest(string NewPassword);

/// <summary>
/// Validator for password reset request.
/// </summary>
public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .WithMessage("Password must be at least 8 characters.");
    }
}

/// <summary>
/// HTTP endpoint for resetting backoffice user passwords.
/// POST /api/backoffice-identity/users/{userId}/reset-password
/// Requires SystemAdmin role.
/// </summary>
public static class ResetBackofficeUserPasswordEndpoint
{
    private static readonly PasswordHasher<BackofficeUser> PasswordHasher = new();

    [Authorize(Policy = "SystemAdmin")]
    [WolverinePost("/api/backoffice-identity/users/{userId}/reset-password")]
    public static async Task<IResult> Handle(
        Guid userId,
        ResetPasswordRequest request,
        BackofficeIdentityDbContext db,
        CancellationToken ct)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return Results.Problem(
                detail: $"User with ID '{userId}' not found.",
                statusCode: Microsoft.AspNetCore.Http.StatusCodes.Status404NotFound);
        }

        // Hash new password using PBKDF2-SHA256
        user.PasswordHash = PasswordHasher.HashPassword(user, request.NewPassword);

        // Invalidate refresh token to force re-authentication
        user.RefreshToken = null;
        user.RefreshTokenExpiresAt = null;

        var response = new ResetPasswordResponse(
            user.Id,
            DateTimeOffset.UtcNow);

        return Results.Ok(response);
    }
}
