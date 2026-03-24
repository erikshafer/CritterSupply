using Marten;
using Messages.Contracts.Inventory;
using VendorPortal.VendorProductCatalog;

namespace VendorPortal.Analytics;

/// <summary>
/// Handles <see cref="StockReplenished"/> integration messages from the Inventory BC.
/// Updates the <see cref="InventorySnapshot"/> document for the owning vendor.
/// No SignalR push is emitted here — inventory level changes triggered by replenishment
/// are captured via a subsequent <see cref="InventoryAdjusted"/> event in the Inventory BC.
///
/// Unknown SKUs (no active VendorProductCatalog entry) are silently skipped.
/// </summary>
public static class StockReplenishedHandler
{
    public static async Task Handle(
        StockReplenished @event,
        IDocumentSession session,
        CancellationToken ct)
    {
        var catalogEntry = await session.LoadAsync<VendorProductCatalogEntry>(@event.Sku, ct);
        if (catalogEntry is null || !catalogEntry.IsActive)
        {
            return;
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
            LastUpdatedAt = @event.ReplenishedAt
        };

        session.Store(snapshot);
        await session.SaveChangesAsync(ct);
    }
}
