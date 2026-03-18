using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;

namespace Backoffice.Web.Auth;

public sealed record LoginRequest(string Email, string Password);

/// <summary>
/// Matches the JSON response from BackofficeIdentity.Api's login endpoint.
/// The API returns user info nested under the "user" property.
/// </summary>
public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    BackofficeUserInfoResponse User);

/// <summary>
/// Nested user info returned by the login endpoint.
/// Role is kebab-case (e.g., "system-admin") to match JWT claims.
/// </summary>
public sealed record BackofficeUserInfoResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Role);

public sealed record RefreshResponse(string AccessToken);

/// <summary>
/// Handles authentication flows: login, refresh, logout.
///
/// GOTCHA: In Blazor WASM, HttpClient is preconfigured with the app's base address.
/// For cross-origin API calls, named clients with explicit base addresses are required.
/// Cookie-based refresh tokens work in WASM via the browser's cookie jar —
/// but only with CORS AllowCredentials configured on the server.
/// </summary>
public sealed class BackofficeAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BackofficeAuthState _authState;
    private readonly NavigationManager _navigationManager;
    private readonly ILogger<BackofficeAuthService> _logger;

    public BackofficeAuthService(
        IHttpClientFactory httpClientFactory,
        BackofficeAuthState authState,
        NavigationManager navigationManager,
        ILogger<BackofficeAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _authState = authState;
        _navigationManager = navigationManager;
        _logger = logger;
    }

    public async Task<bool> LoginAsync(string email, string password)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("BackofficeIdentityApi");
            var response = await client.PostAsJsonAsync("/api/backoffice-identity/auth/login",
                new LoginRequest(email, password));

            if (!response.IsSuccessStatusCode)
                return false;

            var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (loginResponse is null || loginResponse.User is null)
                return false;

            var expiresAt = ParseTokenExpiry(loginResponse.AccessToken);
            var userId = ParseAdminUserId(loginResponse.AccessToken);

            if (userId == Guid.Empty)
            {
                _logger.LogError("Login response token is missing sub (user ID) claim");
                return false;
            }

            _authState.SetAuthenticated(
                accessToken: loginResponse.AccessToken,
                email: loginResponse.User.Email,
                firstName: loginResponse.User.FirstName,
                lastName: loginResponse.User.LastName,
                role: loginResponse.User.Role,
                adminUserId: userId,
                expiresAt: expiresAt);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login request failed for email {Email}", email);
            return false;
        }
    }

    public async Task<bool> RefreshAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("BackofficeIdentityApi");

            if (!string.IsNullOrEmpty(_authState.AccessToken))
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _authState.AccessToken);

            var response = await client.PostAsJsonAsync("/api/backoffice-identity/auth/refresh", new { });

            if (!response.IsSuccessStatusCode)
                return false;

            var refreshResponse = await response.Content.ReadFromJsonAsync<RefreshResponse>();
            if (refreshResponse is null || string.IsNullOrEmpty(refreshResponse.AccessToken))
                return false;

            var expiresAt = ParseTokenExpiry(refreshResponse.AccessToken);
            _authState.UpdateAccessToken(refreshResponse.AccessToken, expiresAt);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token refresh request failed");
            return false;
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("BackofficeIdentityApi");
            await client.PostAsJsonAsync("/api/backoffice-identity/auth/logout", new { });
        }
        finally
        {
            _authState.ClearAuthentication();
            _navigationManager.NavigateTo("/login?signedOut=true");
        }
    }

    private DateTimeOffset ParseTokenExpiry(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            return new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero);
        }
        catch (Exception ex)
        {
            // Return immediate expiry to force re-authentication rather than granting
            // an assumed 15-minute window on a potentially malformed token.
            _logger.LogError(ex, "Failed to parse token expiry — forcing immediate expiry");
            return DateTimeOffset.UtcNow;
        }
    }

    private Guid ParseAdminUserId(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            // JWT generator uses JwtRegisteredClaimNames.Sub ("sub") for user ID
            var userIdStr = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;

            var userId = Guid.TryParse(userIdStr, out var u) ? u : Guid.Empty;

            if (userId == Guid.Empty)
                _logger.LogWarning("Token is missing required sub (AdminUserId) claim");

            return userId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse AdminUserId from token");
            return Guid.Empty;
        }
    }
}
