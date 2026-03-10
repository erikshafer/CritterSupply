namespace Messages.Contracts.VendorPortal;

/// <summary>
/// Published by Vendor Portal when a vendor submits a product image upload request.
/// Carries the pre-uploaded image storage keys (claim-check pattern).
/// Received by Product Catalog BC for review.
/// </summary>
/// <param name="RequestId">Unique ID of the change request (for correlation with response).</param>
/// <param name="VendorTenantId">The vendor tenant submitting the request.</param>
/// <param name="Sku">The product SKU for which images are being uploaded.</param>
/// <param name="ImageStorageKeys">Storage keys for the pre-uploaded images (claim-check references).</param>
/// <param name="AdditionalNotes">Optional notes from the vendor about the images.</param>
/// <param name="SubmittedAt">When the request was submitted.</param>
public sealed record ImageUploadRequested(
    Guid RequestId,
    Guid VendorTenantId,
    string Sku,
    IReadOnlyList<string> ImageStorageKeys,
    string? AdditionalNotes,
    DateTimeOffset SubmittedAt);
