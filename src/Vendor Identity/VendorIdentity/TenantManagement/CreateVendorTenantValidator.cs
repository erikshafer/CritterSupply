using FluentValidation;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;

namespace VendorIdentity.TenantManagement;

public sealed class CreateVendorTenantValidator : AbstractValidator<CreateVendorTenant>
{
    public CreateVendorTenantValidator(VendorIdentityDbContext db)
    {
        RuleFor(x => x.OrganizationName)
            .NotEmpty()
            .WithMessage("Organization name is required")
            .MaximumLength(200)
            .WithMessage("Organization name must not exceed 200 characters")
            .MustAsync(async (name, cancellationToken) =>
            {
                var exists = await db.Tenants.AnyAsync(t => t.OrganizationName == name, cancellationToken);
                return !exists;
            })
            .WithMessage("Organization name '{PropertyValue}' is already registered");

        RuleFor(x => x.ContactEmail)
            .NotEmpty()
            .WithMessage("Contact email is required")
            .EmailAddress()
            .WithMessage("Contact email must be a valid email address")
            .MaximumLength(256)
            .WithMessage("Contact email must not exceed 256 characters");
    }
}
