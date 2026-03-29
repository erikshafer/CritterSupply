using Messages.Contracts.VendorIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;
using Wolverine;
using Wolverine.Http;

namespace VendorIdentity.UserInvitations;

public static class RevokeVendorUserInvitationHandler
{
    [Authorize]
    [WolverinePost("/api/vendor-identity/tenants/{tenantId}/users/{userId}/invitation/revoke")]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        RevokeVendorUserInvitation command,
        VendorIdentityDbContext db,
        CancellationToken cancellation)
    {
        var invitation = await db.Invitations
            .Where(i => i.VendorUserId == command.UserId
                        && i.VendorTenantId == command.TenantId
                        && i.Status == InvitationStatus.Pending)
            .OrderByDescending(i => i.InvitedAt)
            .FirstAsync(cancellation);

        var now = DateTimeOffset.UtcNow;

        invitation.Status = InvitationStatus.Revoked;
        invitation.RevokedAt = now;

        await db.SaveChangesAsync(cancellation);

        var integrationEvent = new VendorUserInvitationRevoked(
            invitation.Id,
            command.UserId,
            command.TenantId,
            command.Reason,
            now
        );

        var outgoing = new OutgoingMessages();
        outgoing.Add(integrationEvent);

        return (Results.Ok(), outgoing);
    }
}
