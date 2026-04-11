namespace Inventory.Management;

/// <summary>
/// Per-warehouse capacity utilization view for the Fulfillment routing engine.
/// Async multi-stream projection over ProductInventory events, keyed by warehouse.
/// Exposes total on-hand, available, reserved, committed, picked, and quarantined
/// across all SKUs at a given warehouse.
///
/// Slice 39 (P3): Unblocks Fulfillment routing engine capacity-based decisions.
/// </summary>
public sealed class FulfillmentCenterCapacityView
{
    /// <summary>
    /// Warehouse ID (e.g., "NJ-FC", "OH-FC"). Document key.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Total number of distinct SKUs stocked at this warehouse.
    /// </summary>
    public int SkuCount { get; set; }

    /// <summary>
    /// Total units available for new reservations across all SKUs.
    /// </summary>
    public int TotalAvailable { get; set; }

    /// <summary>
    /// Total units reserved (soft-hold) across all SKUs.
    /// </summary>
    public int TotalReserved { get; set; }

    /// <summary>
    /// Total units committed (hard-allocated, pre-pick) across all SKUs.
    /// </summary>
    public int TotalCommitted { get; set; }

    /// <summary>
    /// Total units picked (in bins, pre-ship) across all SKUs.
    /// </summary>
    public int TotalPicked { get; set; }

    /// <summary>
    /// Total units quarantined across all SKUs.
    /// </summary>
    public int TotalQuarantined { get; set; }

    /// <summary>
    /// Total on-hand units: Available + Reserved + Committed + Picked.
    /// </summary>
    public int TotalOnHand => TotalAvailable + TotalReserved + TotalCommitted + TotalPicked;

    /// <summary>
    /// Total units in transit outbound from this warehouse.
    /// </summary>
    public int TotalInTransitOut { get; set; }

    /// <summary>
    /// Timestamp of the most recent event applied to this view.
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; }
}
