namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Published when an admin assigns a product SKU to a vendor tenant.
/// This is the load-bearing pillar for Vendor Portal analytics and change request invariants.
/// Triggers upsert of VendorProductCatalog document (SKU→VendorTenantId lookup).
/// </summary>
/// <param name="Sku">The product SKU being assigned.</param>
/// <param name="VendorTenantId">The vendor receiving the SKU assignment.</param>
/// <param name="AssociatedBy">Username of the admin who made the assignment.</param>
/// <param name="AssociatedAt">Timestamp of the assignment.</param>
/// <param name="PreviousVendorTenantId">
/// Null when this is a new assignment; non-null when this is a reassignment from another vendor.
/// Subscribers can use this to clean up old vendor associations.
/// </param>
/// <param name="ReassignmentNote">
/// Optional admin note explaining why the assignment or reassignment was made.
/// Null for first-time assignments unless an explicit note was provided.
/// Used for audit trail (e.g., "Contract Termination", "Acquisition", "Data Entry Correction").
/// </param>
public sealed record VendorProductAssociated(
    string Sku,
    Guid VendorTenantId,
    string AssociatedBy,
    DateTimeOffset AssociatedAt,
    Guid? PreviousVendorTenantId = null,
    string? ReassignmentNote = null
);
