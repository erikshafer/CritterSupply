namespace Messages.Contracts.VendorIdentity;

/// <summary>
/// Roles for vendor users defining their permissions within the vendor portal.
/// </summary>
public enum VendorRole
{
    /// <summary>
    /// Full administrative access: can invite/deactivate users, change roles, submit change requests, view analytics.
    /// </summary>
    Admin,

    /// <summary>
    /// Catalog management: can submit/withdraw change requests, view analytics, acknowledge alerts.
    /// </summary>
    CatalogManager,

    /// <summary>
    /// Read-only access: can view analytics, change request status, and dashboard views.
    /// </summary>
    ReadOnly
}
