using Marten.Events.Projections;

namespace Inventory.Management;

/// <summary>
/// Inline multi-stream projection that produces one <see cref="WarehouseSkuDetailView"/> per
/// ProductInventory aggregate stream (SKU + WarehouseId). Identity is extracted via
/// <see cref="InventoryStreamId.Compute"/> from each event's Sku and WarehouseId fields.
///
/// Covers all ProductInventory domain events including S3 additions:
/// transfers (StockTransferredOut/In), quarantine (StockQuarantined/Released/Disposed).
/// </summary>
public class WarehouseSkuDetailViewProjection : MultiStreamProjection<WarehouseSkuDetailView, Guid>
{
    public WarehouseSkuDetailViewProjection()
    {
        // P0 — Foundation
        Identity<InventoryInitialized>(e => InventoryStreamId.Compute(e.Sku, e.WarehouseId));
        Identity<StockReserved>(e => InventoryStreamId.Compute(e.Sku, e.WarehouseId));
        Identity<ReservationCommitted>(e => InventoryStreamId.Compute(e.Sku, e.WarehouseId));
        Identity<ReservationReleased>(e => InventoryStreamId.Compute(e.Sku, e.WarehouseId));
        Identity<StockReceived>(e => InventoryStreamId.Compute(e.Sku, e.WarehouseId));
        Identity<StockRestocked>(e => InventoryStreamId.Compute(e.Sku, e.WarehouseId));
        Identity<InventoryAdjusted>(e => InventoryStreamId.Compute(e.Sku, e.WarehouseId));
        Identity<LowStockThresholdBreached>(e => InventoryStreamId.Compute(e.Sku, e.WarehouseId));

        // P1 — Failure Modes
        Identity<StockPicked>(e => InventoryStreamId.Compute(e.Sku, e.WarehouseId));
        Identity<StockShipped>(e => InventoryStreamId.Compute(e.Sku, e.WarehouseId));
        Identity<ReservationExpired>(e => InventoryStreamId.Compute(e.Sku, e.WarehouseId));
        Identity<BackorderRegistered>(e => InventoryStreamId.Compute(e.Sku, e.WarehouseId));
        Identity<BackorderCleared>(e => InventoryStreamId.Compute(e.Sku, e.WarehouseId));

        // P2 — Transfers + Quarantine
        Identity<StockTransferredOut>(e => InventoryStreamId.Compute(e.Sku, e.WarehouseId));
        Identity<StockTransferredIn>(e => InventoryStreamId.Compute(e.Sku, e.WarehouseId));
        Identity<StockQuarantined>(e => InventoryStreamId.Compute(e.Sku, e.WarehouseId));
        Identity<QuarantineReleased>(e => InventoryStreamId.Compute(e.Sku, e.WarehouseId));
        Identity<QuarantineDisposed>(e => InventoryStreamId.Compute(e.Sku, e.WarehouseId));
    }

    // ---------------------------------------------------------------------------
    // P0 — Foundation events
    // ---------------------------------------------------------------------------

    public void Apply(WarehouseSkuDetailView view, InventoryInitialized e)
    {
        view.Id = InventoryStreamId.Compute(e.Sku, e.WarehouseId);
        view.Sku = e.Sku;
        view.WarehouseId = e.WarehouseId;
        view.AvailableQuantity = e.InitialQuantity;
        view.LastUpdated = e.InitializedAt;
    }

    public void Apply(WarehouseSkuDetailView view, StockReserved e)
    {
        view.AvailableQuantity -= e.Quantity;
        view.ReservedQuantity += e.Quantity;
        view.LastUpdated = e.ReservedAt;
    }

    public void Apply(WarehouseSkuDetailView view, ReservationCommitted e)
    {
        // Reserved → Committed: quantity moves between buckets.
        // We can't know the exact quantity from this event alone (it doesn't carry qty),
        // so we rely on the reservation tracking being paired with StockReserved.
        // For the view, we approximate: the commit event doesn't change totals,
        // but we note the timestamp. Actual bucket transfer requires handler-level info.
        view.LastUpdated = e.CommittedAt;
    }

