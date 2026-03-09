namespace VendorIdentity.Commands;

/// <summary>
/// Creates a new vendor tenant organization.
/// </summary>
public sealed record CreateVendorTenant(
    string OrganizationName,
    string ContactEmail
);
