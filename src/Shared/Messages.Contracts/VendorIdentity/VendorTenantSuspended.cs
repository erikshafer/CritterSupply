namespace Messages.Contracts.VendorIdentity;

/// <summary>
/// Published when an admin suspends a vendor tenant.
/// All users in the tenant lose access; in-flight change requests freeze in current state.
/// </summary>
public sealed record VendorTenantSuspended(
    Guid VendorTenantId,
    string Reason,
    DateTimeOffset SuspendedAt
);
