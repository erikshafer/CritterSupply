using Messages.Contracts.VendorIdentity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;
using Wolverine;
using Wolverine.Http;

namespace VendorIdentity.TenantManagement;

public static class TerminateVendorTenantHandler
{
    [WolverinePost("/api/vendor-identity/tenants/{tenantId}/terminate")]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        TerminateVendorTenant command,
        VendorIdentityDbContext db,
        CancellationToken cancellation)
    {
        var tenant = await db.Tenants.FirstAsync(t => t.Id == command.TenantId, cancellation);

        tenant.Status = VendorTenantStatus.Terminated;
        tenant.TerminatedAt = DateTimeOffset.UtcNow;
        tenant.TerminationReason = command.Reason;

        await db.SaveChangesAsync(cancellation);

        var integrationEvent = new VendorTenantTerminated(
            tenant.Id,
            command.Reason,
            tenant.TerminatedAt.Value
        );

        var outgoing = new OutgoingMessages();
        outgoing.Add(integrationEvent);

        return (Results.Ok(), outgoing);
    }
}
