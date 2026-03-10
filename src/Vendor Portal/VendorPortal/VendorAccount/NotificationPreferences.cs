namespace VendorPortal.VendorAccount;

/// <summary>
/// Notification preference toggles for a vendor account.
/// Opt-out model: all notifications are enabled by default when a vendor account is created.
/// Vendors can explicitly disable notifications they don't want.
/// </summary>
public sealed record NotificationPreferences
{
    /// <summary>Receive low-stock alert notifications via SignalR.</summary>
    public bool LowStockAlerts { get; init; } = true;

    /// <summary>Receive change request decision notifications (approved/rejected/needs-more-info).</summary>
    public bool ChangeRequestDecisions { get; init; } = true;

    /// <summary>Receive inventory level update notifications.</summary>
    public bool InventoryUpdates { get; init; } = true;

    /// <summary>Receive sales metric update notifications.</summary>
    public bool SalesMetrics { get; init; } = true;

    /// <summary>Default instance with all notifications enabled.</summary>
    public static NotificationPreferences AllEnabled => new();
}
