using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace VendorPortal.Api.Hubs;

/// <summary>
/// SignalR hub for real-time Vendor Portal updates.
/// JWT-authenticated: VendorTenantId extracted from claims only (never query string).
/// POC: Uses plain Hub (not WolverineHub) for push-only — upgrade to WolverineHub in Phase 3 for bidirectional.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class VendorPortalHub : Hub
{
    private readonly ILogger<VendorPortalHub> _logger;

    public VendorPortalHub(ILogger<VendorPortalHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.User?.FindFirst("VendorTenantId")?.Value;
        var userId = Context.User?.FindFirst("VendorUserId")?.Value;
        var role = Context.User?.FindFirst("Role")?.Value;

        if (tenantId is null || userId is null)
        {
            _logger.LogWarning("Hub connection rejected: missing VendorTenantId or VendorUserId claims");
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"vendor:{tenantId}");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");

        _logger.LogInformation("Vendor hub connected: user={UserId} tenant={TenantId} role={Role}",
            userId, tenantId, role);

        await Clients.Caller.SendAsync("Connected", new
        {
            message = "Connected to Vendor Portal",
            tenantId,
            userId,
            connectedAt = DateTimeOffset.UtcNow
        });

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Vendor hub disconnected: connectionId={ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
