using Alba;
using Shouldly;
using Xunit;

namespace CustomerIdentity.Api.IntegrationTests;

public class AuthenticationTests : IClassFixture<TestFixture>, IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public AuthenticationTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAddressesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task login_with_valid_credentials_returns_customer_info_and_sets_cookie()
    {
        // Arrange
        var request = new { email = "alice@critter.test", password = "password" };

        // Act
        var result = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(request).ToUrl("/api/auth/login");
            scenario.StatusCodeShouldBe(200);
        });

        // Assert
        var setCookieHeader = result.Context.Response.Headers["Set-Cookie"].ToString();
        setCookieHeader.ShouldContain("CritterSupply.Auth");

        var response = result.ReadAsJson<LoginResponse>();
        response.ShouldNotBeNull();
        response.CustomerId.ShouldBe(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        response.Email.ShouldBe("alice@critter.test");
        response.FirstName.ShouldBe("Alice");
        response.LastName.ShouldBe("Anderson");
    }

    [Fact]
    public async Task login_with_invalid_email_returns_401()
    {
        // Arrange
        var request = new { email = "nonexistent@critter.test", password = "password" };

        // Act & Assert
        await _fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(request).ToUrl("/api/auth/login");
            scenario.StatusCodeShouldBe(401);
        });
    }

    [Fact]
    public async Task login_with_empty_password_succeeds_in_dev_mode()
    {
        // Arrange
        var request = new { email = "bob@critter.test", password = "" };

        // Act
        var result = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(request).ToUrl("/api/auth/login");
            scenario.StatusCodeShouldBe(200);
        });

        // Assert - Dev mode accepts any password (even empty)
        var response = result.ReadAsJson<LoginResponse>();
        response.ShouldNotBeNull();
        response.Email.ShouldBe("bob@critter.test");
        response.FirstName.ShouldBe("Bob");
    }

    [Fact]
    public async Task get_current_user_without_authentication_returns_401()
    {
        // Act & Assert
        await _fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url("/api/auth/me");
            scenario.StatusCodeShouldBe(401);
        });
    }

    [Fact]
    public async Task get_current_user_with_valid_session_returns_user_info()
    {
        // Arrange - Login first to get session cookie
        var loginRequest = new { email = "charlie@critter.test", password = "password" };
        var loginResult = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(loginRequest).ToUrl("/api/auth/login");
            scenario.StatusCodeShouldBe(200);
        });

        var setCookieHeader = loginResult.Context.Response.Headers["Set-Cookie"].ToString();
        setCookieHeader.ShouldNotBeEmpty();

        // Extract cookie value (simplified - in real scenario might need more parsing)
        var cookieValue = setCookieHeader.Split(';')[0].Replace("CritterSupply.Auth=", "");

        // Act - Use session cookie to get current user
        var result = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url("/api/auth/me");
            scenario.WithRequestHeader("Cookie", $"CritterSupply.Auth={cookieValue}");
            scenario.StatusCodeShouldBe(200);
        });

        // Assert
        var response = result.ReadAsJson<CurrentUserResponse>();
        response.ShouldNotBeNull();
        response.CustomerId.ShouldBe(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        response.Email.ShouldBe("charlie@critter.test");
        response.FirstName.ShouldBe("Charlie");
        response.LastName.ShouldBe("Chen");
    }

    [Fact]
    public async Task logout_clears_authentication_cookie()
    {
        // Arrange - Login first
        var loginRequest = new { email = "alice@critter.test", password = "password" };
        var loginResult = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(loginRequest).ToUrl("/api/auth/login");
            scenario.StatusCodeShouldBe(200);
        });

        var setCookieHeader = loginResult.Context.Response.Headers["Set-Cookie"].ToString();
        var cookieValue = setCookieHeader.Split(';')[0].Replace("CritterSupply.Auth=", "");

        // Act - Logout
        var logoutResult = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Url("/api/auth/logout");
            scenario.WithRequestHeader("Cookie", $"CritterSupply.Auth={cookieValue}");
            scenario.StatusCodeShouldBe(200);
        });

        // Assert - Cookie should be expired/cleared
        var logoutSetCookie = logoutResult.Context.Response.Headers["Set-Cookie"].ToString();
        logoutSetCookie.ShouldContain("expires=");
    }

    [Fact]
    public async Task authentication_flow_complete_scenario()
    {
        // Step 1: Login as Bob
        var loginRequest = new { email = "bob@critter.test", password = "password" };
        var loginResult = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(loginRequest).ToUrl("/api/auth/login");
            scenario.StatusCodeShouldBe(200);
        });

        var loginResponse = loginResult.ReadAsJson<LoginResponse>();
        loginResponse.Email.ShouldBe("bob@critter.test");

        var setCookieHeader = loginResult.Context.Response.Headers["Set-Cookie"].ToString();
        var cookieValue = setCookieHeader.Split(';')[0].Replace("CritterSupply.Auth=", "");

        // Step 2: Verify current user
        var meResult = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url("/api/auth/me");
            scenario.WithRequestHeader("Cookie", $"CritterSupply.Auth={cookieValue}");
            scenario.StatusCodeShouldBe(200);
        });

        var meResponse = meResult.ReadAsJson<CurrentUserResponse>();
        meResponse.Email.ShouldBe("bob@critter.test");

        // Step 3: Logout
        var logoutResult = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Url("/api/auth/logout");
            scenario.WithRequestHeader("Cookie", $"CritterSupply.Auth={cookieValue}");
            scenario.StatusCodeShouldBe(200);
        });

        // Verify logout response sets expired cookie
        var logoutSetCookie = logoutResult.Context.Response.Headers["Set-Cookie"].ToString();
        logoutSetCookie.ShouldContain("expires=");

        // Step 4: Verify accessing /me without cookie returns 401
        await _fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url("/api/auth/me");
            // Don't send cookie - verify unauthenticated access returns 401
            scenario.StatusCodeShouldBe(401);
        });
    }

    // DTOs for deserialization
    private sealed record LoginResponse(Guid CustomerId, string Email, string FirstName, string LastName);
    private sealed record CurrentUserResponse(Guid CustomerId, string Email, string FirstName, string LastName);
}
