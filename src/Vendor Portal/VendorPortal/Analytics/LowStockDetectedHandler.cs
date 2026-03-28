using Marten;
using Messages.Contracts.Inventory;
using VendorPortal.RealTime;
using VendorPortal.VendorProductCatalog;

namespace VendorPortal.Analytics;

/// <summary>
/// Handles <see cref="LowStockDetected"/> integration messages from the Inventory BC.
/// Looks up the owning vendor via <see cref="VendorProductCatalogEntry"/>,
/// upserts an active <see cref="LowStockAlert"/> document (dedup: one per VendorTenantId + Sku),
/// and returns a <see cref="LowStockAlertRaised"/> SignalR message to be published to the
/// vendor's hub group via Wolverine's SignalR transport.
///
/// Unknown SKUs (no active VendorProductCatalog entry) return null — no hub push, no exception.
/// </summary>
public static class LowStockDetectedHandler
{
    public static async Task<LowStockAlertRaised?> Handle(
        LowStockDetected @event,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Look up vendor ownership for this SKU
        var catalogEntry = await session.LoadAsync<VendorProductCatalogEntry>(@event.Sku, ct);
        if (catalogEntry is null || !catalogEntry.IsActive)
        {
            // Unknown SKU — silently skip. Wolverine will emit a structured log at debug level.
            return null;
        }

        var vendorTenantId = catalogEntry.VendorTenantId;
        var alertId = LowStockAlert.BuildId(vendorTenantId, @event.Sku);

        // Dedup: load existing alert to determine if this is new or an update
        var existing = await session.LoadAsync<LowStockAlert>(alertId, ct);

        var alert = new LowStockAlert
        {
            Id = alertId,
            VendorTenantId = vendorTenantId,
            Sku = @event.Sku,
            WarehouseId = @event.WarehouseId,
            CurrentQuantity = @event.CurrentQuantity,
            ThresholdQuantity = @event.ThresholdQuantity,
            FirstDetectedAt = existing?.FirstDetectedAt ?? @event.DetectedAt,
            LastUpdatedAt = @event.DetectedAt,
            IsActive = true
        };

        session.Store(alert);

        // Push real-time alert to vendor's hub group only on NEW alerts (not quantity updates).
        // Rationale: firing a hub push on every quantity update would cause UI noise for alerts
        // that are already visible. The updated quantity is reflected when:
        //   a) The vendor explicitly refreshes the dashboard, OR
        //   b) An InventoryAdjusted event arrives (handled by InventoryAdjustedHandler) which
        //      triggers a lightweight API re-fetch of the authoritative alert count.
        if (existing is null)
        {
            return new LowStockAlertRaised(
                VendorTenantId: vendorTenantId,
                Sku: @event.Sku,
                WarehouseId: @event.WarehouseId,
                CurrentQuantity: @event.CurrentQuantity,
                ThresholdQuantity: @event.ThresholdQuantity,
                DetectedAt: @event.DetectedAt);
        }

        return null;
    }
}
