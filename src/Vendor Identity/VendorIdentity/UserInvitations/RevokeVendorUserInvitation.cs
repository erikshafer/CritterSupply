namespace VendorIdentity.UserInvitations;

/// <summary>
/// Revokes a pending invitation for a vendor user. The invitation can no longer be accepted.
/// </summary>
public sealed record RevokeVendorUserInvitation(
    Guid TenantId,
    Guid UserId,
    string Reason
);
