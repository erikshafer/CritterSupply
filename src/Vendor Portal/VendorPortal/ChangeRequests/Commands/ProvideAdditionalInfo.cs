namespace VendorPortal.ChangeRequests.Commands;

/// <summary>
/// Provides additional information requested by the Catalog BC.
/// Transitions the request from NeedsMoreInfo → Submitted and re-publishes the integration
/// message to the Catalog BC with the updated notes.
/// </summary>
/// <param name="RequestId">ID of the NeedsMoreInfo request.</param>
/// <param name="VendorTenantId">The tenant responding (from JWT claims — must match the request).</param>
/// <param name="Response">The vendor's answer to the Catalog BC's question.</param>
public sealed record ProvideAdditionalInfo(
    Guid RequestId,
    Guid VendorTenantId,
    string Response);
