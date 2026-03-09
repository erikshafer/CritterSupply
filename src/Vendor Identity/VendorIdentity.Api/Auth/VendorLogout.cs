using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace VendorIdentity.Api.Auth;

public sealed class VendorLogoutEndpoint
{
    [AllowAnonymous]
    [WolverinePost("/api/vendor-identity/auth/logout")]
    public static IResult Logout(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete("vendor_refresh_token", new CookieOptions
        {
            Path = "/api/vendor-identity/auth"
        });
        return Results.Ok();
    }
}
