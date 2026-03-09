namespace VendorIdentity.Entities;

public sealed class VendorUserInvitation
{
    public Guid Id { get; set; }
    public Guid VendorUserId { get; set; }
    public Guid VendorTenantId { get; set; }
    public string Token { get; set; } = default!; // Hash of the token sent in email (never store raw token)
    public Messages.Contracts.VendorIdentity.VendorRole InvitedRole { get; set; }
    public InvitationStatus Status { get; set; }
    public DateTimeOffset InvitedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public int ResendCount { get; set; }

    // Navigation properties
    public VendorUser VendorUser { get; set; } = default!;
}
