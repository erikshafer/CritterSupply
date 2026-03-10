using FluentValidation;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;
using VendorIdentity.UserInvitations;

namespace VendorIdentity.UserManagement;

public sealed class ReactivateVendorUserValidator : AbstractValidator<ReactivateVendorUser>
{
    public ReactivateVendorUserValidator(VendorIdentityDbContext db)
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
                return user is not null && user.Status == VendorUserStatus.Deactivated;
            })
            .WithMessage("User can only be reactivated when in Deactivated status");
    }
}
