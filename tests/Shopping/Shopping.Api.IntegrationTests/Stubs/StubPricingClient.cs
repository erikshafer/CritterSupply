using Shopping.Clients;

namespace Shopping.Api.IntegrationTests.Stubs;

/// <summary>
/// Stub implementation of IPricingClient for testing.
/// Returns predefined prices without making real HTTP calls to Pricing BC.
/// </summary>
public sealed class StubPricingClient : IPricingClient
{
    private readonly Dictionary<string, PriceDto> _prices = new();

    /// <summary>
    /// Default price used when SKU is not configured (simulates all products having a price).
    /// </summary>
    public decimal DefaultPrice { get; set; } = 29.99m;

    /// <summary>
    /// Configure a specific price for a SKU.
    /// </summary>
    public void SetPrice(string sku, decimal basePrice, string status = "Published")
    {
        var normalizedSku = sku.ToUpperInvariant();
        _prices[normalizedSku] = new PriceDto(normalizedSku, basePrice, "USD", status);
    }

    /// <summary>
    /// Remove a SKU from the price list (simulates product not yet priced).
    /// </summary>
    public void RemovePrice(string sku)
    {
        var normalizedSku = sku.ToUpperInvariant();
        _prices.Remove(normalizedSku);
    }

    /// <summary>
    /// Clear all configured prices.
    /// </summary>
    public void Clear()
    {
        _prices.Clear();
    }

    public Task<PriceDto?> GetPriceAsync(string sku, CancellationToken ct = default)
    {
        var normalizedSku = sku.ToUpperInvariant();

        // Return configured price if exists
        if (_prices.TryGetValue(normalizedSku, out var price))
        {
            return Task.FromResult<PriceDto?>(price);
        }

        // Return default price (simulates all products having a price in integration tests)
        return Task.FromResult<PriceDto?>(new PriceDto(normalizedSku, DefaultPrice, "USD", "Published"));
    }
}
