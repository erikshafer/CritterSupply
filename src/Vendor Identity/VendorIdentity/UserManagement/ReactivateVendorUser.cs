namespace VendorIdentity.UserManagement;

/// <summary>
/// Reactivates a previously deactivated vendor user. The user can log in again.
/// </summary>
public sealed record ReactivateVendorUser(
    Guid TenantId,
    Guid UserId
);
