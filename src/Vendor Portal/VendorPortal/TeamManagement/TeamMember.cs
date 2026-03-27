namespace VendorPortal.TeamManagement;

/// <summary>
/// Marten document representing a vendor team member in the BFF's local read model.
/// Populated from VendorIdentity integration events (VendorUserInvited, VendorUserActivated,
/// VendorUserDeactivated, VendorUserReactivated, VendorUserRoleChanged).
/// Multi-tenancy: all queries must filter by VendorTenantId.
/// </summary>
public sealed class TeamMember
{
    /// <summary>Marten document Id — equals VendorUserId.</summary>
    public Guid Id { get; set; }

    public Guid VendorTenantId { get; set; }
    public string Email { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string Role { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset? InvitedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}
