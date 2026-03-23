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
