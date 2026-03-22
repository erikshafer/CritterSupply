using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace Backoffice.Web.Tests;

/// <summary>
/// Mock NavigationManager for bUnit tests.
/// </summary>
public sealed class MockNavigationManager : NavigationManager
{
    public MockNavigationManager()
    {
        Initialize("https://localhost/", "https://localhost/");
    }

    protected override void NavigateToCore(string uri, bool forceLoad)
    {
        // No-op for tests
    }
}

/// <summary>
/// Mock AuthenticationStateProvider that returns a test user with specified role.
/// </summary>
public sealed class MockAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly string _role;

    public MockAuthenticationStateProvider(string role)
    {
        _role = role;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimTypes.Email, "test@example.com"),
            new Claim(ClaimTypes.Role, _role),
            new Claim("sub", Guid.NewGuid().ToString())
        }, "test");

        var user = new ClaimsPrincipal(identity);
        return Task.FromResult(new AuthenticationState(user));
    }
}

/// <summary>
/// Mock HttpMessageHandler for testing HTTP requests.
/// </summary>
public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _sendAsync;

    public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> sendAsync)
    {
        _sendAsync = sendAsync;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return _sendAsync(request);
    }
}
