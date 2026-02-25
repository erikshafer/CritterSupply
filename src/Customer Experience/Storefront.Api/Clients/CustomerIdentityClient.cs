using Storefront.Clients;
using System.Text.Json;

namespace Storefront.Api.Clients;

public sealed class CustomerIdentityClient : ICustomerIdentityClient
{
    private readonly HttpClient _httpClient;

    public CustomerIdentityClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("CustomerIdentityClient");
    }

    public async Task<IReadOnlyList<CustomerAddressDto>> GetCustomerAddressesAsync(
        Guid customerId,
        string? addressType = null,
        CancellationToken ct = default)
    {
        var url = $"/api/customers/{customerId}/addresses";
        if (!string.IsNullOrEmpty(addressType))
            url += $"?type={Uri.EscapeDataString(addressType)}";

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        var list = JsonSerializer.Deserialize<List<CustomerAddressDto>>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return (IReadOnlyList<CustomerAddressDto>)(list ?? new List<CustomerAddressDto>());
    }

    public async Task<LoginResponse?> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var request = new { email, password };
        var response = await _httpClient.PostAsJsonAsync("/api/auth/login", request, ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<LoginResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsync("/api/auth/logout", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<CurrentUserResponse?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync("/api/auth/me", ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<CurrentUserResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
}
