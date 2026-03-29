using FluentValidation;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;

namespace VendorIdentity.UserInvitations;

/// <summary>
/// Revokes a pending invitation for a vendor user. The invitation can no longer be accepted.
/// </summary>
public sealed record RevokeVendorUserInvitation(
    Guid TenantId,
    Guid UserId,
    string Reason
);

public sealed class RevokeVendorUserInvitationValidator : AbstractValidator<RevokeVendorUserInvitation>
{
    public RevokeVendorUserInvitationValidator(VendorIdentityDbContext db)
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

        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("Revocation reason is required")
            .MaximumLength(500)
            .WithMessage("Revocation reason must not exceed 500 characters");
    }
}
