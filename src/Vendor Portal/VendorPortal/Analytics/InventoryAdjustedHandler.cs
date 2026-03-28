using Marten;
using Messages.Contracts.Inventory;
using VendorPortal.RealTime;
using VendorPortal.VendorProductCatalog;

namespace VendorPortal.Analytics;

/// <summary>
/// Handles <see cref="InventoryAdjusted"/> integration messages from the Inventory BC.
/// Updates the <see cref="InventorySnapshot"/> document for the owning vendor
/// and returns an <see cref="InventoryLevelUpdated"/> SignalR message to be published
/// to the vendor's hub group via Wolverine's SignalR transport.
///
/// Unknown SKUs (no active VendorProductCatalog entry) return null — no hub push, no exception.
/// </summary>
public static class InventoryAdjustedHandler
{
    public static async Task<InventoryLevelUpdated?> Handle(
        InventoryAdjusted @event,
        IDocumentSession session,
        CancellationToken ct)
    {
        var catalogEntry = await session.LoadAsync<VendorProductCatalogEntry>(@event.Sku, ct);
        if (catalogEntry is null || !catalogEntry.IsActive)
        {
            return null;
        }

        var vendorTenantId = catalogEntry.VendorTenantId;
        var snapshotId = InventorySnapshot.BuildId(vendorTenantId, @event.Sku, @event.WarehouseId);

        var snapshot = new InventorySnapshot
        {
            Id = snapshotId,
            VendorTenantId = vendorTenantId,
            Sku = @event.Sku,
            WarehouseId = @event.WarehouseId,
            CurrentQuantity = @event.NewQuantity,
            LastUpdatedAt = @event.AdjustedAt
        };

        session.Store(snapshot);

        return new InventoryLevelUpdated(
            VendorTenantId: vendorTenantId,
            Sku: @event.Sku,
            WarehouseId: @event.WarehouseId,
            NewQuantity: @event.NewQuantity,
            AdjustedAt: @event.AdjustedAt);
    }
}
