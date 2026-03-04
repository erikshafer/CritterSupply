using Microsoft.AspNetCore.SignalR;

namespace Storefront.Api;

/// <summary>
/// SignalR hub for real-time storefront updates.
/// Receives messages from Wolverine's SignalR transport and pushes to connected clients.
/// Messages are wrapped in CloudEvents envelope by Wolverine.
/// </summary>
public sealed class StorefrontHub : Hub
{
    /// <summary>
    /// Called when a client connects to the hub.
    /// Adds connection to customer-specific group for targeted message delivery.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        // Extract customerId from query string
        var customerId = Context.GetHttpContext()?.Request.Query["customerId"].ToString();

        if (!string.IsNullOrEmpty(customerId) && Guid.TryParse(customerId, out var customerGuid))
        {
            // Add connection to customer-specific group
            // Wolverine will target messages to this group based on IStorefrontWebSocketMessage.CustomerId
            await Groups.AddToGroupAsync(Context.ConnectionId, $"customer:{customerGuid}");
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called when a client disconnects from the hub.
    /// Cleanup happens automatically via SignalR group management.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
