using Marten.Events.Projections;

namespace Inventory.Management;

/// <summary>
/// Multi-stream projection that aggregates ProductInventory events across all warehouses
/// into a single <see cref="StockAvailabilityView"/> per SKU.
/// Identity is extracted from each event's Sku field, routing events from multiple
/// ProductInventory streams (one per SKU:WarehouseId pair) into a single SKU-keyed view.
///
/// Registered as <see cref="Marten.Events.Projections.ProjectionLifecycle.Inline"/> in Program.cs.
/// </summary>
public class StockAvailabilityViewProjection : MultiStreamProjection<StockAvailabilityView, string>
{
    public StockAvailabilityViewProjection()
    {
        Identity<InventoryInitialized>(e => e.Sku);
        Identity<StockReserved>(e => e.Sku);
        Identity<ReservationReleased>(e => e.Sku);
        Identity<ReservationCommitted>(e => e.Sku);
        Identity<StockReceived>(e => e.Sku);
        Identity<StockRestocked>(e => e.Sku);
        Identity<InventoryAdjusted>(e => e.Sku);
        Identity<StockShipped>(e => e.Sku);
        Identity<ReservationExpired>(e => e.Sku);
        // LowStockThresholdBreached does not affect available quantity — no Identity needed
        // StockPicked does not change available quantity (Committed → Picked) — no view update
        // StockDiscrepancyFound is audit-only — no view update
        // BackorderRegistered/BackorderCleared do not affect available quantity
        // CycleCountInitiated/CycleCountCompleted are audit-only — adjustments via InventoryAdjusted
        // DamageRecorded/StockWrittenOff are audit-only — adjustments via InventoryAdjusted
    }

    public void Apply(StockAvailabilityView view, InventoryInitialized e)
    {
        view.Sku = e.Sku;
        view.Id = e.Sku;
        SetWarehouse(view, e.WarehouseId, e.InitialQuantity);
    }

    public void Apply(StockAvailabilityView view, StockReserved e)
    {
        var current = GetWarehouseQuantity(view, e.WarehouseId);
        SetWarehouse(view, e.WarehouseId, current - e.Quantity);
    }

    public void Apply(StockAvailabilityView view, ReservationReleased e)
    {
        var current = GetWarehouseQuantity(view, e.WarehouseId);
        SetWarehouse(view, e.WarehouseId, current + e.Quantity);
    }

    public void Apply(StockAvailabilityView view, ReservationCommitted e)
    {
        // Commit moves stock from Reserved → Committed.
        // Available quantity was already decremented on StockReserved; no change here.
    }

    public void Apply(StockAvailabilityView view, StockReceived e)
    {
        var current = GetWarehouseQuantity(view, e.WarehouseId);
        SetWarehouse(view, e.WarehouseId, current + e.Quantity);
    }

    public void Apply(StockAvailabilityView view, StockRestocked e)
    {
        var current = GetWarehouseQuantity(view, e.WarehouseId);
        SetWarehouse(view, e.WarehouseId, current + e.Quantity);
    }

    public void Apply(StockAvailabilityView view, InventoryAdjusted e)
    {
        var current = GetWarehouseQuantity(view, e.WarehouseId);
        SetWarehouse(view, e.WarehouseId, current + e.AdjustmentQuantity);
    }

    /// <summary>
    /// StockShipped removes stock that has physically left the building.
    /// The AvailableQuantity was already decremented at reservation time; this decrements
    /// the view to reflect that the stock is no longer on-hand at the warehouse.
    /// NOTE: StockAvailabilityView tracks *available* quantity, not total on-hand.
    /// Shipped stock was never available (it was reserved/committed/picked), so
    /// no change to available quantity here.
    /// </summary>
    public void Apply(StockAvailabilityView view, StockShipped e)
    {
        // No change: stock was already unavailable (Reserved → Committed → Picked → Shipped).
        // Available quantity was decremented when StockReserved was applied.
    }

    /// <summary>
    /// ReservationExpired returns stock to the available pool (same as ReservationReleased).
    /// </summary>
    public void Apply(StockAvailabilityView view, ReservationExpired e)
    {
        var current = GetWarehouseQuantity(view, e.WarehouseId);
        SetWarehouse(view, e.WarehouseId, current + e.Quantity);
    }

    // --- helpers ---

    private static int GetWarehouseQuantity(StockAvailabilityView view, string warehouseId)
    {
        var entry = view.Warehouses.FirstOrDefault(w => w.WarehouseId == warehouseId);
        return entry?.AvailableQuantity ?? 0;
    }

    private static void SetWarehouse(StockAvailabilityView view, string warehouseId, int quantity)
    {
        var index = view.Warehouses.FindIndex(w => w.WarehouseId == warehouseId);
        if (index >= 0)
        {
            view.Warehouses[index] = new WarehouseAvailability(warehouseId, quantity);
        }
        else
        {
            view.Warehouses.Add(new WarehouseAvailability(warehouseId, quantity));
        }
    }
}
