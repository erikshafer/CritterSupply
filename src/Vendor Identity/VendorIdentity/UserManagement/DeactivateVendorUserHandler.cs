using Messages.Contracts.VendorIdentity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;
using VendorIdentity.UserInvitations;
using Wolverine;
using Wolverine.Http;

namespace VendorIdentity.UserManagement;

public static class DeactivateVendorUserHandler
{
    [WolverinePost("/api/vendor-identity/tenants/{tenantId}/users/{userId}/deactivate")]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        DeactivateVendorUser command,
        VendorIdentityDbContext db,
        CancellationToken cancellation)
    {
        var user = await db.Users.FirstAsync(
            u => u.Id == command.UserId && u.VendorTenantId == command.TenantId,
            cancellation);

        var now = DateTimeOffset.UtcNow;

        user.Status = VendorUserStatus.Deactivated;
        user.DeactivatedAt = now;

        await db.SaveChangesAsync(cancellation);

        var integrationEvent = new VendorUserDeactivated(
            user.Id,
            user.VendorTenantId,
            command.Reason,
            now
        );

        var outgoing = new OutgoingMessages();
        outgoing.Add(integrationEvent);

        return (Results.Ok(), outgoing);
    }
}
