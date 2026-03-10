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
