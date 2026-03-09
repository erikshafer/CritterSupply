namespace Messages.Contracts.VendorIdentity;

/// <summary>
/// Published when a vendor user completes registration by accepting an invitation and setting their password.
/// User status transitions from Invited to Active.
/// </summary>
public sealed record VendorUserActivated(
    Guid UserId,
    Guid VendorTenantId,
    VendorRole Role,
    DateTimeOffset ActivatedAt
);
