using AdminIdentity.Identity;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AdminIdentity.UserManagement;

/// <summary>
/// Command to change an admin user's role.
/// Only SystemAdmin role can change user roles.
/// </summary>
public sealed record ChangeAdminUserRole(Guid UserId, AdminRole NewRole)
{
    public sealed class ChangeAdminUserRoleValidator : AbstractValidator<ChangeAdminUserRole>
    {
        public ChangeAdminUserRoleValidator()
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
public sealed record ChangeAdminUserRoleResponse(
    Guid UserId,
    string Email,
    string PreviousRole,
    string NewRole,
    DateTimeOffset ChangedAt);

/// <summary>
/// Handler for changing admin user roles.
/// </summary>
public static class ChangeAdminUserRoleHandler
{
    public static async Task<AdminUser?> Load(
        ChangeAdminUserRole command,
        AdminIdentityDbContext db,
        CancellationToken ct)
    {
        return await db.Users
            .Where(u => u.Id == command.UserId)
            .FirstOrDefaultAsync(ct);
    }

    public static async Task<(ChangeAdminUserRoleResponse?, ProblemDetails?)> Handle(
        ChangeAdminUserRole command,
        AdminUser? user,
        AdminIdentityDbContext db,
        CancellationToken ct)
    {
        // User not found
        if (user is null)
        {
            return (null, new ProblemDetails
            {
                Detail = $"Admin user with ID '{command.UserId}' was not found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        // User is deactivated
        if (user.Status == AdminUserStatus.Deactivated)
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
            return (new ChangeAdminUserRoleResponse(
                user.Id,
                user.Email,
                user.Role.ToString(),
                user.Role.ToString(),
                DateTimeOffset.UtcNow), null);
        }

        var previousRole = user.Role;
        user.Role = command.NewRole;

        await db.SaveChangesAsync(ct);

        var response = new ChangeAdminUserRoleResponse(
            user.Id,
            user.Email,
            previousRole.ToString(),
            user.Role.ToString(),
            DateTimeOffset.UtcNow);

        return (response, null);
    }
}
