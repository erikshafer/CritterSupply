using System.Text.Json;

namespace Storefront.Clients;

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
}
