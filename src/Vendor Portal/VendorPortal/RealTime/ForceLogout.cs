namespace VendorPortal.RealTime;

/// <summary>
/// Pushed to <c>user:{userId}</c> when the user's account is deactivated.
/// Clients must disconnect the hub, clear the JWT from memory, and redirect to an "Access Revoked" page.
/// </summary>
public sealed record ForceLogout(
    Guid VendorUserId,
    string Reason,
    DateTimeOffset RevokedAt) : IVendorUserMessage;
