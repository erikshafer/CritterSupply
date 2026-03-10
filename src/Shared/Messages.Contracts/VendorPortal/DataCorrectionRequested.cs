namespace Messages.Contracts.VendorPortal;

/// <summary>
/// Published by Vendor Portal when a vendor submits a product data correction request.
/// Used for corrections to structured fields (e.g., weight, dimensions, category, UPC).
/// Received by Product Catalog BC for review.
/// </summary>
/// <param name="RequestId">Unique ID of the change request (for correlation with response).</param>
/// <param name="VendorTenantId">The vendor tenant submitting the request.</param>
/// <param name="Sku">The product SKU for which the correction is requested.</param>
/// <param name="CorrectionType">The type of correction (e.g., "Weight", "Dimensions", "Category", "UPC").</param>
/// <param name="CorrectionDetails">Free-text description of the correction requested.</param>
/// <param name="AdditionalNotes">Optional notes from the vendor explaining the correction.</param>
/// <param name="SubmittedAt">When the request was submitted.</param>
public sealed record DataCorrectionRequested(
    Guid RequestId,
    Guid VendorTenantId,
    string Sku,
    string CorrectionType,
    string CorrectionDetails,
    string? AdditionalNotes,
    DateTimeOffset SubmittedAt);
