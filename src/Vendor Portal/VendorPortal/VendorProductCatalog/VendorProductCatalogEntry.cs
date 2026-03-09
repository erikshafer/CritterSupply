namespace VendorPortal.VendorProductCatalog;

/// <summary>
/// Marten document: SKU → VendorTenantId lookup.
/// This is NOT tenant-isolated — it IS the lookup that tells us which tenant owns a SKU.
/// All analytics projections use this to attribute orders to the correct vendor.
/// The Id field mirrors the SKU to enable O(1) lookups by product identifier.
/// </summary>
public sealed class VendorProductCatalogEntry
{
    /// <summary>Marten document Id — equals Sku for O(1) lookups.</summary>
    public string Id { get; init; } = null!;

    /// <summary>The product SKU this entry tracks.</summary>
    public string Sku { get; init; } = null!;

    /// <summary>The vendor tenant that currently owns this SKU.</summary>
    public Guid VendorTenantId { get; init; }

    /// <summary>Username of the admin who created or last updated this assignment.</summary>
    public string AssociatedBy { get; init; } = null!;

    /// <summary>Timestamp when the current assignment was made.</summary>
    public DateTimeOffset AssociatedAt { get; init; }

    /// <summary>
    /// True while the assignment is active.
    /// Set to false if the product is discontinued or the assignment is revoked.
    /// </summary>
    public bool IsActive { get; init; }
}
