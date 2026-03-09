namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Published when an admin assigns a product SKU to a vendor tenant.
/// This is the load-bearing pillar for Vendor Portal analytics and change request invariants.
/// Triggers upsert of VendorProductCatalog document (SKU→VendorTenantId lookup).
/// </summary>
public sealed record VendorProductAssociated(
    string Sku,
    Guid VendorTenantId,
    string AssociatedBy,
    DateTimeOffset AssociatedAt
);
