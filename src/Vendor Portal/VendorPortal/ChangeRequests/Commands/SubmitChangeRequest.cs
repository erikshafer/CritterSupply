namespace VendorPortal.ChangeRequests.Commands;

/// <summary>
/// Submits an existing Draft change request to the Catalog BC for review.
/// Transitions the request from Draft → Submitted and publishes the appropriate
/// integration message to the Catalog BC.
///
/// Invariant: if an active request already exists for the same SKU+Type+Tenant,
/// it is automatically superseded by this one.
/// </summary>
/// <param name="RequestId">ID of the Draft request to submit.</param>
/// <param name="VendorTenantId">The tenant submitting (from JWT claims — must match the request).</param>
public sealed record SubmitChangeRequest(
    Guid RequestId,
    Guid VendorTenantId);
