using System.Text.Json;

namespace Storefront.Clients;

public sealed class ShoppingClient : IShoppingClient
{
    private readonly HttpClient _httpClient;

    public ShoppingClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("ShoppingClient");
    }

    public async Task<CartDto> GetCartAsync(Guid cartId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/carts/{cartId}", ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<CartDto>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException($"Cart {cartId} not found");
    }
}
