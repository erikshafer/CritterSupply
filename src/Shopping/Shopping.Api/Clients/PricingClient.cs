using Shopping.Clients;
using System.Text.Json;

namespace Shopping.Api.Clients;

public sealed class PricingClient : IPricingClient
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PricingClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("PricingClient");
    }

    public async Task<PriceDto?> GetPriceAsync(string sku, CancellationToken ct = default)
    {
        // Normalize SKU to uppercase (Pricing BC requirement)
        var normalizedSku = sku.ToUpperInvariant();

        var response = await _httpClient.GetAsync($"/api/pricing/products/{normalizedSku}", ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var priceResponse = await response.Content.ReadFromJsonAsync<PricingApiResponse>(JsonOptions, ct);
        if (priceResponse is null)
            return null;

        return new PriceDto(
            priceResponse.Sku ?? normalizedSku,
            priceResponse.BasePrice,
            priceResponse.Currency ?? "USD",
            priceResponse.Status ?? "Unknown");
    }

    // Response type matching Pricing BC CurrentPriceView API
    private sealed record PricingApiResponse(
        string? Sku,
        decimal BasePrice,
        string? Currency,
        string? Status);
}
