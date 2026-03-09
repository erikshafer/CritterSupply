namespace Messages.Contracts.VendorIdentity;

/// <summary>
/// Published when an admin reactivates a previously deactivated vendor user.
/// User can log in again.
/// </summary>
public sealed record VendorUserReactivated(
    Guid UserId,
    Guid VendorTenantId,
    DateTimeOffset ReactivatedAt
);
