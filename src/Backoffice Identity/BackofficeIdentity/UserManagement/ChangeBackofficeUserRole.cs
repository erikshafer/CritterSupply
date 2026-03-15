using BackofficeIdentity.Identity;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BackofficeIdentity.UserManagement;

/// <summary>
/// Command to change an backoffice user's role.
/// Only SystemAdmin role can change user roles.
/// </summary>
public sealed record ChangeBackofficeUserRole(Guid UserId, BackofficeRole NewRole)
{
    public sealed class ChangeBackofficeUserRoleValidator : AbstractValidator<ChangeBackofficeUserRole>
    {
        public ChangeBackofficeUserRoleValidator()
        {
            RuleFor(x => x.UserId)
                .NotEmpty()
                .WithMessage("User ID is required.");

            RuleFor(x => x.NewRole)
                .IsInEnum()
                .WithMessage("Valid admin role is required.");
        }
    }
}

/// <summary>
/// Response returned when a role change succeeds.
/// </summary>
public sealed record ChangeBackofficeUserRoleResponse(
    Guid UserId,
    string Email,
    string PreviousRole,
    string NewRole,
    DateTimeOffset ChangedAt);

/// <summary>
/// Handler for changing backoffice user roles.
/// </summary>
public static class ChangeBackofficeUserRoleHandler
{
    public static async Task<BackofficeUser?> Load(
        ChangeBackofficeUserRole command,
        BackofficeIdentityDbContext db,
        CancellationToken ct)
    {
        return await db.Users
            .Where(u => u.Id == command.UserId)
            .FirstOrDefaultAsync(ct);
    }

    public static async Task<(ChangeBackofficeUserRoleResponse?, ProblemDetails?)> Handle(
        ChangeBackofficeUserRole command,
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

        // User is deactivated
        if (user.Status == BackofficeUserStatus.Deactivated)
        {
            return (null, new ProblemDetails
            {
                Detail = "Cannot change the role of a deactivated user. Reactivate the user first.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Idempotent: role already set
        if (user.Role == command.NewRole)
        {
            return (new ChangeBackofficeUserRoleResponse(
                user.Id,
                user.Email,
                user.Role.ToString(),
                user.Role.ToString(),
                DateTimeOffset.UtcNow), null);
        }

        var previousRole = user.Role;
        user.Role = command.NewRole;

        await db.SaveChangesAsync(ct);

        var response = new ChangeBackofficeUserRoleResponse(
            user.Id,
            user.Email,
            previousRole.ToString(),
            user.Role.ToString(),
            DateTimeOffset.UtcNow);

        return (response, null);
    }
}
