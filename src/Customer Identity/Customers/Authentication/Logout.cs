using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace CustomerIdentity.Authentication;

public static class Logout
{
    /// <summary>
    /// POST /api/auth/logout
    /// Signs out the user and clears the session cookie.
    /// </summary>
    [WolverinePost("/api/auth/logout")]
    public static async Task<IResult> Handle(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Ok();
    }
}
