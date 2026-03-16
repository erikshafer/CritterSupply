using Backoffice.Clients;
using System.Text.Json;

namespace Backoffice.Api.Clients;

public sealed class CustomerIdentityClient : ICustomerIdentityClient
{
    private readonly HttpClient _httpClient;

    public CustomerIdentityClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("CustomerIdentityClient");
    }

    public async Task<CustomerDto?> GetCustomerByEmailAsync(string email, CancellationToken ct = default)
    {
        var url = $"/api/customers?email={Uri.EscapeDataString(email)}";
        var response = await _httpClient.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync(ct);
        var list = JsonSerializer.Deserialize<List<CustomerDto>>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return list?.FirstOrDefault();
    }

    public async Task<CustomerDto?> GetCustomerAsync(Guid customerId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/customers/{customerId}", ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<CustomerDto>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    public async Task<IReadOnlyList<CustomerAddressDto>> GetCustomerAddressesAsync(
        Guid customerId,
        CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/customers/{customerId}/addresses", ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        var list = JsonSerializer.Deserialize<List<CustomerAddressDto>>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return (IReadOnlyList<CustomerAddressDto>)(list ?? new List<CustomerAddressDto>());
    }
}
