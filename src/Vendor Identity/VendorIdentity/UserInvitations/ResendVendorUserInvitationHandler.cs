using System.Security.Cryptography;
using System.Text;
using Messages.Contracts.VendorIdentity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;
using Wolverine;
using Wolverine.Http;

namespace VendorIdentity.UserInvitations;

public static class ResendVendorUserInvitationHandler
{
    [WolverinePost("/api/vendor-identity/tenants/{tenantId}/users/{userId}/invitation/resend")]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        ResendVendorUserInvitation command,
        VendorIdentityDbContext db,
        CancellationToken cancellation)
    {
        var invitation = await db.Invitations
            .Where(i => i.VendorUserId == command.UserId
                        && i.VendorTenantId == command.TenantId
                        && i.Status == InvitationStatus.Pending)
            .OrderByDescending(i => i.InvitedAt)
            .FirstAsync(cancellation);

        // Generate new cryptographically random invitation token (32 bytes = 256 bits)
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToBase64String(tokenBytes);

        // Store SHA-256 hash of token (never store raw token)
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        var now = DateTimeOffset.UtcNow;

        invitation.Token = tokenHash;
        invitation.ExpiresAt = now.AddHours(72);
        invitation.ResendCount += 1;

        await db.SaveChangesAsync(cancellation);

        var integrationEvent = new VendorUserInvitationResent(
            invitation.Id,
            command.UserId,
            command.TenantId,
            invitation.ResendCount,
            now,
            invitation.ExpiresAt
        );

        var outgoing = new OutgoingMessages();
        outgoing.Add(integrationEvent);

        return (Results.Ok(), outgoing);
    }
}
