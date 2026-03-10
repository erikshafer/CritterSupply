namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Published by Product Catalog BC when a vendor's image upload request is rejected.
/// Received by Vendor Portal to update the change request status and notify the vendor.
/// </summary>
public sealed record ImageChangeRejected(
    Guid RequestId,
    string Sku,
    Guid VendorTenantId,
    string Reason,
    DateTimeOffset RejectedAt);
