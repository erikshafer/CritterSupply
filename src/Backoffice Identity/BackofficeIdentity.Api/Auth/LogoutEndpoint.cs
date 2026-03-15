using BackofficeIdentity.Authentication;
using Wolverine.Http;

namespace BackofficeIdentity.Api.Auth;

/// <summary>
/// HTTP endpoint for backoffice user logout.
/// POST /api/backoffice-identity/auth/logout
/// Invalidates refresh token and deletes HttpOnly cookie.
/// </summary>
public static class LogoutEndpoint
{
    [WolverinePost("/api/backoffice-identity/auth/logout")]
    public static IResult Handle(bool success, HttpContext httpContext)
    {
        // Delete refresh token cookie
        httpContext.Response.Cookies.Delete("RefreshToken", new CookieOptions
        {
            HttpOnly = true,
            Secure = httpContext.Request.IsHttps, // Secure in HTTPS environments; allows HTTP in local dev
            SameSite = SameSiteMode.Strict
        });

        return Results.Ok(new { Message = "Logged out successfully." });
    }

    /// <summary>
    /// Wolverine Before method to extract refresh token from HttpOnly cookie.
    /// </summary>
    public static (Logout?, ProblemDetails?) Before(HttpContext httpContext)
    {
        if (!httpContext.Request.Cookies.TryGetValue("RefreshToken", out var refreshToken) || string.IsNullOrWhiteSpace(refreshToken))
        {
            // Idempotent: return success even if no refresh token (already logged out)
            return (new Logout(string.Empty), null);
        }

        return (new Logout(refreshToken), null);
    }
}
