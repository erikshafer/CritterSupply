namespace VendorPortal.TeamManagement;

/// <summary>
/// Marten document representing a pending invitation in the BFF's local read model.
/// Populated from VendorIdentity integration events (VendorUserInvited,
/// VendorUserActivated, VendorUserInvitationResent, VendorUserInvitationRevoked).
/// Multi-tenancy: all queries must filter by VendorTenantId.
/// </summary>
public sealed class TeamInvitation
{
    /// <summary>Marten document Id — equals VendorUserId (one active invitation per user).</summary>
    public Guid Id { get; set; }

    public Guid VendorTenantId { get; set; }
    public string Email { get; set; } = null!;
    public string Role { get; set; } = null!;
    public string Status { get; set; } = null!;
    public int ResendCount { get; set; }
    public DateTimeOffset InvitedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
