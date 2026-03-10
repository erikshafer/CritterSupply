using Marten;
using Messages.Contracts.ProductCatalog;

namespace VendorPortal.VendorProductCatalog;

/// <summary>
/// Handles the <see cref="VendorProductAssociated"/> integration event published by
/// Product Catalog when an admin assigns or reassigns a SKU to a vendor.
/// Upserts the <see cref="VendorProductCatalogEntry"/> so Vendor Portal always has
/// a current authoritative SKU → VendorTenantId lookup.
/// </summary>
public static class VendorProductAssociatedHandler
{
    public static async Task Handle(
        VendorProductAssociated integrationEvent,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Store() performs an upsert — safe for both new assignments and reassignments.
        // PreviousVendorTenantId is available on the event if downstream projections
        // need to de-associate the SKU from the old vendor (e.g., analytics).
        // TODO(Phase 2): Handle ProductDiscontinued integration event to set IsActive = false.
        //   A separate handler will subscribe to ProductDiscontinued and update IsActive on the entry.
        var entry = new VendorProductCatalogEntry
        {
            Id = integrationEvent.Sku,
            Sku = integrationEvent.Sku,
            VendorTenantId = integrationEvent.VendorTenantId,
            AssociatedBy = integrationEvent.AssociatedBy,
            AssociatedAt = integrationEvent.AssociatedAt,
            IsActive = true
        };

        session.Store(entry);
        await session.SaveChangesAsync(ct);
    }
}
