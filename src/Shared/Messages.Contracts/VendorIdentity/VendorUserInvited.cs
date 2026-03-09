namespace Messages.Contracts.VendorIdentity;

/// <summary>
/// Published when an admin invites a new user to a vendor tenant.
/// The invitation expires after 72 hours if not accepted.
/// </summary>
public sealed record VendorUserInvited(
    Guid UserId,
    Guid VendorTenantId,
    string Email,
    VendorRole Role,
    DateTimeOffset InvitedAt,
    DateTimeOffset ExpiresAt
);
