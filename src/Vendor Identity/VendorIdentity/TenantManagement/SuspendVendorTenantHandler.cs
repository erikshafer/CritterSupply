using Messages.Contracts.VendorIdentity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;
using Wolverine;
using Wolverine.Http;

namespace VendorIdentity.TenantManagement;

public static class SuspendVendorTenantHandler
{
    [WolverinePost("/api/vendor-identity/tenants/{tenantId}/suspend")]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        SuspendVendorTenant command,
        VendorIdentityDbContext db,
        CancellationToken cancellation)
    {
        var tenant = await db.Tenants.FirstAsync(t => t.Id == command.TenantId, cancellation);

        tenant.Status = VendorTenantStatus.Suspended;
        tenant.SuspendedAt = DateTimeOffset.UtcNow;
        tenant.SuspensionReason = command.Reason;

        await db.SaveChangesAsync(cancellation);

        var integrationEvent = new VendorTenantSuspended(
            tenant.Id,
            command.Reason,
            tenant.SuspendedAt.Value
        );

        var outgoing = new OutgoingMessages();
        outgoing.Add(integrationEvent);

        return (Results.Ok(), outgoing);
    }
}
