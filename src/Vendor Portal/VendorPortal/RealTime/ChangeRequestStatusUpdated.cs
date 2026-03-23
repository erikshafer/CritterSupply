namespace VendorPortal.RealTime;

/// <summary>
/// Pushed to <c>vendor:{tenantId}</c> when a change request status changes.
/// All users in the tenant see this broadcast — useful for team awareness.
/// Clients should refresh the change request list badge count.
/// </summary>
public sealed record ChangeRequestStatusUpdated(
    Guid VendorTenantId,
    Guid RequestId,
    string Sku,
    string Status,
    DateTimeOffset UpdatedAt) : IVendorTenantMessage;
