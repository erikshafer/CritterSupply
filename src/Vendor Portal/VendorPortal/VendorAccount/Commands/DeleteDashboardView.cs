namespace VendorPortal.VendorAccount.Commands;

/// <summary>
/// Deletes a saved dashboard view by its ViewId from the vendor's account.
/// </summary>
public sealed record DeleteDashboardViewCommand(
    Guid VendorTenantId,
    Guid ViewId);
