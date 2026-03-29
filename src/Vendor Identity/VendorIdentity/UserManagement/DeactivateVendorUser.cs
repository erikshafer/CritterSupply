using FluentValidation;
using Messages.Contracts.VendorIdentity;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;
using VendorIdentity.UserInvitations;

namespace VendorIdentity.UserManagement;

/// <summary>
/// Deactivates an active vendor user. The user can no longer log in.
/// </summary>
public sealed record DeactivateVendorUser(
    Guid TenantId,
    Guid UserId,
    string Reason
);

public sealed class DeactivateVendorUserValidator : AbstractValidator<DeactivateVendorUser>
{
    public DeactivateVendorUserValidator(VendorIdentityDbContext db)
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
                return user is not null && user.Status == VendorUserStatus.Active;
            })
            .WithMessage("User can only be deactivated when in Active status")
            .MustAsync(async (command, userId, cancellationToken) =>
            {
                var user = await db.Users.FirstOrDefaultAsync(
                    u => u.Id == userId && u.VendorTenantId == command.TenantId,
                    cancellationToken);

                // If user is not an Admin, no last-admin check needed
                if (user is null || user.Role != VendorRole.Admin)
                    return true;

                // Count active admins in the tenant
                var activeAdminCount = await db.Users.CountAsync(
                    u => u.VendorTenantId == command.TenantId
                         && u.Role == VendorRole.Admin
                         && u.Status == VendorUserStatus.Active,
                    cancellationToken);

                return activeAdminCount > 1;
            })
            .WithMessage("Cannot deactivate the last active Admin in the tenant");

        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("Deactivation reason is required")
            .MaximumLength(500)
            .WithMessage("Deactivation reason must not exceed 500 characters");
    }
}
