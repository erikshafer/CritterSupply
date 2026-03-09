namespace Messages.Contracts.VendorIdentity;

/// <summary>
/// Published when an admin deactivates a vendor user.
/// Triggers force-logout via SignalR to user:{userId} group (Phase 2+).
/// </summary>
public sealed record VendorUserDeactivated(
    Guid UserId,
    Guid VendorTenantId,
    string Reason,
    DateTimeOffset DeactivatedAt
);
