namespace Messages.Contracts.VendorIdentity;

/// <summary>
/// Published when an admin revokes a pending invitation for a vendor user.
/// The invitation can no longer be accepted.
/// </summary>
public sealed record VendorUserInvitationRevoked(
    Guid InvitationId,
    Guid UserId,
    Guid VendorTenantId,
    string Reason,
    DateTimeOffset RevokedAt
);
