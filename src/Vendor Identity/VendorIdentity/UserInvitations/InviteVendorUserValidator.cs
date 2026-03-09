using FluentValidation;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;

namespace VendorIdentity.UserInvitations;

public sealed class InviteVendorUserValidator : AbstractValidator<InviteVendorUser>
{
    public InviteVendorUserValidator(VendorIdentityDbContext db)
    {
        RuleFor(x => x.VendorTenantId)
            .NotEmpty()
            .WithMessage("Vendor tenant ID is required")
            .MustAsync(async (tenantId, cancellationToken) =>
            {
                return await db.Tenants.AnyAsync(t => t.Id == tenantId, cancellationToken);
            })
            .WithMessage("Vendor tenant '{PropertyValue}' does not exist");

        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("Email is required")
            .EmailAddress()
            .WithMessage("Email must be a valid email address")
            .MaximumLength(256)
            .WithMessage("Email must not exceed 256 characters")
            .MustAsync(async (email, cancellationToken) =>
            {
                // Email must be unique system-wide (not per-tenant)
                var exists = await db.Users.AnyAsync(u => u.Email == email, cancellationToken);
                return !exists;
            })
            .WithMessage("Email '{PropertyValue}' is already registered");

        RuleFor(x => x.FirstName)
            .NotEmpty()
            .WithMessage("First name is required")
            .MaximumLength(100)
            .WithMessage("First name must not exceed 100 characters");

        RuleFor(x => x.LastName)
            .NotEmpty()
            .WithMessage("Last name is required")
            .MaximumLength(100)
            .WithMessage("Last name must not exceed 100 characters");

        RuleFor(x => x.Role)
            .IsInEnum()
            .WithMessage("Role must be Admin, CatalogManager, or ReadOnly");
    }
}
