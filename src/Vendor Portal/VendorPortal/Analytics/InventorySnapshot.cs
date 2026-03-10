namespace VendorPortal.Analytics;

/// <summary>
/// Marten document tracking current inventory levels per SKU per warehouse, scoped to a vendor tenant.
/// Updated by <see cref="InventoryAdjusted"/> and <see cref="StockReplenished"/> integration messages.
/// Used by the analytics dashboard to show real-time stock levels without a round-trip to the Inventory BC.
/// </summary>
public sealed class InventorySnapshot
{
    /// <summary>Document ID — composite: "{VendorTenantId}:{Sku}:{WarehouseId}".</summary>
    public string Id { get; init; } = null!;

    public Guid VendorTenantId { get; init; }

    public string Sku { get; init; } = null!;

    public string WarehouseId { get; init; } = null!;

    /// <summary>Most recent known quantity on hand.</summary>
    public int CurrentQuantity { get; init; }

    /// <summary>Timestamp of the last inventory event that updated this snapshot.</summary>
    public DateTimeOffset LastUpdatedAt { get; init; }

    /// <summary>
    /// Constructs a deterministic composite document ID.
    /// Enables O(1) lookups by tenant + SKU + warehouse.
    /// </summary>
    public static string BuildId(Guid vendorTenantId, string sku, string warehouseId)
        => $"{vendorTenantId}:{sku}:{warehouseId}";
}
