using Messages.Contracts.VendorIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;
using Wolverine;
using Wolverine.Http;

namespace VendorIdentity.TenantManagement;

public static class ReinstateVendorTenantHandler
{
    [Authorize]
    [WolverinePost("/api/vendor-identity/tenants/{tenantId}/reinstate")]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        ReinstateVendorTenant command,
        VendorIdentityDbContext db,
        CancellationToken cancellation)
    {
        var tenant = await db.Tenants.FirstAsync(t => t.Id == command.TenantId, cancellation);

        var reinstatedAt = DateTimeOffset.UtcNow;

        tenant.Status = VendorTenantStatus.Active;
        tenant.SuspendedAt = null;
        tenant.SuspensionReason = null;

        await db.SaveChangesAsync(cancellation);

        var integrationEvent = new VendorTenantReinstated(
            tenant.Id,
            reinstatedAt
        );

        var outgoing = new OutgoingMessages();
        outgoing.Add(integrationEvent);

        return (Results.Ok(), outgoing);
    }
}
