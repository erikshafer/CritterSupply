using FluentValidation;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;

namespace VendorIdentity.TenantManagement;

public sealed class TerminateVendorTenantValidator : AbstractValidator<TerminateVendorTenant>
{
    public TerminateVendorTenantValidator(VendorIdentityDbContext db)
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
                return tenant is not null && tenant.Status != VendorTenantStatus.Terminated;
            })
            .WithMessage("Cannot terminate an already terminated tenant");

        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("Termination reason is required")
            .MaximumLength(500)
            .WithMessage("Termination reason must not exceed 500 characters");
    }
}
