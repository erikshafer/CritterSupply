namespace Messages.Contracts.VendorPortal;

/// <summary>
/// Published by Vendor Portal when a vendor submits a product description change request.
/// Received by Product Catalog BC for review and approval/rejection.
/// </summary>
/// <param name="RequestId">Unique ID of the change request (for correlation with response).</param>
/// <param name="VendorTenantId">The vendor tenant submitting the request.</param>
/// <param name="Sku">The product SKU for which the description change is requested.</param>
/// <param name="NewDescription">The proposed new description text.</param>
/// <param name="AdditionalNotes">Optional notes from the vendor explaining the change.</param>
/// <param name="SubmittedAt">When the request was submitted.</param>
public sealed record DescriptionChangeRequested(
    Guid RequestId,
    Guid VendorTenantId,
    string Sku,
    string NewDescription,
    string? AdditionalNotes,
    DateTimeOffset SubmittedAt);
