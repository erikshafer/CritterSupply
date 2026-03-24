namespace VendorPortal.RealTime;

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
