using Messages.Contracts.VendorIdentity;

namespace VendorIdentity.UserManagement;

/// <summary>
/// Changes the role of a vendor user within their tenant.
/// </summary>
public sealed record ChangeVendorUserRole(
    Guid TenantId,
    Guid UserId,
    VendorRole NewRole
);
