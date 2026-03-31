using System.Diagnostics.CodeAnalysis;
using BackofficeIdentity.Identity;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace BackofficeIdentity.Authentication;

/// <summary>
/// Command to authenticate an backoffice user and issue JWT access token + refresh token.
/// </summary>
public sealed record Login(string Email, string Password)
{
    public sealed class LoginValidator : AbstractValidator<Login>
    {
        public LoginValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty()
                .EmailAddress()
                .WithMessage("Valid email address is required.");

            RuleFor(x => x.Password)
                .NotEmpty()
                .WithMessage("Password is required.");
        }
    }
}

/// <summary>
/// Response returned on successful login.
/// Access token should be sent in Authorization header as Bearer token.
/// Refresh token is set as HttpOnly cookie by the API.
/// </summary>
public sealed record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    BackofficeUserInfo User);

/// <summary>
/// Backoffice user information included in login/refresh responses.
/// </summary>
public sealed record BackofficeUserInfo(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Role);

/// <summary>
/// Handler for backoffice user login.
/// Verifies email/password, generates JWT access token + refresh token.
/// Uses PBKDF2-SHA256 password hashing via ASP.NET Core Identity's PasswordHasher&lt;T&gt;.
/// </summary>
public static class LoginHandler
{
    private static readonly PasswordHasher<BackofficeUser> PasswordHasher = new();

    public static async Task<BackofficeUser?> Load(
        Login command,
        BackofficeIdentityDbContext db,
        CancellationToken ct)
    {
        return await db.Users
            .Where(u => u.Email == command.Email)
            .FirstOrDefaultAsync(ct);
    }

    public static async Task<(LoginResponse?, ProblemDetails?)> Handle(
        Login command,
        BackofficeUser? user,
        BackofficeIdentityDbContext db,
        IJwtTokenGenerator tokenGenerator,
        CancellationToken ct)
    {
        // User not found
        if (user is null)
        {
            return (null, new ProblemDetails
            {
                Detail = "Invalid email or password.",
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

        // Verify password using PBKDF2-SHA256 (via ASP.NET Core Identity PasswordHasher<T>)
        var verificationResult = PasswordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            command.Password);

        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return (null, new ProblemDetails
            {
                Detail = "Invalid email or password.",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        // Generate tokens
        var accessToken = tokenGenerator.GenerateAccessToken(user);
        var refreshToken = GenerateRefreshToken();
        var refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7);

        // Update user record with refresh token and last login timestamp
        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiresAt = refreshTokenExpiresAt;
        user.LastLoginAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        var response = new LoginResponse(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(15),
            User: new BackofficeUserInfo(
                user.Id,
                user.Email,
                user.FirstName,
                user.LastName,
                user.Role.ToRoleString()));

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

/// <summary>
/// Interface for JWT token generation.
/// Implemented in the API project to avoid coupling domain logic to System.IdentityModel.Tokens.Jwt.
/// </summary>
[SuppressMessage("ReSharper", "ArrangeTypeMemberModifiers")]
public interface IJwtTokenGenerator
{
    string GenerateAccessToken(BackofficeUser user);
}

/// <summary>
/// Problem details for validation/authentication failures.
/// </summary>
public sealed record ProblemDetails
{
    public string? Detail { get; init; }
    public int? Status { get; init; }
}

/// <summary>
/// ASP.NET Core StatusCodes constants for handler use.
/// </summary>
public static class StatusCodes
{
    public const int Status401Unauthorized = 401;
    public const int Status403Forbidden = 403;
    public const int Status404NotFound = 404;
    public const int Status400BadRequest = 400;
}
