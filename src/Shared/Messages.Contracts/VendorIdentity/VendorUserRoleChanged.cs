namespace Messages.Contracts.VendorIdentity;

/// <summary>
/// Published when an admin changes a vendor user's role.
/// New permissions apply on next login (JWT refresh).
/// </summary>
public sealed record VendorUserRoleChanged(
    Guid UserId,
    Guid VendorTenantId,
    VendorRole OldRole,
    VendorRole NewRole,
    DateTimeOffset ChangedAt
);
