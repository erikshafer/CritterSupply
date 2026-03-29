using Messages.Contracts.VendorIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;
using VendorIdentity.UserInvitations;
using Wolverine;
using Wolverine.Http;

namespace VendorIdentity.UserManagement;

public static class ChangeVendorUserRoleHandler
{
    [Authorize]
    [WolverinePatch("/api/vendor-identity/tenants/{tenantId}/users/{userId}/role")]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        ChangeVendorUserRole command,
        VendorIdentityDbContext db,
        CancellationToken cancellation)
    {
        var user = await db.Users.FirstAsync(
            u => u.Id == command.UserId && u.VendorTenantId == command.TenantId,
            cancellation);

        var oldRole = user.Role;
        var now = DateTimeOffset.UtcNow;

        user.Role = command.NewRole;

        await db.SaveChangesAsync(cancellation);

        var integrationEvent = new VendorUserRoleChanged(
            user.Id,
            user.VendorTenantId,
            oldRole,
            command.NewRole,
            now
        );

        var outgoing = new OutgoingMessages();
        outgoing.Add(integrationEvent);

        return (Results.Ok(), outgoing);
    }
}
