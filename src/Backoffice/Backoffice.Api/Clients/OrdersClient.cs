using Backoffice.Clients;
using System.Text.Json;

namespace Backoffice.Api.Clients;

public sealed class OrdersClient : IOrdersClient
{
    private readonly HttpClient _httpClient;

    public OrdersClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("OrdersClient");
    }

    public async Task<SearchOrdersResultDto> SearchOrdersAsync(
        string query,
        CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/orders/search?query={Uri.EscapeDataString(query)}", ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<SearchOrdersResultDto>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return result ?? new SearchOrdersResultDto(query, 0, Array.Empty<OrderSummaryDto>());
    }

    public async Task<IReadOnlyList<OrderSummaryDto>> GetOrdersAsync(
        Guid customerId,
        int? limit = null,
        CancellationToken ct = default)
    {
        var url = $"/api/orders?customerId={customerId}";
        if (limit.HasValue)
            url += $"&limit={limit.Value}";

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        var list = JsonSerializer.Deserialize<List<OrderSummaryDto>>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return (IReadOnlyList<OrderSummaryDto>)(list ?? new List<OrderSummaryDto>());
    }

    public async Task<OrderDetailDto?> GetOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/orders/{orderId}", ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<OrderDetailDto>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    public async Task CancelOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsync($"/api/orders/{orderId}/cancel", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<ReturnableItemDto>> GetReturnableItemsAsync(
        Guid orderId,
        CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/orders/{orderId}/returnable-items", ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        var list = JsonSerializer.Deserialize<List<ReturnableItemDto>>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return (IReadOnlyList<ReturnableItemDto>)(list ?? new List<ReturnableItemDto>());
    }
}
