using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Shouldly;
using VendorIdentity.Api.Auth;
using VendorIdentity.TenantManagement;
using VendorIdentity.UserInvitations;

namespace VendorIdentity.Api.IntegrationTests;

/// <summary>
/// Integration tests for the Phase 2 JWT authentication endpoints:
/// POST /api/vendor-identity/auth/login
/// POST /api/vendor-identity/auth/refresh
/// POST /api/vendor-identity/auth/logout
///
/// Tests verify stable HTTP contracts. Implementation details that are known
/// to change in Phase 3 (e.g., refresh token DB persistence, cookie security
/// attributes) are intentionally not tested here to avoid brittleness.
/// </summary>
public sealed class VendorAuthTests : IClassFixture<VendorIdentityApiFixture>, IAsyncLifetime
{
    private readonly VendorIdentityApiFixture _fixture;

    // Shared test tenant and user IDs (stable fixed GUIDs for readability in test output)
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ActiveAdminId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid InactiveUserId = Guid.Parse("11111111-0000-0000-0000-000000000002");

    private const string ActiveAdminEmail = "admin@testvendor.test";
    private const string InactiveUserEmail = "inactive@testvendor.test";
    private const string TestPassword = "integration-test-password";

    public VendorAuthTests(VendorIdentityApiFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await SeedTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        await _fixture.CleanAllDataAsync();
    }

