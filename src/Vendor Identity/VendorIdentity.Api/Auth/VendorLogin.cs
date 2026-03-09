using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;
using VendorIdentity.TenantManagement;
using VendorIdentity.UserInvitations;
using Wolverine.Http;

namespace VendorIdentity.Api.Auth;

public sealed record VendorLoginRequest(string Email, string Password);

public sealed record VendorLoginResponse(
    string AccessToken,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    string TenantName);

public sealed class VendorLoginEndpoint
{
    [AllowAnonymous]
    [WolverinePost("/api/vendor-identity/auth/login")]
    public static async Task<IResult> Login(
        VendorLoginRequest request,
        VendorIdentityDbContext dbContext,
        JwtTokenService tokenService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var user = await dbContext.Users
            .Include(u => u.VendorTenant)
            .FirstOrDefaultAsync(u => u.Email == request.Email, ct);

        if (user is null)
            return Results.Unauthorized();

        if (user.Status != VendorUserStatus.Active)
            return Results.Unauthorized();

        if (user.VendorTenant.Status == VendorTenantStatus.Terminated)
            return Results.Unauthorized();

        var hasher = new PasswordHasher<VendorUser>();
        var verifyResult = hasher.VerifyHashedPassword(user, user.PasswordHash ?? "", request.Password);
        if (verifyResult == PasswordVerificationResult.Failed)
            return Results.Unauthorized();

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        var accessToken = tokenService.CreateAccessToken(user, user.VendorTenant);
        var refreshToken = tokenService.CreateRefreshToken();

        httpContext.Response.Cookies.Append("vendor_refresh_token", refreshToken, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = false, // POC: set to true in production
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            Path = "/api/vendor-identity/auth"
        });

        return Results.Ok(new VendorLoginResponse(
            AccessToken: accessToken,
            Email: user.Email,
            FirstName: user.FirstName,
            LastName: user.LastName,
            Role: user.Role.ToString(),
            TenantName: user.VendorTenant.OrganizationName));
    }
}
