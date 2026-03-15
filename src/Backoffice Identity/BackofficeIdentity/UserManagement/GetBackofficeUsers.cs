using BackofficeIdentity.Identity;
using Microsoft.EntityFrameworkCore;

namespace BackofficeIdentity.UserManagement;

/// <summary>
/// Query to retrieve all backoffice users.
/// Only SystemAdmin role can list backoffice users.
/// </summary>
public sealed record GetBackofficeUsers;

/// <summary>
/// Backoffice user summary for list views.
/// Does NOT include password hash or refresh tokens.
/// </summary>
public sealed record BackofficeUserSummary(
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
/// Handler for listing all backoffice users.
/// Returns users ordered by creation date (newest first).
/// </summary>
public static class GetBackofficeUsersHandler
{
    public static async Task<IReadOnlyList<BackofficeUserSummary>> Handle(
        GetBackofficeUsers query,
        BackofficeIdentityDbContext db,
        CancellationToken ct)
    {
        var users = await db.Users
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new BackofficeUserSummary(
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
