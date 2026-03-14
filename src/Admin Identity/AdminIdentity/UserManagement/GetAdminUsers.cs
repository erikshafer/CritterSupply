using AdminIdentity.Identity;
using Microsoft.EntityFrameworkCore;

namespace AdminIdentity.UserManagement;

/// <summary>
/// Query to retrieve all admin users.
/// Only SystemAdmin role can list admin users.
/// </summary>
public sealed record GetAdminUsers;

/// <summary>
/// Admin user summary for list views.
/// Does NOT include password hash or refresh tokens.
/// </summary>
public sealed record AdminUserSummary(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? DeactivatedAt);

/// <summary>
/// Handler for listing all admin users.
/// Returns users ordered by creation date (newest first).
/// </summary>
public static class GetAdminUsersHandler
{
    public static async Task<IReadOnlyList<AdminUserSummary>> Handle(
        GetAdminUsers query,
        AdminIdentityDbContext db,
        CancellationToken ct)
    {
        var users = await db.Users
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new AdminUserSummary(
                u.Id,
                u.Email,
                u.FirstName,
                u.LastName,
                u.Role.ToString(),
                u.Status.ToString(),
                u.CreatedAt,
                u.LastLoginAt,
                u.DeactivatedAt))
            .ToListAsync(ct);

        return users;
    }
}
