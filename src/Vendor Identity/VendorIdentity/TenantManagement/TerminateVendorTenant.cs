namespace VendorIdentity.TenantManagement;

/// <summary>
/// Permanently terminates a vendor tenant. This is a terminal state.
/// </summary>
public sealed record TerminateVendorTenant(
    Guid TenantId,
    string Reason
);
