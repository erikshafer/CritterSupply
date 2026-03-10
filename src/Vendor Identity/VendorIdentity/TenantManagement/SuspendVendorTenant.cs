namespace VendorIdentity.TenantManagement;

/// <summary>
/// Suspends an active vendor tenant. All users lose access until reinstated.
/// </summary>
public sealed record SuspendVendorTenant(
    Guid TenantId,
    string Reason
);
