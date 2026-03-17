using Microsoft.AspNetCore.SignalR.Client;
using Backoffice.Web.Auth;

namespace Backoffice.Web.Hub;

/// <summary>
/// Singleton service that owns the SignalR HubConnection to BackofficeHub.
///
/// KEY PATTERN: AccessTokenProvider is a factory delegate called on every connection
/// attempt (including auto-reconnects). This ensures that after a token refresh,
/// the next reconnect uses the new token — no stale JWT in WebSocket headers.
///
/// GOTCHA: HubConnection is NOT thread-safe. Use it only from the Blazor
/// render thread (EventCallback, StateHasChanged).
/// </summary>
public sealed class BackofficeHubService : IAsyncDisposable
{
    private readonly BackofficeAuthState _authState;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BackofficeHubService> _logger;

    private HubConnection? _connection;
    public HubConnectionState State => _connection?.State ?? HubConnectionState.Disconnected;
    public event Action? OnStateChanged;

    public BackofficeHubService(
        BackofficeAuthState authState,
        IConfiguration configuration,
        ILogger<BackofficeHubService> logger)
    {
        _authState = authState;
        _configuration = configuration;
        _logger = logger;
    }

    private readonly SemaphoreSlim _connectLock = new(1, 1);

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        // Guard against concurrent reconnect calls (e.g., rapid "Reconnect" button clicks)
        if (!await _connectLock.WaitAsync(0, ct))
            return;

        try
        {
            // If connection exists and is already active (not in terminal state), do nothing.
            if (_connection is not null &&
                _connection.State is HubConnectionState.Connected or HubConnectionState.Connecting or HubConnectionState.Reconnecting)
            {
                return;
            }

            // If connection exists but is in the terminal Disconnected state (retry policy exhausted),
            // dispose the old connection before creating a fresh one.
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            var apiUrl = _configuration["ApiClients:BackofficeApiUrl"] ?? "http://localhost:5243";
            var hubUrl = $"{apiUrl}/hub/backoffice";

            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    // AccessTokenProvider is called on every connection attempt.
                    // Lambda captures _authState so reconnects always use the freshest token.
                    options.AccessTokenProvider = () =>
                        Task.FromResult<string?>(_authState.AccessToken);
                })
                .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)])
                .Build();

            _connection.Reconnecting += _ =>
            {
                _logger.LogInformation("Hub reconnecting...");
                OnStateChanged?.Invoke();
                return Task.CompletedTask;
            };

            _connection.Reconnected += _ =>
            {
                _logger.LogInformation("Hub reconnected");
                OnStateChanged?.Invoke();
                return Task.CompletedTask;
            };

            _connection.Closed += _ =>
            {
                _logger.LogWarning("Hub connection closed");
                OnStateChanged?.Invoke();
                return Task.CompletedTask;
            };

            await _connection.StartAsync(ct);
            _logger.LogInformation("Hub connected: state={State}", _connection.State);
            OnStateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to BackofficeHub");
            OnStateChanged?.Invoke();
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        if (_connection is null)
            return;

        await _connection.StopAsync();
        await _connection.DisposeAsync();
        _connection = null;
        OnStateChanged?.Invoke();
    }

    public HubConnection? Connection => _connection;

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();

        _connectLock.Dispose();
    }
}
