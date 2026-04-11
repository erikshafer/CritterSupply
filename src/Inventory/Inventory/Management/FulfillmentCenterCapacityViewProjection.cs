using Marten.Events.Projections;

namespace Inventory.Management;

/// <summary>
/// Async multi-stream projection producing one <see cref="FulfillmentCenterCapacityView"/>
/// per warehouse. Identity extracted from WarehouseId on each event.
///
/// Aggregates capacity utilization across all SKUs at each warehouse.
/// Consumed by Fulfillment's routing engine via GET /api/inventory/fc-capacity/{warehouseId}.
///
/// Slice 39 (P3): FC capacity data exposure.
/// </summary>
public class FulfillmentCenterCapacityViewProjection : MultiStreamProjection<FulfillmentCenterCapacityView, string>
{
    public FulfillmentCenterCapacityViewProjection()
    {
        // P0 — Foundation
        Identity<InventoryInitialized>(e => e.WarehouseId);
        Identity<StockReserved>(e => e.WarehouseId);
        Identity<ReservationCommitted>(e => e.WarehouseId);
        Identity<ReservationReleased>(e => e.WarehouseId);
        Identity<StockReceived>(e => e.WarehouseId);
        Identity<StockRestocked>(e => e.WarehouseId);
        Identity<InventoryAdjusted>(e => e.WarehouseId);

        // P1 — Physical operations
        Identity<StockPicked>(e => e.WarehouseId);
        Identity<StockShipped>(e => e.WarehouseId);
        Identity<ReservationExpired>(e => e.WarehouseId);

        // P2 — Transfers + Quarantine
        Identity<StockTransferredOut>(e => e.WarehouseId);
        Identity<StockTransferredIn>(e => e.WarehouseId);
        Identity<StockQuarantined>(e => e.WarehouseId);
        Identity<QuarantineReleased>(e => e.WarehouseId);
        Identity<QuarantineDisposed>(e => e.WarehouseId);
    }

    // ---------------------------------------------------------------------------
    // P0 — Foundation
    // ---------------------------------------------------------------------------

    public void Apply(FulfillmentCenterCapacityView view, InventoryInitialized e)
    {
        view.Id = e.WarehouseId;
        view.SkuCount++;
        view.TotalAvailable += e.InitialQuantity;
        view.LastUpdated = e.InitializedAt;
    }

    public void Apply(FulfillmentCenterCapacityView view, StockReserved e)
    {
        view.TotalAvailable -= e.Quantity;
        view.TotalReserved += e.Quantity;
        view.LastUpdated = e.ReservedAt;
    }

    public void Apply(FulfillmentCenterCapacityView view, ReservationCommitted e)
    {
        // Commit moves reservation to committed — view tracks timestamp only
        // since ReservationCommitted doesn't carry the quantity.
        view.LastUpdated = e.CommittedAt;
    }

    public void Apply(FulfillmentCenterCapacityView view, ReservationReleased e)
    {
        view.TotalReserved -= e.Quantity;
        view.TotalAvailable += e.Quantity;
        view.LastUpdated = e.ReleasedAt;
    }

    public void Apply(FulfillmentCenterCapacityView view, StockReceived e)
    {
        view.TotalAvailable += e.Quantity;
        view.LastUpdated = e.ReceivedAt;
    }

    public void Apply(FulfillmentCenterCapacityView view, StockRestocked e)
    {
        view.TotalAvailable += e.Quantity;
        view.LastUpdated = e.RestockedAt;
    }

    public void Apply(FulfillmentCenterCapacityView view, InventoryAdjusted e)
    {
        view.TotalAvailable += e.AdjustmentQuantity;
        view.LastUpdated = e.AdjustedAt;
    }

    // ---------------------------------------------------------------------------
    // P1 — Physical operations
    // ---------------------------------------------------------------------------

    public void Apply(FulfillmentCenterCapacityView view, StockPicked e)
    {
        view.TotalCommitted -= e.Quantity;
        view.TotalPicked += e.Quantity;
        view.LastUpdated = e.PickedAt;
    }

    public void Apply(FulfillmentCenterCapacityView view, StockShipped e)
    {
        view.TotalPicked -= e.Quantity;
        view.LastUpdated = e.ShippedAt;
    }

    public void Apply(FulfillmentCenterCapacityView view, ReservationExpired e)
    {
        view.TotalReserved -= e.Quantity;
        view.TotalAvailable += e.Quantity;
        view.LastUpdated = e.ExpiredAt;
    }

    // ---------------------------------------------------------------------------
    // P2 — Transfers + Quarantine
    // ---------------------------------------------------------------------------

    public void Apply(FulfillmentCenterCapacityView view, StockTransferredOut e)
    {
        view.TotalAvailable -= e.Quantity;
        view.TotalInTransitOut += e.Quantity;
        view.LastUpdated = e.TransferredAt;
    }

    public void Apply(FulfillmentCenterCapacityView view, StockTransferredIn e)
    {
        view.TotalAvailable += e.Quantity;
        view.LastUpdated = e.ReceivedAt;
    }

    public void Apply(FulfillmentCenterCapacityView view, StockQuarantined e)
    {
        view.TotalQuarantined += e.Quantity;
        view.LastUpdated = e.QuarantinedAt;
    }

    public void Apply(FulfillmentCenterCapacityView view, QuarantineReleased e)
    {
        view.TotalQuarantined -= e.Quantity;
        view.LastUpdated = e.ReleasedAt;
    }

    public void Apply(FulfillmentCenterCapacityView view, QuarantineDisposed e)
    {
        view.TotalQuarantined -= e.Quantity;
        view.LastUpdated = e.DisposedAt;
    }
}
