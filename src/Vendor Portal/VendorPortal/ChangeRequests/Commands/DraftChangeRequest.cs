namespace VendorPortal.ChangeRequests.Commands;

/// <summary>
/// Creates a change request in Draft state.
/// The request is saved locally but not yet submitted to the Catalog BC.
/// </summary>
/// <param name="RequestId">Caller-supplied request ID (idempotency key).</param>
/// <param name="VendorTenantId">The tenant submitting the request (from JWT claims).</param>
/// <param name="SubmittedByUserId">The user creating the draft (from JWT claims).</param>
/// <param name="Sku">The product SKU this change applies to.</param>
/// <param name="Type">Type of change: Description, Image, or DataCorrection.</param>
/// <param name="Title">Short summary of the change (shown in list views).</param>
/// <param name="Details">Full details of the change. For Image type, a brief description of the new images.</param>
/// <param name="AdditionalNotes">Optional vendor notes.</param>
/// <param name="ImageStorageKeys">Pre-uploaded image storage keys (Image type only).</param>
public sealed record DraftChangeRequest(
    Guid RequestId,
    Guid VendorTenantId,
    Guid SubmittedByUserId,
    string Sku,
    ChangeRequestType Type,
    string Title,
    string Details,
    string? AdditionalNotes = null,
    IReadOnlyList<string>? ImageStorageKeys = null);
