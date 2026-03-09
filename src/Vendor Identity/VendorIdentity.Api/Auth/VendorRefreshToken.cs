using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using VendorIdentity.Identity;
using VendorIdentity.UserInvitations;
using Wolverine.Http;

namespace VendorIdentity.Api.Auth;

public sealed record VendorRefreshResponse(string AccessToken);

public sealed class VendorRefreshEndpoint
{
    [AllowAnonymous]
    [WolverinePost("/api/vendor-identity/auth/refresh")]
    public static async Task<IResult> Refresh(
        HttpContext httpContext,
        VendorIdentityDbContext dbContext,
        JwtTokenService tokenService,
        JwtSettings jwtSettings,
        ILogger<VendorRefreshEndpoint> logger,
        CancellationToken ct)
    {
        var refreshCookie = httpContext.Request.Cookies["vendor_refresh_token"];
        if (string.IsNullOrEmpty(refreshCookie))
            return Results.Unauthorized();

        var authHeader = httpContext.Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Bearer "))
            return Results.Unauthorized();

        var oldToken = authHeader["Bearer ".Length..];
        var handler = new JwtSecurityTokenHandler();

        string? userIdString;
        try
        {
            var tokenValidation = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtSettings.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey)),
                ValidateLifetime = false, // Allow expired tokens for refresh
            };
            var principal = handler.ValidateToken(oldToken, tokenValidation, out _);
            userIdString = principal.FindFirst("VendorUserId")?.Value;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Token validation failed during refresh");
            return Results.Unauthorized();
        }

        if (!Guid.TryParse(userIdString, out var userId))
            return Results.Unauthorized();

        var user = await dbContext.Users
            .Include(u => u.VendorTenant)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null || user.Status != VendorUserStatus.Active)
            return Results.Unauthorized();

        var newAccessToken = tokenService.CreateAccessToken(user, user.VendorTenant);

        var newRefreshToken = tokenService.CreateRefreshToken();
        httpContext.Response.Cookies.Append("vendor_refresh_token", newRefreshToken, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = !httpContext.Request.IsHttps ? false : true, // POC: always true in production (HTTPS)
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            Path = "/api/vendor-identity/auth"
        });

        return Results.Ok(new VendorRefreshResponse(newAccessToken));
    }
}
