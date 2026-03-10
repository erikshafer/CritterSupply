namespace VendorPortal.Analytics;

/// <summary>
/// Marten document representing an active low-stock alert for a SKU owned by a vendor tenant.
/// Deduplication rule: one active alert per VendorTenantId + Sku combination.
/// Alerts persist until explicitly acknowledged via AcknowledgeLowStockAlert.
/// </summary>
public sealed class LowStockAlert
{
    /// <summary>Document ID — composite: "{VendorTenantId}:{Sku}" (enables O(1) dedup lookup).</summary>
    public string Id { get; init; } = null!;

    public Guid VendorTenantId { get; init; }

    public string Sku { get; init; } = null!;

    public string WarehouseId { get; init; } = null!;

    /// <summary>Quantity at the time the alert was first raised (or last updated).</summary>
    public int CurrentQuantity { get; init; }

    public int ThresholdQuantity { get; init; }

    /// <summary>When the first low-stock event was detected for this SKU.</summary>
    public DateTimeOffset FirstDetectedAt { get; init; }

    /// <summary>When the most recent low-stock event updated this alert.</summary>
    public DateTimeOffset LastUpdatedAt { get; init; }

    /// <summary>True while the alert has not been acknowledged by the vendor.</summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Constructs a deterministic composite document ID for deduplication.
    /// One active alert per vendor tenant + SKU.
    /// </summary>
    public static string BuildId(Guid vendorTenantId, string sku)
        => $"{vendorTenantId}:{sku}";
}
