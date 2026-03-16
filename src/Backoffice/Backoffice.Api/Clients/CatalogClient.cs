using Backoffice.Clients;
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
}
