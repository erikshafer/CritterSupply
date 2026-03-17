using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Backoffice.Api;

/// <summary>
/// SignalR hub for real-time Backoffice notifications.
///
/// Hub groups are role-based for targeted message delivery:
/// - role:executive - Live dashboard metrics
/// - role:operations-manager - System alerts and warnings
/// - role:customer-service - Customer service notifications
/// - role:warehouse-manager - Fulfillment updates
/// - role:warehouse-clerk - Fulfillment updates
/// - role:system-admin - System health and diagnostics
///
/// This hub only supports server→client push (no client→server commands).
/// Inherits from Hub (not WolverineHub) since bidirectional messaging is not needed.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class BackofficeHub : Hub
{
    private readonly ILogger<BackofficeHub> _logger;

    public BackofficeHub(ILogger<BackofficeHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Called when a client connects to the hub.
    /// Adds the connection to role-based groups based on authenticated user's roles.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst("UserId")?.Value;
        var role = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        if (userId is null || role is null)
        {
            _logger.LogWarning("Hub connection rejected: missing UserId or Role claims");
            Context.Abort();
            return;
        }

        // Add to role-based group for targeted broadcasts
        // Normalize role to lowercase kebab-case for group naming consistency
        var groupName = $"role:{role.ToLowerInvariant()}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        _logger.LogInformation("Backoffice hub connected: user={UserId} role={Role} group={Group}",
            userId, role, groupName);

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called when a client disconnects from the hub.
    /// Automatic group cleanup is handled by SignalR framework.
    /// </summary>
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Backoffice hub disconnected: connectionId={ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
