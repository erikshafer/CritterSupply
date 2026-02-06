using Storefront.Clients;
using System.Text.Json;

namespace Storefront.Api.Clients;

public sealed class CatalogClient : ICatalogClient
{
    private readonly HttpClient _httpClient;

    public CatalogClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("CatalogClient");
    }

    public async Task<ProductDto?> GetProductAsync(string sku, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/products/{sku}", ct);
        
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<ProductDto>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    public async Task<PagedResult<ProductDto>> GetProductsAsync(
        string? category = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var url = $"/api/products?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(category))
            url += $"&category={Uri.EscapeDataString(category)}";

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<PagedResult<ProductDto>>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new PagedResult<ProductDto>(Array.Empty<ProductDto>(), 0, page, pageSize);
    }
}
