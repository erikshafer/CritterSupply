using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace VendorPortal.Web.Auth;

/// <summary>
/// Custom AuthenticationStateProvider for Blazor WASM.
/// Reads auth state from VendorAuthState (in-memory JWT storage).
///
/// GOTCHA: In WASM, there's no server-side HttpContext.
/// Authentication state must be maintained entirely in memory.
/// </summary>
public sealed class VendorAuthStateProvider : AuthenticationStateProvider
{
    private readonly VendorAuthState _authState;
    private readonly ILogger<VendorAuthStateProvider> _logger;

    public VendorAuthStateProvider(VendorAuthState authState, ILogger<VendorAuthStateProvider> logger)
    {
        _authState = authState;
        _logger = logger;
        _authState.OnChange += OnAuthStateChanged;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (!_authState.IsAuthenticated || string.IsNullOrEmpty(_authState.AccessToken))
        {
            var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
            return Task.FromResult(new AuthenticationState(anonymous));
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(_authState.AccessToken);

            if (jwt.ValidTo < DateTime.UtcNow)
            {
                _logger.LogInformation("Access token is expired — returning anonymous state");
                var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
                return Task.FromResult(new AuthenticationState(anonymous));
            }

            var identity = new ClaimsIdentity(jwt.Claims, "jwt");
            var user = new ClaimsPrincipal(identity);
            return Task.FromResult(new AuthenticationState(user));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse access token — returning anonymous state");
            var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
            return Task.FromResult(new AuthenticationState(anonymous));
        }
    }

    private void OnAuthStateChanged() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}
