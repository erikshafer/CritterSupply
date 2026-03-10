using Marten;
using Microsoft.Extensions.Logging;
using VendorPortal.VendorAccount.Commands;

namespace VendorPortal.VendorAccount.Handlers;

/// <summary>
/// Updates notification preference toggles on the vendor's account.
/// Opt-out model: all notifications enabled by default, vendor disables what they don't want.
/// </summary>
public static class UpdateNotificationPreferencesHandler
{
    public static async Task<NotificationPreferences?> Handle(
        UpdateNotificationPreferencesCommand command,
        IDocumentSession session,
        ILogger logger,
        CancellationToken ct)
    {
        var account = await session.LoadAsync<VendorAccount>(command.VendorTenantId, ct);
        if (account is null)
        {
            logger.LogWarning(
                "Cannot update notification preferences — VendorAccount not found for tenant {TenantId}",
                command.VendorTenantId);
            return null;
        }

        account.NotificationPreferences = new NotificationPreferences
        {
            LowStockAlerts = command.LowStockAlerts,
            ChangeRequestDecisions = command.ChangeRequestDecisions,
            InventoryUpdates = command.InventoryUpdates,
            SalesMetrics = command.SalesMetrics,
        };
        account.UpdatedAt = DateTimeOffset.UtcNow;

        session.Store(account);
        await session.SaveChangesAsync(ct);

        logger.LogInformation(
            "Updated notification preferences for tenant {TenantId}: " +
            "LowStock={LowStock}, ChangeRequests={ChangeRequests}, Inventory={Inventory}, Sales={Sales}",
            command.VendorTenantId,
            command.LowStockAlerts, command.ChangeRequestDecisions,
            command.InventoryUpdates, command.SalesMetrics);

        return account.NotificationPreferences;
    }
}
