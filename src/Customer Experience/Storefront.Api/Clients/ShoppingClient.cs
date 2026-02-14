using Storefront.Clients;
using System.Net.Http.Json;
using System.Text.Json;

namespace Storefront.Api.Clients;

public sealed class ShoppingClient : IShoppingClient
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ShoppingClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("ShoppingClient");
    }

    public async Task<CartDto?> GetCartAsync(Guid cartId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/carts/{cartId}", ct);

        if (!response.IsSuccessStatusCode)
            return null; // Cart not found

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<CartDto>(content, JsonOptions);
    }

    public async Task<Guid> InitializeCartAsync(Guid customerId, CancellationToken ct = default)
    {
        var payload = new { customerId, sessionId = (string?)null };
        var response = await _httpClient.PostAsJsonAsync("/api/carts", payload, ct);
        response.EnsureSuccessStatusCode();

        // Extract cart ID from Location header (e.g., "/api/carts/abc-123")
        var location = response.Headers.Location?.ToString();
        if (location is null)
            throw new InvalidOperationException("Location header missing from cart initialization response");

        var cartIdString = location.Split('/').Last();
        return Guid.Parse(cartIdString);
    }

    public async Task AddItemAsync(Guid cartId, string sku, int quantity, decimal unitPrice, CancellationToken ct = default)
    {
        var payload = new { cartId, sku, quantity, unitPrice };
        var response = await _httpClient.PostAsJsonAsync($"/api/carts/{cartId}/items", payload, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveItemAsync(Guid cartId, string sku, CancellationToken ct = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/carts/{cartId}/items/{sku}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task ChangeQuantityAsync(Guid cartId, string sku, int newQuantity, CancellationToken ct = default)
    {
        var payload = new { cartId, sku, newQuantity };
        var response = await _httpClient.PutAsJsonAsync($"/api/carts/{cartId}/items/{sku}/quantity", payload, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task ClearCartAsync(Guid cartId, string? reason = null, CancellationToken ct = default)
    {
        var url = $"/api/carts/{cartId}";
        if (!string.IsNullOrWhiteSpace(reason))
            url += $"?reason={Uri.EscapeDataString(reason)}";

        var response = await _httpClient.DeleteAsync(url, ct);
        response.EnsureSuccessStatusCode();
    }
}
