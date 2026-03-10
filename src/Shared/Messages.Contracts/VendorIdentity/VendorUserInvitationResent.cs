namespace Messages.Contracts.VendorIdentity;

/// <summary>
/// Published when an admin resends a pending invitation for a vendor user.
/// The invitation token is regenerated and the expiry is extended by 72 hours.
/// </summary>
public sealed record VendorUserInvitationResent(
    Guid InvitationId,
    Guid UserId,
    Guid VendorTenantId,
    int ResendCount,
    DateTimeOffset ResentAt,
    DateTimeOffset NewExpiresAt
);
