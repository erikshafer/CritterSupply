namespace VendorPortal.RealTime;

/// <summary>
/// Pushed to <c>vendor:{tenantId}</c> when sales metrics change.
/// Lightweight notification: "data changed, please refresh."
/// Clients should re-fetch the dashboard summary to get updated numbers.
/// </summary>
public sealed record SalesMetricUpdated(
    Guid VendorTenantId,
    DateTimeOffset UpdatedAt) : IVendorTenantMessage;
