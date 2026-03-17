namespace Backoffice.Web.Auth;

/// <summary>
/// Background token refresh service for Blazor WASM.
///
/// GOTCHA: WASM doesn't support IHostedService (no server-side host).
/// Instead, use System.Threading.Timer and start it manually in Program.cs.
///
/// Timer checks token expiry every 30 seconds. If token expires in less than 5 minutes,
/// triggers a refresh. This ensures the user never hits a 401 mid-session.
/// </summary>
public sealed class TokenRefreshService : IAsyncDisposable
{
    private readonly BackofficeAuthState _authState;
    private readonly BackofficeAuthService _authService;
    private readonly ILogger<TokenRefreshService> _logger;
    private Timer? _timer;

    public TokenRefreshService(
        BackofficeAuthState authState,
        BackofficeAuthService authService,
        ILogger<TokenRefreshService> logger)
    {
        _authState = authState;
        _authService = authService;
        _logger = logger;
    }

    public void Start()
    {
        // Check every 30 seconds
        _timer = new Timer(CheckTokenExpiry, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
        _logger.LogInformation("Token refresh service started (check interval: 30s)");
    }

    private async void CheckTokenExpiry(object? state)
    {
        if (!_authState.IsAuthenticated || string.IsNullOrEmpty(_authState.AccessToken))
            return;

        var timeUntilExpiry = _authState.TokenExpiresAt - DateTimeOffset.UtcNow;

        // Refresh if token expires in less than 5 minutes
        if (timeUntilExpiry < TimeSpan.FromMinutes(5))
        {
            _logger.LogInformation("Token expires in {TimeUntilExpiry:mm\\:ss} — triggering refresh",
                timeUntilExpiry);

            var refreshed = await _authService.RefreshAsync();

            if (refreshed)
                _logger.LogInformation("Token refreshed successfully");
            else
                _logger.LogWarning("Token refresh failed — user may be logged out soon");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_timer is not null)
            await _timer.DisposeAsync();
    }
}
