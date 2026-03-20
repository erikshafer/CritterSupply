using System.Net.Http.Json;
using Backoffice.Clients;

namespace Backoffice.Api.Clients;

/// <summary>
/// HTTP client for BackofficeIdentity BC user management operations.
/// Calls BackofficeIdentity.Api endpoints at http://localhost:5249.
/// All endpoints require JWT bearer token with SystemAdmin role.
/// </summary>
public sealed class BackofficeIdentityClient : IBackofficeIdentityClient
{
    private readonly HttpClient _httpClient;

    public BackofficeIdentityClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<BackofficeUserSummaryDto>> ListUsersAsync()
    {
        var response = await _httpClient.GetAsync("/api/backoffice-identity/users");

        if (!response.IsSuccessStatusCode)
        {
            return Array.Empty<BackofficeUserSummaryDto>();
        }

        var users = await response.Content.ReadFromJsonAsync<IReadOnlyList<BackofficeUserSummaryDto>>();
        return users ?? Array.Empty<BackofficeUserSummaryDto>();
    }

    public async Task<CreateUserResultDto?> CreateUserAsync(
        string email,
        string password,
        string firstName,
        string lastName,
        string role)
    {
        var request = new
        {
            Email = email,
            Password = password,
            FirstName = firstName,
            LastName = lastName,
            Role = role
        };

        var response = await _httpClient.PostAsJsonAsync("/api/backoffice-identity/users", request);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<CreateUserResultDto>();
    }

    public async Task<bool> ChangeUserRoleAsync(Guid userId, string newRole)
    {
        var request = new { NewRole = newRole };
        var response = await _httpClient.PutAsJsonAsync(
            $"/api/backoffice-identity/users/{userId}/role",
            request);

        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeactivateUserAsync(Guid userId, string reason)
    {
        var request = new { Reason = reason };
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/backoffice-identity/users/{userId}/deactivate",
            request);

        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ResetUserPasswordAsync(Guid userId, string newPassword)
    {
        var request = new { NewPassword = newPassword };
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/backoffice-identity/users/{userId}/reset-password",
            request);

        return response.IsSuccessStatusCode;
    }
}
