using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using VendorPortal.VendorAccount;
using Wolverine;
using Wolverine.Http;

namespace VendorPortal.Api.Account;

public sealed record UpdateNotificationPreferencesRequest(
    bool LowStockAlerts,
    bool ChangeRequestDecisions,
    bool InventoryUpdates,
    bool SalesMetrics);

public sealed class UpdateNotificationPreferencesEndpoint
{
    [WolverinePut("/api/vendor-portal/account/preferences")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public static async Task<IResult> UpdateNotificationPreferences(
        UpdateNotificationPreferencesRequest request,
        HttpContext httpContext,
        IMessageBus bus,
        CancellationToken ct)
    {
        var tenantIdString = httpContext.User.FindFirst("VendorTenantId")?.Value;
        if (tenantIdString is null || !Guid.TryParse(tenantIdString, out var tenantId))
            return Results.Unauthorized();

        var command = new UpdateNotificationPreferencesCommand(
            VendorTenantId: tenantId,
            LowStockAlerts: request.LowStockAlerts,
            ChangeRequestDecisions: request.ChangeRequestDecisions,
            InventoryUpdates: request.InventoryUpdates,
            SalesMetrics: request.SalesMetrics);

        var updated = await bus.InvokeAsync<NotificationPreferences?>(command, ct);
        if (updated is null)
            return Results.NotFound("Vendor account not found. Please contact support.");

        return Results.Ok(new NotificationPreferencesResponse(
            updated.LowStockAlerts,
            updated.ChangeRequestDecisions,
            updated.InventoryUpdates,
            updated.SalesMetrics));
    }
}
