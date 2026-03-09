namespace Messages.Contracts.VendorIdentity;

/// <summary>
/// Published when an admin reinstates a previously suspended vendor tenant.
/// Users can log in again; frozen change requests resume from their previous state.
/// </summary>
public sealed record VendorTenantReinstated(
    Guid VendorTenantId,
    DateTimeOffset ReinstatedAt
);
