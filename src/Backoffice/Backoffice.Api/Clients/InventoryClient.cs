using Backoffice.Clients;
using System.Net.Http.Json;
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

    public async Task<IReadOnlyList<InventoryListItemDto>> ListInventoryAsync(
        int? page = null,
        int? pageSize = null,
        CancellationToken ct = default)
    {
        var queryParams = new List<string>();
        if (page.HasValue)
            queryParams.Add($"page={page.Value}");
        if (pageSize.HasValue)
            queryParams.Add($"pageSize={pageSize.Value}");

        var query = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : string.Empty;
        var response = await _httpClient.GetAsync($"/api/inventory{query}", ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        var list = JsonSerializer.Deserialize<List<InventoryListItemDto>>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return (IReadOnlyList<InventoryListItemDto>)(list ?? new List<InventoryListItemDto>());
    }

    public async Task<AdjustInventoryResultDto?> AdjustInventoryAsync(
        string sku,
        int adjustmentQuantity,
        string reason,
        string adjustedBy,
        CancellationToken ct = default)
    {
        var request = new
        {
            AdjustmentQuantity = adjustmentQuantity,
            Reason = reason,
            AdjustedBy = adjustedBy
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"/api/inventory/{Uri.EscapeDataString(sku)}/adjust",
            request,
            ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<AdjustInventoryResultDto>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    public async Task<ReceiveStockResultDto?> ReceiveInboundStockAsync(
        string sku,
        int quantity,
        string source,
        CancellationToken ct = default)
    {
        var request = new
        {
            Quantity = quantity,
            Source = source
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"/api/inventory/{Uri.EscapeDataString(sku)}/receive",
            request,
            ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<ReceiveStockResultDto>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
}
