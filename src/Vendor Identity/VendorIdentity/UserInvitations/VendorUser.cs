using VendorIdentity.TenantManagement;

namespace VendorIdentity.UserInvitations;

public sealed class VendorUser
{
    public Guid Id { get; set; }
    public Guid VendorTenantId { get; set; }
    public string Email { get; set; } = default!;
    public string? PasswordHash { get; set; }
    public string FirstName { get; set; } = default!;
    public string LastName { get; set; } = default!;
    public Messages.Contracts.VendorIdentity.VendorRole Role { get; set; }
    public VendorUserStatus Status { get; set; }
    public DateTimeOffset? InvitedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    // Navigation properties
    public VendorTenant VendorTenant { get; set; } = default!;
    public ICollection<VendorUserInvitation> Invitations { get; set; } = new List<VendorUserInvitation>();
}
