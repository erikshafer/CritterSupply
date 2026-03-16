using Backoffice.Clients;
using System.Text.Json;

namespace Backoffice.Api.Clients;

public sealed class InventoryClient : IInventoryClient
{
    private readonly HttpClient _httpClient;

    public InventoryClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("InventoryClient");
    }

    public async Task<StockLevelDto?> GetStockLevelAsync(string sku, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/inventory/{Uri.EscapeDataString(sku)}", ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<StockLevelDto>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    public async Task<IReadOnlyList<LowStockDto>> GetLowStockAsync(
        int? threshold = null,
        CancellationToken ct = default)
    {
        var url = "/api/inventory/low-stock";
        if (threshold.HasValue)
            url += $"?threshold={threshold.Value}";

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        var list = JsonSerializer.Deserialize<List<LowStockDto>>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return (IReadOnlyList<LowStockDto>)(list ?? new List<LowStockDto>());
    }
}
