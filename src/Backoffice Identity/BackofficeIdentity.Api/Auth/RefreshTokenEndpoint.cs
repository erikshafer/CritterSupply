using BackofficeIdentity.Authentication;
using Wolverine.Http;

namespace BackofficeIdentity.Api.Auth;

/// <summary>
/// HTTP endpoint for refreshing access tokens.
/// POST /api/backoffice-identity/auth/refresh
/// Reads refresh token from HttpOnly cookie, returns new access token + rotated refresh token.
/// </summary>
public static class RefreshTokenEndpoint
{
    [WolverinePost("/api/backoffice-identity/auth/refresh")]
    public static IResult Handle(
        RefreshTokenResponse? response,
        ProblemDetails? problem,
        HttpContext httpContext)
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
                detail: "Token refresh failed.",
                statusCode: 500);
        }

        // Set new refresh token as HttpOnly cookie (refresh token rotation)
        httpContext.Response.Cookies.Append("RefreshToken", response.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = httpContext.Request.IsHttps, // Secure in HTTPS environments; allows HTTP in local dev
            SameSite = SameSiteMode.Strict,
            Expires = response.ExpiresAt.AddDays(7)
        });

        // Return new access token
        return Results.Ok(new
        {
            response.AccessToken,
            response.ExpiresAt,
            response.User
        });
    }

    /// <summary>
    /// Wolverine Before method to extract refresh token from HttpOnly cookie.
    /// </summary>
    public static (RefreshToken?, ProblemDetails?) Before(HttpContext httpContext)
    {
        if (!httpContext.Request.Cookies.TryGetValue("RefreshToken", out var refreshToken) || string.IsNullOrWhiteSpace(refreshToken))
        {
            return (null, new ProblemDetails
            {
                Detail = "Refresh token not found. Please log in again.",
                Status = 401
            });
        }

        return (new RefreshToken(refreshToken), null);
    }
}
