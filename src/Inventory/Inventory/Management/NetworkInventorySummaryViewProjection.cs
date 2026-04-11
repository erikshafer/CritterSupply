using Marten.Events.Projections;

namespace Inventory.Management;

/// <summary>
/// Async multi-stream projection that aggregates warehouse-level inventory
/// into a network-wide summary per SKU.
/// Registered as Async — dashboard read model, tolerable staleness.
/// </summary>
public class NetworkInventorySummaryViewProjection : MultiStreamProjection<NetworkInventorySummaryView, string>
{
    public NetworkInventorySummaryViewProjection()
    {
        Identity<InventoryInitialized>(e => e.Sku);
        Identity<StockReserved>(e => e.Sku);
        Identity<ReservationReleased>(e => e.Sku);
        Identity<ReservationCommitted>(e => e.Sku);
        Identity<StockReceived>(e => e.Sku);
        Identity<StockRestocked>(e => e.Sku);
        Identity<InventoryAdjusted>(e => e.Sku);
        Identity<ReservationExpired>(e => e.Sku);
        Identity<StockTransferredOut>(e => e.Sku);
        Identity<StockTransferredIn>(e => e.Sku);
        Identity<StockQuarantined>(e => e.Sku);
        Identity<QuarantineReleased>(e => e.Sku);
        Identity<QuarantineDisposed>(e => e.Sku);
    }

    public void Apply(NetworkInventorySummaryView view, InventoryInitialized e)
    {
        view.Sku = e.Sku;
        view.Id = e.Sku;
        SetWarehouse(view, e.WarehouseId, e.InitialQuantity, 0, 0);
    }

    public void Apply(NetworkInventorySummaryView view, StockReserved e)
    {
        var wh = GetWarehouse(view, e.WarehouseId);
        SetWarehouse(view, e.WarehouseId,
            wh.AvailableQuantity - e.Quantity,
            wh.ReservedQuantity + e.Quantity,
            wh.QuarantinedQuantity);
    }

    public void Apply(NetworkInventorySummaryView view, ReservationReleased e)
    {
        var wh = GetWarehouse(view, e.WarehouseId);
        SetWarehouse(view, e.WarehouseId,
            wh.AvailableQuantity + e.Quantity,
            wh.ReservedQuantity - e.Quantity,
            wh.QuarantinedQuantity);
    }

    public void Apply(NetworkInventorySummaryView view, ReservationCommitted e)
    {
        // Reserved → Committed: no change in our simplified summary
        // (both are "reserved" from a network perspective)
    }

    public void Apply(NetworkInventorySummaryView view, StockReceived e)
    {
        var wh = GetWarehouse(view, e.WarehouseId);
        SetWarehouse(view, e.WarehouseId,
            wh.AvailableQuantity + e.Quantity,
            wh.ReservedQuantity,
            wh.QuarantinedQuantity);
    }

    public void Apply(NetworkInventorySummaryView view, StockRestocked e)
    {
        var wh = GetWarehouse(view, e.WarehouseId);
        SetWarehouse(view, e.WarehouseId,
            wh.AvailableQuantity + e.Quantity,
            wh.ReservedQuantity,
            wh.QuarantinedQuantity);
    }

    public void Apply(NetworkInventorySummaryView view, InventoryAdjusted e)
    {
        var wh = GetWarehouse(view, e.WarehouseId);
        SetWarehouse(view, e.WarehouseId,
            wh.AvailableQuantity + e.AdjustmentQuantity,
            wh.ReservedQuantity,
            wh.QuarantinedQuantity);
    }

    public void Apply(NetworkInventorySummaryView view, ReservationExpired e)
    {
        var wh = GetWarehouse(view, e.WarehouseId);
        SetWarehouse(view, e.WarehouseId,
            wh.AvailableQuantity + e.Quantity,
            wh.ReservedQuantity - e.Quantity,
            wh.QuarantinedQuantity);
    }

    public void Apply(NetworkInventorySummaryView view, StockTransferredOut e)
    {
        var wh = GetWarehouse(view, e.WarehouseId);
        SetWarehouse(view, e.WarehouseId,
            wh.AvailableQuantity - e.Quantity,
            wh.ReservedQuantity,
            wh.QuarantinedQuantity);
    }

    public void Apply(NetworkInventorySummaryView view, StockTransferredIn e)
    {
        var wh = GetWarehouse(view, e.WarehouseId);
        SetWarehouse(view, e.WarehouseId,
            wh.AvailableQuantity + e.Quantity,
            wh.ReservedQuantity,
            wh.QuarantinedQuantity);
    }

    public void Apply(NetworkInventorySummaryView view, StockQuarantined e)
    {
        var wh = GetWarehouse(view, e.WarehouseId);
        SetWarehouse(view, e.WarehouseId,
            wh.AvailableQuantity,
            wh.ReservedQuantity,
            wh.QuarantinedQuantity + e.Quantity);
    }

    public void Apply(NetworkInventorySummaryView view, QuarantineReleased e)
    {
        var wh = GetWarehouse(view, e.WarehouseId);
        SetWarehouse(view, e.WarehouseId,
            wh.AvailableQuantity,
            wh.ReservedQuantity,
            wh.QuarantinedQuantity - e.Quantity);
    }

    public void Apply(NetworkInventorySummaryView view, QuarantineDisposed e)
    {
        var wh = GetWarehouse(view, e.WarehouseId);
        SetWarehouse(view, e.WarehouseId,
            wh.AvailableQuantity,
            wh.ReservedQuantity,
            wh.QuarantinedQuantity - e.Quantity);
    }

    // --- helpers ---

    private static WarehouseQuantitySummary GetWarehouse(NetworkInventorySummaryView view, string warehouseId)
    {
        return view.Warehouses.FirstOrDefault(w => w.WarehouseId == warehouseId)
            ?? new WarehouseQuantitySummary(warehouseId, 0, 0, 0);
    }

    private static void SetWarehouse(NetworkInventorySummaryView view, string warehouseId,
        int available, int reserved, int quarantined)
    {
        var index = view.Warehouses.FindIndex(w => w.WarehouseId == warehouseId);
        var entry = new WarehouseQuantitySummary(warehouseId, available, reserved, quarantined);
        if (index >= 0)
            view.Warehouses[index] = entry;
        else
            view.Warehouses.Add(entry);
    }
}
