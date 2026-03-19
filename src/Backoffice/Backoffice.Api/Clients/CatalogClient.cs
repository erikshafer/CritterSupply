using Backoffice.Clients;
using System.Net.Http.Json;
using System.Text.Json;

namespace Backoffice.Api.Clients;

public sealed class CatalogClient : ICatalogClient
{
    private readonly HttpClient _httpClient;

    public CatalogClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("CatalogClient");
    }

    public async Task<ProductDto?> GetProductAsync(string sku, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/products/{Uri.EscapeDataString(sku)}", ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<ProductDto>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    public async Task<bool> UpdateProductDescriptionAsync(string sku, string description, CancellationToken ct = default)
    {
        var request = new { Sku = sku, Description = description };
        using var response = await _httpClient.PutAsJsonAsync(
            $"/api/products/{Uri.EscapeDataString(sku)}/description",
            request,
            ct);

        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateProductDisplayNameAsync(string sku, string displayName, CancellationToken ct = default)
    {
        var request = new { Sku = sku, Name = displayName };
        using var response = await _httpClient.PutAsJsonAsync(
            $"/api/products/{Uri.EscapeDataString(sku)}/display-name",
            request,
            ct);

        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DiscontinueProductAsync(string sku, CancellationToken ct = default)
    {
        var request = new { Sku = sku, NewStatus = "Discontinued" };
        using var response = await _httpClient.PatchAsJsonAsync(
            $"/api/products/{Uri.EscapeDataString(sku)}/status",
            request,
            ct);

        return response.IsSuccessStatusCode;
    }

    public async Task<ProductListResult?> ListProductsAsync(int page = 1, int pageSize = 20, string? category = null, string? status = null, CancellationToken ct = default)
    {
        var queryParams = new List<string>
        {
            $"page={page}",
            $"pageSize={pageSize}"
        };

        if (!string.IsNullOrEmpty(category))
            queryParams.Add($"category={Uri.EscapeDataString(category)}");

        if (!string.IsNullOrEmpty(status))
            queryParams.Add($"status={Uri.EscapeDataString(status)}");

        var query = string.Join("&", queryParams);
        var response = await _httpClient.GetAsync($"/api/products?{query}", ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<ProductListResult>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
}
