using System.Security.Cryptography;
using System.Text;
using Messages.Contracts.VendorIdentity;
using Microsoft.AspNetCore.Authorization;
using VendorIdentity.Identity;
using Wolverine;
using Wolverine.Http;

namespace VendorIdentity.UserInvitations;

public static class InviteVendorUserHandler
{
    [Authorize]
    [WolverinePost("/api/vendor-identity/tenants/{tenantId}/users/invite")]
    public static async Task<(CreationResponse, OutgoingMessages)> Handle(
        InviteVendorUser command,
        VendorIdentityDbContext db,
        CancellationToken cancellation)
    {
        // Generate cryptographically random invitation token (32 bytes = 256 bits)
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToBase64String(tokenBytes);

        // Store SHA-256 hash of token (never store raw token)
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        var user = new VendorUser
        {
            Id = Guid.NewGuid(),
            VendorTenantId = command.TenantId,
            Email = command.Email,
            FirstName = command.FirstName,
            LastName = command.LastName,
            Role = command.Role,
            Status = VendorUserStatus.Invited,
            InvitedAt = DateTimeOffset.UtcNow
        };

        db.Users.Add(user);

        var invitation = new VendorUserInvitation
        {
            Id = Guid.NewGuid(),
            VendorUserId = user.Id,
            VendorTenantId = command.TenantId,
            Token = tokenHash,
            InvitedRole = command.Role,
            Status = InvitationStatus.Pending,
            InvitedAt = user.InvitedAt.Value,
            ExpiresAt = user.InvitedAt.Value.AddHours(72), // 72-hour expiry
            ResendCount = 0
        };

        db.Invitations.Add(invitation);

        await db.SaveChangesAsync(cancellation);

        var integrationEvent = new VendorUserInvited(
            user.Id,
            user.VendorTenantId,
            user.Email,
            user.Role,
            user.InvitedAt.Value,
            invitation.ExpiresAt
        );

        // Note: In a real implementation, you would also publish an email notification
        // event with the rawToken (sent only once in email, never stored).
        // For Phase 1, we're just focusing on the core entities and integration events.

        var outgoing = new OutgoingMessages();
        outgoing.Add(integrationEvent);

        var response = new CreationResponse($"/api/vendor-identity/tenants/{command.TenantId}/users/{user.Id}");

        return (response, outgoing);
    }
}
