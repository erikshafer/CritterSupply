namespace Backoffice.Clients;

/// <summary>
/// HTTP client for interacting with Pricing BC (PricingManager use)
/// </summary>
public interface IPricingClient
{
    /// <summary>
    /// Set base price for a product (works for both Unpriced and Published products)
    /// </summary>
    Task<SetBasePriceResult?> SetBasePriceAsync(string sku, decimal amount, string currency = "USD", CancellationToken ct = default);

    /// <summary>
    /// Schedule a future price change for a product
    /// </summary>
    Task<SchedulePriceChangeResult?> SchedulePriceChangeAsync(string sku, decimal newAmount, string currency, DateTimeOffset scheduledFor, CancellationToken ct = default);

    /// <summary>
    /// Get current price information for a product
    /// </summary>
    Task<ProductPriceDto?> GetProductPriceAsync(string sku, CancellationToken ct = default);
}

/// <summary>
/// Result of setting base price
/// </summary>
public sealed record SetBasePriceResult(
    string Sku,
    decimal Amount,
    string Currency,
    string Status,
    string Message);

/// <summary>
/// Result of scheduling a price change
/// </summary>
public sealed record SchedulePriceChangeResult(
    string Sku,
    Guid ScheduleId,
    decimal Amount,
    string Currency,
    DateTimeOffset ScheduledFor,
    string Message);

/// <summary>
/// Product price information from Pricing BC
/// </summary>
public sealed record ProductPriceDto(
    string Sku,
    decimal? BasePrice,
    string? Currency,
    string Status,
    DateTimeOffset? LastChangedAt);
