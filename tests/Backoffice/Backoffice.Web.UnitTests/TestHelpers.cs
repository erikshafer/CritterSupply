using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

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
/// A simple mock HttpMessageHandler for bUnit tests that returns preconfigured responses.
/// </summary>
public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _responses = new();
    private readonly HashSet<string> _pendingRequests = [];

    public void SetResponse<T>(string pathPrefix, T content)
    {
        _responses[pathPrefix] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(content, options: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            })
        };
    }

    public void SetPendingResponse(string pathPrefix)
    {
        _pendingRequests.Add(pathPrefix);
    }

    public void SetErrorResponse(string pathPrefix, HttpStatusCode statusCode)
    {
        _responses[pathPrefix] = () => new HttpResponseMessage(statusCode);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.PathAndQuery ?? "";

        if (_pendingRequests.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            // Simulate never-completing request for loading state tests
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        foreach (var (prefix, responseFactory) in _responses)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return responseFactory();
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}

/// <summary>
/// Mock IHttpClientFactory that creates HttpClient instances backed by a shared mock handler.
/// </summary>
public sealed class MockHttpClientFactory(MockHttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5243") // Backoffice API URL
        };
    }
}
