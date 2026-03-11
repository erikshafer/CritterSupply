using FluentValidation;
using Messages.Contracts.VendorIdentity;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;
using VendorIdentity.UserInvitations;

namespace VendorIdentity.UserManagement;

public sealed class ChangeVendorUserRoleValidator : AbstractValidator<ChangeVendorUserRole>
{
    public ChangeVendorUserRoleValidator(VendorIdentityDbContext db)
    {
        RuleFor(x => x.TenantId)
            .NotEmpty()
            .WithMessage("Tenant ID is required")
            .MustAsync(async (tenantId, cancellationToken) =>
            {
                return await db.Tenants.AnyAsync(t => t.Id == tenantId, cancellationToken);
            })
            .WithMessage("Tenant '{PropertyValue}' does not exist");

        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required")
            .MustAsync(async (command, userId, cancellationToken) =>
            {
                return await db.Users.AnyAsync(
                    u => u.Id == userId && u.VendorTenantId == command.TenantId,
                    cancellationToken);
            })
            .WithMessage("User '{PropertyValue}' does not exist in this tenant")
            .MustAsync(async (command, userId, cancellationToken) =>
            {
                var user = await db.Users.FirstOrDefaultAsync(
                    u => u.Id == userId && u.VendorTenantId == command.TenantId,
                    cancellationToken);

                // If user is not currently Admin, or they're not being demoted, no last-admin check needed
                if (user is null || user.Role != VendorRole.Admin || command.NewRole == VendorRole.Admin)
                    return true;

                // Demoting from Admin: count active admins in the tenant
                var activeAdminCount = await db.Users.CountAsync(
                    u => u.VendorTenantId == command.TenantId
                         && u.Role == VendorRole.Admin
                         && u.Status == VendorUserStatus.Active,
                    cancellationToken);

                return activeAdminCount > 1;
            })
            .WithMessage("Cannot demote the last active Admin in the tenant");

        RuleFor(x => x.NewRole)
            .IsInEnum()
            .WithMessage("Role must be Admin, CatalogManager, or ReadOnly");
    }
}
