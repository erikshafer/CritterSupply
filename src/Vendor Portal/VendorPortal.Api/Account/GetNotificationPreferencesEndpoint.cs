using System.Security.Claims;
using Marten;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using VendorPortal.VendorAccount;
using Wolverine.Http;

namespace VendorPortal.Api.Account;

public sealed record NotificationPreferencesResponse(
    bool LowStockAlerts,
    bool ChangeRequestDecisions,
    bool InventoryUpdates,
    bool SalesMetrics);

public sealed class GetNotificationPreferencesEndpoint
{
    [WolverineGet("/api/vendor-portal/account/preferences")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public static async Task<IResult> GetNotificationPreferences(
        HttpContext httpContext,
        IQuerySession querySession,
        CancellationToken ct)
    {
        var tenantIdString = httpContext.User.FindFirst("VendorTenantId")?.Value;
        if (tenantIdString is null || !Guid.TryParse(tenantIdString, out var tenantId))
            return Results.Unauthorized();

        var account = await querySession.LoadAsync<VendorPortal.VendorAccount.VendorAccount>(tenantId, ct);

        // Return all-enabled defaults if account hasn't been initialized yet
        var prefs = account?.NotificationPreferences ?? NotificationPreferences.AllEnabled;

        return Results.Ok(new NotificationPreferencesResponse(
            prefs.LowStockAlerts,
            prefs.ChangeRequestDecisions,
            prefs.InventoryUpdates,
            prefs.SalesMetrics));
    }
}
