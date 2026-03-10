namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Published by Product Catalog BC when a vendor's description change request is rejected.
/// Received by Vendor Portal to update the change request status and notify the vendor.
/// </summary>
public sealed record DescriptionChangeRejected(
    Guid RequestId,
    string Sku,
    Guid VendorTenantId,
    string Reason,
    DateTimeOffset RejectedAt);
