using FluentValidation;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;

namespace VendorIdentity.TenantManagement;

public sealed class ReinstateVendorTenantValidator : AbstractValidator<ReinstateVendorTenant>
{
    public ReinstateVendorTenantValidator(VendorIdentityDbContext db)
    {
        RuleFor(x => x.TenantId)
            .NotEmpty()
            .WithMessage("Tenant ID is required")
            .MustAsync(async (tenantId, cancellationToken) =>
            {
                return await db.Tenants.AnyAsync(t => t.Id == tenantId, cancellationToken);
            })
            .WithMessage("Tenant '{PropertyValue}' does not exist")
            .MustAsync(async (tenantId, cancellationToken) =>
            {
                var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
                return tenant is not null && tenant.Status == VendorTenantStatus.Suspended;
            })
            .WithMessage("Tenant can only be reinstated when in Suspended status");
    }
}
