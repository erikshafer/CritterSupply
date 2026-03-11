namespace VendorIdentity.TenantManagement;

/// <summary>
/// Reinstates a previously suspended vendor tenant. Users regain access.
/// </summary>
public sealed record ReinstateVendorTenant(
    Guid TenantId
);
