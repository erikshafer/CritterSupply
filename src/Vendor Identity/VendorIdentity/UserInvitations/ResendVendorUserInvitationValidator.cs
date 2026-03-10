using FluentValidation;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;

namespace VendorIdentity.UserInvitations;

public sealed class ResendVendorUserInvitationValidator : AbstractValidator<ResendVendorUserInvitation>
{
    public ResendVendorUserInvitationValidator(VendorIdentityDbContext db)
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
                return await db.Invitations.AnyAsync(
                    i => i.VendorUserId == userId
                         && i.VendorTenantId == command.TenantId
                         && i.Status == InvitationStatus.Pending,
                    cancellationToken);
            })
            .WithMessage("No pending invitation found for this user");
    }
}
