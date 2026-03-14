using AdminIdentity.Identity;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AdminIdentity.Authentication;

/// <summary>
/// Command to logout an admin user by invalidating their refresh token.
/// Access token cannot be revoked (JWT is stateless), but it expires in 15 minutes.
/// </summary>
public sealed record Logout(string RefreshTokenValue)
{
    public sealed class LogoutValidator : AbstractValidator<Logout>
    {
        public LogoutValidator()
        {
            RuleFor(x => x.RefreshTokenValue)
                .NotEmpty()
                .WithMessage("Refresh token is required.");
        }
    }
}

/// <summary>
/// Handler for admin user logout.
/// Invalidates the refresh token to prevent future token refresh.
/// Access token remains valid until expiry (15 minutes).
/// </summary>
public static class LogoutHandler
{
    public static async Task<AdminUser?> Load(
        Logout command,
        AdminIdentityDbContext db,
        CancellationToken ct)
    {
        return await db.Users
            .Where(u => u.RefreshToken == command.RefreshTokenValue)
            .FirstOrDefaultAsync(ct);
    }

    public static async Task<bool> Handle(
        Logout command,
        AdminUser? user,
        AdminIdentityDbContext db,
        CancellationToken ct)
    {
        // User not found or refresh token already invalidated
        if (user is null)
        {
            // Return true anyway (idempotent operation)
            return true;
        }

        // Invalidate refresh token
        user.RefreshToken = null;
        user.RefreshTokenExpiresAt = null;

        await db.SaveChangesAsync(ct);

        return true;
    }
}
