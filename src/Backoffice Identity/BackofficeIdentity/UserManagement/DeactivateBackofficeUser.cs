using BackofficeIdentity.Identity;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BackofficeIdentity.UserManagement;

/// <summary>
/// Command to deactivate an backoffice user account.
/// Deactivated users cannot log in but their data is retained for audit purposes.
/// Only SystemAdmin role can deactivate users.
/// </summary>
public sealed record DeactivateBackofficeUser(Guid UserId, string Reason)
{
    public sealed class DeactivateBackofficeUserValidator : AbstractValidator<DeactivateBackofficeUser>
    {
        public DeactivateBackofficeUserValidator()
        {
            RuleFor(x => x.UserId)
                .NotEmpty()
                .WithMessage("User ID is required.");

            RuleFor(x => x.Reason)
                .NotEmpty()
                .MaximumLength(500)
                .WithMessage("Deactivation reason is required (max 500 characters).");
        }
    }
}

/// <summary>
/// Response returned when a user is deactivated.
/// </summary>
public sealed record DeactivateBackofficeUserResponse(
    Guid UserId,
    string Email,
    string Reason,
    DateTimeOffset DeactivatedAt);

/// <summary>
/// Handler for deactivating backoffice users.
/// Invalidates refresh tokens to force logout.
/// </summary>
public static class DeactivateBackofficeUserHandler
{
    public static async Task<BackofficeUser?> Load(
        DeactivateBackofficeUser command,
        BackofficeIdentityDbContext db,
        CancellationToken ct)
    {
        return await db.Users
            .Where(u => u.Id == command.UserId)
            .FirstOrDefaultAsync(ct);
    }

    public static async Task<(DeactivateBackofficeUserResponse?, ProblemDetails?)> Handle(
        DeactivateBackofficeUser command,
        BackofficeUser? user,
        BackofficeIdentityDbContext db,
        CancellationToken ct)
    {
        // User not found
        if (user is null)
        {
            return (null, new ProblemDetails
            {
                Detail = $"Backoffice user with ID '{command.UserId}' was not found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        // Idempotent: user already deactivated
        if (user.Status == BackofficeUserStatus.Deactivated)
        {
            return (new DeactivateBackofficeUserResponse(
                user.Id,
                user.Email,
                user.DeactivationReason ?? command.Reason,
                user.DeactivatedAt!.Value), null);
        }

        // Deactivate user
        user.Status = BackofficeUserStatus.Deactivated;
        user.DeactivatedAt = DateTimeOffset.UtcNow;
        user.DeactivationReason = command.Reason;

        // Invalidate refresh token to force logout
        user.RefreshToken = null;
        user.RefreshTokenExpiresAt = null;

        await db.SaveChangesAsync(ct);

        var response = new DeactivateBackofficeUserResponse(
            user.Id,
            user.Email,
            user.DeactivationReason,
            user.DeactivatedAt.Value);

        return (response, null);
    }
}
