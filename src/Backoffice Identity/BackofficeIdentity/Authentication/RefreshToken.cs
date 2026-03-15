using BackofficeIdentity.Identity;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace BackofficeIdentity.Authentication;

/// <summary>
/// Command to refresh an expired access token using a valid refresh token.
/// Refresh token is typically sent from HttpOnly cookie.
/// </summary>
public sealed record RefreshToken(string RefreshTokenValue)
{
    public sealed class RefreshTokenValidator : AbstractValidator<RefreshToken>
    {
        public RefreshTokenValidator()
        {
            RuleFor(x => x.RefreshTokenValue)
                .NotEmpty()
                .WithMessage("Refresh token is required.");
        }
    }
}

/// <summary>
/// Response returned on successful token refresh.
/// New access token issued, refresh token rotated for security.
/// </summary>
public sealed record RefreshTokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    BackofficeUserInfo User);

/// <summary>
/// Handler for refresh token rotation.
/// Validates the refresh token, issues a new access token and refresh token.
/// Implements refresh token rotation pattern for enhanced security.
/// </summary>
public static class RefreshTokenHandler
{
    public static async Task<BackofficeUser?> Load(
        RefreshToken command,
        BackofficeIdentityDbContext db,
        CancellationToken ct)
    {
        return await db.Users
            .Where(u => u.RefreshToken == command.RefreshTokenValue)
            .FirstOrDefaultAsync(ct);
    }

    public static async Task<(RefreshTokenResponse?, ProblemDetails?)> Handle(
        RefreshToken command,
        BackofficeUser? user,
        BackofficeIdentityDbContext db,
        IJwtTokenGenerator tokenGenerator,
        CancellationToken ct)
    {
        // Refresh token not found or user not found
        if (user is null)
        {
            return (null, new ProblemDetails
            {
                Detail = "Invalid or expired refresh token.",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        // User is deactivated
        if (user.Status == BackofficeUserStatus.Deactivated)
        {
            return (null, new ProblemDetails
            {
                Detail = "This account has been deactivated.",
                Status = StatusCodes.Status403Forbidden
            });
        }

        // Refresh token expired
        if (user.RefreshTokenExpiresAt is null || user.RefreshTokenExpiresAt.Value < DateTimeOffset.UtcNow)
        {
            return (null, new ProblemDetails
            {
                Detail = "Refresh token has expired. Please log in again.",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        // Generate new tokens (refresh token rotation)
        var accessToken = tokenGenerator.GenerateAccessToken(user);
        var newRefreshToken = GenerateRefreshToken();
        var refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7);

        // Update user record with new refresh token
        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiresAt = refreshTokenExpiresAt;

        await db.SaveChangesAsync(ct);

        var response = new RefreshTokenResponse(
            AccessToken: accessToken,
            RefreshToken: newRefreshToken,
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(15),
            User: new BackofficeUserInfo(
                user.Id,
                user.Email,
                user.FirstName,
                user.LastName,
                user.Role.ToString()));

        return (response, null);
    }

    /// <summary>
    /// Generates a cryptographically secure random refresh token.
    /// </summary>
    private static string GenerateRefreshToken()
    {
        var randomBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }
}
