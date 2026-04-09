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
        // LowStockThresholdBreached does not affect available quantity — no Identity needed
        // StockShipped added in S2 when physical tracking is implemented
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
