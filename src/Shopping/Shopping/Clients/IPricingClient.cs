namespace Shopping.Clients;

/// <summary>
/// Client interface for Pricing BC operations.
/// Used by Shopping BC to retrieve server-authoritative prices during cart operations.
/// </summary>
public interface IPricingClient
{
    /// <summary>
    /// Gets the current price for a single SKU.
    /// Returns null if the SKU has not been priced or price is not found.
    /// </summary>
    Task<PriceDto?> GetPriceAsync(string sku, CancellationToken ct = default);
}

/// <summary>
/// Price data transfer object from Pricing BC.
/// Maps to CurrentPriceView from Pricing BC.
/// </summary>
public sealed record PriceDto(
    string Sku,
    decimal BasePrice,
    string Currency,
    string Status);
