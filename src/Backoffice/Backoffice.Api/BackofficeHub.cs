using Microsoft.AspNetCore.SignalR;

namespace Backoffice.Api;

/// <summary>
/// SignalR hub for real-time Backoffice notifications.
///
/// Hub groups are role-based for targeted message delivery:
/// - role:executive - Live dashboard metrics
/// - role:operations - System alerts and warnings
/// - role:cs-agent - Customer service notifications
/// - role:warehouse-clerk - Fulfillment updates
/// - role:system-admin - System health and diagnostics
///
/// This hub only supports server→client push (no client→server commands).
/// Inherits from Hub (not WolverineHub) since bidirectional messaging is not needed.
/// </summary>
public sealed class BackofficeHub : Hub
{
    /// <summary>
    /// Called when a client connects to the hub.
    /// Adds the connection to role-based groups based on authenticated user's roles.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        // Role-based group management will be implemented when authentication is added
        // For now, hub accepts all connections but messages are sent to specific groups
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called when a client disconnects from the hub.
    /// Automatic group cleanup is handled by SignalR framework.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
