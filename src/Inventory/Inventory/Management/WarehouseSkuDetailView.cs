namespace Inventory.Management;

/// <summary>
/// Per-warehouse, per-SKU detail read model showing a full quantity breakdown.
/// Inline multi-stream projection keyed by UUID v5 (matching ProductInventory stream ID).
/// Used by warehouse dashboards, backoffice, and operational tooling.
/// </summary>
public sealed class WarehouseSkuDetailView
{
    /// <summary>
    /// UUID v5 stream ID matching the ProductInventory aggregate (inventory:{SKU}:{WarehouseId}).
    /// </summary>
    public Guid Id { get; set; }

    public string Sku { get; set; } = string.Empty;
    public string WarehouseId { get; set; } = string.Empty;

    /// <summary>
    /// Stock available for new reservations.
    /// </summary>
    public int AvailableQuantity { get; set; }

    /// <summary>
    /// Stock soft-held for checkout (not yet committed).
    /// </summary>
    public int ReservedQuantity { get; set; }

    /// <summary>
    /// Stock hard-allocated for confirmed orders (post-commit, pre-pick).
    /// </summary>
    public int CommittedQuantity { get; set; }

    /// <summary>
    /// Stock physically picked from bins but not yet handed to carrier.
    /// </summary>
    public int PickedQuantity { get; set; }

    /// <summary>
    /// Stock quarantined pending investigation (not available, not on-hand for fulfillment).
    /// </summary>
    public int QuarantinedQuantity { get; set; }

    /// <summary>
    /// Stock that has been transferred out from this warehouse and is in transit to another.
    /// Decremented when the transfer is received or cancelled.
    /// </summary>
    public int InTransitOutQuantity { get; set; }

    /// <summary>
    /// Total on-hand stock at this warehouse: Available + Reserved + Committed + Picked.
    /// Excludes quarantined and in-transit stock.
    /// </summary>
    public int TotalOnHand => AvailableQuantity + ReservedQuantity + CommittedQuantity + PickedQuantity;

    /// <summary>
    /// Timestamp of the most recent event applied to this view.
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; }
}
