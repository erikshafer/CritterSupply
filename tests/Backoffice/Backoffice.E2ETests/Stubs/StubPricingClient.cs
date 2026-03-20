using System.Net;
using Backoffice.Clients;

namespace Backoffice.E2ETests.Stubs;

/// <summary>
/// Stub implementation of IPricingClient for E2E tests.
/// Returns in-memory test data configured per scenario with floor/ceiling constraints.
/// </summary>
public sealed class StubPricingClient : IPricingClient
{
    private readonly Dictionary<string, ProductPriceDto> _prices = new();
    private readonly Dictionary<string, decimal> _floorPrices = new();
    private readonly Dictionary<string, decimal> _ceilingPrices = new();

    /// <summary>
    /// When true, all API methods will throw HttpRequestException with 401 Unauthorized.
    /// Used by SessionExpirySteps to simulate session expiry.
    /// </summary>
    public bool SimulateSessionExpired { get; set; }

    /// <summary>
    /// Setup helper: Set current price for a product
    /// </summary>
    public void SetCurrentPrice(string sku, decimal price)
    {
        _prices[sku] = new ProductPriceDto(
            sku,
            price,
            "USD",
            "Published",
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Setup helper: Set floor price constraint
    /// </summary>
    public void SetFloorPrice(string sku, decimal floorPrice)
    {
        _floorPrices[sku] = floorPrice;
    }

    /// <summary>
    /// Setup helper: Set ceiling price constraint
    /// </summary>
    public void SetCeilingPrice(string sku, decimal ceilingPrice)
    {
        _ceilingPrices[sku] = ceilingPrice;
    }

    public Task<SetBasePriceResult?> SetBasePriceAsync(string sku, decimal amount, string currency = "USD", CancellationToken ct = default)
    {
        if (SimulateSessionExpired)
            throw new HttpRequestException("Session expired", null, HttpStatusCode.Unauthorized);

        // Enforce floor price constraint
        if (_floorPrices.TryGetValue(sku, out var floor) && amount < floor)
        {
            return Task.FromResult<SetBasePriceResult?>(new SetBasePriceResult(
                sku,
                amount,
                currency,
                "Failed",
                $"Price cannot be below floor price of ${floor:F2}"));
        }

        // Enforce ceiling price constraint
        if (_ceilingPrices.TryGetValue(sku, out var ceiling) && amount > ceiling)
        {
            return Task.FromResult<SetBasePriceResult?>(new SetBasePriceResult(
                sku,
                amount,
                currency,
                "Failed",
                $"Price cannot exceed ceiling price of ${ceiling:F2}"));
        }

        // Update in-memory price
        _prices[sku] = new ProductPriceDto(sku, amount, currency, "Published", DateTimeOffset.UtcNow);

        return Task.FromResult<SetBasePriceResult?>(new SetBasePriceResult(
            sku,
            amount,
            currency,
            "Success",
            "Price updated successfully"));
    }

    public Task<SchedulePriceChangeResult?> SchedulePriceChangeAsync(string sku, decimal newAmount, string currency, DateTimeOffset scheduledFor, CancellationToken ct = default)
    {
        if (SimulateSessionExpired)
            throw new HttpRequestException("Session expired", null, HttpStatusCode.Unauthorized);

        // Not used in current scenarios
        return Task.FromResult<SchedulePriceChangeResult?>(null);
    }

    public Task<ProductPriceDto?> GetProductPriceAsync(string sku, CancellationToken ct = default)
    {
        if (SimulateSessionExpired)
            throw new HttpRequestException("Session expired", null, HttpStatusCode.Unauthorized);

        return Task.FromResult(_prices.GetValueOrDefault(sku));
    }

    public void Clear()
    {
        _prices.Clear();
        _floorPrices.Clear();
        _ceilingPrices.Clear();
    }
}
