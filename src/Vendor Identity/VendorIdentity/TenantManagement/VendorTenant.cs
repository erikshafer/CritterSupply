using VendorIdentity.UserInvitations;

namespace VendorIdentity.TenantManagement;

public sealed class VendorTenant
{
    public Guid Id { get; set; }
    public string OrganizationName { get; set; } = default!;
    public string ContactEmail { get; set; } = default!;
    public VendorTenantStatus Status { get; set; }
    public DateTimeOffset OnboardedAt { get; set; }
    public DateTimeOffset? SuspendedAt { get; set; }
    public string? SuspensionReason { get; set; }
    public DateTimeOffset? TerminatedAt { get; set; }
    public string? TerminationReason { get; set; }

    // Navigation properties
    public ICollection<VendorUser> Users { get; set; } = new List<VendorUser>();
}
