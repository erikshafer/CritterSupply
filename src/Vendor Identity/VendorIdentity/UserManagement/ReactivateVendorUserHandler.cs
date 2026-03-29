using Messages.Contracts.VendorIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;
using VendorIdentity.UserInvitations;
using Wolverine;
using Wolverine.Http;

namespace VendorIdentity.UserManagement;

public static class ReactivateVendorUserHandler
{
    [Authorize]
    [WolverinePost("/api/vendor-identity/tenants/{tenantId}/users/{userId}/reactivate")]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        ReactivateVendorUser command,
        VendorIdentityDbContext db,
        CancellationToken cancellation)
    {
        var user = await db.Users.FirstAsync(
            u => u.Id == command.UserId && u.VendorTenantId == command.TenantId,
            cancellation);

        var now = DateTimeOffset.UtcNow;

        user.Status = VendorUserStatus.Active;
        user.DeactivatedAt = null;

        await db.SaveChangesAsync(cancellation);

        var integrationEvent = new VendorUserReactivated(
            user.Id,
            user.VendorTenantId,
            now
        );

        var outgoing = new OutgoingMessages();
        outgoing.Add(integrationEvent);

        return (Results.Ok(), outgoing);
    }
}