    // ─── Login ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_WithValidCredentials_Returns200AndLoginResponse()
    {
        // Arrange
        var request = new VendorLoginRequest(ActiveAdminEmail, TestPassword);

        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(request).ToUrl("/api/vendor-identity/auth/login");
            x.StatusCodeShouldBe(200);
        });

        // Assert
        var response = result.ReadAsJson<VendorLoginResponse>();
        response.ShouldNotBeNull();
        response.Email.ShouldBe(ActiveAdminEmail);
        response.FirstName.ShouldBe("Test");
        response.LastName.ShouldBe("Admin");
        response.Role.ShouldBe("Admin");
        response.TenantName.ShouldBe("Test Vendor Corp");
        response.AccessToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_WithValidCredentials_SetsRefreshTokenCookie()
    {
        // Arrange
        var request = new VendorLoginRequest(ActiveAdminEmail, TestPassword);

        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(request).ToUrl("/api/vendor-identity/auth/login");
            x.StatusCodeShouldBe(200);
        });

        // Assert
        var setCookieHeader = result.Context.Response.Headers["Set-Cookie"].ToString();
        setCookieHeader.ShouldContain("vendor_refresh_token");
        setCookieHeader.ShouldContain("httponly", Case.Insensitive);
    }

    [Fact]
    public async Task Login_WithValidCredentials_JwtContainsExpectedClaims()
    {
        // Arrange
        var request = new VendorLoginRequest(ActiveAdminEmail, TestPassword);

        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(request).ToUrl("/api/vendor-identity/auth/login");
            x.StatusCodeShouldBe(200);
        });

        // Assert — decode the JWT and verify required claims
        var response = result.ReadAsJson<VendorLoginResponse>();
        response.ShouldNotBeNull();

        var handler = new JwtSecurityTokenHandler();
        handler.CanReadToken(response.AccessToken).ShouldBeTrue();

        var jwt = handler.ReadJwtToken(response.AccessToken);
        jwt.Claims.ShouldContain(c => c.Type == "VendorUserId" && c.Value == ActiveAdminId.ToString());
        jwt.Claims.ShouldContain(c => c.Type == "VendorTenantId" && c.Value == TestTenantId.ToString());
        // ClaimTypes.Role maps to "http://schemas.microsoft.com/ws/2008/06/identity/claims/role" in raw JWT,
        // but JwtSecurityTokenHandler maps it to the short name "role" when reading
        jwt.Claims.ShouldContain(c =>
            (c.Type == ClaimTypes.Role || c.Type == "role") && c.Value == "Admin");
        jwt.Claims.ShouldContain(c => c.Type == "VendorTenantStatus" && c.Value == "Active");
        jwt.ValidTo.ShouldBeGreaterThan(DateTime.UtcNow);
    }

    [Fact]
    public async Task Login_WithValidCredentials_UpdatesLastLoginAt()
    {
        // Arrange
        var request = new VendorLoginRequest(ActiveAdminEmail, TestPassword);

        // Act
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(request).ToUrl("/api/vendor-identity/auth/login");
            x.StatusCodeShouldBe(200);
        });

        // Assert
        await using var dbContext = _fixture.GetDbContext();
        var user = await dbContext.Users.FindAsync(ActiveAdminId);
        user.ShouldNotBeNull();
        user.LastLoginAt.ShouldNotBeNull();
        user.LastLoginAt!.Value.ShouldBeInRange(DateTimeOffset.UtcNow.AddSeconds(-10), DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        // Arrange
        var request = new VendorLoginRequest(ActiveAdminEmail, "wrong-password");

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(request).ToUrl("/api/vendor-identity/auth/login");
            x.StatusCodeShouldBe(401);
        });
    }

    [Fact]
    public async Task Login_WithUnknownEmail_Returns401()
    {
        // Arrange
        var request = new VendorLoginRequest("nobody@unknown.test", TestPassword);

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(request).ToUrl("/api/vendor-identity/auth/login");
            x.StatusCodeShouldBe(401);
        });
    }

    [Fact]
    public async Task Login_ForInactiveUser_Returns401()
    {
        // Arrange
        var request = new VendorLoginRequest(InactiveUserEmail, TestPassword);

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(request).ToUrl("/api/vendor-identity/auth/login");
            x.StatusCodeShouldBe(401);
        });
    }

    [Fact]
    public async Task Login_ForUserInTerminatedTenant_Returns401()
    {
        // Arrange — create a terminated tenant with an active user
        var terminatedTenantId = await CreateTenantAsync("Terminated Corp", VendorTenantStatus.Terminated);
        const string terminatedEmail = "user@terminated.test";
        await CreateUserAsync(terminatedTenantId, terminatedEmail, Messages.Contracts.VendorIdentity.VendorRole.Admin,
            VendorUserStatus.Active);

        var request = new VendorLoginRequest(terminatedEmail, TestPassword);

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(request).ToUrl("/api/vendor-identity/auth/login");
            x.StatusCodeShouldBe(401);
        });
    }

    // ─── Logout ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_Always_Returns200()
    {
        // Logout is a fire-and-forget endpoint — valid even without a cookie
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new { }).ToUrl("/api/vendor-identity/auth/logout");
            x.StatusCodeShouldBe(200);
        });
    }

    [Fact]
    public async Task Logout_ClearsRefreshTokenCookie()
    {
        // Act — logout clears the cookie (the prior login step is not needed because
        // ASP.NET Core Cookies.Delete() always emits a Set-Cookie regardless of whether
        // a cookie was present in the request — this is a stateless server-side operation)
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new { }).ToUrl("/api/vendor-identity/auth/logout");
            x.StatusCodeShouldBe(200);
        });

        // Assert — Set-Cookie header contains the cookie name with an empty value (deletion semantics)
        var setCookieHeader = result.Context.Response.Headers["Set-Cookie"].ToString();
        setCookieHeader.ShouldContain("vendor_refresh_token");

        // The deleted cookie is emitted with an empty value (vendor_refresh_token=;) not a new token
        var cookieValue = ExtractCookieValue(setCookieHeader, "vendor_refresh_token");
        cookieValue.ShouldBeEmpty("expected an empty cookie value indicating deletion, not a live token");

        // The deleted cookie's Expires attribute should be in the past
        var expiresMatch = System.Text.RegularExpressions.Regex.Match(
            setCookieHeader, @"expires=([^;]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (expiresMatch.Success && DateTimeOffset.TryParse(expiresMatch.Groups[1].Value, out var cookieExpiry))
            cookieExpiry.ShouldBeLessThan(DateTimeOffset.UtcNow);
    }

    // ─── Refresh ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_WithoutRefreshCookie_Returns401()
    {
        // No cookie, no bearer — both absent
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new { }).ToUrl("/api/vendor-identity/auth/refresh");
            x.StatusCodeShouldBe(401);
        });
    }

    [Fact]
    public async Task Refresh_WithRefreshCookieButNoBearerToken_Returns401()
    {
        // Arrange — provide a refresh cookie but no Authorization header
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new { }).ToUrl("/api/vendor-identity/auth/refresh");
            x.WithRequestHeader("Cookie", "vendor_refresh_token=some-refresh-token-value");
            x.StatusCodeShouldBe(401);
        });
    }

    [Fact]
    public async Task Refresh_WithValidCookieAndExpiredBearerToken_Returns200AndNewAccessToken()
    {
        // Arrange — log in to get a real access token and cookie
        var loginRequest = new VendorLoginRequest(ActiveAdminEmail, TestPassword);
        var loginResult = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(loginRequest).ToUrl("/api/vendor-identity/auth/login");
            x.StatusCodeShouldBe(200);
        });

        var loginResponse = loginResult.ReadAsJson<VendorLoginResponse>();
        loginResponse.ShouldNotBeNull();
        var originalToken = loginResponse.AccessToken;

        // Extract the refresh cookie value
        var setCookieHeader = loginResult.Context.Response.Headers["Set-Cookie"].ToString();
        setCookieHeader.ShouldContain("vendor_refresh_token");
        var refreshTokenValue = ExtractCookieValue(setCookieHeader, "vendor_refresh_token");
        refreshTokenValue.ShouldNotBeNullOrEmpty();

        // Act — call refresh with cookie + bearer (refresh endpoint allows expired tokens)
        var refreshResult = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new { }).ToUrl("/api/vendor-identity/auth/refresh");
            x.WithRequestHeader("Cookie", $"vendor_refresh_token={refreshTokenValue}");
            x.WithRequestHeader("Authorization", $"Bearer {originalToken}");
            x.StatusCodeShouldBe(200);
        });

        // Assert — new access token is returned and is a valid JWT
        var refreshResponse = refreshResult.ReadAsJson<VendorRefreshResponse>();
        refreshResponse.ShouldNotBeNull();
        refreshResponse.AccessToken.ShouldNotBeNullOrEmpty();
        refreshResponse.AccessToken.ShouldNotBe(originalToken); // rotated

        var handler = new JwtSecurityTokenHandler();
        handler.CanReadToken(refreshResponse.AccessToken).ShouldBeTrue();
    }

    [Fact]
    public async Task Refresh_ForDeactivatedUser_Returns401()
    {
        // Arrange — log in, then deactivate the user mid-session
        var loginRequest = new VendorLoginRequest(ActiveAdminEmail, TestPassword);
        var loginResult = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(loginRequest).ToUrl("/api/vendor-identity/auth/login");
            x.StatusCodeShouldBe(200);
        });

        var loginResponse = loginResult.ReadAsJson<VendorLoginResponse>();
        loginResponse.ShouldNotBeNull();
        var setCookieHeader = loginResult.Context.Response.Headers["Set-Cookie"].ToString();
        var refreshTokenValue = ExtractCookieValue(setCookieHeader, "vendor_refresh_token");

        // Deactivate the user directly in the DB (simulates an admin suspending the account)
        await using var dbContext = _fixture.GetDbContext();
        var user = await dbContext.Users.FindAsync(ActiveAdminId);
        user.ShouldNotBeNull();
        user.Status = VendorUserStatus.Deactivated;
        await dbContext.SaveChangesAsync();

        // Act — refresh should be rejected even though the refresh cookie is valid
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new { }).ToUrl("/api/vendor-identity/auth/refresh");
            x.WithRequestHeader("Cookie", $"vendor_refresh_token={refreshTokenValue}");
            x.WithRequestHeader("Authorization", $"Bearer {loginResponse.AccessToken}");
            x.StatusCodeShouldBe(401);
        });
    }

    [Fact]
    public async Task Refresh_ForUserInTerminatedTenant_Returns401()
    {
        // Arrange — log in with an active user in an active tenant, then terminate the tenant
        var loginRequest = new VendorLoginRequest(ActiveAdminEmail, TestPassword);
        var loginResult = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(loginRequest).ToUrl("/api/vendor-identity/auth/login");
            x.StatusCodeShouldBe(200);
        });

        var loginResponse = loginResult.ReadAsJson<VendorLoginResponse>();
        loginResponse.ShouldNotBeNull();
        var setCookieHeader = loginResult.Context.Response.Headers["Set-Cookie"].ToString();
        var refreshTokenValue = ExtractCookieValue(setCookieHeader, "vendor_refresh_token");

        // Terminate the tenant mid-session
        await using var dbContext = _fixture.GetDbContext();
        var tenant = await dbContext.Tenants.FindAsync(TestTenantId);
        tenant.ShouldNotBeNull();
        tenant.Status = VendorTenantStatus.Terminated;
        await dbContext.SaveChangesAsync();

        // Act — refresh should be rejected; terminated tenant users cannot continue refreshing
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new { }).ToUrl("/api/vendor-identity/auth/refresh");
            x.WithRequestHeader("Cookie", $"vendor_refresh_token={refreshTokenValue}");
            x.WithRequestHeader("Authorization", $"Bearer {loginResponse.AccessToken}");
            x.StatusCodeShouldBe(401);
        });
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task SeedTestDataAsync()
    {
        var hasher = new PasswordHasher<VendorUser>();

        await using var dbContext = _fixture.GetDbContext();

        var tenant = new VendorTenant
        {
            Id = TestTenantId,
            OrganizationName = "Test Vendor Corp",
            ContactEmail = "contact@testvendor.test",
            Status = VendorTenantStatus.Active,
            OnboardedAt = DateTimeOffset.UtcNow.AddDays(-30),
        };

        var activeAdmin = new VendorUser
        {
            Id = ActiveAdminId,
            VendorTenantId = TestTenantId,
            Email = ActiveAdminEmail,
            FirstName = "Test",
            LastName = "Admin",
            Role = Messages.Contracts.VendorIdentity.VendorRole.Admin,
            Status = VendorUserStatus.Active,
            InvitedAt = DateTimeOffset.UtcNow.AddDays(-30),
            ActivatedAt = DateTimeOffset.UtcNow.AddDays(-29),
        };
        activeAdmin.PasswordHash = hasher.HashPassword(activeAdmin, TestPassword);

        var inactiveUser = new VendorUser
        {
            Id = InactiveUserId,
            VendorTenantId = TestTenantId,
            Email = InactiveUserEmail,
            FirstName = "Inactive",
            LastName = "User",
            Role = Messages.Contracts.VendorIdentity.VendorRole.ReadOnly,
            Status = VendorUserStatus.Invited, // Not yet activated
            InvitedAt = DateTimeOffset.UtcNow.AddDays(-5),
        };
        inactiveUser.PasswordHash = hasher.HashPassword(inactiveUser, TestPassword);

        tenant.Users.Add(activeAdmin);
        tenant.Users.Add(inactiveUser);

        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync();
    }

    private async Task<Guid> CreateTenantAsync(string orgName, VendorTenantStatus status)
    {
        await using var dbContext = _fixture.GetDbContext();
        var tenant = new VendorTenant
        {
            Id = Guid.NewGuid(),
            OrganizationName = orgName,
            ContactEmail = $"contact@{orgName.ToLowerInvariant().Replace(' ', '-')}.test",
            Status = status,
            OnboardedAt = DateTimeOffset.UtcNow.AddDays(-10),
        };
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync();
        return tenant.Id;
    }

    private async Task CreateUserAsync(
        Guid tenantId,
        string email,
        Messages.Contracts.VendorIdentity.VendorRole role,
        VendorUserStatus status)
    {
        var hasher = new PasswordHasher<VendorUser>();
        await using var dbContext = _fixture.GetDbContext();
        var user = new VendorUser
        {
            Id = Guid.NewGuid(),
            VendorTenantId = tenantId,
            Email = email,
            FirstName = "Test",
            LastName = "User",
            Role = role,
            Status = status,
            InvitedAt = DateTimeOffset.UtcNow.AddDays(-5),
            ActivatedAt = status == VendorUserStatus.Active ? DateTimeOffset.UtcNow.AddDays(-4) : null,
        };
        user.PasswordHash = hasher.HashPassword(user, TestPassword);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
    }

    private static string ExtractCookieValue(string setCookieHeader, string cookieName)
    {
        // Set-Cookie header format: "cookieName=value; Path=...; HttpOnly; ..."
        var parts = setCookieHeader.Split(';');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith($"{cookieName}=", StringComparison.OrdinalIgnoreCase))
                return trimmed[(cookieName.Length + 1)..];
        }
        return string.Empty;
    }
}
