using BackofficeIdentity.Identity;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BackofficeIdentity.UserManagement;

/// <summary>
/// Command to reset a backoffice user's password.
/// Only SystemAdmin role can reset passwords.
/// </summary>
public sealed record ResetBackofficeUserPassword(
    Guid UserId,
    string NewPassword);

/// <summary>
/// Validator for password reset command.
/// </summary>
public sealed class ResetBackofficeUserPasswordValidator : AbstractValidator<ResetBackofficeUserPassword>
{
    public ResetBackofficeUserPasswordValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required.");

        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .WithMessage("Password must be at least 8 characters.");
    }
}

/// <summary>
/// Response returned when password is reset.
/// </summary>
public sealed record ResetPasswordResponse(
    Guid UserId,
    DateTimeOffset ResetAt);

/// <summary>
/// Handler for resetting backoffice user passwords.
/// Uses PBKDF2-SHA256 password hashing via ASP.NET Core Identity's PasswordHasher&lt;T&gt;.
/// Invalidates refresh token to force re-authentication.
/// </summary>
public static class ResetBackofficeUserPasswordHandler
{
    private static readonly PasswordHasher<BackofficeUser> PasswordHasher = new();

    public static async Task<(ResetPasswordResponse?, ProblemDetails?)> Handle(
        ResetBackofficeUserPassword command,
        BackofficeIdentityDbContext db,
        CancellationToken ct)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == command.UserId, ct);

        if (user is null)
        {
            return (null, new ProblemDetails
            {
                Detail = $"User with ID '{command.UserId}' not found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        // Hash new password using PBKDF2-SHA256
        user.PasswordHash = PasswordHasher.HashPassword(user, command.NewPassword);

        // Invalidate refresh token to force re-authentication
        user.RefreshToken = null;
        user.RefreshTokenExpiresAt = null;

        await db.SaveChangesAsync(ct);

        var response = new ResetPasswordResponse(
            user.Id,
            DateTimeOffset.UtcNow);

        return (response, null);
    }
}
