namespace Messages.Contracts.VendorIdentity;

/// <summary>
/// Published when an admin permanently terminates a vendor tenant.
/// All in-flight change requests are auto-rejected. This is a terminal state.
/// </summary>
public sealed record VendorTenantTerminated(
    Guid VendorTenantId,
    DateTimeOffset TerminatedAt
);
