using BackofficeIdentity.Authentication;
using Wolverine.Http;

namespace BackofficeIdentity.Api.Auth;

/// <summary>
/// HTTP endpoint for backoffice user login.
/// POST /api/backoffice-identity/auth/login
/// Returns JWT access token + refresh token (as HttpOnly cookie).
/// </summary>
public static class LoginEndpoint
{
    [WolverinePost("/api/backoffice-identity/auth/login")]
    public static IResult Handle(LoginResponse? response, ProblemDetails? problem, HttpContext httpContext)
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
                detail: "Login failed.",
                statusCode: 500);
        }

        // Set refresh token as HttpOnly cookie (secure, not accessible to JavaScript)
        httpContext.Response.Cookies.Append("RefreshToken", response.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = httpContext.Request.IsHttps, // Secure in HTTPS environments; allows HTTP in local dev
            SameSite = SameSiteMode.Strict,
            Expires = response.ExpiresAt.AddDays(7) // 7-day refresh token
        });

        // Return access token in response body (client stores in memory, NOT localStorage)
        return Results.Ok(new
        {
            response.AccessToken,
            response.ExpiresAt,
            response.User
        });
    }
}
