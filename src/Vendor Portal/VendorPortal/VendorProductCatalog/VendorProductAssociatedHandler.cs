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
        VendorProductAssociated @event,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Store() performs an upsert — safe for both new assignments and reassignments.
        // PreviousVendorTenantId is available on the event if downstream projections
        // need to de-associate the SKU from the old vendor (e.g., analytics).
        var entry = new VendorProductCatalogEntry
        {
            Id = @event.Sku,
            Sku = @event.Sku,
            VendorTenantId = @event.VendorTenantId,
            AssociatedBy = @event.AssociatedBy,
            AssociatedAt = @event.AssociatedAt,
            IsActive = true
        };

        session.Store(entry);
        await session.SaveChangesAsync(ct);
    }
}
