namespace VendorPortal.VendorAccount.Commands;

/// <summary>
/// Saves a named dashboard view with filter criteria for a vendor tenant.
/// The saved view enables quick-load of frequently-used dashboard configurations.
/// </summary>
public sealed record SaveDashboardViewCommand(
    Guid VendorTenantId,
    string ViewName,
    DashboardFilterCriteria FilterCriteria);
