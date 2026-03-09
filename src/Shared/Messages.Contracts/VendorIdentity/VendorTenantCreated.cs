namespace Messages.Contracts.VendorIdentity;

/// <summary>
/// Published when a new vendor tenant organization is created by an admin.
/// Triggers initialization of tenant-scoped projections in Vendor Portal BC.
/// </summary>
public sealed record VendorTenantCreated(
    Guid VendorTenantId,
    string OrganizationName,
    string ContactEmail,
    DateTimeOffset CreatedAt
);
