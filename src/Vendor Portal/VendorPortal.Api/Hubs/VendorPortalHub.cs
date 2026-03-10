using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace VendorPortal.Api.Hubs;

/// <summary>
/// SignalR hub for real-time Vendor Portal updates.
/// JWT-authenticated: VendorTenantId and VendorUserId extracted from claims only (never query string).
/// Dual group membership: vendor:{tenantId} for tenant-wide messages, user:{userId} for personal messages.
///
/// Phase 3: server→client push only — Wolverine publishes hub messages via IHubContext.
/// Phase 4 will upgrade to WolverineHub for bidirectional client→server routing (change requests).
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
        var tenantStatus = Context.User?.FindFirst("VendorTenantStatus")?.Value;
        var role = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        if (tenantId is null || userId is null)
        {
            _logger.LogWarning("Hub connection rejected: missing VendorTenantId or VendorUserId claims");
            Context.Abort();
            return;
        }

        if (tenantStatus is "Suspended" or "Terminated")
        {
            _logger.LogWarning("Hub connection rejected: tenant {TenantId} has status {Status}",
                tenantId, tenantStatus);
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"vendor:{tenantId}");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");

        _logger.LogInformation("Vendor hub connected: user={UserId} tenant={TenantId} role={Role}",
            userId, tenantId, role);

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Vendor hub disconnected: connectionId={ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
