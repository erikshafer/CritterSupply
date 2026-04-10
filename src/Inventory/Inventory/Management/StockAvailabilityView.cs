namespace Inventory.Management;

/// <summary>
/// Per-warehouse availability entry within a <see cref="StockAvailabilityView"/>.
/// </summary>
public sealed record WarehouseAvailability(string WarehouseId, int AvailableQuantity);

/// <summary>
/// Multi-stream projection document keyed by SKU.
/// Aggregates available inventory across all warehouses for a given SKU.
/// Used by Fulfillment's routing engine to query warehouse-level availability
/// before sending StockReservationRequested to Inventory.
/// Registered as Inline projection — critical checkout path; stale data leads to double-booking.
/// </summary>
public sealed class StockAvailabilityView
{
    /// <summary>
    /// SKU serves as the document identity (string key).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public List<WarehouseAvailability> Warehouses { get; set; } = new();

    public int TotalAvailable => Warehouses.Sum(w => w.AvailableQuantity);
}
