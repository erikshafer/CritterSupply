namespace Inventory.Management;

/// <summary>
/// Read model for tracking backorder impact across the inventory network.
/// One row per SKU — tracks active backorder count and lifecycle.
/// Async projection — dashboard read model, not on critical path.
/// </summary>
public sealed class BackorderImpactView
{
    /// <summary>
    /// SKU serves as the document identity (string key).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    /// <summary>
    /// Number of warehouses currently reporting pending backorders for this SKU.
    /// </summary>
    public int ActiveBackorderCount { get; set; }

    /// <summary>
    /// Warehouses with active backorders.
    /// </summary>
    public List<string> AffectedWarehouses { get; set; } = new();

    /// <summary>
    /// Timestamp of the most recent backorder event.
    /// </summary>
    public DateTimeOffset? LastBackorderAt { get; set; }

    /// <summary>
    /// Timestamp of the most recent backorder clearance.
    /// </summary>
    public DateTimeOffset? LastClearedAt { get; set; }
}
