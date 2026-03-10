namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Published by Product Catalog BC when additional information is needed from the vendor
/// before a change request can be processed.
/// Received by Vendor Portal to update the change request status and prompt the vendor.
/// </summary>
public sealed record MoreInfoRequestedForChangeRequest(
    Guid RequestId,
    string Sku,
    Guid VendorTenantId,
    string Question,
    DateTimeOffset RequestedAt);
