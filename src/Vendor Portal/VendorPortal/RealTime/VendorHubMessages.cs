namespace VendorPortal.RealTime;

/// <summary>
/// Pushed to <c>vendor:{tenantId}</c> when a SKU's stock drops below its threshold.
/// Clients should update the low-stock alert count in the dashboard header badge.
/// </summary>
public sealed record LowStockAlertRaised(
    Guid VendorTenantId,
    string Sku,
    string WarehouseId,
    int CurrentQuantity,
    int ThresholdQuantity,
    DateTimeOffset DetectedAt) : IVendorTenantMessage;

/// <summary>
/// Pushed to <c>vendor:{tenantId}</c> when sales metrics change.
/// Lightweight notification: "data changed, please refresh."
/// Clients should re-fetch the dashboard summary to get updated numbers.
/// </summary>
public sealed record SalesMetricUpdated(
    Guid VendorTenantId,
    DateTimeOffset UpdatedAt) : IVendorTenantMessage;

/// <summary>
/// Pushed to <c>vendor:{tenantId}</c> when inventory levels change for a SKU.
/// Clients should update the displayed quantity without a full dashboard reload.
/// </summary>
public sealed record InventoryLevelUpdated(
    Guid VendorTenantId,
    string Sku,
    string WarehouseId,
    int NewQuantity,
    DateTimeOffset AdjustedAt) : IVendorTenantMessage;

/// <summary>
/// Pushed to <c>vendor:{tenantId}</c> when a change request status changes.
/// All users in the tenant see this broadcast — useful for team awareness.
/// Clients should refresh the change request list badge count.
/// </summary>
public sealed record ChangeRequestStatusUpdated(
    Guid VendorTenantId,
    Guid RequestId,
    string Sku,
    string Status,
    DateTimeOffset UpdatedAt) : IVendorTenantMessage;

/// <summary>
/// Pushed to <c>user:{userId}</c> as a personal decision notification.
/// Only the submitting vendor user receives this — shown as a toast with context.
/// Decision values: "Approved", "Rejected", "NeedsMoreInfo".
/// </summary>
public sealed record ChangeRequestDecisionPersonal(
    Guid VendorUserId,
    Guid RequestId,
    string Sku,
    string Decision,
    string? Reason,
    DateTimeOffset DecidedAt) : IVendorUserMessage;

/// <summary>
/// Pushed to <c>user:{userId}</c> when the user's account is deactivated.
/// Clients must disconnect the hub, clear the JWT from memory, and redirect to an "Access Revoked" page.
/// </summary>
public sealed record ForceLogout(
    Guid VendorUserId,
    string Reason,
    DateTimeOffset RevokedAt) : IVendorUserMessage;
