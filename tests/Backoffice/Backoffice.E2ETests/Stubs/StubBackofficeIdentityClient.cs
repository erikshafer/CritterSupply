using Backoffice.Clients;

namespace Backoffice.E2ETests.Stubs;

/// <summary>
/// Stub implementation of IBackofficeIdentityClient for E2E testing.
/// Provides in-memory user management without requiring BackofficeIdentity BC.
/// </summary>
public class StubBackofficeIdentityClient : IBackofficeIdentityClient
{
    private readonly List<BackofficeUserSummaryDto> _users = new();

    /// <summary>
    /// When true, all methods will throw UnauthorizedAccessException to simulate session expiry.
    /// </summary>
    public bool SimulateSessionExpired { get; set; }

    public Task<IReadOnlyList<BackofficeUserSummaryDto>> ListUsersAsync()
    {
        if (SimulateSessionExpired)
        {
            throw new UnauthorizedAccessException("Session expired");
        }

        return Task.FromResult<IReadOnlyList<BackofficeUserSummaryDto>>(_users);
    }

    public Task<CreateUserResultDto?> CreateUserAsync(
        string email,
        string password,
        string firstName,
        string lastName,
        string role)
    {
        if (SimulateSessionExpired)
        {
            throw new UnauthorizedAccessException("Session expired");
        }

        // Check for duplicate email
        if (_users.Any(u => u.Email == email))
        {
            return Task.FromResult<CreateUserResultDto?>(null);
        }

        var user = new CreateUserResultDto(
            Id: Guid.NewGuid(),
            Email: email,
            FirstName: firstName,
            LastName: lastName,
            Role: role,
            CreatedAt: DateTimeOffset.UtcNow);

        var userSummary = new BackofficeUserSummaryDto(
            Id: user.Id,
            Email: user.Email,
            FirstName: user.FirstName,
            LastName: user.LastName,
            Role: user.Role,
            Status: "Active",
            CreatedAt: user.CreatedAt,
            LastLoginAt: null,
            DeactivatedAt: null);

        _users.Add(userSummary);
        return Task.FromResult<CreateUserResultDto?>(user);
    }

    public Task<bool> ChangeUserRoleAsync(Guid userId, string newRole)
    {
        if (SimulateSessionExpired)
        {
            throw new UnauthorizedAccessException("Session expired");
        }

        var user = _users.FirstOrDefault(u => u.Id == userId);
        if (user is null) return Task.FromResult(false);

        _users.Remove(user);
        _users.Add(user with { Role = newRole });
        return Task.FromResult(true);
    }

    public Task<bool> DeactivateUserAsync(Guid userId, string reason)
    {
        if (SimulateSessionExpired)
        {
            throw new UnauthorizedAccessException("Session expired");
        }

        var user = _users.FirstOrDefault(u => u.Id == userId);
        if (user is null) return Task.FromResult(false);

        _users.Remove(user);
        _users.Add(user with { Status = "Deactivated", DeactivatedAt = DateTimeOffset.UtcNow });
        return Task.FromResult(true);
    }

    public Task<bool> ResetUserPasswordAsync(Guid userId, string newPassword)
    {
        if (SimulateSessionExpired)
        {
            throw new UnauthorizedAccessException("Session expired");
        }

        var user = _users.FirstOrDefault(u => u.Id == userId);
        return Task.FromResult(user is not null);
    }

    /// <summary>
    /// Test helper: Add a pre-configured user to the stub.
    /// </summary>
    public void AddUser(BackofficeUserSummaryDto user) => _users.Add(user);

    /// <summary>
    /// Test helper: Clear all users.
    /// </summary>
    public void Clear()
    {
        _users.Clear();
        SimulateSessionExpired = false;
    }
}
