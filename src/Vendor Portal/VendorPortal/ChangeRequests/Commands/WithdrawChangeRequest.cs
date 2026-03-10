namespace VendorPortal.ChangeRequests.Commands;

/// <summary>
/// Withdraws a change request. Allowed from Draft, Submitted, or NeedsMoreInfo states.
/// Transitions the request to Withdrawn (terminal state).
/// </summary>
/// <param name="RequestId">ID of the request to withdraw.</param>
/// <param name="VendorTenantId">The tenant withdrawing (from JWT claims — must match the request).</param>
public sealed record WithdrawChangeRequest(
    Guid RequestId,
    Guid VendorTenantId);
