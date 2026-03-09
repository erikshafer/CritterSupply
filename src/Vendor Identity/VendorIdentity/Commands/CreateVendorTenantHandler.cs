using Messages.Contracts.VendorIdentity;
using VendorIdentity.Data;
using VendorIdentity.Entities;
using Wolverine;

namespace VendorIdentity.Commands;

public sealed class CreateVendorTenantHandler
{
    public static async Task<(Guid TenantId, OutgoingMessages Events)> Handle(
        CreateVendorTenant command,
        VendorIdentityDbContext db,
        CancellationToken cancellation)
    {
        var tenant = new VendorTenant
        {
            Id = Guid.NewGuid(),
            OrganizationName = command.OrganizationName,
            ContactEmail = command.ContactEmail,
            Status = VendorTenantStatus.Onboarding,
            OnboardedAt = DateTimeOffset.UtcNow
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(cancellation);

        var integrationEvent = new VendorTenantCreated(
            tenant.Id,
            tenant.OrganizationName,
            tenant.ContactEmail,
            tenant.OnboardedAt
        );

        var outgoing = new OutgoingMessages();
        outgoing.Add(integrationEvent);

        return (tenant.Id, outgoing);
    }
}
