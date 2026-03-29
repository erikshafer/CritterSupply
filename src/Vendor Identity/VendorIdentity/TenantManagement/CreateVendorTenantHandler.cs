using Messages.Contracts.VendorIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using VendorIdentity.Identity;
using Wolverine;
using Wolverine.Http;

namespace VendorIdentity.TenantManagement;

public static class CreateVendorTenantHandler
{
    [Authorize]
    [WolverinePost("/api/vendor-identity/tenants")]
    public static async Task<(CreationResponse, OutgoingMessages)> Handle(
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

        var response = new CreationResponse($"/api/vendor-identity/tenants/{tenant.Id}");

        return (response, outgoing);
    }
}