    public void Apply(WarehouseSkuDetailView view, ReservationReleased e)
    {
        view.ReservedQuantity -= e.Quantity;
        view.AvailableQuantity += e.Quantity;
        view.LastUpdated = e.ReleasedAt;
    }

    public void Apply(WarehouseSkuDetailView view, StockReceived e)
    {
        view.AvailableQuantity += e.Quantity;
        view.LastUpdated = e.ReceivedAt;
    }

    public void Apply(WarehouseSkuDetailView view, StockRestocked e)
    {
        view.AvailableQuantity += e.Quantity;
        view.LastUpdated = e.RestockedAt;
    }

    public void Apply(WarehouseSkuDetailView view, InventoryAdjusted e)
    {
        view.AvailableQuantity += e.AdjustmentQuantity;
        view.LastUpdated = e.AdjustedAt;
    }

    public void Apply(WarehouseSkuDetailView view, LowStockThresholdBreached e)
    {
        view.LastUpdated = e.DetectedAt;
    }

    // ---------------------------------------------------------------------------
    // P1 — Physical operations + failure modes
    // ---------------------------------------------------------------------------

    public void Apply(WarehouseSkuDetailView view, StockPicked e)
    {
        view.CommittedQuantity -= e.Quantity;
        view.PickedQuantity += e.Quantity;
        view.LastUpdated = e.PickedAt;
    }

    public void Apply(WarehouseSkuDetailView view, StockShipped e)
    {
        view.PickedQuantity -= e.Quantity;
        view.LastUpdated = e.ShippedAt;
    }

    public void Apply(WarehouseSkuDetailView view, ReservationExpired e)
    {
        view.ReservedQuantity -= e.Quantity;
        view.AvailableQuantity += e.Quantity;
        view.LastUpdated = e.ExpiredAt;
    }

    public void Apply(WarehouseSkuDetailView view, BackorderRegistered e)
    {
        view.LastUpdated = e.RegisteredAt;
    }

    public void Apply(WarehouseSkuDetailView view, BackorderCleared e)
    {
        view.LastUpdated = e.ClearedAt;
    }

    // ---------------------------------------------------------------------------
    // P2 — Transfer events (S3 carryover — in-transit tracking)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Stock has been deducted from this warehouse for an outbound transfer.
    /// AvailableQuantity was already decremented by the aggregate; track in-transit outbound.
    /// </summary>
    public void Apply(WarehouseSkuDetailView view, StockTransferredOut e)
    {
        view.AvailableQuantity -= e.Quantity;
        view.InTransitOutQuantity += e.Quantity;
        view.LastUpdated = e.TransferredAt;
    }

    /// <summary>
    /// Stock has arrived at this warehouse from an inbound transfer.
    /// AvailableQuantity increases.
    /// </summary>
    public void Apply(WarehouseSkuDetailView view, StockTransferredIn e)
    {
        view.AvailableQuantity += e.Quantity;
        view.LastUpdated = e.ReceivedAt;
    }

    // ---------------------------------------------------------------------------
    // P2 — Quarantine events (S3 carryover)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Stock quarantined: quarantine bucket increases. Available decrement handled by
    /// companion InventoryAdjusted event.
    /// </summary>
    public void Apply(WarehouseSkuDetailView view, StockQuarantined e)
    {
        view.QuarantinedQuantity += e.Quantity;
        view.LastUpdated = e.QuarantinedAt;
    }

    /// <summary>
    /// Quarantine released: quarantine bucket decreases. Available restoration handled by
    /// companion InventoryAdjusted event.
    /// </summary>
    public void Apply(WarehouseSkuDetailView view, QuarantineReleased e)
    {
        view.QuarantinedQuantity -= e.Quantity;
        view.LastUpdated = e.ReleasedAt;
    }

    /// <summary>
    /// Quarantine disposed: quarantine bucket decreases. Write-off handled by
    /// companion StockWrittenOff → InventoryAdjusted event pair.
    /// </summary>
    public void Apply(WarehouseSkuDetailView view, QuarantineDisposed e)
    {
        view.QuarantinedQuantity -= e.Quantity;
        view.LastUpdated = e.DisposedAt;
    }
}
