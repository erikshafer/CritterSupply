namespace VendorIdentity.UserInvitations;

/// <summary>
/// Resends a pending invitation for a vendor user with a new token and extended expiry.
/// </summary>
public sealed record ResendVendorUserInvitation(
    Guid TenantId,
    Guid UserId
);
