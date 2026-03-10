namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Published by Product Catalog BC when a vendor's data correction request is approved.
/// Received by Vendor Portal to update the change request status and notify the vendor.
/// </summary>
public sealed record DataCorrectionApproved(
    Guid RequestId,
    string Sku,
    Guid VendorTenantId,
    DateTimeOffset ApprovedAt);
