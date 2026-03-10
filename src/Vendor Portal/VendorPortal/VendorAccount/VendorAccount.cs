namespace VendorPortal.VendorAccount;

/// <summary>
/// Marten document representing a vendor tenant's portal account.
/// Stores saved dashboard views and notification preferences.
///
/// Initialized automatically when a new vendor tenant is onboarded (VendorTenantCreated event).
/// The Id field mirrors the VendorTenantId — one account per tenant.
///
/// Multi-tenancy: all queries must filter by VendorTenantId (from JWT claims only — never request params).
/// </summary>
public sealed class VendorAccount
{
    /// <summary>Marten document Id — equals VendorTenantId for O(1) lookups.</summary>
    public Guid Id { get; init; }

    /// <summary>The vendor tenant that owns this account.</summary>
    public Guid VendorTenantId { get; init; }

    /// <summary>Tenant organization name (snapshot from VendorTenantCreated).</summary>
    public string OrganizationName { get; init; } = null!;

    /// <summary>Contact email (snapshot from VendorTenantCreated).</summary>
    public string ContactEmail { get; init; } = null!;

    /// <summary>
    /// Notification preference toggles. Opt-out model: all enabled by default.
    /// Vendors can disable specific notification types they don't want.
    /// </summary>
    public NotificationPreferences NotificationPreferences { get; set; } = NotificationPreferences.AllEnabled;

    /// <summary>
    /// Saved dashboard views for quick-load filters.
    /// Each view captures a named set of filter criteria the vendor has saved.
    /// </summary>
    public List<SavedDashboardView> SavedDashboardViews { get; set; } = [];

    /// <summary>When this account was initialized.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the account was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
