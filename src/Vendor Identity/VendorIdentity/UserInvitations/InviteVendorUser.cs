using Messages.Contracts.VendorIdentity;

namespace VendorIdentity.UserInvitations;

/// <summary>
/// Invites a new user to a vendor tenant.
/// </summary>
public sealed record InviteVendorUser(
    Guid TenantId,
    string Email,
    string FirstName,
    string LastName,
    VendorRole Role
);
