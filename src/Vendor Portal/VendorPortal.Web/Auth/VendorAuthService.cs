using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;

namespace VendorPortal.Web.Auth;

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginResponse(
    string AccessToken,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    string TenantName);

public sealed record RefreshResponse(string AccessToken);

/// <summary>
/// Handles authentication flows: login, refresh, logout.
///
/// GOTCHA: In Blazor WASM, HttpClient is preconfigured with the app's base address.
/// For cross-origin API calls, named clients with explicit base addresses are required.
/// Cookie-based refresh tokens work in WASM via the browser's cookie jar —
/// but only with CORS AllowCredentials configured on the server.
/// </summary>
public sealed class VendorAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly VendorAuthState _authState;
    private readonly NavigationManager _navigationManager;
    private readonly ILogger<VendorAuthService> _logger;

    public VendorAuthService(
        IHttpClientFactory httpClientFactory,
        VendorAuthState authState,
        NavigationManager navigationManager,
        ILogger<VendorAuthService> logger)
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
            var client = _httpClientFactory.CreateClient("VendorIdentityApi");
            var response = await client.PostAsJsonAsync("/api/vendor-identity/auth/login",
                new LoginRequest(email, password));

            if (!response.IsSuccessStatusCode)
                return false;

            var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (loginResponse is null)
                return false;

            var expiresAt = ParseTokenExpiry(loginResponse.AccessToken);
            var (tenantId, userId) = ParseTenantAndUserId(loginResponse.AccessToken);

            if (tenantId == Guid.Empty || userId == Guid.Empty)
            {
                _logger.LogError("Login response token is missing VendorTenantId or VendorUserId claims");
                return false;
            }

            _authState.SetAuthenticated(
                accessToken: loginResponse.AccessToken,
                email: loginResponse.Email,
                firstName: loginResponse.FirstName,
                lastName: loginResponse.LastName,
                role: loginResponse.Role,
                tenantName: loginResponse.TenantName,
                vendorTenantId: tenantId,
                vendorUserId: userId,
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
            var client = _httpClientFactory.CreateClient("VendorIdentityApi");

            if (!string.IsNullOrEmpty(_authState.AccessToken))
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _authState.AccessToken);

            var response = await client.PostAsJsonAsync("/api/vendor-identity/auth/refresh", new { });

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
            var client = _httpClientFactory.CreateClient("VendorIdentityApi");
            await client.PostAsJsonAsync("/api/vendor-identity/auth/logout", new { });
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

    private (Guid tenantId, Guid userId) ParseTenantAndUserId(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            var tenantIdStr = jwt.Claims.FirstOrDefault(c => c.Type == "VendorTenantId")?.Value;
            var userIdStr = jwt.Claims.FirstOrDefault(c => c.Type == "VendorUserId")?.Value;

            var tenantId = Guid.TryParse(tenantIdStr, out var t) ? t : Guid.Empty;
            var userId = Guid.TryParse(userIdStr, out var u) ? u : Guid.Empty;

            if (tenantId == Guid.Empty || userId == Guid.Empty)
                _logger.LogWarning("Token is missing required VendorTenantId or VendorUserId claims");

            return (tenantId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse tenant/user IDs from token");
            return (Guid.Empty, Guid.Empty);
        }
    }
}
