using FluentValidation;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;

namespace VendorIdentity.TenantManagement;

public sealed class SuspendVendorTenantValidator : AbstractValidator<SuspendVendorTenant>
{
    public SuspendVendorTenantValidator(VendorIdentityDbContext db)
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
                return tenant is not null && tenant.Status == VendorTenantStatus.Active;
            })
            .WithMessage("Tenant can only be suspended when in Active status");

        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("Suspension reason is required")
            .MaximumLength(500)
            .WithMessage("Suspension reason must not exceed 500 characters");
    }
}
