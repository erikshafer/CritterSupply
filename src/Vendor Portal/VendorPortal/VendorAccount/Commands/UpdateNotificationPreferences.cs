namespace VendorPortal.VendorAccount.Commands;

/// <summary>
/// Updates notification preference toggles for a vendor account.
/// Opt-out model: all notifications are enabled by default.
/// Vendors can explicitly disable individual notification types.
/// </summary>
public sealed record UpdateNotificationPreferencesCommand(
    Guid VendorTenantId,
    bool LowStockAlerts,
    bool ChangeRequestDecisions,
    bool InventoryUpdates,
    bool SalesMetrics);
