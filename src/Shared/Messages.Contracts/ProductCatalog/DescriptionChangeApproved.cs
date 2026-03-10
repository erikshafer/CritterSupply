namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Published by Product Catalog BC when a vendor's description change request is approved.
/// Received by Vendor Portal to update the change request status and notify the vendor.
/// </summary>
public sealed record DescriptionChangeApproved(
    Guid RequestId,
    string Sku,
    Guid VendorTenantId,
    DateTimeOffset ApprovedAt);
