using Messages.Contracts.VendorIdentity;

namespace VendorIdentity.UserManagement;

/// <summary>
/// Deactivates an active vendor user. The user can no longer log in.
/// </summary>
public sealed record DeactivateVendorUser(
    Guid TenantId,
    Guid UserId,
    string Reason
);
