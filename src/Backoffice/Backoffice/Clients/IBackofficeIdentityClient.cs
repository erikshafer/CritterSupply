namespace Backoffice.Clients;

/// <summary>
/// Client interface for BackofficeIdentity BC user management operations.
/// All methods require SystemAdmin role on the backend.
/// </summary>
public interface IBackofficeIdentityClient
{
    /// <summary>
    /// Retrieves all backoffice users.
    /// Returns users ordered by creation date (newest first).
    /// </summary>
    Task<IReadOnlyList<BackofficeUserSummaryDto>> ListUsersAsync();

    /// <summary>
    /// Creates a new backoffice user.
    /// Returns created user details or null if creation failed (e.g., email already exists).
    /// </summary>
    Task<CreateUserResultDto?> CreateUserAsync(
        string email,
        string password,
        string firstName,
        string lastName,
        string role);

    /// <summary>
    /// Changes a backoffice user's role.
    /// Returns true if successful, false if user not found.
    /// </summary>
    Task<bool> ChangeUserRoleAsync(Guid userId, string newRole);

    /// <summary>
    /// Deactivates a backoffice user account.
    /// Returns true if successful, false if user not found.
    /// </summary>
    Task<bool> DeactivateUserAsync(Guid userId, string reason);

    /// <summary>
    /// Resets a backoffice user's password.
    /// Invalidates the user's refresh token, forcing re-authentication.
    /// Returns true if successful, false if user not found.
    /// </summary>
    Task<bool> ResetUserPasswordAsync(Guid userId, string newPassword);
}

/// <summary>
/// DTO for backoffice user summary (list view).
/// Matches BackofficeUserSummary from BackofficeIdentity BC.
/// </summary>
public sealed record BackofficeUserSummaryDto(
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
/// DTO for created user response.
/// Matches CreateBackofficeUserResponse from BackofficeIdentity BC.
/// </summary>
public sealed record CreateUserResultDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    DateTimeOffset CreatedAt);
