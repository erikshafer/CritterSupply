using System.Text.Json;

namespace Storefront.Clients;

public sealed class OrdersClient : IOrdersClient
{
    private readonly HttpClient _httpClient;

    public OrdersClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("OrdersClient");
    }

    public async Task<CheckoutDto> GetCheckoutAsync(Guid checkoutId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/checkouts/{checkoutId}", ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<CheckoutDto>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException($"Checkout {checkoutId} not found");
    }

    public async Task<PagedResult<OrderDto>> GetOrdersAsync(
        Guid customerId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var url = $"/api/orders?customerId={customerId}&page={page}&pageSize={pageSize}";

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<PagedResult<OrderDto>>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new PagedResult<OrderDto>(Array.Empty<OrderDto>(), 0, page, pageSize);
    }
}
