using JasperFx.Events;
using Marten.Events.Projections;

namespace Inventory.Management;

/// <summary>
/// Event-per-document projection that creates one <see cref="AlertFeedView"/> row per alert event.
/// Registered as Async because the alert feed is not on the critical checkout path.
/// </summary>
public sealed class AlertFeedViewProjection : EventProjection
{
    public AlertFeedView Transform(IEvent<StockDiscrepancyFound> input)
    {
        var e = input.Data;
        var severity = e.DiscrepancyType switch
        {
            DiscrepancyType.ZeroPick => "Critical",
            DiscrepancyType.ShortPick => "Warning",
            DiscrepancyType.CycleCount => "Warning",
            DiscrepancyType.ShortTransfer => "Warning",
            _ => "Warning"
        };

        return new AlertFeedView
        {
            Id = input.Id,
            Sku = e.Sku,
            WarehouseId = e.WarehouseId,
            AlertType = $"Discrepancy:{e.DiscrepancyType}",
            Description = e.Description,
            DetectedAt = e.DetectedAt,
            Severity = severity
        };
    }

    public AlertFeedView Transform(IEvent<LowStockThresholdBreached> input)
    {
        var e = input.Data;
        return new AlertFeedView
        {
            Id = input.Id,
            Sku = e.Sku,
            WarehouseId = e.WarehouseId,
            AlertType = "LowStock",
            Description = $"Available dropped from {e.PreviousQuantity} to {e.NewQuantity} (threshold: {e.Threshold})",
            DetectedAt = e.DetectedAt,
            Severity = "Warning"
        };
    }
}
