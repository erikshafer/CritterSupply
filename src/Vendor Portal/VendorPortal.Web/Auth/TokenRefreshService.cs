namespace VendorPortal.Web.Auth;

/// <summary>
/// Background timer that proactively refreshes the JWT access token.
/// Runs every 13 minutes (before the 15-minute access token expires).
///
/// GOTCHA (WASM vs Blazor Server):
/// In Blazor Server, IHostedService would be used. In Blazor WASM, there's no
/// IHostedService — background work must use System.Threading.Timer, managed
/// within the Blazor component lifecycle. This service is registered as a
/// singleton and started by MainLayout.
///
/// GOTCHA: WASM apps are frozen when the browser tab is not active.
/// On tab resume, CheckAndRefreshIfNeededAsync should be called to handle
/// any timer drift caused by browser tab throttling.
/// </summary>
public sealed class TokenRefreshService : IAsyncDisposable
{
    private readonly VendorAuthState _authState;
    private readonly VendorAuthService _authService;
    private readonly ILogger<TokenRefreshService> _logger;

    private Timer? _timer;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(13);
    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromMinutes(3);

    public TokenRefreshService(
        VendorAuthState authState,
        VendorAuthService authService,
        ILogger<TokenRefreshService> logger)
    {
        _authState = authState;
        _authService = authService;
        _logger = logger;
    }

    public void Start()
    {
        _timer = new Timer(async _ =>
        {
            try
            {
                await TryRefreshAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in token refresh timer callback");
            }
        }, null, RefreshInterval, RefreshInterval);
        _logger.LogInformation("Token refresh timer started (interval: {Interval})", RefreshInterval);
    }

    /// <summary>
    /// Called on tab focus restore to handle throttled timers.
    /// </summary>
    public async Task CheckAndRefreshIfNeededAsync()
    {
        if (!_authState.IsAuthenticated)
            return;

        var timeUntilExpiry = _authState.TokenExpiresAt - DateTimeOffset.UtcNow;
        if (timeUntilExpiry <= ExpiryBuffer)
        {
            _logger.LogInformation("Token near expiry ({TimeLeft}), refreshing proactively", timeUntilExpiry);
            await TryRefreshAsync();
        }
    }

    private async Task TryRefreshAsync()
    {
        if (!_authState.IsAuthenticated)
            return;

        _logger.LogDebug("Attempting token refresh...");
        var success = await _authService.RefreshAsync();

        if (!success)
            _logger.LogWarning("Token refresh failed — user will be logged out on next request");
        else
            _logger.LogDebug("Token refreshed successfully. New expiry: {Expiry}", _authState.TokenExpiresAt);
    }

    public async ValueTask DisposeAsync()
    {
        if (_timer is not null)
        {
            await _timer.DisposeAsync();
        }
    }
}
